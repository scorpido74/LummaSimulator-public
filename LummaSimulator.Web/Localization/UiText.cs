using System.Globalization;
using System.Resources;
using LummaSimulator.Web.Configuration;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

namespace LummaSimulator.Web.Localization;

// One offline resource catalog for Razor, server errors and browser messages.
public sealed class UiText
{
    public static readonly ResourceManager Resources = new("LummaSimulator.Web.Resources.Localization.UiText", typeof(UiText).Assembly);
    public static Dictionary<string, string> Catalog(string language) => Resources.GetResourceSet(
        CultureInfo.GetCultureInfo(language), true, true)!.Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
}

public static class LanguagePreference
{
    public const string VisitorCookie = "__Host-Lumma.VisitorLanguage";
    public const string PresenterCookie = "__Host-Lumma.PresenterLanguage";
    public static string CookieFor(PathString path) => path.StartsWithSegments("/Presenter") || path.StartsWithSegments("/api/presenter") ? PresenterCookie : VisitorCookie;
    public static CookieOptions Options => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict,
        Path = "/", IsEssential = true, MaxAge = TimeSpan.FromDays(180) };
    public static void ResetVisitor(HttpContext context)
    {
        var options = Options;
        options.MaxAge = null;
        context.Response.Cookies.Delete(VisitorCookie, options);
    }
    public static Task<ProviderCultureResult?> Resolve(HttpContext context, ExperienceSettings settings)
    {
        var language = context.Request.Cookies[CookieFor(context.Request.Path)];
        return Task.FromResult<ProviderCultureResult?>(settings.AvailableLanguages.Contains(language)
            ? new ProviderCultureResult(language!) : new ProviderCultureResult(settings.DefaultLanguage));
    }
}
