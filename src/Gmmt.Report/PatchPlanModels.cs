using Gmmt.Core.Classification;

namespace Gmmt.Report;

// ─────────────────────────────────────────────────────────────
// Planned action for a single resource
// ─────────────────────────────────────────────────────────────

/// <summary>
/// What the pipeline intends to do with a resource.
/// </summary>
public enum PlanActionKind
{
    /// <summary>Replace bytecode of an existing code entry.</summary>
    ReplaceCode,
    /// <summary>Patch string content inside a code entry.</summary>
    PatchString,
    /// <summary>Transplant a completely new code entry.</summary>
    TransplantCode,
    /// <summary>Transplant a new sprite (with textures).</summary>
    TransplantSprite,
    /// <summary>Update an existing sprite's texture data.</summary>
    UpdateSprite,
    /// <summary>Transplant a new sound.</summary>
    TransplantSound,
    /// <summary>Update an existing sound.</summary>
    UpdateSound,
    /// <summary>Transplant a new font.</summary>
    TransplantFont,
    /// <summary>Update an existing font.</summary>
    UpdateFont,
    /// <summary>Transplant a new background/tileset.</summary>
    TransplantBackground,
    /// <summary>Update an existing background.</summary>
    UpdateBackground,
    /// <summary>Transplant a new script entry.</summary>
    TransplantScript,
    /// <summary>Transplant a new game object.</summary>
    TransplantObject,
    /// <summary>Transplant a new room.</summary>
    TransplantRoom,
    /// <summary>Bind new events to an existing object.</summary>
    PatchObjectEvents,
    /// <summary>Patch scalar properties on an existing object.</summary>
    PatchObjectProperties,
}

/// <summary>
/// Confidence that the planned action will succeed and produce correct results.
/// </summary>
public enum PlanConfidence
{
    /// <summary>Strong match, identical structure. Should succeed.</summary>
    High,
    /// <summary>Reasonable match, minor risk. Likely succeeds.</summary>
    Medium,
    /// <summary>Heuristic match. May produce incorrect results.</summary>
    Low,
    /// <summary>Cannot determine confidence.</summary>
    Unknown,
}

/// <summary>
/// A single action the pipeline plans to execute.
/// </summary>
public sealed record PlanAction
{
    /// <summary>What kind of operation.</summary>
    public required PlanActionKind Kind { get; init; }

    /// <summary>Target resource name (code entry, sprite name, etc.).</summary>
    public required string Resource { get; init; }

    /// <summary>How confident we are this will work.</summary>
    public required PlanConfidence Confidence { get; init; }

    /// <summary>Human-readable description of the action.</summary>
    public required string Description { get; init; }

    /// <summary>Optional detail (old→new value for strings, frame count for sprites, etc.).</summary>
    public string? Detail { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Skipped resource with reason
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Why a particular mod change will NOT be applied.
/// </summary>
public sealed record PlanSkip
{
    /// <summary>What kind of operation was intended.</summary>
    public required PlanActionKind Kind { get; init; }

    /// <summary>Resource name.</summary>
    public required string Resource { get; init; }

    /// <summary>Why it's skipped.</summary>
    public required string Reason { get; init; }

    /// <summary>Optional detail.</summary>
    public string? Detail { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Complete patch plan
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Complete plan of what translate-xdelta will do, without actually doing it.
/// Produced in --dry-run mode. Contains classification, planned actions, and skipped items.
/// </summary>
public sealed class PatchPlan
{
    /// <summary>Pre-flight classification result.</summary>
    public required ModClassification Classification { get; init; }

    /// <summary>All actions the pipeline will attempt.</summary>
    public required IReadOnlyList<PlanAction> Actions { get; init; }

    /// <summary>All items that will be skipped with reasons.</summary>
    public required IReadOnlyList<PlanSkip> Skipped { get; init; }

    // ── Derived stats ──

    public int ActionCount => Actions.Count;
    public int SkipCount => Skipped.Count;

    public int HighConfidenceCount => Actions.Count(a => a.Confidence == PlanConfidence.High);
    public int MediumConfidenceCount => Actions.Count(a => a.Confidence == PlanConfidence.Medium);
    public int LowConfidenceCount => Actions.Count(a => a.Confidence == PlanConfidence.Low);

    public int CodeReplaceCount => Actions.Count(a => a.Kind == PlanActionKind.ReplaceCode);
    public int StringPatchCount => Actions.Count(a => a.Kind == PlanActionKind.PatchString);
    public int TransplantCount => Actions.Count(a =>
        a.Kind is PlanActionKind.TransplantCode or PlanActionKind.TransplantSprite or
        PlanActionKind.TransplantSound or PlanActionKind.TransplantFont or
        PlanActionKind.TransplantBackground or PlanActionKind.TransplantScript or
        PlanActionKind.TransplantObject or PlanActionKind.TransplantRoom);
    public int UpdateCount => Actions.Count(a =>
        a.Kind is PlanActionKind.UpdateSprite or PlanActionKind.UpdateSound or
        PlanActionKind.UpdateFont or PlanActionKind.UpdateBackground);
}
