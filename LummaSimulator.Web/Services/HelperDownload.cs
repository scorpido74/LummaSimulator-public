using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Services;

public sealed record HelperPackage(string Path,string FileName,string Version,string Sha256,string? CertificateThumbprint);

public sealed class HelperDownload(IWebHostEnvironment environment,IConfiguration configuration)
{
    private readonly object gate=new();
    private (string Path,long Length,DateTime Modified)? fingerprint;
    private HelperPackage? cached;

    public HelperPackage? Current()
    {
        if(!configuration.GetValue<bool>("Helper:Enabled"))return null;
        lock(gate)
        {
            try
            {
                var directory=configuration["Helper:PackageDirectory"];
                if(string.IsNullOrWhiteSpace(directory))directory=Path.Combine(environment.WebRootPath,"downloads");
                if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))return null;
                var packages=new DirectoryInfo(directory).GetFiles("LummaSimulator-PresenterHelper-v*-win-x64.zip");
                if(packages.Length!=1)return null;
                var file=packages[0];
                var current=(file.FullName,file.Length,file.LastWriteTimeUtc);
                if(fingerprint==current)return cached;
                using var stream=file.OpenRead();
                using var zip=new ZipArchive(stream,ZipArchiveMode.Read,true);
                var version=Regex.Match(file.Name,@"^LummaSimulator-PresenterHelper-v([0-9][a-zA-Z0-9.-]{0,40})-win-x64\.zip$").Groups[1].Value;
                var versionEntry=zip.GetEntry("HELPER-VERSION.txt");
                if(versionEntry is {Length:>0 and <=64})
                {
                    using var reader=new StreamReader(versionEntry.Open());
                    version=reader.ReadToEnd().Trim();
                }
                if(!Regex.IsMatch(version,@"^[0-9][a-zA-Z0-9.-]{0,40}$"))return null;
                string? thumbprint=null;
                if(zip.GetEntry("Portal.cer") is {Length:>0 and <=16384} entry)
                {
                    using var bytes=new MemoryStream();
                    using var certificateFile=entry.Open();
                    certificateFile.CopyTo(bytes);
                    using var certificate=X509CertificateLoader.LoadCertificate(bytes.ToArray());
                    thumbprint=certificate.Thumbprint;
                }
                stream.Position=0;
                cached=new(file.FullName,file.Name,version,Convert.ToHexString(SHA256.HashData(stream)),thumbprint);
                fingerprint=current;
                return cached;
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
        }
    }
}
