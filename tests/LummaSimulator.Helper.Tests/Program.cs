using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using LummaSimulator.Helper;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS: " + message); }
void Reject(Action action, string message) { var rejected = false; try { action(); } catch (Exception e) when (e is CryptographicException or InvalidDataException or FormatException or JsonException) { rejected = true; } Check(rejected, message); }
var root = Path.Combine(Path.GetTempPath(), "LummaHelperTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
using var rsa = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=Synthetic helper publisher", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
certificateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, false));
using var cert = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
var publisher = Convert.ToHexString(SHA256.HashData(cert.RawData));
// Only the test assembly can inject a synthetic trust policy. Production always validates the OS chain and code-signing EKU.
var verifier = new HelperReleaseVerifier(publisher, c => c.Thumbprint == cert.Thumbprint);
var package = Path.Combine(root, "synthetic.zip");
var bytes = Encoding.UTF8.GetBytes("Synthetic helper payload; never executed.");
var files = new Dictionary<string, string>();
using (var archive = ZipFile.Open(package, ZipArchiveMode.Create)) foreach (var file in new[] { "LummaSimulator.PresenterHelper.exe", "LummaSimulator.HelperUpdater.exe" }) {
    using var output = archive.CreateEntry(file).Open(); output.Write(bytes); files.Add(file, Convert.ToHexString(SHA256.HashData(bytes)));
}
var packageHash = SHA256.HashData(File.ReadAllBytes(package));
var release = new HelperRelease("0.2.0", [1], new FileInfo(package).Length, Convert.ToHexString(packageHash), "/lumma-helper/updates/v1/package.zip",
    Convert.ToBase64String(rsa.SignHash(packageHash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), files);
SignedHelperRelease Sign(HelperRelease item) { var payload = JsonSerializer.SerializeToUtf8Bytes(item, HelperProtocol.Json); return new(1, Convert.ToBase64String(payload), Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), Convert.ToBase64String(cert.RawData)); }
var envelope = Sign(release);
var valid = verifier.Verify(envelope, new(0, 1, 0)); verifier.VerifyPackage(envelope, valid, package); Check(valid.Version == "0.2.0", "Trusted manifest and separate package signature/hash verified");
Reject(() => verifier.Verify(envelope with { Signature = Convert.ToBase64String(new byte[256]) }, new(0, 1, 0)), "Invalid manifest signature rejected");
Reject(() => new HelperReleaseVerifier(new string('A', 64), _ => true).Verify(envelope, new(0, 1, 0)), "Wrong trusted publisher rejected");
Reject(() => new HelperReleaseVerifier(publisher, _ => false).Verify(envelope, new(0, 1, 0)), "Invalid certificate chain/EKU policy rejected");
Reject(() => verifier.Verify(envelope, new(0, 2, 0)), "Repeated version rejected");
Reject(() => verifier.Verify(envelope, new(0, 1, 0), new(0, 3, 0)), "Version below accepted high-water mark rejected");
Reject(() => verifier.Verify(Sign(release with { Protocols = [2] }), new(0, 1, 0)), "Incompatible helper protocol rejected before extraction");
Reject(() => verifier.Verify(Sign(release with { UpdatePath = "https://other.invalid/evil.zip" }), new(0, 1, 0)), "Update redirect or foreign update path rejected");
var changed = Path.Combine(root, "changed.zip"); File.Copy(package, changed); using (var output = new FileStream(changed, FileMode.Append)) output.WriteByte(0);
Reject(() => verifier.VerifyPackage(envelope, valid, changed), "Changed package size rejected");
var wrongHash = release with { Sha256 = new string('A', 64) };
Reject(() => verifier.VerifyPackage(Sign(wrongHash), wrongHash, package), "Changed package SHA-256 rejected");
var wrongSignature = release with { PackageSignature = Convert.ToBase64String(new byte[256]) };
Reject(() => verifier.VerifyPackage(Sign(wrongSignature), wrongSignature, package), "Invalid package signature rejected independently of manifest signature");
foreach (var path in new[] { "../escape.exe", "/absolute", "C:/escape", "a\\b", "con.txt", "folder/../escape", "a:stream", "file. ", "NUL", "folder//file" })
    Check(!HelperReleaseVerifier.SafePath(path), "Unsafe package path rejected: " + path);

