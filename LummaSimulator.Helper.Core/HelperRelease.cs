using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LummaSimulator.Helper;

public sealed record SignedHelperRelease(int Schema, string Payload, string Signature, string Certificate);
public sealed record HelperRelease(string Version, int[] Protocols, long Size, string Sha256, string UpdatePath, string PackageSignature, Dictionary<string, string> Files);

public sealed class HelperReleaseVerifier
{
    private readonly string _publisher;
    private readonly Func<X509Certificate2, bool> _trusted;
    public HelperReleaseVerifier(string publisher) : this(publisher, TrustedPublisher) { }
    internal HelperReleaseVerifier(string publisher, Func<X509Certificate2, bool> trusted) {
        if (!HelperProtocol.IsHash(publisher)) throw new InvalidDataException("No trusted update publisher configured."); _publisher = publisher; _trusted = trusted;
    }
    private static bool TrustedPublisher(X509Certificate2 certificate) {
        using var chain = new X509Chain(); chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.3"));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online; chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10); return chain.Build(certificate);
    }
    public HelperRelease Verify(SignedHelperRelease envelope, Version current, Version? highest = null) {
        if (envelope.Schema != 1 || envelope.Payload.Length > 1572864 || envelope.Certificate.Length > 16384 || envelope.Signature.Length > 16384) throw new InvalidDataException("Invalid release envelope.");
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(envelope.Certificate));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.RawData), Convert.FromHexString(_publisher)) || !_trusted(certificate)) throw new CryptographicException("Untrusted update publisher.");
        using var rsa = certificate.GetRSAPublicKey() ?? throw new CryptographicException("RSA signing certificate required.");
        if (rsa.KeySize < 2048) throw new CryptographicException("Signing key too small.");
        var payload = Convert.FromBase64String(envelope.Payload);
        if (!rsa.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException("Release signature invalid.");
        var release = JsonSerializer.Deserialize<HelperRelease>(payload, HelperProtocol.Json) ?? throw new InvalidDataException();
        if (!Version.TryParse(release.Version, out var target) || target <= current || highest is not null && target < highest) throw new InvalidDataException("Older or repeated release rejected.");
        if (release.Protocols is null || release.Protocols.Length is < 1 or > 8 || !release.Protocols.Contains(HelperProtocol.Version)) throw new InvalidDataException("Helper protocol unsupported.");
        if (release.Size is < 1 or > 268435456 || !HelperProtocol.IsHash(release.Sha256) || release.UpdatePath != "/lumma-helper/updates/v1/package.zip"
            || release.PackageSignature.Length > 16384 || release.Files is null || release.Files.Count is < 2 or > 4096) throw new InvalidDataException("Invalid release metadata.");
        foreach (var (path, hash) in release.Files) if (!SafePath(path) || !HelperProtocol.IsHash(hash)) throw new InvalidDataException("Invalid package file.");
        if (release.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != release.Files.Count
            || !release.Files.ContainsKey("LummaSimulator.PresenterHelper.exe") || !release.Files.ContainsKey("LummaSimulator.HelperUpdater.exe")) throw new InvalidDataException("Incomplete helper package.");
        return release;
    }
    public void VerifyPackage(SignedHelperRelease envelope, HelperRelease release, string package) {
        using var input = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != release.Size) throw new InvalidDataException("Package size mismatch.");
        var hash = SHA256.HashData(input);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(release.Sha256))) throw new CryptographicException("Package hash mismatch.");
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(envelope.Certificate));
        using var rsa = certificate.GetRSAPublicKey() ?? throw new CryptographicException();
        if (!rsa.VerifyHash(hash, Convert.FromBase64String(release.PackageSignature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new CryptographicException("Package signature mismatch.");
    }
    public static bool SafePath(string path) => path.Length is > 0 and < 180 && !path.Contains('\\') && !path.Contains(':') && !path.StartsWith('/')
        && path.Split('/').All(p => p.Length > 0 && p is not "." and not ".." && !p.EndsWith('.') && !p.EndsWith(' ')
            && !p.Any(c => c < 32 || c is '<' or '>' or '"' or '|' or '?' or '*') && !new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase));
}
