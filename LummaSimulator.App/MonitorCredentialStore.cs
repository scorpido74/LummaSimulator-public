using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LummaSimulator.Configuration;

namespace LummaSimulator.App.Services;
public sealed class MonitorCredential
{
    public int Version { get; set; } = 1;
    public string Origin { get; set; } = "";
    public string Key { get; set; } = "";
}
public sealed class MonitorCredentialStore(string directory)
{
    public static MonitorCredentialStore ForSettings(DesktopSettings settings)
    {
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RuntimePaths.DataRoot(settings.DataDirectory).ToUpperInvariant())));
        return new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LummaSimulator", "MonitorCredentials", scope));
    }
    private string Root => UserPrivateFiles.LocalPath(directory);
    private string FilePath => Path.Combine(Root, "monitor-credential.dpapi");
    public static bool ValidKey(string? key) => key is { Length: 43 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    public MonitorCredential? Load()
    {
        var root = Root; if (!Directory.Exists(root)) return null;
        UserPrivateFiles.Verify(root, true);
        if (!File.Exists(FilePath)) return null;
        UserPrivateFiles.Verify(FilePath, false);
        if (new FileInfo(FilePath).Length > 16384) throw new IOException("MonitorCredentialInvalid");
        var clear = Dpapi.Transform(File.ReadAllBytes(FilePath), false);
        try {
            var item = JsonSerializer.Deserialize<MonitorCredential>(clear) ?? throw new IOException("MonitorCredentialInvalid");
            if (item.Version != 1 || !ValidKey(item.Key)) throw new IOException("MonitorCredentialInvalid");
            SessionMetadataClient.ValidateOrigin(item.Origin); return item;
        } finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(string origin, string key)
    {
        if (!ValidKey(key)) throw new IOException("MonitorCredentialInvalid");
        SessionMetadataClient.ValidateOrigin(origin);
        UserPrivateFiles.EnsureDirectory(Root);
        if (File.Exists(FilePath)) UserPrivateFiles.Verify(FilePath, false);
        var clear = JsonSerializer.SerializeToUtf8Bytes(new MonitorCredential { Origin = origin, Key = key });
        byte[] encrypted;
        try { encrypted = Dpapi.Transform(clear, true); } finally { CryptographicOperations.ZeroMemory(clear); }
        var pending = Path.Combine(Root, "monitor-credential." + Guid.NewGuid().ToString("N") + ".pending");
        using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { output.Write(encrypted); output.Flush(true); }
        UserPrivateFiles.Verify(pending, false);
        File.Move(pending, FilePath, true);
    }
    public void Remove()
    {
        if (!Directory.Exists(Root)) return;
        UserPrivateFiles.Verify(Root, true);
        if (!File.Exists(FilePath)) return;
        UserPrivateFiles.Verify(FilePath, false); File.Delete(FilePath);
    }
    // DPAPI CurrentUser, UI forbidden. Native wrapper avoids a new package in linked-source tests.
    private static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        public static byte[] Transform(byte[] bytes, bool protect)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
            try {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                var success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success || output.Length is < 1 or > 16384) throw new CryptographicException("MonitorCredentialProtection");
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            } finally {
                for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
            }
        }
    }
}
