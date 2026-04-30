namespace Gmmt.Diff;

// ─────────────────────────────────────────────────────────────
// Extracted string reference within a code entry
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A single string reference extracted from a code entry's instruction stream.
/// Identity is (CodeEntryName, Ordinal). InstructionIndex is debug-only.
/// </summary>
public sealed record StringRef(
    /// <summary>Content of the referenced UndertaleString.</summary>
    string Content,
    /// <summary>0-based ordinal: this is the Nth string-pushing instruction in the code entry.</summary>
    int Ordinal,
    /// <summary>Instruction index within the code entry. Debug/logging only — NOT identity.</summary>
    int InstructionIndex
);

/// <summary>
/// Neighboring string content for validating ordinal-based matches.
/// </summary>
public sealed record ContextFingerprint(
    /// <summary>Content of the string ref at ordinal-1, or null if this is the first.</summary>
    string? Preceding,
    /// <summary>Content of the string ref at ordinal+1, or null if this is the last.</summary>
    string? Following
);

// ─────────────────────────────────────────────────────────────
// Diff result for a single code entry's string references
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Confidence of a string change match.
/// Higher is better. Only ExactOrdinalAndContext is auto-applied in MVP.
/// </summary>
public enum StringMatchConfidence
{
    /// <summary>Ordinal matches AND surrounding strings match.</summary>
    ExactOrdinalAndContext,
    /// <summary>Ordinal matches but surrounding strings differ from expectation.</summary>
    ExactOrdinalOnly,
    /// <summary>Matched by content scan — ordinal was wrong or count differs.</summary>
    ContentMatchOnly,
    /// <summary>Multiple candidates found — human review required.</summary>
    Ambiguous,
    /// <summary>No match found in target for this string change.</summary>
    NotFound,
}

/// <summary>
/// A single detected string change within a code entry.
/// </summary>
public sealed record StringChange
{
    public required string CodeEntryName { get; init; }
    /// <summary>Ordinal of this string ref in the modded code entry's string-ref sequence.</summary>
    public required int ModdedOrdinal { get; init; }
    /// <summary>Ordinal in the vanilla code entry, or null if this is a new string ref.</summary>
    public int? VanillaOrdinal { get; init; }
    public required string OldContent { get; init; }
    public required string NewContent { get; init; }
    public required ContextFingerprint ModdedContext { get; init; }
    public required StringMatchConfidence Confidence { get; init; }
}

/// <summary>
/// Outcome status for diffing a single code entry's strings.
/// </summary>
public enum CodeStringDiffStatus
{
    /// <summary>Diff completed successfully with zero or more changes found.</summary>
    Ok,
    /// <summary>Code entry exists in vanilla but not in modded (or vice versa).</summary>
    CodeEntryMissing,
    /// <summary>String ref counts differ and LCS alignment could not resolve all changes.</summary>
    CountMismatchUnresolved,
    /// <summary>One or more changes are ambiguous.</summary>
    Ambiguous,
}

/// <summary>
/// Complete diff result for one code entry's string references.
/// </summary>
public sealed class CodeStringDiffResult
{
    public required string CodeEntryName { get; init; }
    public required CodeStringDiffStatus Status { get; init; }
    public required IReadOnlyList<StringChange> Changes { get; init; }
    /// <summary>Human-readable diagnostic when status is not Ok.</summary>
    public string? Diagnostic { get; init; }
    public int VanillaStringRefCount { get; init; }
    public int ModdedStringRefCount { get; init; }
}
