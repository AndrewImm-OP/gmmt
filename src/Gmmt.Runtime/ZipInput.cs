using System.IO.Compression;

namespace Gmmt.Runtime;

public static class ZipInput
{
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    public static void Validate(ZipArchive archive)
    {
        if (archive.Entries.Count > 8192 || archive.Entries.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("ZIP exceeds extraction limits.");
        foreach (var entry in archive.Entries) ValidateEntry(entry);
    }
    private static void ValidateEntry(ZipArchiveEntry entry)
    {
        var name = entry.FullName;
        if (entry.Length > MaxEntryBytes || name.StartsWith('/') || name.Contains('\\') || name.Contains(':')
            || name.Split('/').Any(part => part == "..") || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            throw new InvalidDataException("Unsafe or oversized ZIP entry: " + name);
    }
    public static string Extract(ZipArchiveEntry entry, string directory)
    {
        ValidateEntry(entry);
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("ZIP entry escapes extraction directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = Path.GetDirectoryName(path);
        while (current != null && current.StartsWith(root, StringComparison.Ordinal))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("ZIP extraction directory contains a symlink.");
            if (current == root) break;
            current = Path.GetDirectoryName(current);
        }
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("ZIP extraction target is a symlink.");
        entry.ExtractToFile(path, true);
        return path;
    }
}
