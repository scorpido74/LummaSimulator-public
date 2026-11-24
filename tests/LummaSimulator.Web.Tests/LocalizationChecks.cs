using System.Globalization;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Localization;
using LummaSimulator.Web.Services;
using Fido2NetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace LummaSimulator.Web.Tests;

internal static class LocalizationChecks
{
    public static async Task RunAsync(string root)
    {
        var nl = UiText.Catalog("nl-NL");
        var en = UiText.Catalog("en-GB");
        TestProgram.Check(nl.Keys.ToHashSet().SetEquals(en.Keys) && nl.Count >= 190,
            "Offline resource catalogs contain identical complete key sets");
        TestProgram.Check(en["Je bent veilig aangemeld!"] == "You have signed in securely!"
            && nl["Je bent veilig aangemeld!"] == "Je bent veilig aangemeld!", "Both offline resource cultures resolve actual translations");
        var settings = new ExperienceSettings();
        var web = new WebAuthnSettings { RpId = "localhost", Origins = ["https://localhost:7243"] };
        settings.Validate(web);
        TestProgram.Check(settings.DefaultLanguage == "nl-NL" && !settings.StartCodesRequired && !settings.PrizeEnabledByDefault,
            "Defaults remain Dutch, public demo, prize disabled");
        foreach (var invalid in new ExperienceSettings[] {
            new() { DefaultLanguage = "fr-FR" }, new() { AvailableLanguages = ["nl-NL", "fr-FR"] },
            new() { Hostname = "other.invalid" }, new() { VisitorPrefix = "bad prefix" },
            new() { AccountIdleMinutes = 30 }, new() { StartCodesRequired = true }, new() { PrizeEnabledByDefault = true } })
        {
            var rejected = false;
            try { invalid.Validate(web); } catch (InvalidOperationException) { rejected = true; }
            TestProgram.Check(rejected, "Invalid or not-yet-implemented experience configuration fails closed");
        }
        new WebAuthnSettings { RpId = "demo.example", Origins = ["https://demo.example"] }.Validate(false, "demo.example");
        TestProgram.Check(true, "Explicit custom hostname and matching RP ID/origin accepted without automatic RP changes");
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{LanguagePreference.VisitorCookie}=en-GB; {LanguagePreference.PresenterCookie}=nl-NL";
        context.Request.Path = "/Demo";
        var visitor = await LanguagePreference.Resolve(context, settings);
        context.Request.Path = "/Presenter/Login";
        var presenter = await LanguagePreference.Resolve(context, settings);
        TestProgram.Check(visitor!.UICultures[0].Value == "en-GB" && presenter!.UICultures[0].Value == "nl-NL",
            "Visitor and presenter preferences use separate browser cookies");
        using var store = new DemoStore(Path.Combine(root, "configuration.db"));
        await store.InitializeAsync();
        var clock = new TestClock();
        var service = new DemoService(store, new Fido2(web.ToFidoConfiguration()), clock,
            NullLogger<DemoService>.Instance, settings: new ExperienceSettings { VisitorPrefix = "Example", OrganizationName = "Example Org", AccountIdleMinutes = 5 });
        var demo = await service.CreateAsync("yubikey", default);
        TestProgram.Check(System.Text.RegularExpressions.Regex.IsMatch(demo.Account.Name,"^[A-Z][a-z]+[0-9]*$") && demo.Account.UserHandle.Length == 32,
            "Synthetic word alias replaces technical display prefix without changing user-handle format");
        var operation = Guid.NewGuid().ToString("D");
        var options = await service.OptionsAsync(demo.Token, true, default, operation);
        using (var parsed = System.Text.Json.JsonDocument.Parse(options))
            TestProgram.Check(parsed.RootElement.GetProperty("user").GetProperty("displayName").GetString()==demo.Account.Name&&parsed.RootElement.GetProperty("user").GetProperty("name").GetString()==demo.Account.Name,
                "New registration offers the same alias as both visible account names without changing RP ID");
        clock.Advance(TimeSpan.FromMinutes(6));
        TestProgram.Check(await service.GetAsync(demo.Token, false, default) is null, "Configured shorter account lifetime is enforced");
    }
}
