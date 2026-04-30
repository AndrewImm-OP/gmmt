namespace Gmmt.VanillaLibrary;

/// <summary>
/// The index file structure for the vanilla library.
/// </summary>
public record VanillaIndex
{
    /// <summary>Schema version for future compatibility</summary>
    public int Version { get; init; } = 1;

    /// <summary>All registered vanilla entries</summary>
    public List<VanillaEntry> Entries { get; init; } = [];
}
