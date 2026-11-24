using System.Buffers.Binary;
using System.IO;

namespace LummaSimulator.Configuration;

/// <summary>Only bounded, local PNG artwork. Never resolve URLs, shares or arbitrary paths.</summary>
public static class BrandingAssets
{
    public static string? ResolveLogo(string directory, string? file)
    {
        if (string.IsNullOrEmpty(file) || file.Length > 100 || file.Contains("..", StringComparison.Ordinal)
            || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || file.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))) return null;
        try
        {
            var root = Path.GetFullPath(directory);
            if (new Uri(root).IsUnc) return null;
            var driveRoot = Path.GetPathRoot(root)!;
            if (new DriveInfo(driveRoot).DriveType != DriveType.Fixed) return null;
            var current = driveRoot;
            foreach (var component in root[driveRoot.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, component);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            var path = Path.Combine(root, file);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 24 or > 2_097_152 || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            using var input = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24]; input.ReadExactly(header);
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
            return header[..8].SequenceEqual(signature) && header[12..16].SequenceEqual("IHDR"u8)
                && width is > 0 and <= 4096 && height is > 0 and <= 4096 ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
