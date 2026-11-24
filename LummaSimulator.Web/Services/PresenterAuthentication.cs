using System.Formats.Cbor;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fido2NetLib;
using Fido2NetLib.Objects;
using LummaSimulator.Web.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;

namespace LummaSimulator.Web.Services;

public sealed record PresenterAccount(string Id, string Login, string Name, string Role, bool Active, long Created, long? LastLogin);
public sealed record PresenterKey(string Id, string Name, long Registered, long? Used);
public sealed record PresenterAudit(long Id, long At, string? Actor, string Action, string? Target);
public sealed record PresenterInvitation(string AccountId, string Code);

public sealed partial class PresenterService(DemoStore store, IConfiguration configuration, TimeProvider clock, IFido2 fido)
{
    public const string Scheme = "Presenter";
    public const string Policy = "PresenterOnly";
    public const string AdminPolicy = "AdministratorOnly";
    public const string BrowserCookie = "__Host-Lumma.PresenterCeremony";
    public const string EnrollmentCookie = "__Host-Lumma.PresenterEnrollment";
    private int SessionMinutes => Math.Clamp(configuration.GetValue("Presenter:SessionMinutes",30),5,30);
    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    public static CookieOptions ShortCookie => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true };
    private static DemoRequestException Denied() => new("PresenterDenied");
    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args)
    { var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql; foreach (var (k,v) in args) q.Parameters.AddWithValue(k,v ?? DBNull.Value); return q; }
    private static async Task<int> Exec(SqliteConnection c, SqliteTransaction tx, string sql, CancellationToken ct, params (string, object?)[] args)
    { using var q = Cmd(c,tx,sql,args); return await q.ExecuteNonQueryAsync(ct); }
    private static async Task<object?> Scalar(SqliteConnection c, SqliteTransaction tx, string sql, CancellationToken ct, params (string, object?)[] args)
    { using var q = Cmd(c,tx,sql,args); return await q.ExecuteScalarAsync(ct); }
    private static Task Audit(SqliteConnection c, SqliteTransaction tx, long at, string? actor, string action, string? target, CancellationToken ct) =>
        Exec(c,tx,"INSERT INTO PresenterAudit(At,Actor,Action,Target) VALUES($at,$actor,$action,$target)",ct,("$at",at),("$actor",actor),("$action",action),("$target",target));

    internal static async Task InitializeAsync(DemoStore store, CancellationToken ct)
    {
        await using var c = await store.OpenAsync(ct); using var tx = c.BeginTransaction(deferred:false);
        await Exec(c,tx,"""
            CREATE TABLE IF NOT EXISTS PresenterAccounts (
              Id TEXT PRIMARY KEY, Login TEXT NOT NULL UNIQUE COLLATE NOCASE, Name TEXT NOT NULL,
              Role TEXT NOT NULL CHECK(Role IN ('Administrator','Presenter')), Active INTEGER NOT NULL DEFAULT 0,
              Pending INTEGER NOT NULL DEFAULT 1, Handle BLOB NOT NULL UNIQUE, Created INTEGER NOT NULL, Activated INTEGER, LastLogin INTEGER);
            CREATE TABLE IF NOT EXISTS PresenterKeys (
              Id TEXT PRIMARY KEY, Ref TEXT NOT NULL UNIQUE, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), PublicKey BLOB NOT NULL,
              Counter INTEGER NOT NULL, BackupEligible INTEGER NOT NULL, BackedUp INTEGER NOT NULL,
              Name TEXT NOT NULL, Registered INTEGER NOT NULL, Used INTEGER);
            CREATE TABLE IF NOT EXISTS PresenterAuthSessions (
              Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL,
              Reauthenticated INTEGER);
            CREATE TABLE IF NOT EXISTS PresenterInvitations (
              Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL, Bootstrap INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS PresenterEnrollmentGrants (
              Hash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Expires INTEGER NOT NULL, Bootstrap INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS PresenterCeremonies (
              BrowserHash TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES PresenterAccounts(Id), Kind TEXT NOT NULL,
              OperationId TEXT NOT NULL, Options TEXT NOT NULL, Expires INTEGER NOT NULL, Binding TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PresenterAudit (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, At INTEGER NOT NULL, Actor TEXT, Action TEXT NOT NULL, Target TEXT);
            CREATE TRIGGER IF NOT EXISTS PresenterAuditNoUpdate BEFORE UPDATE ON PresenterAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END;
            CREATE TRIGGER IF NOT EXISTS PresenterAuditNoDelete BEFORE DELETE ON PresenterAudit BEGIN SELECT RAISE(ABORT,'Audit is append-only'); END;
            INSERT OR IGNORE INTO ExperienceMigrations(Version) VALUES(4);
            """,ct);
        await MigrateDiscoverableLoginAsync(c,tx,ct);
        tx.Commit();
    }

    private static void ValidateAccount(string login, string name, string role)
    { if (!Regex.IsMatch(login,"\\A[A-Za-z0-9._-]{3,64}\\z") || string.IsNullOrWhiteSpace(name) || name.Length>100 || role is not ("Administrator" or "Presenter")) throw Denied(); }
    public async Task<bool> IsSetupRequiredAsync(CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct); using var tx=c.BeginTransaction(deferred:true);
        return Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Activated IS NOT NULL",ct))==0;
    }

    internal async Task<PresenterInvitation> IssueBootstrapAsync(string login, string name, CancellationToken ct)
    {
        ValidateAccount(login,name,"Administrator");
        await using var c=await store.OpenAsync(ct); using var tx=c.BeginTransaction(deferred:false);
        if (Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Activated IS NOT NULL",ct))!=0) throw Denied();
        await Exec(c,tx,"DELETE FROM PresenterInvitations; DELETE FROM PresenterEnrollmentGrants; DELETE FROM PresenterCeremonies; UPDATE PresenterAccounts SET Pending=0 WHERE Activated IS NULL",ct);
        var stale=await Scalar(c,tx,"SELECT Id FROM PresenterAccounts WHERE Login=$login AND Activated IS NULL",ct,("$login",login)) as string;
        PresenterInvitation result;
        if(stale is not null) {
            var code=DemoIdentity.RandomToken();
            await Exec(c,tx,"UPDATE PresenterAccounts SET Pending=1,Name=$name WHERE Id=$id; INSERT INTO PresenterInvitations(Hash,AccountId,Expires,Bootstrap) VALUES($hash,$id,$expires,1)",ct,("$name",name.Trim()),("$id",stale),("$hash",DemoIdentity.Hash(code)),("$expires",Now+600000));
            result=new(stale,code);
        } else result=await CreatePending(c,tx,login,name,"Administrator",true,ct);
        await Audit(c,tx,Now,null,"BootstrapIssued",result.AccountId,ct); tx.Commit(); return result;
    }
    private async Task<PresenterInvitation> CreatePending(SqliteConnection c, SqliteTransaction tx, string login, string name, string role, bool bootstrap, CancellationToken ct)
    {
        var id=Guid.NewGuid().ToString("D"); var code=DemoIdentity.RandomToken();
        if (Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Login=$login",ct,("$login",login)))!=0) throw Denied();
        await Exec(c,tx,"INSERT INTO PresenterAccounts(Id,Login,Name,Role,Handle,Created) VALUES($id,$login,$name,$role,$handle,$now); INSERT INTO PresenterInvitations(Hash,AccountId,Expires,Bootstrap) VALUES($hash,$id,$expires,$bootstrap)",ct,
            ("$id",id),("$login",login),("$name",name.Trim()),("$role",role),("$handle",System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),("$now",Now),("$hash",DemoIdentity.Hash(code)),("$expires",Now+600000),("$bootstrap",bootstrap));
        return new(id,code);
    }

    public async Task<string> OpenEnrollmentAsync(string code, bool directLoopback, CancellationToken ct, bool bootstrapOnly = false)
    {
        if (!DemoIdentity.IsToken(code)) throw Denied();
        await using var c=await store.OpenAsync(ct); using var tx=c.BeginTransaction(deferred:false);
        string account; bool bootstrap; long expires;
        using(var q=Cmd(c,tx,"SELECT i.AccountId,i.Bootstrap,i.Expires FROM PresenterInvitations i JOIN PresenterAccounts a ON a.Id=i.AccountId WHERE i.Hash=$hash AND i.Expires>$now AND (a.Pending=1 AND a.Active=0 AND a.Activated IS NULL)",("$hash",DemoIdentity.Hash(code)),("$now",Now)))
        using(var r=await q.ExecuteReaderAsync(ct)) { if(!await r.ReadAsync(ct)) throw Denied(); account=r.GetString(0);bootstrap=r.GetBoolean(1);expires=r.GetInt64(2); }
        if(!await EventEnrollmentValid(c,tx,account,ct))throw Denied();
        if(bootstrap && !directLoopback) throw Denied();
        if(bootstrapOnly && (!bootstrap || Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts WHERE Activated IS NOT NULL",ct))!=0)) throw Denied();
        var grant=DemoIdentity.RandomToken();
        await Exec(c,tx,"DELETE FROM PresenterInvitations WHERE Hash=$old; INSERT INTO PresenterEnrollmentGrants(Hash,AccountId,Expires,Bootstrap) VALUES($hash,$account,$expires,$bootstrap)",ct,
            ("$old",DemoIdentity.Hash(code)),("$hash",DemoIdentity.Hash(grant)),("$account",account),("$expires",expires),("$bootstrap",bootstrap));
        await Audit(c,tx,Now,account,"EnrollmentOpened",account,ct); tx.Commit(); return grant;
    }

    internal async Task<(string Id,string Role)> Actor(SqliteConnection c,SqliteTransaction tx,ClaimsPrincipal principal,bool admin,bool fresh,CancellationToken ct)
    {
        var secret=principal.FindFirstValue("sid"); var id=principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if(!DemoIdentity.IsToken(secret) || principal.Identity?.IsAuthenticated!=true || principal.FindFirstValue("amr")!="webauthn") throw Denied();
        string role; long? reauth;
        using(var q=Cmd(c,tx,"SELECT a.Role,s.Reauthenticated FROM PresenterAuthSessions s JOIN PresenterAccounts a ON a.Id=s.AccountId WHERE s.Hash=$hash AND a.Id=$id AND a.Active=1 AND s.Expires>$now",("$hash",DemoIdentity.Hash(secret!)),("$id",id),("$now",Now)))
        using(var r=await q.ExecuteReaderAsync(ct)) { if(!await r.ReadAsync(ct)) throw Denied();role=r.GetString(0);reauth=r.IsDBNull(1)?null:r.GetInt64(1); }
        if(admin && role!="Administrator") throw Denied();
        if(fresh) {
            if(reauth is null || reauth<Now-300000) throw new DemoRequestException("PresenterReauthenticate");
            await Exec(c,tx,"UPDATE PresenterAuthSessions SET Reauthenticated=NULL WHERE Hash=$hash",ct,("$hash",DemoIdentity.Hash(secret!)));
        }
        return(id!,role);
    }
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);
        try { var actor=await Actor(c,tx,principal,false,false,ct);
            if(!principal.IsInRole("Presenter") || principal.IsInRole("Administrator") != (actor.Role=="Administrator")) return false; return true;
        } catch(DemoRequestException) { return false; }
    }
    public async Task SignOutAsync(ClaimsPrincipal principal,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var secret=principal.FindFirstValue("sid");
        if(DemoIdentity.IsToken(secret)) { await Exec(c,tx,"DELETE FROM PresenterAuthSessions WHERE Hash=$hash",ct,("$hash",DemoIdentity.Hash(secret!))); await Audit(c,tx,Now,principal.FindFirstValue(ClaimTypes.NameIdentifier),"SignedOut",null,ct); }
        tx.Commit();
    }

    public async Task<string> OptionsAsync(string browser,string operation,string kind,string login,string? enrollment,ClaimsPrincipal principal,bool directLoopback,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(browser) || !Guid.TryParseExact(operation,"D",out _) || kind is not ("login" or "reauth" or "enroll")) throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
        var bh=DemoIdentity.Hash(browser);
        await Exec(c,tx,"DELETE FROM PresenterCeremonies WHERE Expires<=$now",ct,("$now",Now));
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterCeremonies WHERE BrowserHash=$hash",ct,("$hash",bh)))!=0) throw new DemoRequestException("PresenterPending");
        // Normal login leaves the account unresolved until the assertion arrives.
        // The legacy login argument is deliberately ignored; reauthentication stays account-bound.
        if(kind=="login") {
            var discoverable=fido.GetAssertionOptions(new(){AllowedCredentials=[],UserVerification=UserVerificationRequirement.Required}).ToJson();
            await Exec(c,tx,"INSERT INTO PresenterCeremonies VALUES($browser,NULL,'login',$op,$options,$expires,$binding)",ct,
                ("$browser",bh),("$op",operation),("$options",discoverable),("$expires",Now+120000),("$binding",bh));
            tx.Commit();return discoverable;
        }
        string id; string binding;
        if(kind=="reauth") {
            var actor=await Actor(c,tx,principal,false,false,ct);id=actor.Id;binding=DemoIdentity.Hash(principal.FindFirstValue("sid")!);
        } else if(DemoIdentity.IsToken(enrollment)) {
            using var q=Cmd(c,tx,"SELECT g.AccountId,g.Bootstrap FROM PresenterEnrollmentGrants g JOIN PresenterAccounts a ON a.Id=g.AccountId WHERE g.Hash=$hash AND g.Expires>$now AND (a.Pending=1 AND a.Active=0 AND a.Activated IS NULL)",("$hash",DemoIdentity.Hash(enrollment!)),("$now",Now));
            using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct) || (r.GetBoolean(1)&&!directLoopback)) throw Denied();id=r.GetString(0);binding=DemoIdentity.Hash(enrollment!);
            // Close the reader before the shared provenance query on this transaction.
            r.Dispose();
            if(!await EventEnrollmentValid(c,tx,id,ct))throw Denied();
        } else {
            var actor=await Actor(c,tx,principal,false,true,ct);id=actor.Id;binding=DemoIdentity.Hash(principal.FindFirstValue("sid")!);
        }
        byte[] handle;string display;
        using(var q=Cmd(c,tx,"SELECT Handle,Name FROM PresenterAccounts WHERE Id=$id",("$id",id)))
        using(var r=await q.ExecuteReaderAsync(ct)) { await r.ReadAsync(ct);handle=(byte[])r[0];display=r.GetString(1); }
        var keys=new List<PublicKeyCredentialDescriptor>();
        using(var q=Cmd(c,tx,"SELECT Id FROM PresenterKeys WHERE AccountId=$id",("$id",id)))
        using(var r=await q.ExecuteReaderAsync(ct)) while(await r.ReadAsync(ct)) keys.Add(new(WebEncoders.Base64UrlDecode(r.GetString(0))));
        if(kind=="enroll" && keys.Count>=10) throw Denied();
        if(kind!="enroll" && keys.Count==0) throw Denied();
        var options=kind=="enroll" ? fido.RequestNewCredential(new() { User=new(){Id=handle,Name="Presenter-"+id,DisplayName=display},ExcludeCredentials=keys,
            AuthenticatorSelection=new(){ResidentKey=ResidentKeyRequirement.Required,UserVerification=UserVerificationRequirement.Required},AttestationPreference=AttestationConveyancePreference.None }).ToJson()
            : fido.GetAssertionOptions(new(){AllowedCredentials=keys,UserVerification=UserVerificationRequirement.Required}).ToJson();
        await Exec(c,tx,"INSERT INTO PresenterCeremonies VALUES($browser,$id,$kind,$op,$options,$expires,$binding)",ct,("$browser",bh),("$id",id),("$kind",kind),("$op",operation),("$options",options),("$expires",Now+120000),("$binding",binding));
        tx.Commit();return options;
    }

    public async Task<ClaimsPrincipal?> VerifyAsync(string browser,string operation,string kind,JsonElement response,string keyName,string? enrollment,ClaimsPrincipal principal,bool directLoopback,CancellationToken ct)
    {
        if(!DemoIdentity.IsToken(browser) || !Guid.TryParseExact(operation,"D",out _)) throw Denied();
        // Consume before verification in its own commit: even invalid responses burn the challenge.
        string? id;string options,binding;
        await using(var c=await store.OpenAsync(ct)) { using var tx=c.BeginTransaction(deferred:false);
            using(var q=Cmd(c,tx,"DELETE FROM PresenterCeremonies WHERE BrowserHash=$browser AND OperationId=$op AND Kind=$kind AND Expires>$now RETURNING AccountId,Options,Binding",("$browser",DemoIdentity.Hash(browser)),("$op",operation),("$kind",kind),("$now",Now)))
            using(var r=await q.ExecuteReaderAsync(ct)) { if(!await r.ReadAsync(ct)) throw Denied();id=r.IsDBNull(0)?null:r.GetString(0);options=r.GetString(1);binding=r.GetString(2); }
            tx.Commit(); }
        try {
            await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);
            if(id is null) {
                if(kind!="login" || binding!=DemoIdentity.Hash(browser)) throw Denied();
                var assertion=response.Deserialize<AuthenticatorAssertionRawResponse>() ?? throw Denied();
                // Resolve exclusively in the persistent presenter credential namespace.
                id=await Scalar(c,tx,"SELECT AccountId FROM PresenterKeys WHERE Id=$key",ct,
                    ("$key",WebEncoders.Base64UrlEncode(assertion.RawId))) as string ?? throw Denied();
            }
            byte[] handle;string role;string personalName;
            using(var q=Cmd(c,tx,"SELECT Handle,Role,Name FROM PresenterAccounts WHERE Id=$id",("$id",id)))
            using(var r=await q.ExecuteReaderAsync(ct)) { if(!await r.ReadAsync(ct)) throw Denied();handle=(byte[])r[0];role=r.GetString(1);personalName=r.GetString(2); }
            ClaimsPrincipal? signedIn=null;
            if(kind=="enroll") {
                var grant=DemoIdentity.IsToken(enrollment) && DemoIdentity.Hash(enrollment!)==binding;
                if(grant) {
                    var valid=Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEnrollmentGrants g JOIN PresenterAccounts a ON a.Id=g.AccountId WHERE g.Hash=$hash AND g.AccountId=$id AND g.Expires>$now AND (a.Pending=1 AND a.Active=0 AND a.Activated IS NULL) AND (g.Bootstrap=0 OR $local=1)",ct,
                        ("$hash",binding),("$id",id),("$now",Now),("$local",directLoopback)));
                    if(valid!=1) throw Denied();
                    if(!await EventEnrollmentValid(c,tx,id,ct))throw Denied();
                } else {
                    var actor=await Actor(c,tx,principal,false,false,ct);
                    if(actor.Id!=id || DemoIdentity.Hash(principal.FindFirstValue("sid")!)!=binding) throw Denied();
                }
                if(string.IsNullOrWhiteSpace(keyName) || keyName.Length>60 || Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterKeys WHERE AccountId=$id",ct,("$id",id)))>=10) throw Denied();
                var raw=response.Deserialize<AuthenticatorAttestationRawResponse>() ?? throw Denied(); RequireNone(raw.Response.AttestationObject);
                var verified=await fido.MakeNewCredentialAsync(new(){AttestationResponse=raw,OriginalOptions=CredentialCreateOptions.FromJson(options),
                    IsCredentialIdUniqueToUserCallback=async(args,token)=>Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterKeys WHERE Id=$id",token,("$id",WebEncoders.Base64UrlEncode(args.CredentialId))))==0},ct);
                if(!verified.User.Id.AsSpan().SequenceEqual(handle)) throw Denied();
                await Exec(c,tx,"INSERT INTO PresenterKeys(Id,Ref,AccountId,PublicKey,Counter,BackupEligible,BackedUp,Name,Registered) VALUES($key,$ref,$id,$public,$counter,$be,$bs,$name,$now); UPDATE PresenterAccounts SET Active=CASE WHEN Activated IS NULL THEN 1 ELSE Active END,Pending=0,Activated=COALESCE(Activated,$now) WHERE Id=$id; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=$id; DELETE FROM PresenterInvitations WHERE AccountId=$id",ct,
                    ("$key",WebEncoders.Base64UrlEncode(verified.Id)),("$ref",Guid.NewGuid().ToString("D")),("$id",id),("$public",verified.PublicKey),("$counter",(long)verified.SignCount),("$be",verified.IsBackupEligible),("$bs",verified.IsBackedUp),("$name",keyName.Trim()),("$now",Now));
                await Audit(c,tx,Now,id,"CredentialRegistered",id,ct);
            } else {
                if(kind=="reauth") { var actor=await Actor(c,tx,principal,false,false,ct); if(actor.Id!=id || DemoIdentity.Hash(principal.FindFirstValue("sid")!)!=binding) throw Denied(); }
                else if(kind!="login" || binding!=DemoIdentity.Hash(browser)) throw Denied();
                if(Convert.ToInt64(await Scalar(c,tx,"SELECT Active FROM PresenterAccounts WHERE Id=$id",ct,("$id",id)))!=1) throw Denied();
                var raw=response.Deserialize<AuthenticatorAssertionRawResponse>() ?? throw Denied();var key=WebEncoders.Base64UrlEncode(raw.RawId);
                if(kind=="login" && (raw.Response.UserHandle is not {Length:>0} || !raw.Response.UserHandle.AsSpan().SequenceEqual(handle))) throw Denied();
                byte[] publicKey;uint counter;bool backup;
                using(var q=Cmd(c,tx,"SELECT PublicKey,Counter,BackupEligible FROM PresenterKeys WHERE Id=$key AND AccountId=$id",("$key",key),("$id",id)))
                using(var r=await q.ExecuteReaderAsync(ct)){ if(!await r.ReadAsync(ct))throw Denied();publicKey=(byte[])r[0];counter=(uint)r.GetInt64(1);backup=r.GetBoolean(2); }
                var verified=await fido.MakeAssertionAsync(new(){AssertionResponse=raw,OriginalOptions=AssertionOptions.FromJson(options),StoredPublicKey=publicKey,StoredSignatureCounter=counter,
                    IsUserHandleOwnerOfCredentialIdCallback=(args,_)=>Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(handle) && WebEncoders.Base64UrlEncode(args.CredentialId)==key)},ct);
                if(((raw.Response.AuthenticatorData[32]&8)!=0)!=backup)throw Denied();
                await Exec(c,tx,"UPDATE PresenterKeys SET Counter=$counter,BackedUp=$bs,Used=$now WHERE Id=$key",ct,("$counter",(long)verified.SignCount),("$bs",verified.IsBackedUp),("$now",Now),("$key",key));
                if(kind=="reauth") await Exec(c,tx,"UPDATE PresenterAuthSessions SET Reauthenticated=$now WHERE Hash=$hash",ct,("$now",Now),("$hash",binding));
                else {
                    var secret=DemoIdentity.RandomToken();
                    // Logging in again replaces only the session carried by this browser, never another presenter's session.
                    var old=principal.FindFirstValue("sid");if(DemoIdentity.IsToken(old)) await Exec(c,tx,"DELETE FROM PresenterAuthSessions WHERE Hash=$hash",ct,("$hash",DemoIdentity.Hash(old!)));
                    await Exec(c,tx,"INSERT INTO PresenterAuthSessions(Hash,AccountId,Expires) VALUES($hash,$id,$expires); UPDATE PresenterAccounts SET LastLogin=$now WHERE Id=$id",ct,("$hash",DemoIdentity.Hash(secret)),("$id",id),("$expires",Now+(long)TimeSpan.FromMinutes(SessionMinutes).TotalMilliseconds),("$now",Now));
                    var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,personalName),new(ClaimTypes.Role,"Presenter"),new("sid",secret),new("amr","webauthn")};
                    if(role=="Administrator")claims.Add(new(ClaimTypes.Role,"Administrator"));signedIn=new(new ClaimsIdentity(claims,Scheme));
                }
                await Audit(c,tx,Now,id,kind=="reauth"?"Reauthenticated":"SignedIn",id,ct);
            }
            tx.Commit();return signedIn;
        } catch(Exception e) when(e is not OperationCanceledException) {
            await using var c=await store.OpenAsync(CancellationToken.None);using var tx=c.BeginTransaction(deferred:false);await Audit(c,tx,Now,id,"CeremonyRejected",id,CancellationToken.None);tx.Commit();throw Denied();
        }
    }
    public async Task CancelAsync(string browser,string operation,CancellationToken ct)
    { if(!DemoIdentity.IsToken(browser))return;await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);await Exec(c,tx,"DELETE FROM PresenterCeremonies WHERE BrowserHash=$browser AND OperationId=$op",ct,("$browser",DemoIdentity.Hash(browser)),("$op",operation));tx.Commit(); }

    public async Task<PresenterInvitation> CreateAccountAsync(ClaimsPrincipal principal,string login,string name,string role,CancellationToken ct)
    { ValidateAccount(login,name,role);await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,true,ct);var invitation=await CreatePending(c,tx,login,name,role,false,ct);await Audit(c,tx,Now,actor.Id,"AccountCreated",invitation.AccountId,ct);tx.Commit();return invitation; }
    public async Task SetAccountAsync(ClaimsPrincipal principal,string target,string role,bool active,CancellationToken ct)
    {
        if(role is not ("Administrator" or "Presenter"))throw Denied();
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,true,ct);
        string currentRole;bool wasActive;bool pending;
        using(var q=Cmd(c,tx,"SELECT Role,Active,Pending FROM PresenterAccounts WHERE Id=$id",("$id",target)))
        using(var r=await q.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))throw Denied();currentRole=r.GetString(0);wasActive=r.GetBoolean(1);pending=r.GetBoolean(2);}
        if(active && (pending || Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterKeys WHERE AccountId=$id",ct,("$id",target)))==0))throw Denied();
        if(!active && Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PresenterEventScopes'",ct))!=0
            && Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterEventScopes WHERE OwnerId=$id",ct,("$id",target)))!=0)
            throw new DemoRequestException("EventOwnerTransferRequired");
        if(wasActive && currentRole=="Administrator" && (!active || role!="Administrator") && Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterAccounts a WHERE Active=1 AND Role='Administrator' AND EXISTS(SELECT 1 FROM PresenterKeys k WHERE k.AccountId=a.Id)",ct))<=1)throw new DemoRequestException("PresenterLastAdmin");
        await Exec(c,tx,"UPDATE PresenterAccounts SET Role=$role,Active=$active WHERE Id=$id; DELETE FROM PresenterAuthSessions WHERE AccountId=$id; DELETE FROM PresenterInvitations WHERE AccountId=$id; DELETE FROM PresenterEnrollmentGrants WHERE AccountId=$id; DELETE FROM PresenterCeremonies WHERE AccountId=$id",ct,("$role",role),("$active",active),("$id",target));
        await Audit(c,tx,Now,actor.Id,"AccountUpdated",target,ct);
        if(currentRole!=role) await Audit(c,tx,Now,actor.Id,"RoleChangedTo"+role,target,ct);
        if(wasActive!=active) await Audit(c,tx,Now,actor.Id,active?"AccountEnabled":"AccountDisabled",target,ct);
        tx.Commit();
    }
    public async Task RemoveKeyAsync(ClaimsPrincipal principal,string target,string key,CancellationToken ct)
    {
        await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:false);var actor=await Actor(c,tx,principal,true,true,ct);
        if(Convert.ToInt64(await Scalar(c,tx,"SELECT COUNT(*) FROM PresenterKeys WHERE AccountId=$id",ct,("$id",target)))<=1)throw new DemoRequestException("PresenterLastKey");
        if(await Exec(c,tx,"DELETE FROM PresenterKeys WHERE AccountId=$id AND Ref=$key",ct,("$id",target),("$key",key))!=1)throw Denied();
        await Exec(c,tx,"DELETE FROM PresenterAuthSessions WHERE AccountId=$id",ct,("$id",target));
        await Audit(c,tx,Now,actor.Id,"CredentialRemoved",target,ct);tx.Commit();
    }
    public async Task<IReadOnlyList<PresenterAccount>> AccountsAsync(ClaimsPrincipal principal,CancellationToken ct)
    { await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Actor(c,tx,principal,true,false,ct);var result=new List<PresenterAccount>();using var q=Cmd(c,tx,"SELECT Id,Login,Name,Role,Active,Created,LastLogin FROM PresenterAccounts ORDER BY Created");using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetBoolean(4),r.GetInt64(5),r.IsDBNull(6)?null:r.GetInt64(6)));return result; }
    public async Task<IReadOnlyList<PresenterKey>> KeysAsync(ClaimsPrincipal principal,string? target,CancellationToken ct)
    { await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);var actor=await Actor(c,tx,principal,false,false,ct);target??=actor.Id;if(target!=actor.Id && actor.Role!="Administrator")throw Denied();var result=new List<PresenterKey>();using var q=Cmd(c,tx,"SELECT Ref,Name,Registered,Used FROM PresenterKeys WHERE AccountId=$id ORDER BY Registered",("$id",target));using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3)));return result; }
    public async Task<IReadOnlyList<PresenterAudit>> AuditAsync(ClaimsPrincipal principal,long before,CancellationToken ct)
    { await using var c=await store.OpenAsync(ct);using var tx=c.BeginTransaction(deferred:true);await Actor(c,tx,principal,true,false,ct);var result=new List<PresenterAudit>();using var q=Cmd(c,tx,"SELECT Id,At,Actor,Action,Target FROM PresenterAudit WHERE Id<$before ORDER BY Id DESC LIMIT 200",("$before",before<=0?long.MaxValue:before));using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new(r.GetInt64(0),r.GetInt64(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4)));return result; }

    private static void RequireNone(byte[] bytes)
    { var r=new CborReader(bytes,CborConformanceMode.Strict);r.ReadStartMap();var seen=new HashSet<string>();for(var i=0;i<3;i++){var k=r.ReadTextString();if(!seen.Add(k))throw Denied();switch(k){case "fmt":if(r.ReadTextString()!="none")throw Denied();break;case "authData":r.ReadByteString();break;case "attStmt":r.ReadStartMap();r.ReadEndMap();break;default:throw Denied();}}r.ReadEndMap();if(r.BytesRemaining!=0)throw Denied(); }
}
