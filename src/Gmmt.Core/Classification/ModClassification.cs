namespace Gmmt.Core.Classification;

// ─────────────────────────────────────────────────────────────
// Verdict
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Top-level portability verdict for a mod.
/// Determines whether automatic translation should proceed.
/// </summary>
public enum PortabilityVerdict
{
    /// <summary>
    /// All mod changes are index-independent and expressible on the target.
    /// Strings, sprites, sounds, fonts, simple object/code changes.
    /// Expected success rate: 90–100%.
    /// </summary>
    Portable,

    /// <summary>
    /// Most changes are portable, but some risky items exist.
    /// A few ambiguous strings, some complex code, minor issues.
    /// Expected success rate: 70–90%.
    /// </summary>
    MostlyPortable,

    /// <summary>
    /// Significant portions of the mod cannot be auto-translated.
    /// GMS2-only code in a GMS1 target, child entries, rooms with layers, etc.
    /// Some patches will apply, but manual work is required.
    /// Expected success rate: 30–70%.
    /// </summary>
    PatchableOnly,

    /// <summary>
    /// The mod fundamentally requires runtime features absent from the target.
    /// Automatic translation will produce a broken output.
    /// Do not proceed — output would be worse than no output.
    /// </summary>
    NotPortable,
}

// ─────────────────────────────────────────────────────────────
// Issue severity
// ─────────────────────────────────────────────────────────────

/// <summary>
/// How severely an issue affects portability.
/// </summary>
public enum ClassificationSeverity
{
    /// <summary>Makes the mod fundamentally non-portable. Single blocker → NotPortable.</summary>
    Blocker,
    /// <summary>Significant portability problem. Many majors → PatchableOnly.</summary>
    Major,
    /// <summary>Moderate concern. Some minors → MostlyPortable.</summary>
    Minor,
    /// <summary>Informational only. Does not affect verdict.</summary>
    Info,
}

// ─────────────────────────────────────────────────────────────
// Individual issue
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A single portability concern detected during pre-flight analysis.
/// </summary>
public sealed record ClassificationIssue
{
    public required ClassificationSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    /// <summary>Optional detail (affected resource names, counts, etc.).</summary>
    public string? Detail { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Quantitative breakdown
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Quantitative breakdown of mod changes by portability class.
/// </summary>
public sealed record ClassificationBreakdown
{
    // ── Resource counts ──
    public int NewSpriteCount { get; init; }
    public int NewSoundCount { get; init; }
    public int NewFontCount { get; init; }
    public int NewBackgroundCount { get; init; }
    public int ModifiedSpriteCount { get; init; }
    public int ModifiedSoundCount { get; init; }
    public int ModifiedFontCount { get; init; }
    public int ModifiedBackgroundCount { get; init; }

    // ── Code analysis ──
    public int NewCodeEntryCount { get; init; }
    public int NewCodePortableCount { get; init; }
    public int NewCodeUnsupportedCount { get; init; }
    public int NewCodeWithChildEntriesCount { get; init; }
    public int NewCodeWithGms2OnlyRefsCount { get; init; }

    public int ModifiedCodeEntryCount { get; init; }
    public int ModifiedCodeWithChildEntriesCount { get; init; }
    public int ModifiedCodeWithGms2OnlyRefsCount { get; init; }

    // ── Other changes ──
    public int NewObjectCount { get; init; }
    public int NewScriptCount { get; init; }
    public int NewRoomCount { get; init; }
    public int StringCountDelta { get; init; }

    // ── Derived ──

    /// <summary>Total new resources (sprites + sounds + fonts + backgrounds + objects + scripts + rooms).</summary>
    public int TotalNewResources =>
        NewSpriteCount + NewSoundCount + NewFontCount + NewBackgroundCount +
        NewObjectCount + NewScriptCount + NewRoomCount;

    /// <summary>Total modified resources.</summary>
    public int TotalModifiedResources =>
        ModifiedSpriteCount + ModifiedSoundCount + ModifiedFontCount + ModifiedBackgroundCount;

    // ── Weighted portability estimation ──
    // Weights reflect impact on game functionality. A broken code entry matters
    // far more than a missing string translation or sprite swap.

    private const int WeightStringChange = 1;
    private const int WeightResourceChange = 2;
    private const int WeightNewObject = 4;
    private const int WeightNewScript = 4;
    private const int WeightCodeEntry = 10;
    private const int WeightNewRoom = 15;

    /// <summary>
    /// Estimated percentage of mod changes that are auto-portable.
    /// Weighted: code entries count ~10x more than string/resource changes.
    /// A single broken gameplay script drags the estimate down harder than
    /// 10 untranslated strings.
    /// </summary>
    public int EstimatedPortablePercent
    {
        get
        {
            // Portable code
            int portableModifiedCode = ModifiedCodeEntryCount
                - ModifiedCodeWithChildEntriesCount - ModifiedCodeWithGms2OnlyRefsCount;
            if (portableModifiedCode < 0) portableModifiedCode = 0;

            int unsupportedNewCode = NewCodeUnsupportedCount;
            int unsupportedModifiedCode = ModifiedCodeWithChildEntriesCount + ModifiedCodeWithGms2OnlyRefsCount;

            // Weighted totals
            double totalWeight =
                Math.Abs(StringCountDelta) * WeightStringChange +
                (double)TotalNewResources * WeightResourceChange +
                (double)TotalModifiedResources * WeightResourceChange +
                (double)NewObjectCount * WeightNewObject +
                (double)NewScriptCount * WeightNewScript +
                (double)NewRoomCount * WeightNewRoom +
                (double)(NewCodeEntryCount + ModifiedCodeEntryCount) * WeightCodeEntry;

            if (totalWeight < 0.001)
                return 100; // Empty mod — trivially portable

            // Unsupported weight
            double unsupportedWeight =
                (double)(unsupportedNewCode + unsupportedModifiedCode) * WeightCodeEntry;

            double portableWeight = totalWeight - unsupportedWeight;
            if (portableWeight < 0) portableWeight = 0;

            return (int)(portableWeight / totalWeight * 100);
        }
    }
}

// ─────────────────────────────────────────────────────────────
// Full classification result
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Complete pre-flight classification of a mod's portability.
/// </summary>
public sealed class ModClassification
{
    /// <summary>Top-level verdict.</summary>
    public required PortabilityVerdict Verdict { get; init; }

    /// <summary>One-line human-readable summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Quantitative breakdown.</summary>
    public required ClassificationBreakdown Breakdown { get; init; }

    /// <summary>All detected issues, ordered by severity (blockers first).</summary>
    public required IReadOnlyList<ClassificationIssue> Issues { get; init; }

    /// <summary>True if the pipeline should abort (NotPortable and not forced).</summary>
    public bool ShouldAbort => Verdict == PortabilityVerdict.NotPortable;

    /// <summary>Estimated success rate as a percentage (0–100).</summary>
    public int EstimatedSuccessPercent => Breakdown.EstimatedPortablePercent;
}
