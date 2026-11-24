using System.Globalization;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Configuration;

public sealed class ExperienceSettings
{
    public string OrganizationName { get; set; } = "Infinigate";
    public string? LogoFile { get; set; }
    public string VisitorPrefix { get; set; } = "Visitor";
    public string DefaultLanguage { get; set; } = "nl-NL";
    public string[] AvailableLanguages { get; set; } = ["nl-NL", "en-GB"];
    public int AccountIdleMinutes { get; set; } = 29;
    public string Hostname { get; set; } = "";
    public bool StartCodesRequired { get; set; }
    public int StartCodeLifetimeMinutes { get; set; } = 10;
    public bool PrizeEnabledByDefault { get; set; }

    public void Validate(WebAuthnSettings webAuthn)
    {
        if (string.IsNullOrWhiteSpace(OrganizationName) || OrganizationName.Length > 80
            || !Regex.IsMatch(VisitorPrefix, "\\A[A-Za-z0-9]{1,32}\\z"))
            throw new InvalidOperationException("Configure a valid organization and alphanumeric visitor prefix.");
        if (AvailableLanguages.Length == 0 || AvailableLanguages.Distinct().Count() != AvailableLanguages.Length
            || AvailableLanguages.Any(c => c is not ("nl-NL" or "en-GB")) || !AvailableLanguages.Contains(DefaultLanguage))
            throw new InvalidOperationException("Select only implemented languages and an available default language.");
        foreach (var language in AvailableLanguages) _ = CultureInfo.GetCultureInfo(language);
        if (AccountIdleMinutes is < 1 or > 29 || StartCodeLifetimeMinutes is < 1 or > 30)
            throw new InvalidOperationException("Account idle lifetime must be 1–29 minutes; start codes 1–30 minutes.");
        if (!string.IsNullOrEmpty(Hostname) && Hostname != webAuthn.RpId)
            throw new InvalidOperationException("Hostname must exactly match the explicitly configured WebAuthn RP ID.");
        // Admission and prize transactions belong to the next development phase.
        // Fail closed rather than silently enable an unimplemented security policy.
        if (StartCodesRequired || PrizeEnabledByDefault)
            throw new InvalidOperationException("Start codes and Prize Experience cannot be enabled before their implementation and validation.");
    }
}
