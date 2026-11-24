using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.DataProtection;

namespace LummaSimulator.Web.Services;

public sealed class EventEditionService(EventEditionStore manifest, DemoStore database, PresenterService presenters,
    TimeProvider clock, IDataProtectionProvider protection, ExperienceSettings settings)
{
    private readonly EventAccess _access=new(database,presenters,clock,manifest);
    private readonly IDataProtector _tickets = protection.CreateProtector("Lumma.EventEdition.Choice.v1");
    private sealed record Ticket(string PortalRunId, string SessionHash, string Generation, DateTimeOffset Expires);
    private static DemoRequestException Denied() => new("EventEditionDenied");
    private async Task<DemoAccount> Visitor(string? secret, CancellationToken ct)
    {
        if (!DemoIdentity.IsToken(secret)) throw Denied();
        var now = clock.GetUtcNow();
        var account = (await database.AccountsAsync(DemoIdentity.Hash(secret!), ct)).SingleOrDefault();
        if (account is null || account.LastSeen <= now.AddMinutes(-settings.AccountIdleMinutes).ToUnixTimeMilliseconds()
            || account.CredentialCount == 0 || account.LastAuthenticatedAt is not { } at || at > now.ToUnixTimeMilliseconds()
            || now.ToUnixTimeMilliseconds() - at >= 1_800_000
            || !await new SecurityAuditService(database,clock,configuration:null).IsAuthenticatedAsync(account,ct)) throw Denied();
        return account;
    }
    public async Task<IReadOnlyList<EditionEvent>> ListAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        await presenters.EventOperatorAsync(principal, ct);
        return await manifest.AccessAsync<IReadOnlyList<EditionEvent>>(false, async m => {
            var allowed=new List<EditionEvent>();
            foreach(var item in m.Events){await _access.RegisterEditionAsync(principal,item.Id,item.Name,false,ct);if(await _access.CanAsync(principal,"edition",item.Id,false,ct))allowed.Add(item);}
            return allowed;
        }, ct);
    }
    public async Task<string> CreateAsync(ClaimsPrincipal principal, string name, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var actor = await presenters.EventOperatorAsync(principal, ct);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl) || end <= start) throw Denied();
        return await manifest.AccessAsync(true, async m => {
            if (m.Events.Count >= 100) throw Denied();
            var id=Guid.NewGuid().ToString("D");await _access.RegisterEditionAsync(principal,id,name.Trim(),true,ct);
            m.Events.Add(new() { Id = id, Name = name.Trim(), PlannedStart = start.ToUniversalTime(),
                PlannedEnd = end.ToUniversalTime(), PresenterId = actor.Id });
            return id;
        }, ct);
    }
    public async Task StatusAsync(ClaimsPrincipal principal, string id, bool close, CancellationToken ct)
    {
        await presenters.EventOperatorAsync(principal, ct);
        await _access.RequireAsync(principal,"edition",id,false,ct);
        await manifest.AccessAsync(true, m => {
            var item = m.Events.SingleOrDefault(e => e.Id == id) ?? throw Denied();
            if (close) { if (item.Status != "Active") throw Denied(); item.Status = "Closed"; }
            else { if (item.Status != "Scheduled") throw Denied(); item.Status = "Active"; }
            return Task.FromResult(true);
        }, ct);
    }
    public async Task<string> LinkAsync(ClaimsPrincipal principal, string eventId, string portalRun, string wpfRun, CancellationToken ct)
    {
        var actor = await presenters.EventOperatorAsync(principal, ct);
        await _access.RequireAsync(principal,"edition",eventId,false,ct);
        if (!Guid.TryParseExact(portalRun, "D", out var p) || !Guid.TryParseExact(wpfRun, "D", out var w)) throw Denied();
        portalRun = p.ToString("D"); wpfRun = w.ToString("D");
        return await manifest.AccessAsync(true, async m => {
            await database.Gate.WaitAsync(ct);
            try
            {
                var account = (await database.AccountsAsync(null, ct)).SingleOrDefault(a => a.DemoRunId == portalRun);
                if (account is null || account.LastSeen <= clock.GetUtcNow().AddMinutes(-settings.AccountIdleMinutes).ToUnixTimeMilliseconds()) throw Denied();
                var item = m.Events.SingleOrDefault(e => e.Id == eventId && e.Status == "Active") ?? throw Denied();
                var context=await database.ScalarAsync("SELECT Kind||':'||EventId FROM PresenterDemoContexts WHERE DemoId=$id",ct,("$id",account.Id)) as string;
                if(context!="edition:"+eventId)throw Denied();
                if (item.Runs.Count >= 1000 || m.Events.SelectMany(e => e.Runs).Any(r => r.PortalRunId == portalRun || r.WpfRunId == wpfRun)) throw Denied();
                var code = DemoIdentity.RandomToken();
                item.Runs.Add(new() { PortalRunId = portalRun, WpfRunId = wpfRun, CodeHash = DemoIdentity.Hash(code), ActorId = actor.Id,
                    SessionHash = actor.SessionHash, Expires = clock.GetUtcNow().AddMinutes(30), Generation = Guid.NewGuid().ToString("D") });
                return code;
            }
            finally { database.Gate.Release(); }
        }, ct);
    }
    public async Task<string?> TicketAsync(string? secret, CancellationToken ct)
    {
        return await manifest.AccessAsync(false, async m => {
            await database.Gate.WaitAsync(ct);
            try
            {
                var account = await Visitor(secret, ct);
                var run = m.Events.Where(e => e.Status == "Active").SelectMany(e => e.Runs).SingleOrDefault(r => r.PortalRunId == account.DemoRunId);
                if (run is null || run.Expires <= clock.GetUtcNow() || !await presenters.EventOperatorValidAsync(run.ActorId, run.SessionHash, ct)) return null;
                if(!await _access.ActorValidAsync(run.ActorId,run.SessionHash,"edition",m.Events.Single(e=>e.Runs.Contains(run)).Id,ct))return null;
                return _tickets.Protect(JsonSerializer.Serialize(new Ticket(account.DemoRunId, account.SessionHash, run.Generation, run.Expires)));
            }
            finally { database.Gate.Release(); }
        }, ct);
    }
    public async Task<EditionAction> ChoiceAsync(string? secret, string ticket, string actionType, CancellationToken ct)
    {
        if (actionType is not ("ClickFixFollowSimulated" or "ClickFixStopSelected") || ticket.Length > 4096) throw Denied();
        Ticket claim;
        try { claim = JsonSerializer.Deserialize<Ticket>(_tickets.Unprotect(ticket)) ?? throw Denied(); }
        catch (Exception e) when (e is CryptographicException or JsonException) { throw Denied(); }
        return await manifest.AccessAsync(true, async m => {
            await database.Gate.WaitAsync(ct);
            try
            {
                var account = await Visitor(secret, ct);
                var run = m.Events.Where(e => e.Status == "Active").SelectMany(e => e.Runs).SingleOrDefault(r => r.PortalRunId == account.DemoRunId) ?? throw Denied();
                if (claim.PortalRunId != account.DemoRunId || claim.SessionHash != account.SessionHash || claim.Generation != run.Generation
                    || claim.Expires <= clock.GetUtcNow() || run.Expires <= clock.GetUtcNow()
                    || !await presenters.EventOperatorValidAsync(run.ActorId, run.SessionHash, ct)) throw Denied();
                if(!await _access.ActorValidAsync(run.ActorId,run.SessionHash,"edition",m.Events.Single(e=>e.Runs.Contains(run)).Id,ct))throw Denied();
                if (run.Action is { } previous) { if (previous.ActionType != actionType) throw Denied(); return previous; }
                return run.Action = new(account.DemoRunId, actionType, 1, clock.GetUtcNow());
            }
            finally { database.Gate.Release(); }
        }, ct);
    }
    public async Task<EditionSnapshot> PollAsync(string? code, string wpfRun, CancellationToken ct)
    {
        if (!DemoIdentity.IsToken(code) || !Guid.TryParseExact(wpfRun, "D", out _)) throw Denied();
        return await manifest.AccessAsync<EditionSnapshot>(false, async m => {
            var item = m.Events.SingleOrDefault(e => e.Runs.Any(r => r.WpfRunId == wpfRun)) ?? throw Denied();
            var run = item.Runs.Single(r => r.WpfRunId == wpfRun);
            if(!await _access.ActorValidAsync(run.ActorId,run.SessionHash,"edition",item.Id,ct))throw Denied();
            if (run.CodeHash.Length != 64 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(run.CodeHash), Convert.FromHexString(DemoIdentity.Hash(code!)))
                || run.Expires <= clock.GetUtcNow() || !await presenters.EventOperatorValidAsync(run.ActorId, run.SessionHash, ct)) throw Denied();
            // No new visitor actions or misleading active state after account reset/expiry.
            await database.Gate.WaitAsync(ct);
            try { if (!(await database.AccountsAsync(null, ct)).Any(a => a.DemoRunId == run.PortalRunId
                && a.LastSeen > clock.GetUtcNow().AddMinutes(-settings.AccountIdleMinutes).ToUnixTimeMilliseconds())) throw Denied(); }
            finally { database.Gate.Release(); }
            return new(run.WpfRunId, run.PortalRunId, item.Id, item.Name, item.Status, item.PlannedStart, item.PlannedEnd, run.Action);
        }, ct);
    }
    public async Task RecoverAsync(ClaimsPrincipal principal, bool confirmed, CancellationToken ct)
    {
        await presenters.EventOperatorAsync(principal, ct);
        if (!confirmed || !principal.IsInRole("Administrator")) throw Denied();
        await manifest.RecoverAsync(ct);
    }
}
