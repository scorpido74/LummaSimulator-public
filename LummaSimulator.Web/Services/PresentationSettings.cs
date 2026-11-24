using System.Text.Json;
using System.Security.Claims;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Services;
public sealed record PresentationProfile(string Organization,string DisplayName,string Language,string EventType,int RetentionDays,string ProposedBaseUrl,long Revision)
{
    public DemoNetworkPlan? Network {get;init;}
    public VisitorDefault? DefaultVisitorEvent {get;init;}
}
public sealed record VisitorDefault(string Kind,string Id,string Name,bool Available=false);
public sealed class PresentationSettings(DemoStore store,PresenterService presenters,ExperienceSettings defaults,TimeProvider clock,Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>? localization=null,Microsoft.Extensions.Configuration.IConfiguration? configuration=null)
{
    private PresentationProfile? _current;
    private HashSet<string> _logos=[];
    public PresentationProfile Current=>Volatile.Read(ref _current)??new PresentationProfile(defaults.OrganizationName,"LummaSimulator",defaults.DefaultLanguage,"experience",7,"",0)
        {Network=configuration?.GetSection("DemoNetwork").Get<DemoNetworkPlan>()};
    public string? Logo(string slot)=>Volatile.Read(ref _logos).Contains(slot)?"/branding/logo/"+slot:null;
    public async Task LoadAsync(CancellationToken ct,bool applyDefaults=false)
    {
        var json=await store.ScalarAsync("SELECT Json FROM PresenterPresentationSettings WHERE Id=1",ct) as string;
        if(json is not null){
            var profile=JsonSerializer.Deserialize<PresentationProfile>(json)??throw new InvalidDataException("PresentationSettings");
            if(!defaults.AvailableLanguages.Contains(profile.Language))throw new InvalidDataException("PresentationSettingsLanguage");
            Volatile.Write(ref _current,profile);
            if(applyDefaults){defaults.OrganizationName=profile.Organization;defaults.DefaultLanguage=profile.Language;
                if(localization is not null)localization.Value.DefaultRequestCulture=new Microsoft.AspNetCore.Localization.RequestCulture(profile.Language);}
        }
        var logos=new HashSet<string>();await using var c=await store.OpenAsync(ct);using var q=c.CreateCommand();q.CommandText="SELECT Slot FROM PresenterLogos";
        using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))logos.Add(r.GetString(0));Volatile.Write(ref _logos,logos);
    }
    public async Task SaveAsync(ClaimsPrincipal principal,PresentationProfile profile,CancellationToken ct)
    {
        if(profile.Network is not null && !profile.Network.IsValid())throw new DemoRequestException("PresenterConfirm");
        static bool Text(string s,int max)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=max&&!s.Any(char.IsControl);
        if(!Text(profile.Organization,80)||!Text(profile.DisplayName,100)||profile.Language is not ("nl-NL" or "en-GB")||!defaults.AvailableLanguages.Contains(profile.Language)||profile.EventType is not ("experience" or "prize" or "edition")||profile.RetentionDays is not (7 or 14 or 90))throw new DemoRequestException("PresenterConfirm");
        if(profile.ProposedBaseUrl.Length>255||profile.ProposedBaseUrl.Length!=0&&(!Uri.TryCreate(profile.ProposedBaseUrl,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length!=0||u.Query.Length!=0||u.Fragment.Length!=0||u.AbsolutePath!="/"||profile.ProposedBaseUrl!=u.GetLeftPart(UriPartial.Authority)))throw new DemoRequestException("PresenterConfirm");
        await using(var c=await store.OpenAsync(ct)){using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,true,true,ct);
            var previous=await EventAccess.Scalar(c,tx,"SELECT Json FROM PresenterPresentationSettings WHERE Id=1",ct) as string;
            // Ordinary presentation edits must not erase the independently managed network plan.
            var stored=previous is null?null:JsonSerializer.Deserialize<PresentationProfile>(previous);
            profile=profile with{DefaultVisitorEvent=stored?.DefaultVisitorEvent};
            if(profile.Network is null)profile=profile with{Network=stored is null?Current.Network:stored.Network};
            if(profile.Network is not null && !profile.Network.IsValid())throw new DemoRequestException("PresenterConfirm");
            var revision=previous is null?0:JsonSerializer.Deserialize<PresentationProfile>(previous)!.Revision;
            if(profile.Revision!=revision)throw new DemoRequestException("PresenterEventChanged");
            await EventAccess.Execute(c,tx,"INSERT INTO PresenterPresentationSettings VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json; INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($now,$actor,'PresentationSettingsChanged','installation')",ct,("$json",JsonSerializer.Serialize(profile with{Revision=revision+1})),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$actor",actor.Id));tx.Commit();}
        await LoadAsync(ct,true);
    }
    public async Task SaveVisitorDefaultAsync(ClaimsPrincipal principal,string kind,string id,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);
        using var tx=c.BeginTransaction(deferred:false);
        var actor=await presenters.Actor(c,tx,principal,true,false,ct);
        if(!await EventAccess.Allowed(c,tx,actor.Id,actor.Role,kind,id,true,ct))throw new DemoRequestException("PresenterDenied");
        var name=await EventAccess.VisitorDefaultNameAsync(c,tx,kind,id,ct)
            ??throw new DemoRequestException("VisitorDefaultUnavailable");
        var json=await EventAccess.Scalar(c,tx,"SELECT Json FROM PresenterPresentationSettings WHERE Id=1",ct) as string;
        var profile=json is null?Current:JsonSerializer.Deserialize<PresentationProfile>(json)!;
        await presenters.Actor(c,tx,principal,true,true,ct);
        if(profile.DefaultVisitorEvent is {} existing&&existing.Kind==kind&&existing.Id==id)return;
        var updated=profile with{DefaultVisitorEvent=new(kind,id,name),Revision=profile.Revision+1};
        await EventAccess.Execute(c,tx,"INSERT INTO PresenterPresentationSettings VALUES(1,$json) ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json",ct,("$json",JsonSerializer.Serialize(updated)));
        await EventAccess.Execute(c,tx,"INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($now,$actor,'DefaultVisitorEventChanged',$target)",ct,
            ("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$actor",actor.Id),("$target",kind+":"+id));
        tx.Commit();
        await LoadAsync(ct);
    }
    public async Task UploadAsync(ClaimsPrincipal principal,string slot,byte[] image,CancellationToken ct)
    {
        if(slot is not ("organization" or "presenter" or "visitor"))throw new DemoRequestException("PresenterConfirm");
        var png=SafeLogo.Validate(image);
        await using(var c=await store.OpenAsync(ct)){using var tx=c.BeginTransaction(deferred:false);var actor=await presenters.Actor(c,tx,principal,true,true,ct);
            await EventAccess.Execute(c,tx,"INSERT INTO PresenterLogos VALUES($slot,$image) ON CONFLICT(Slot) DO UPDATE SET Image=excluded.Image; INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($now,$actor,'BrandingChanged',$slot)",ct,("$slot",slot),("$image",png),("$now",clock.GetUtcNow().ToUnixTimeMilliseconds()),("$actor",actor.Id));tx.Commit();}
        await LoadAsync(ct);
    }
    public async Task<byte[]?> ReadLogoAsync(string slot,CancellationToken ct)=>slot is "organization" or "presenter" or "visitor"?await store.ScalarAsync("SELECT Image FROM PresenterLogos WHERE Slot=$slot",ct,("$slot",slot)) as byte[]:null;
}
