using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gmmt.Core.Loading;

namespace Gmmt.VanillaLibrary;

public sealed class VanillaLibraryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _libraryPath;
    private readonly string _indexPath;
    private VanillaIndex _index;

    public string LibraryPath => _libraryPath;

    public VanillaLibraryManager(string? libraryPath = null)
    {
        _libraryPath = libraryPath ?? GetDefaultLibraryPath();
        _indexPath = Path.Combine(_libraryPath, "index.json");
        _index = LoadIndex();
    }

    /// <summary>
    /// Gets the default library path based on the current platform.
    /// </summary>
    public static string GetDefaultLibraryPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "gmmt", "vanilla");
        }
        else
        {
            // Linux/Mac: ~/.local/share/gmmt/vanilla
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "gmmt", "vanilla");
        }
    }

    /// <summary>
    /// Returns all entries in the library.
    /// </summary>
    public IReadOnlyList<VanillaEntry> GetEntries() => _index.Entries.AsReadOnly();

    /// <summary>
    /// Returns entries matching optional filters, ordered by relevance.
    /// </summary>
    public IEnumerable<VanillaEntry> GetCandidates(string? gameName = null, string? platform = null)
    {
        var query = _index.Entries.AsEnumerable();

        if (gameName is not null)
            query = query.Where(e => e.GameName?.Equals(gameName, StringComparison.OrdinalIgnoreCase) == true);

        if (platform is not null)
            query = query.Where(e => e.Platform?.Equals(platform, StringComparison.OrdinalIgnoreCase) == true);

        // Order by most recently added (user probably added correct one last)
        return query.OrderByDescending(e => e.AddedUtc);
    }

    /// <summary>
    /// Finds an entry by hash (can be partial hash, minimum 8 chars).
    /// </summary>
    public VanillaEntry? FindByHash(string hashPrefix)
    {
        if (hashPrefix.Length < 8)
            return null;

        var matches = _index.Entries
            .Where(e => e.Hash.StartsWith(hashPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Adds a vanilla archive to the library.
    /// </summary>
    public VanillaEntry Add(string archivePath, string[]? tags = null, Action<string>? log = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}");

        log?.Invoke($"Computing hash of {archivePath}...");
        var hash = ComputeSha256(archivePath);

        // Check for duplicates
        var existing = _index.Entries.FirstOrDefault(e => e.Hash == hash);
        if (existing is not null)
        {
            log?.Invoke($"Archive already in library: {existing.ShortHash}");
            return existing;
        }

        log?.Invoke("Loading archive metadata...");
        var loadResult = ArchiveLoader.Load(archivePath, msg => log?.Invoke($"  {msg}"));
        var meta = loadResult.Metadata;

        var fileInfo = new FileInfo(archivePath);

        // Determine platform from file extension
        var platform = Path.GetExtension(archivePath).ToLowerInvariant() switch
        {
            ".win" => "Windows",
            ".unx" => "Linux",
            ".ios" => "iOS",
            ".droid" => "Android",
            _ => null
        };

        var entry = new VanillaEntry
        {
            Hash = hash,
            OriginalPath = Path.GetFullPath(archivePath),
            AddedUtc = DateTime.UtcNow,
            GameName = meta.GameName,
            VersionString = meta.VersionString,
            BytecodeVersion = meta.BytecodeVersion,
            FileSize = fileInfo.Length,
            Platform = platform,
            Tags = tags
        };

        // Copy file to library
        EnsureLibraryExists();
        var destPath = GetArchivePath(hash);
        log?.Invoke($"Copying archive to library: {destPath}");
        File.Copy(archivePath, destPath, overwrite: true);

        // Update index
        _index.Entries.Add(entry);
        SaveIndex();

        log?.Invoke($"Added: {entry.ShortHash} ({entry.GameName} {entry.VersionString})");
        return entry;
    }

    /// <summary>
    /// Removes an entry from the library.
    /// </summary>
    public bool Remove(string hashPrefix, Action<string>? log = null)
    {
        var entry = FindByHash(hashPrefix);
        if (entry is null)
        {
            log?.Invoke($"No unique entry found for hash prefix: {hashPrefix}");
            return false;
        }

        var archivePath = GetArchivePath(entry.Hash);
        if (File.Exists(archivePath))
        {
            log?.Invoke($"Deleting archive: {archivePath}");
            File.Delete(archivePath);
        }

        _index.Entries.Remove(entry);
        SaveIndex();

        log?.Invoke($"Removed: {entry.ShortHash} ({entry.GameName} {entry.VersionString})");
        return true;
    }

    /// <summary>
    /// Gets the path to a stored archive by hash.
    /// </summary>
    public string GetArchivePath(string hash)
    {
        return Path.Combine(_libraryPath, $"{hash}.win");
    }

    /// <summary>
    /// Checks if an archive with the given hash exists in the library.
    /// </summary>
    public bool HasArchive(string hash)
    {
        return _index.Entries.Any(e => e.Hash == hash) && File.Exists(GetArchivePath(hash));
    }

    private void EnsureLibraryExists()
    {
        if (!Directory.Exists(_libraryPath))
            Directory.CreateDirectory(_libraryPath);
    }

    private VanillaIndex LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return new VanillaIndex();

        try
        {
            var json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<VanillaIndex>(json, JsonOptions) ?? new VanillaIndex();
        }
        catch
        {
            return new VanillaIndex();
        }
    }

    private void SaveIndex()
    {
        EnsureLibraryExists();
        var json = JsonSerializer.Serialize(_index, JsonOptions);
        File.WriteAllText(_indexPath, json);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
