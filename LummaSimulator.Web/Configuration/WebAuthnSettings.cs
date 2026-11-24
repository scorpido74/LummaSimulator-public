using Fido2NetLib;

namespace LummaSimulator.Web.Configuration;

public sealed class WebAuthnSettings
{
    public const string DisplayName = "Infinigate | Yubico Passkey Demo";
    public string RpId { get; set; } = "";
    public string[] Origins { get; set; } = [];
    public string? VisitorBaseUrl {get;set;}
    public Uri VisitorLink(string id,string requestPathBase="",string kind="prize")
    {
        if(string.IsNullOrEmpty(VisitorBaseUrl))return LummaSimulator.Shared.ParticipantLink.ForEvent(Origins[0],id,requestPathBase,kind);
        var configured=new Uri(VisitorBaseUrl);
        return LummaSimulator.Shared.ParticipantLink.ForEvent(configured.GetLeftPart(UriPartial.Authority),id,configured.AbsolutePath.TrimEnd('/'),kind);
    }

    public void Validate(bool development, string? configuredHost = null)
    {
        var expectedHost = string.IsNullOrEmpty(configuredHost) ? (development ? "localhost" : "portal.example.test") : configuredHost;
        if (Uri.CheckHostName(expectedHost) != UriHostNameType.Dns || expectedHost != expectedHost.ToLowerInvariant()
            || expectedHost.EndsWith('.') || expectedHost.Contains('*'))
            throw new InvalidOperationException("Configure a canonical DNS hostname, not a URL or wildcard.");
        if (RpId != expectedHost || Origins.Length == 0 || Origins.Length > 8)
            throw new InvalidOperationException("Configure the environment-specific WebAuthn RP ID and explicit HTTPS origins.");
        foreach (var origin in Origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || uri.Host != RpId || uri.UserInfo.Length != 0 || uri.Query.Length != 0
                || uri.Fragment.Length != 0 || uri.AbsolutePath != "/"
                || origin != uri.GetLeftPart(UriPartial.Authority)
                || (!development && !uri.IsDefaultPort))
                throw new InvalidOperationException("WebAuthn origins must be exact canonical HTTPS origins matching the configured RP ID; wildcards are forbidden.");
        }
        if (!string.IsNullOrEmpty(VisitorBaseUrl) &&
            (!Uri.TryCreate(VisitorBaseUrl,UriKind.Absolute,out var visitor) || visitor.Scheme!="https"
             || visitor.UserInfo.Length!=0 || visitor.Query.Length!=0 || visitor.Fragment.Length!=0
             || !Origins.Contains(visitor.GetLeftPart(UriPartial.Authority),StringComparer.Ordinal)
             || visitor.AbsolutePath.IndexOfAny(['?','#','\\'])>=0))
            throw new InvalidOperationException("VisitorBaseUrl must use an existing allowed HTTPS origin and an optional path prefix; RP-ID and origins are not migrated.");
        if (Origins.Distinct(StringComparer.Ordinal).Count() != Origins.Length)
            throw new InvalidOperationException("Duplicate WebAuthn origins are not allowed.");
    }

    public Fido2Configuration ToFidoConfiguration() => new()
    {
        RPID = RpId,
        RPName = DisplayName,
        Origins = Origins.ToHashSet(StringComparer.Ordinal),
        ChallengeSize = 32,
        Timeout = 120000,
        AllowCrossOriginRequests = false
    };
}
