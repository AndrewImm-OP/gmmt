namespace Gmmt.Diff;

/// <summary>
/// Compares two <see cref="InstructionSnapshot"/> values for semantic equality.
/// Ignores the <see cref="InstructionSnapshot.Index"/> field (debug-only).
/// </summary>
public static class InstructionComparer
{
    /// <summary>
    /// Returns true if two instruction snapshots are semantically equivalent.
    /// </summary>
    public static bool AreEqual(InstructionSnapshot a, InstructionSnapshot b)
    {
        // Either unsupported → cannot compare
        if (a.IsUnsupported || b.IsUnsupported)
            return false;

        // Opcode and instruction type must match
        if (a.Kind != b.Kind || a.InstructionType != b.InstructionType)
            return false;

        // Data types
        if (a.Type1 != b.Type1 || a.Type2 != b.Type2)
            return false;

        // Comparison kind
        if (a.ComparisonKind != b.ComparisonKind)
            return false;

        // Instance type
        if (a.TypeInst != b.TypeInst)
            return false;

        // Variable reference type (Normal, Array, StackTop, etc.)
        if (a.ReferenceType != b.ReferenceType)
            return false;

        // Jump offset — compared as-is per spec
        if (a.JumpOffset != b.JumpOffset)
            return false;

        // Arguments count
        if (a.ArgumentsCount != b.ArgumentsCount)
            return false;

        // Extra byte (Dup/CallV)
        if (a.Extra != b.Extra)
            return false;

        // Break sub-opcode
        if (a.ExtendedKind != b.ExtendedKind)
            return false;

        // ── Semantic value comparison ──

        // Strings by content
        if (!StringEquals(a.StringValue, b.StringValue))
            return false;

        // Variables by name + instance type + reference type
        if (!StringEquals(a.VariableName, b.VariableName))
            return false;
        if (a.VariableInstanceType != b.VariableInstanceType)
            return false;

        // Functions by name
        if (!StringEquals(a.FunctionName, b.FunctionName))
            return false;

        // Numeric values
        if (a.IntValue != b.IntValue)
            return false;
        if (a.DoubleValue != b.DoubleValue)
            return false;
        if (a.BoolValue != b.BoolValue)
            return false;

        return true;
    }

    /// <summary>
    /// Produces a human-readable description of the differences between two snapshots.
    /// Returns null if they are equal.
    /// </summary>
    public static string? DescribeDifference(InstructionSnapshot a, InstructionSnapshot b)
    {
        if (AreEqual(a, b))
            return null;

        if (a.IsUnsupported || b.IsUnsupported)
        {
            var reason = a.IsUnsupported ? a.UnsupportedReason : b.UnsupportedReason;
            return $"Unsupported instruction: {reason ?? "unknown"}";
        }

        var parts = new List<string>();

        if (a.Kind != b.Kind)
            parts.Add($"opcode {a.Kind}→{b.Kind}");

        if (a.Type1 != b.Type1)
            parts.Add($"type1 {a.Type1}→{b.Type1}");

        if (a.Type2 != b.Type2)
            parts.Add($"type2 {a.Type2}→{b.Type2}");

        if (a.ComparisonKind != b.ComparisonKind)
            parts.Add($"cmp {a.ComparisonKind}→{b.ComparisonKind}");

        if (a.TypeInst != b.TypeInst)
            parts.Add($"inst {a.TypeInst}→{b.TypeInst}");

        if (a.ReferenceType != b.ReferenceType)
            parts.Add($"refType {a.ReferenceType}→{b.ReferenceType}");

        if (a.JumpOffset != b.JumpOffset)
            parts.Add($"jump {a.JumpOffset}→{b.JumpOffset}");

        if (a.ArgumentsCount != b.ArgumentsCount)
            parts.Add($"args {a.ArgumentsCount}→{b.ArgumentsCount}");

        if (a.Extra != b.Extra)
            parts.Add($"extra {a.Extra}→{b.Extra}");

        if (a.ExtendedKind != b.ExtendedKind)
            parts.Add($"extKind {a.ExtendedKind}→{b.ExtendedKind}");

        if (!StringEquals(a.StringValue, b.StringValue))
            parts.Add($"string \"{Truncate(a.StringValue)}\"→\"{Truncate(b.StringValue)}\"");

        if (!StringEquals(a.VariableName, b.VariableName))
            parts.Add($"var {a.VariableName}→{b.VariableName}");

        if (a.VariableInstanceType != b.VariableInstanceType)
            parts.Add($"varInst {a.VariableInstanceType}→{b.VariableInstanceType}");

        if (!StringEquals(a.FunctionName, b.FunctionName))
            parts.Add($"func {a.FunctionName}→{b.FunctionName}");

        if (a.IntValue != b.IntValue)
            parts.Add($"int {a.IntValue}→{b.IntValue}");

        if (a.DoubleValue != b.DoubleValue)
            parts.Add($"double {a.DoubleValue}→{b.DoubleValue}");

        if (a.BoolValue != b.BoolValue)
            parts.Add($"bool {a.BoolValue}→{b.BoolValue}");

        return parts.Count > 0
            ? string.Join("; ", parts)
            : "instructions differ (unknown field)";
    }

    private static bool StringEquals(string? a, string? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    private static string Truncate(string? s, int maxLen = 60)
    {
        if (s is null) return "(null)";
        if (s.Length <= maxLen) return s;
        return s[..(maxLen - 3)] + "...";
    }
}
