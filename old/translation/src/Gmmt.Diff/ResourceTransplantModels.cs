namespace Gmmt.Diff;

/// <summary>
/// Identifies resources that exist only in the modded archive and need to be
/// transplanted (copied) wholesale to the target archive.
/// Unlike diffs (which compare matching resources), transplants handle entirely new resources.
/// </summary>
public sealed class TransplantManifest
{
    /// <summary>Names of sprites that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewSprites { get; init; }

    /// <summary>
    /// Names of sprites that exist in both vanilla and modded archives but have been
    /// modified (different frame count, dimensions, or texture data).
    /// These need to have their texture data updated in the target archive.
    /// </summary>
    public required IReadOnlyList<string> ModifiedSprites { get; init; }

    /// <summary>Names of backgrounds modified between vanilla and modded.</summary>
    public required IReadOnlyList<string> ModifiedBackgrounds { get; init; }

    /// <summary>Names of fonts modified between vanilla and modded.</summary>
    public required IReadOnlyList<string> ModifiedFonts { get; init; }

    /// <summary>Names of sounds modified between vanilla and modded.</summary>
    public required IReadOnlyList<string> ModifiedSounds { get; init; }

    /// <summary>Names of code entries that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewCodeEntries { get; init; }

    /// <summary>Names of game objects that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewObjects { get; init; }

    /// <summary>Names of rooms that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewRooms { get; init; }

    /// <summary>Names of sounds that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewSounds { get; init; }

    /// <summary>Names of scripts that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewScripts { get; init; }

    /// <summary>Names of fonts that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewFonts { get; init; }

    /// <summary>Names of backgrounds that exist only in the modded archive.</summary>
    public required IReadOnlyList<string> NewBackgrounds { get; init; }

    /// <summary>
    /// Indices of embedded textures in the modded archive that are referenced
    /// by new sprites but don't exist in the target. These are identified during
    /// the sprite transplant phase and tracked here for reporting.
    /// </summary>
    public required IReadOnlyList<int> NewEmbeddedTextureIndices { get; init; }

    public int TotalNewResources =>
        NewSprites.Count + NewCodeEntries.Count + NewObjects.Count +
        NewRooms.Count + NewSounds.Count + NewScripts.Count +
        NewFonts.Count + NewBackgrounds.Count;
}
