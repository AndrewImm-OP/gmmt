using UndertaleModLib.Models;

namespace Gmmt.Diff;

// ─────────────────────────────────────────────────────────────
// Diff result status for a single code entry
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of comparing two code entries instruction-by-instruction.
/// </summary>
public enum CodeDiffStatus
{
    /// <summary>All instructions are semantically identical.</summary>
    Equal,
    /// <summary>Instruction count or content differs.</summary>
    Modified,
    /// <summary>One or more instructions use an opcode/type we cannot compare semantically.</summary>
    Unsupported,
    /// <summary>Code entry exists in one archive but not the other.</summary>
    CodeEntryMissing,
    /// <summary>Multiple equally valid interpretations exist (e.g. duplicate resources).</summary>
    Ambiguous,
}

// ─────────────────────────────────────────────────────────────
// Semantic instruction snapshot — index-free representation
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A single instruction reduced to semantically meaningful, index-free fields.
/// Two snapshots from different archives are equal iff the instructions
/// have the same behavior regardless of resource table ordering.
/// </summary>
public sealed record InstructionSnapshot
{
    /// <summary>Opcode (Kind).</summary>
    public required UndertaleInstruction.Opcode Kind { get; init; }

    /// <summary>Instruction type derived from opcode.</summary>
    public required UndertaleInstruction.InstructionType InstructionType { get; init; }

    /// <summary>First data type (Type1).</summary>
    public UndertaleInstruction.DataType Type1 { get; init; }

    /// <summary>Second data type (Type2).</summary>
    public UndertaleInstruction.DataType Type2 { get; init; }

    /// <summary>Comparison kind for Cmp instructions.</summary>
    public UndertaleInstruction.ComparisonType? ComparisonKind { get; init; }

    /// <summary>Instance type for Push/Pop variable instructions.</summary>
    public UndertaleInstruction.InstanceType? TypeInst { get; init; }

    /// <summary>Variable reference type (Normal, Array, StackTop, etc.).</summary>
    public UndertaleInstruction.VariableType? ReferenceType { get; init; }

    /// <summary>Jump offset for branch instructions — compared as-is per spec.</summary>
    public int? JumpOffset { get; init; }

    /// <summary>Arguments count for Call instructions.</summary>
    public ushort? ArgumentsCount { get; init; }

    /// <summary>Extra byte for Dup/CallV instructions.</summary>
    public byte? Extra { get; init; }

    /// <summary>ExtendedKind for Break sub-opcodes.</summary>
    public short? ExtendedKind { get; init; }

    // ── Semantic value fields (at most one is set) ──

    /// <summary>String content for Push.String instructions.</summary>
    public string? StringValue { get; init; }

    /// <summary>Variable name for variable-referencing instructions.</summary>
    public string? VariableName { get; init; }

    /// <summary>Variable instance type — part of semantic variable identity.</summary>
    public UndertaleInstruction.InstanceType? VariableInstanceType { get; init; }

    /// <summary>Function name for Call instructions.</summary>
    public string? FunctionName { get; init; }

    /// <summary>Integer value for Push.Int16 / Push.Int32 / Break.Int32.</summary>
    public long? IntValue { get; init; }

    /// <summary>Double value for Push.Double.</summary>
    public double? DoubleValue { get; init; }

    /// <summary>Boolean value for Push.Boolean.</summary>
    public bool? BoolValue { get; init; }

    /// <summary>True if this instruction could not be fully resolved to a semantic snapshot.</summary>
    public bool IsUnsupported { get; init; }

    /// <summary>Diagnostic message when IsUnsupported is true.</summary>
    public string? UnsupportedReason { get; init; }

    /// <summary>0-based position in the instruction list (debug/reporting only — NOT part of equality).</summary>
    public int Index { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Snapshot of an entire code entry
// ─────────────────────────────────────────────────────────────

/// <summary>
/// All instruction snapshots for one root code entry.
/// </summary>
public sealed class CodeSnapshot
{
    public required string Name { get; init; }
    public required IReadOnlyList<InstructionSnapshot> Instructions { get; init; }
    public bool HasUnsupported { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Per-instruction diff detail
// ─────────────────────────────────────────────────────────────

/// <summary>
/// A single instruction-level difference between vanilla and modded.
/// </summary>
public sealed record InstructionDiff
{
    /// <summary>0-based instruction index.</summary>
    public required int Index { get; init; }
    public required InstructionSnapshot? Vanilla { get; init; }
    public required InstructionSnapshot? Modded { get; init; }

    /// <summary>Human-readable summary of what differs.</summary>
    public required string Description { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Complete diff result for one code entry
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Full diff result for a single code entry's instructions.
/// </summary>
public sealed class CodeDiffResult
{
    public required string CodeEntryName { get; init; }
    public required CodeDiffStatus Status { get; init; }
    public required IReadOnlyList<InstructionDiff> Differences { get; init; }
    public int VanillaInstructionCount { get; init; }
    public int ModdedInstructionCount { get; init; }
    /// <summary>Human-readable diagnostic when status is not Equal.</summary>
    public string? Diagnostic { get; init; }
}
