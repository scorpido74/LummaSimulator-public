using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace LummaSimulator.Helper;

public sealed class HelperProfile
{
    public int Version { get; set; } = 1;
    public string PortalOrigin { get; set; } = "https://localhost";
    public string AppOrigin { get; set; } = "";
    public string CertificateSha256 { get; set; } = "";
    public string PublisherSha256 { get; set; } = "";
}

[SupportedOSPlatform("windows")]
public sealed class HelperUserStore
{
    public string Root { get; }
    public HelperUserStore(string? root = null) => Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LummaSimulator", "PresenterHelper"));
    public HelperProfile LoadProfile() {
        if (Directory.Exists(Root)) VerifyPrivate(Root, true);
        var path = Path.Combine(Root, "helper.settings.json");
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException();
        var value = JsonSerializer.Deserialize<HelperProfile>(File.ReadAllBytes(path), HelperProtocol.Json) ?? throw new InvalidDataException();
        if (value.Version != 1) throw new InvalidDataException("Unsupported settings version.");
        HelperProtocol.Origin(value.PortalOrigin); if (value.AppOrigin.Length != 0) HelperProtocol.Origin(value.AppOrigin); return value;
    }
    public void SaveProfile(HelperProfile profile) { EnsureRoot(); Atomic(Path.Combine(Root, "helper.settings.json"), JsonSerializer.SerializeToUtf8Bytes(profile, HelperProtocol.Json)); }
    public string? LoadPairing() => LoadToken("helper-pairing.dpapi");
    public string InstallationIdentity() {
        var existing=LoadToken("helper-installation.dpapi");
        if(existing is not null)return existing;
        var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
        SaveToken("helper-installation.dpapi",token);
        return token;
    }
    private string? LoadToken(string fileName) {
        if (Directory.Exists(Root)) VerifyPrivate(Root, true);
        var path = Path.Combine(Root, fileName); if (!File.Exists(path)) return null;
        VerifyPrivate(path, false);
        var encrypted = File.ReadAllBytes(path); if (encrypted.Length > 16384) throw new InvalidDataException();
        var clear = Transform(encrypted, false);
        try { var token = System.Text.Encoding.UTF8.GetString(clear); return HelperProtocol.IsToken(token) ? token : throw new InvalidDataException(); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void SavePairing(string token) => SaveToken("helper-pairing.dpapi",token);
    private void SaveToken(string fileName,string token) {
        if (!HelperProtocol.IsToken(token)) throw new InvalidDataException(); EnsureRoot();
        var clear = System.Text.Encoding.UTF8.GetBytes(token);
        try { Atomic(Path.Combine(Root, fileName), Transform(clear, true)); } finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void EnsureRoot() {
        RejectReparse(Root); if (Directory.Exists(Root)) { VerifyPrivate(Root, true); return; }
        Directory.CreateDirectory(Root); var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException();
        var acl = new DirectorySecurity(); acl.SetOwner(sid); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Root).SetAccessControl(acl);
    }
    public static void VerifyPrivate(string path, bool directory) {
        RejectReparse(path); var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException();
        FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        if (!sid.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("Private helper storage owner mismatch.");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !sid.Equals(rule.IdentityReference)) throw new IOException("Private helper storage permissions mismatch.");
    }
    public static void RejectReparse(string path) {
        path = Path.GetFullPath(path); if (new Uri(path).IsUnc || new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) throw new IOException("Local fixed disk required.");
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse point rejected.");
    }
    public static void Atomic(string path, byte[] bytes) {
        RejectReparse(path); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private static byte[] Transform(byte[] bytes, bool protect) {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
}
