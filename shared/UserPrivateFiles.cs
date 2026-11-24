using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LummaSimulator.Configuration;
public static class UserPrivateFiles
{
    public static string LocalPath(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full)!;
        if (new Uri(full).IsUnc || new DriveInfo(root).DriveType != DriveType.Fixed) throw new IOException("PrivateStoragePath");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("PrivateStoragePath");
        }
        return full;
    }
    public static void EnsureDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        path = LocalPath(path);
        if (!Directory.Exists(path)) {
            Directory.CreateDirectory(path);
            using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User ?? throw new IOException("PrivateStorageIdentity");
            var security = new DirectorySecurity(); security.SetOwner(sid); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        Verify(path, true);
    }
    public static void Verify(string path, bool directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        path = LocalPath(path);
        using var identity = WindowsIdentity.GetCurrent(); var sid = identity.User ?? throw new IOException("PrivateStorageIdentity");
        FileSystemSecurity acl = directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
        if (!sid.Equals(acl.GetOwner(typeof(SecurityIdentifier)))) throw new IOException("PrivateStorageOwner");
        var allowed = false;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier))) {
            if (rule.AccessControlType != AccessControlType.Allow) continue;
            if (!sid.Equals(rule.IdentityReference)) throw new IOException("PrivateStoragePermissions");
            allowed = true;
        }
        if (!allowed) throw new IOException("PrivateStoragePermissions");
    }
}
