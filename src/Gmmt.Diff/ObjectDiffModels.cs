namespace Gmmt.Diff;

/// <summary>
/// Status of comparing one game object between archives.
/// </summary>
public enum ObjectDiffStatus
{
    /// <summary>All compared scalar properties are identical.</summary>
    Equal,
    /// <summary>One or more scalar properties differ.</summary>
    Modified,
    /// <summary>Object exists in one archive but not the other.</summary>
    Missing,
}

/// <summary>
/// A single scalar property difference on a game object.
/// </summary>
public sealed record ObjectPropertyDiff
{
    /// <summary>Property name (e.g. "Visible", "SpriteIndex", "ParentId").</summary>
    public required string Property { get; init; }
    /// <summary>Value in the vanilla archive (display string).</summary>
    public required string VanillaValue { get; init; }
    /// <summary>Value in the modded archive (display string).</summary>
    public required string ModdedValue { get; init; }
}

/// <summary>
/// Full diff result for one game object.
/// </summary>
public sealed class ObjectDiffResult
{
    public required string ObjectName { get; init; }
    public required ObjectDiffStatus Status { get; init; }
    public required IReadOnlyList<ObjectPropertyDiff> Differences { get; init; }
    /// <summary>Human-readable diagnostic when status is Missing.</summary>
    public string? Diagnostic { get; init; }
}