var install = Path.Combine(root, "install"); Directory.CreateDirectory(Path.Combine(install, "current")); File.WriteAllText(Path.Combine(install, "current", "old.txt"), "old");
var tx = new HelperUpdateTransaction(install);
var prepared = tx.Prepare(package, release, envelope); Check(File.Exists(Path.Combine(install, "current", "old.txt")), "Preparing update leaves working installation untouched");
tx.Activate(prepared); Check(!File.Exists(Path.Combine(install, "current", "old.txt")), "Activation installs the verified staging directory");
tx.Recover(); Check(File.ReadAllText(Path.Combine(install, "current", "old.txt")) == "old" && tx.Load()?.Phase == "RolledBack", "Interrupted startup rolls back and preserves the previous working payload");
tx.Recover(); Check(File.Exists(Path.Combine(install, "current", "old.txt")), "Repeated recovery is idempotent");
prepared = tx.Prepare(package, release, envelope); tx.Activate(prepared); tx.Commit(prepared); tx.Recover();
Check(tx.Load()?.Phase == "Committed" && tx.HighestVersion() == new Version(0, 2, 0), "Health-confirmed commit retains current payload and the version high-water mark");
var powerRoot = Path.Combine(root, "power"); Directory.CreateDirectory(Path.Combine(powerRoot, "current")); File.WriteAllText(Path.Combine(powerRoot, "current", "old.txt"), "old");
var power = new HelperUpdateTransaction(powerRoot); var state = power.Prepare(package, release, envelope);
Directory.Move(Path.Combine(powerRoot, "current"), Path.Combine(powerRoot, state.Backup));
HelperUserStore.Atomic(Path.Combine(powerRoot, "helper-update-journal.json"), JsonSerializer.SerializeToUtf8Bytes(state with { Phase = "Activating" }, HelperProtocol.Json));
power.Recover(); Check(File.Exists(Path.Combine(powerRoot, "current", "old.txt")), "Power loss between directory moves restores the working version");
var responses = new Queue<HttpStatusCode>([HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden]);
using var receiver = new AppReceiverClient("https://127.0.0.1", new string('A', 64), new string('B', 43), new Handler(request => {
    Check(request.Headers.GetValues("X-Lumma-Helper-Protocol").Single() == "1" && !request.Headers.Contains("Cookie"), "Receiver uses explicit protocol and separate pairing, never presenter/visitor cookies");
    return new(responses.Dequeue());
}));
foreach (var expected in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.Forbidden }) {
    var rejected = false; try { await receiver.InfoAsync(default); } catch (HttpRequestException e) when (e.StatusCode == expected) { rejected = true; }
    Check(rejected, "Unreachable receiver or revoked pairing cannot start a protected action");
}
var qrDisplay=new HelperQrDisplay();
var eventA=Guid.NewGuid().ToString("D");var eventB=Guid.NewGuid().ToString("D");
var allowedEvents=new LummaSimulator.App.Services.RoundEvent[]{new(eventA,"Synthetic A"),new(eventB,"Synthetic B")};
ReceiverQrState Selection(string id,string origin="https://portal.example.test")=>new("Ready",origin,id,"Untrusted display name",LummaSimulator.Shared.ParticipantLink.EventPath(id));
Check(qrDisplay.Apply("https://portal.example.test",Selection(eventA),allowedEvents)&&qrDisplay.EventName=="Synthetic A"&&qrDisplay.Link!.AbsoluteUri.EndsWith(eventA),"QR uses the WPF event identity, authorized central name and exact public admission URL");
Check(qrDisplay.Apply("https://portal.example.test",Selection(eventB),allowedEvents)&&qrDisplay.EventName=="Synthetic B"&&qrDisplay.Link!.AbsoluteUri.EndsWith(eventB),"WPF selection change replaces the QR payload");
Reject(()=>qrDisplay.Apply("https://portal.example.test",Selection(eventB),[]),"Closed or inaccessible event cannot retain its QR");
Check(qrDisplay.Link is null&&qrDisplay.Code is null,"Rejected event immediately clears previous QR state");
Reject(()=>qrDisplay.Apply("https://portal.example.test",Selection(eventA,"https://other.invalid"),allowedEvents),"Foreign portal origin cannot replace the invitation");
Check(qrDisplay.Apply("https://localhost:7243",Selection(eventA,"https://localhost:7243"),allowedEvents)&&qrDisplay.LaptopOnly,"Loopback invitations are explicitly identified as laptop-only");
Check(!qrDisplay.Apply("https://localhost:7243",new("Unavailable"),allowedEvents)&&qrDisplay.Link is null,"Disconnected WPF selection removes the QR");
Console.WriteLine($"HELPER_CHECKS={checks} ROOT={root}");
Console.WriteLine("STANDARD_USER=" + !new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));

internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(callback(request));
}
