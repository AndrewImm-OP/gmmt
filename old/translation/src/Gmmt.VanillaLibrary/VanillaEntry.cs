namespace Gmmt.VanillaLibrary;

/// <summary>
/// Represents a stored vanilla archive in the library.
/// </summary>
public record VanillaEntry
{
    /// <summary>SHA-256 hash of the file (lowercase hex)</summary>
    public required string Hash { get; init; }

    /// <summary>Original path where the file was added from</summary>
    public required string OriginalPath { get; init; }

    /// <summary>When this entry was added to the library</summary>
    public required DateTime AddedUtc { get; init; }

    /// <summary>Game name from archive metadata</summary>
    public string? GameName { get; init; }

    /// <summary>Game version string (e.g. "1.0.0.1")</summary>
    public string? VersionString { get; init; }

    /// <summary>Bytecode version</summary>
    public int BytecodeVersion { get; init; }

    /// <summary>File size in bytes</summary>
    public long FileSize { get; init; }

    /// <summary>Platform (Windows, Linux, Mac) if known</summary>
    public string? Platform { get; init; }

    /// <summary>User-provided tags (e.g. "steam", "gog")</summary>
    public string[]? Tags { get; init; }

    /// <summary>Returns the filename portion of OriginalPath</summary>
    public string FileName => Path.GetFileName(OriginalPath);

    /// <summary>Returns the short hash (first 8 chars)</summary>
    public string ShortHash => Hash.Length >= 8 ? Hash[..8] : Hash;
}
