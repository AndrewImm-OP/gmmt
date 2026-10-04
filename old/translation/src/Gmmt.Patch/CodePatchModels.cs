using Gmmt.Diff;

namespace Gmmt.Patch;

public enum CodePatchStatus
{
    Applied,
    Skipped,
    NotFound,
    Unsupported,
    Failed
}

public record CodePatchRequest(
    string CodeName,
    CodeSnapshot Snapshot
);

public record CodePatchResult(
    CodePatchStatus Status,
    string CodeName,
    int InstructionCount,
    string? Message
);
