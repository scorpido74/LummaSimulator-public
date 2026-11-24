using LummaSimulator.Configuration;
using LummaSimulator.Web.Configuration;
using Microsoft.Extensions.FileProviders;
namespace LummaSimulator.Web.Services;

public static class ResellerBranding
{
    public static string? LogoUrl(ExperienceSettings settings, string? webRoot, IFileProvider? assets = null)
    {
        if (!string.IsNullOrEmpty(settings.LogoFile))
            return !string.IsNullOrWhiteSpace(webRoot) && BrandingAssets.ResolveLogo(Path.Combine(webRoot, "branding"), settings.LogoFile) is not null
                ? "~/branding/" + settings.LogoFile : null;
        // Development static-web-assets can exist through a provider with no physical WebRootPath.
        var defaultLogoExists = settings.OrganizationName == "Infinigate" && (assets is not null
            ? assets.GetFileInfo("images/infinigate-official.svg").Exists
            : !string.IsNullOrWhiteSpace(webRoot) && File.Exists(Path.Combine(webRoot, "images", "infinigate-official.svg")));
        return defaultLogoExists
            ? "~/images/infinigate-official.svg" : null;
    }
}
