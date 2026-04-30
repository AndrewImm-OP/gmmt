namespace Gmmt.Patch;

/// <summary>
/// Status of a single property patch operation.
/// </summary>
public enum PropertyPatchStatus
{
    /// <summary>Property was set to the requested value.</summary>
    Applied,
    /// <summary>Property already had the requested value — no mutation.</summary>
    AlreadyEqual,
    /// <summary>Referenced resource name could not be resolved in the target archive.</summary>
    ResourceNotFound,
    /// <summary>Patch was skipped due to a precondition failure.</summary>
    Skipped,
}

/// <summary>
/// Result of patching a single scalar property on a game object.
/// </summary>
public sealed record PropertyPatchResult
{
    public required string Property { get; init; }
    public required PropertyPatchStatus Status { get; init; }
    /// <summary>Value before patching (display string).</summary>
    public required string OldValue { get; init; }
    /// <summary>Requested new value (display string).</summary>
    public required string NewValue { get; init; }
    /// <summary>Diagnostic when status is not Applied/AlreadyEqual.</summary>
    public string? Diagnostic { get; init; }
}

/// <summary>
/// Status of patching one game object.
/// </summary>
public enum ObjectPatchStatus
{
    /// <summary>All requested properties were applied successfully.</summary>
    Applied,
    /// <summary>Some properties applied, some failed.</summary>
    PartiallyApplied,
    /// <summary>No properties were applied.</summary>
    Failed,
    /// <summary>Target object not found in archive.</summary>
    ObjectMissing,
    /// <summary>No property operations were requested.</summary>
    NothingToDo,
}

/// <summary>
/// Full result for patching one game object.
/// </summary>
public sealed class ObjectPatchResult
{
    public required string ObjectName { get; init; }
    public required ObjectPatchStatus Status { get; init; }
    public required IReadOnlyList<PropertyPatchResult> Properties { get; init; }
    /// <summary>Diagnostic when status is ObjectMissing or Failed.</summary>
    public string? Diagnostic { get; init; }
}

/// <summary>
/// A single scalar property patch operation to apply.
/// </summary>
public sealed record ObjectPropertyPatch
{
    public required string Property { get; init; }
    /// <summary>
    /// The value to set. Type depends on property:
    /// bool for Visible/Solid/Persistent, int for Depth,
    /// string (name) for SpriteIndex/ParentId, null to clear reference.
    /// </summary>
    public required object? Value { get; init; }
}

/// <summary>
/// A complete patch request for one game object.
/// </summary>
public sealed class ObjectPatchRequest
{
    public required string ObjectName { get; init; }
    public required IReadOnlyList<ObjectPropertyPatch> Patches { get; init; }
}
