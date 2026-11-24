using System.Buffers.Binary;
using LummaSimulator.Configuration;
using LummaSimulator.Web.Configuration;
using LummaSimulator.Web.Services;
using Microsoft.Extensions.FileProviders;

namespace LummaSimulator.Web.Tests;
internal static class BrandingChecks
{
    public static void Run(string temporaryRoot)
    {
        var webRoot = Path.Combine(temporaryRoot, "branding-fixture", "wwwroot");
        var logos = Path.Combine(webRoot, "branding"); Directory.CreateDirectory(logos);
        var settings = new ExperienceSettings { OrganizationName = "Synthetic Reseller" };
        foreach (var absentRoot in new string?[] { null, "", " " })
        {
            TestProgram.Check(ResellerBranding.LogoUrl(new ExperienceSettings(), absentRoot) is null, "Shared layout default branding tolerates absent physical web root");
            TestProgram.Check(ResellerBranding.LogoUrl(new ExperienceSettings { LogoFile = "demo.png" }, absentRoot) is null, "Custom branding with absent root uses text fallback without Path.Combine exception");
        }
        Directory.CreateDirectory(Path.Combine(webRoot, "images"));
        File.WriteAllText(Path.Combine(webRoot, "images", "infinigate-official.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        using (var provider = new PhysicalFileProvider(webRoot))
            TestProgram.Check(ResellerBranding.LogoUrl(new ExperienceSettings(), null, provider) == "~/images/infinigate-official.svg", "Provider-backed static asset remains available when WebRootPath is null");
        TestProgram.Check(ResellerBranding.LogoUrl(settings, webRoot) is null, "Reseller without local logo uses text fallback");
        settings.LogoFile = "demo.png";
        foreach (var candidate in new[] { "../demo.png", "https://example.invalid/logo.png", @"\\host\logo.png", "demo.svg", "demo.png?x=1", "missing.png" })
            TestProgram.Check(BrandingAssets.ResolveLogo(logos, candidate) is null, "Untrusted or missing logo path is rejected without remote loading");
        var header = new byte[24];
        new byte[] {137,80,78,71,13,10,26,10}.CopyTo(header,0); "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16),100);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20),30);
        File.WriteAllBytes(Path.Combine(logos,"demo.png"),header);
        TestProgram.Check(ResellerBranding.LogoUrl(settings, webRoot) == "~/branding/demo.png", "Local bounded PNG header selects fixed branding URL (decoder tested separately)");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16),5000); File.WriteAllBytes(Path.Combine(logos,"demo.png"),header);
        TestProgram.Check(ResellerBranding.LogoUrl(settings, webRoot) is null, "Oversized image dimensions fail to text fallback");
        File.WriteAllText(Path.Combine(logos,"demo.png"),"not an image");
        TestProgram.Check(ResellerBranding.LogoUrl(settings, webRoot) is null, "Invalid image header fails to text fallback");
        settings.OrganizationName = "Infinigate"; settings.LogoFile = null;
        TestProgram.Check(ResellerBranding.LogoUrl(settings, Path.Combine(temporaryRoot, "missing-web-root")) is null, "Missing default artwork retains organisation text fallback");
    }
}
