using System.Text.Json;
using Fido2NetLib;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
using LummaSimulator.Web.Services;
using LummaSimulator.Web.Services.Prizes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LummaSimulator.Web.Tests;
internal static class IdentityChecks
{
    // Deliberately retain fractional milliseconds: persistence uses Unix milliseconds,
    // whereas DateTimeOffset itself has 100 ns tick precision.
    private sealed class IdentityClock : TimeProvider
    {
        private DateTimeOffset _now=new DateTimeOffset(2026,11,3,12,0,0,TimeSpan.Zero).AddTicks(1234);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now+=amount;
    }
    internal static async Task RunAsync(string root)
    {
        using var store=new DemoStore(Path.Combine(root,"identity-audit.db"));await store.InitializeAsync();
        var clock=new IdentityClock();var http=new DefaultHttpContext();http.Request.Headers["X-Lumma-Correlation"]="invalid-secret-canary";
        var audit=new SecurityAuditService(store,clock,new HttpContextAccessor{HttpContext=http});
        var service=new DemoService(store,new Fido2(new WebAuthnSettings{RpId="localhost",Origins=["https://localhost:7243"]}.ToFidoConfiguration()),clock,NullLogger<DemoService>.Instance,audit:audit);
        var user=await service.CreateAsync("laptop",default);var other=await service.CreateAsync("yubikey",default);
        TestProgram.Check(await audit.OwnerAsync(user.Token,default) is null,"Demo ownership alone is not an authenticated session");
        using var key=new SoftwareAuthenticator();
        await service.OptionsAsync(user.Token,true,default);
        await TestProgram.RejectAsync(()=>service.VerifyAsync(user.Token,true,JsonSerializer.SerializeToElement(new{}),default),"Invalid registration is rejected and audited");
        await service.VerifyAsync(user.Token,true,key.Register(await service.OptionsAsync(user.Token,true,default)),default);
        TestProgram.Check(await audit.OwnerAsync(user.Token,default) is null,"Passkey registration alone creates no authenticated session");
        var options=await service.OptionsAsync(user.Token,false,default);
        await TestProgram.RejectAsync(()=>service.VerifyAsync(user.Token,false,key.Authenticate(options,origin:"https://other.invalid"),default),"Invalid authentication is rejected and audited");
        await service.VerifyAsync(user.Token,false,key.Authenticate(await service.OptionsAsync(user.Token,false,default)),default);
        var owner=(await audit.OwnerAsync(user.Token,default))!;
        var sessions=await audit.SessionsAsync(owner,default);
        TestProgram.Check(sessions is [{Status:"Active",Current:true,SessionType:"Normal"}],"Successful assertion creates a current normal session");
        var beforeActivity=sessions.Single();
        TestProgram.Check(clock.GetUtcNow().Ticks%TimeSpan.TicksPerMillisecond!=0,"Identity clock deterministically exercises submillisecond precision");
        clock.Advance(TimeSpan.FromMinutes(1));await service.GetAsync(user.Token,true,default);
        var afterActivity=(await audit.SessionsAsync(owner,default)).Single();
        var persistedNow=DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        TestProgram.Check(afterActivity.LastActivity==persistedNow && afterActivity.LastActivity>beforeActivity.LastActivity,"Actual visitor activity updates LastActivity at the persisted millisecond precision");
        TestProgram.Check(afterActivity.ExpiresAt==beforeActivity.ExpiresAt && afterActivity.CreatedAt==beforeActivity.CreatedAt,"Ordinary visitor activity preserves absolute authentication expiry and creation time");
        var boundId=await store.ScalarAsync("SELECT Id FROM IdentitySessions WHERE BindingHash=$hash",default,("$hash",DemoIdentity.Hash(user.Token)));
        TestProgram.Check(afterActivity.Id==beforeActivity.Id && afterActivity.Id==(string?)boundId && afterActivity.Current && afterActivity.Status=="Active","Visitor activity targets the existing cookie-bound session");
        var entries=await audit.AuditAsync(owner,null,null,null,null,0,100,default);
        foreach(var kind in new[]{IdentityEvent.IDENTITY_CREATED,IdentityEvent.PASSKEY_REGISTRATION_STARTED,IdentityEvent.PASSKEY_REGISTERED,IdentityEvent.PASSKEY_REGISTRATION_FAILED,
            IdentityEvent.AUTHENTICATION_STARTED,IdentityEvent.AUTHENTICATION_SUCCEEDED,IdentityEvent.AUTHENTICATION_FAILED,IdentityEvent.SESSION_CREATED,IdentityEvent.SESSION_ACTIVITY})
            TestProgram.Check(entries.Any(e=>e.Event.EventId==(int)kind && e.Event.EventName==kind.ToString()),"Structured identity event exists: "+kind);
        var rawCredential=(await store.CredentialsAsync(owner.Id,default)).Single().Id;
        var json=JsonSerializer.Serialize(entries);
        TestProgram.Check(!json.Contains(user.Token) && !json.Contains(owner.SessionHash) && !json.Contains(rawCredential) && !json.Contains("invalid-secret-canary")
            && entries.All(e=>Guid.TryParseExact(e.Event.CorrelationId,"D",out _) && e.Event.IpAddress is null && !e.Event.Simulation),"Audit envelopes contain no cookies, raw credential IDs, invalid correlation values or default IP logging");
        TestProgram.Check((await audit.AuditAsync(other.Account,null,null,null,null,0,100,default)).All(e=>e.Event.UserId==other.Account.Id),"Audit results are scoped to the supplied server-derived owner");
        TestProgram.Check((await audit.AuditAsync(owner,null,null,"PASSKEY_REGISTERED",owner.DemoRunId,0,1,default)).Count==1
            && (await audit.AuditAsync(owner,clock.GetUtcNow().AddDays(1).ToUnixTimeMilliseconds(),null,null,null,0,100,default)).Count==0,"Audit filtering and pagination constrain results");
        foreach(var correlation in new string?[]{null,"","bad",Guid.NewGuid().ToString("D")})
            TestProgram.Check(Guid.TryParseExact(SecurityAuditService.Correlation(correlation),"D",out _),"Missing/invalid correlation is safely replaced");
        // Schema accepts future session records, but metadata cannot manufacture a second login.
        await store.ExecuteAsync("""
            INSERT INTO IdentitySessions(Id,UserId,DemoRunId,BindingHash,CreatedAt,LastActivity,ExpiresAt,Method,SessionType)
            VALUES('future-second',$user,$run,'synthetic-unbound-hash',$now,$now,$expires,'WebAuthn','Normal')
            """,default,("$user",owner.Id),("$run",owner.DemoRunId),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$expires",clock.GetUtcNow().AddMinutes(30).ToUnixTimeMilliseconds()));
        sessions=await audit.SessionsAsync(owner,default);
        TestProgram.Check(sessions.Count==2 && sessions.Single(s=>s.Id=="future-second").Status=="Expired" && sessions.Count(s=>s.Status=="Active")==1,"Multiple records per identity supported; unbacked second session never becomes active");
        var unboundBefore=sessions.Single(s=>s.Id=="future-second");
        clock.Advance(TimeSpan.FromSeconds(1));await service.GetAsync(user.Token,true,default);
        var unboundAfter=(await audit.SessionsAsync(owner,default)).Single(s=>s.Id==unboundBefore.Id);
        TestProgram.Check(unboundAfter.LastActivity==unboundBefore.LastActivity && unboundAfter.ExpiresAt==unboundBefore.ExpiresAt
            && unboundAfter.Status=="Expired","Visitor activity does not touch another record with a different cookie binding");
        await audit.RevokeAsync(other.Account,sessions.Single(s=>s.Current).Id,false,default);
        TestProgram.Check(await audit.OwnerAsync(user.Token,default) is not null,"Another user cannot revoke the current user's session");
        var before=await PrizeMigrationChecks.Snapshot(store);await store.InitializeAsync();
        TestProgram.Check(before==await PrizeMigrationChecks.Snapshot(store),"Reopening preserves users, passkeys, sessions and audit exactly");
        clock.Advance(TimeSpan.FromMinutes(20));await service.GetAsync(user.Token,true,default);
        clock.Advance(TimeSpan.FromMinutes(10));
        TestProgram.Check(await audit.OwnerAsync(user.Token,default) is null && (await audit.SessionsAsync(owner,default)).All(s=>s.Status=="Expired"),"Account activity cannot keep absolute-expired authentication active");
        var expiredBefore=(await audit.SessionsAsync(owner,default)).Single(s=>s.Current);
        await service.GetAsync(user.Token,true,default);
        var expiredAfter=(await audit.SessionsAsync(owner,default)).Single(s=>s.Id==expiredBefore.Id);
        TestProgram.Check(expiredAfter.Status=="Expired" && expiredAfter.LastActivity==expiredBefore.LastActivity
            && expiredAfter.ExpiresAt==expiredBefore.ExpiresAt && await audit.OwnerAsync(user.Token,default) is null,"Visitor activity cannot update or reactivate an expired authentication session");
        await service.VerifyAsync(user.Token,false,key.Authenticate(await service.OptionsAsync(user.Token,false,default)),default);
        owner=(await audit.OwnerAsync(user.Token,default))!;
        var id=(await audit.SessionsAsync(owner,default)).Single(s=>s.Current).Id;
        await service.OptionsAsync(user.Token,false,default);
        await audit.RevokeAsync(owner,id,true,default);
        var revokedBefore=(await audit.SessionsAsync(owner,default)).Single(s=>s.Id==id);
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.GetAsync(user.Token,true,default);
        await audit.TouchAsync(owner,default); // Stale in-memory ownership must not bypass revocation.
        var revokedAfter=(await audit.SessionsAsync(owner,default)).Single(s=>s.Id==id);
        TestProgram.Check(revokedAfter.Status=="Revoked" && revokedAfter.LastActivity==revokedBefore.LastActivity
            && revokedAfter.ExpiresAt==revokedBefore.ExpiresAt,"Old-cookie activity and stale ownership cannot update or reactivate a revoked session");
        TestProgram.Check(await audit.OwnerAsync(user.Token,default) is null && await service.GetAsync(user.Token,false,default) is null
            && (await store.CredentialsAsync(owner.Id,default)).Count==1,"Revocation invalidates actual cookie binding and preserves passkeys");
        TestProgram.Check(Convert.ToInt64(await store.ScalarAsync("SELECT COUNT(*) FROM Challenges WHERE DemoId=$user",default,("$user",owner.Id)))==0,"Revocation invalidates pending challenges");
        entries=await audit.AuditAsync(owner,null,null,null,null,0,100,default);
        TestProgram.Check(entries.Any(e=>e.Event.EventName=="SESSION_REVOKED") && entries.Any(e=>e.Event.EventName=="USER_LOGGED_OUT"),"Revocation and logout are audited");
        var immutable=false;
        try { await store.ExecuteAsync("UPDATE IdentityAudit SET EventName='tampered'",default); }
        catch(Microsoft.Data.Sqlite.SqliteException) { immutable=true; }
        TestProgram.Check(immutable,"Identity audit rejects updates at the database boundary");
        using var upgrade=new DemoStore(Path.Combine(root,"identity-upgrade.db"));
        await PrizeMigrationChecks.SeedLegacy(upgrade);await PrizeMigration.InitializeAsync(upgrade,default);
        var old=await PrizeMigrationChecks.Snapshot(upgrade,true);
        await PrizeMigrationChecks.ExpectReject(()=>IdentityMigration.InitializeAsync(upgrade,default,()=>throw new InvalidOperationException("Synthetic rollback")),"Interrupted identity migration rolls back");
        TestProgram.Check(Convert.ToInt64(await upgrade.ScalarAsync("SELECT COUNT(*) FROM sqlite_schema WHERE name LIKE 'Identity%'",default))==0,"Migration rollback leaves no partial identity objects");
        await upgrade.InitializeAsync();
        TestProgram.Check(old==await PrizeMigrationChecks.Snapshot(upgrade,true) && Convert.ToInt64(await upgrade.ScalarAsync("SELECT COUNT(*) FROM IdentitySessions",default))==1,"Upgrade preserves all existing records and backfills original session binding");
        await upgrade.ExecuteAsync("DROP INDEX IdentityAuditFilter",default);
        await PrizeMigrationChecks.ExpectReject(()=>upgrade.InitializeAsync(),"Unexpected partial identity schema fails closed before repair");
    }
}
