namespace Gmmt.Patch;

public enum StringPatchStatus
{
    Applied,
    Skipped,
    NotFound,
    ContextMismatch,
    Ambiguous,
    Failed
}

public enum StringPatchConfidence
{
    Exact,
    ContextMatched,
    Heuristic,
    Low
}

public enum StringDisposition
{
    ModifyInPlace,
    CloneAndRepoint
}

public record ContextFingerprint(
    string? Prev,
    string? Next
);

public record StringPatchRequest(
    string CodeName,
    int Ordinal,
    string OldContent,
    string NewContent,
    ContextFingerprint Context
);

public record StringPatchResult(
    StringPatchStatus Status,
    StringPatchConfidence Confidence,
    StringDisposition? Disposition,
    string CodeName,
    int Ordinal,
    string? OldContent,
    string? NewContent,
    string? Message
);
