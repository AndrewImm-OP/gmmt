using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

namespace Gmmt.Diff;

/// <summary>
/// Builds <see cref="CodeSnapshot"/> from a <see cref="UndertaleCode"/> entry
/// by converting each instruction into an index-free <see cref="InstructionSnapshot"/>.
/// </summary>
public static class CodeSnapshotBuilder
{
    /// <summary>
    /// Build a semantic snapshot for a code entry's instructions.
    /// </summary>
    public static CodeSnapshot Build(UndertaleCode code)
    {
        var snapshots = new List<InstructionSnapshot>(code.Instructions.Count);
        bool hasUnsupported = false;

        for (int i = 0; i < code.Instructions.Count; i++)
        {
            var snap = BuildInstruction(code.Instructions[i], i);
            if (snap.IsUnsupported)
                hasUnsupported = true;
            snapshots.Add(snap);
        }

        return new CodeSnapshot
        {
            Name = code.Name?.Content ?? "(unnamed)",
            Instructions = snapshots,
            HasUnsupported = hasUnsupported,
        };
    }

    private static InstructionSnapshot BuildInstruction(UndertaleInstruction inst, int index)
    {
        var instType = GetInstructionType(inst.Kind);

        return instType switch
        {
            InstructionType.SingleTypeInstruction => BuildSingleType(inst, index, instType),
            InstructionType.DoubleTypeInstruction => BuildDoubleType(inst, index, instType),
            InstructionType.ComparisonInstruction => BuildComparison(inst, index, instType),
            InstructionType.GotoInstruction => BuildGoto(inst, index, instType),
            InstructionType.PushInstruction => BuildPush(inst, index, instType),
            InstructionType.PopInstruction => BuildPop(inst, index, instType),
            InstructionType.CallInstruction => BuildCall(inst, index, instType),
            InstructionType.BreakInstruction => BuildBreak(inst, index, instType),
            _ => MakeUnsupported(inst, index, instType, $"Unknown instruction type: {instType}"),
        };
    }

    // ─────────────────────────────────────────────────────────
    // SingleType: Neg, Not, Dup, Ret, Exit, Popz, CallV
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildSingleType(UndertaleInstruction inst, int index, InstructionType instType)
    {
        byte? extra = inst.Kind is Opcode.Dup or Opcode.CallV
            ? inst.Extra
            : null;

        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            Extra = extra,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // DoubleType: Conv, Mul, Div, Rem, Mod, Add, Sub, And, Or, Xor, Shl, Shr
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildDoubleType(UndertaleInstruction inst, int index, InstructionType instType)
    {
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            Type2 = inst.Type2,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Comparison: Cmp
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildComparison(UndertaleInstruction inst, int index, InstructionType instType)
    {
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            Type2 = inst.Type2,
            ComparisonKind = inst.ComparisonKind,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Goto: B, Bt, Bf, PushEnv, PopEnv
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildGoto(UndertaleInstruction inst, int index, InstructionType instType)
    {
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            JumpOffset = inst.JumpOffset,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Push: Push, PushLoc, PushGlb, PushBltn, PushI
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildPush(UndertaleInstruction inst, int index, InstructionType instType)
    {
        var snap = new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            Index = index,
        };

        return inst.Type1 switch
        {
            DataType.Int16 => snap with { IntValue = inst.ValueShort },
            DataType.Int32 => BuildPushInt32(inst, snap),
            DataType.Int64 => snap with { IntValue = inst.ValueLong },
            DataType.Double => snap with { DoubleValue = inst.ValueDouble },
            DataType.Boolean => snap with { BoolValue = inst.ValueInt != 0 },
            DataType.String => BuildPushString(inst, snap),
            DataType.Variable => BuildPushVariable(inst, snap),
            _ => snap with
            {
                IsUnsupported = true,
                UnsupportedReason = $"Push with unhandled data type: {inst.Type1}",
            },
        };
    }

    private static InstructionSnapshot BuildPushInt32(UndertaleInstruction inst, InstructionSnapshot snap)
    {
        // Int32 push can reference a function in GMS 2.3+
        if (inst.ValueFunction is { } func)
        {
            return snap with
            {
                FunctionName = func.Name?.Content,
            };
        }

        // Int32 push can also reference a variable in GMS 2.3+
        if (inst.ValueVariable is { } variable)
        {
            return snap with
            {
                VariableName = variable.Name?.Content,
                VariableInstanceType = variable.InstanceType,
            };
        }

        return snap with { IntValue = inst.ValueInt };
    }

    private static InstructionSnapshot BuildPushString(UndertaleInstruction inst, InstructionSnapshot snap)
    {
        // String compared by content
        var str = inst.ValueString?.Resource;
        return snap with
        {
            StringValue = str?.Content,
        };
    }

    private static InstructionSnapshot BuildPushVariable(UndertaleInstruction inst, InstructionSnapshot snap)
    {
        var variable = inst.ValueVariable;
        if (variable is null)
        {
            return snap with
            {
                IsUnsupported = true,
                UnsupportedReason = "Push.Variable with null variable reference",
            };
        }

        // Variable identity: name + instance type + reference type
        return snap with
        {
            TypeInst = inst.TypeInst,
            ReferenceType = inst.ReferenceType,
            VariableName = variable.Name?.Content,
            VariableInstanceType = variable.InstanceType,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Pop: variable store
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildPop(UndertaleInstruction inst, int index, InstructionType instType)
    {
        var variable = inst.ValueVariable;

        if (inst.Type1 == DataType.Int16)
        {
            // Swap variant — no variable reference
            return new InstructionSnapshot
            {
                Kind = inst.Kind,
                InstructionType = instType,
                Type1 = inst.Type1,
                Type2 = inst.Type2,
                IntValue = inst.SwapExtra,
                Index = index,
            };
        }

        if (variable is null)
        {
            return MakeUnsupported(inst, index, instType, "Pop with null variable reference");
        }

        // Variable identity: name + instance type + reference type
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            Type2 = inst.Type2,
            TypeInst = inst.TypeInst,
            ReferenceType = inst.ReferenceType,
            VariableName = variable.Name?.Content,
            VariableInstanceType = variable.InstanceType,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Call: function call
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildCall(UndertaleInstruction inst, int index, InstructionType instType)
    {
        var func = inst.ValueFunction;
        if (func is null)
        {
            return MakeUnsupported(inst, index, instType, "Call with null function reference");
        }

        // Function identity: name
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            ArgumentsCount = inst.ArgumentsCount,
            FunctionName = func.Name?.Content,
            Index = index,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Break: sub-opcodes (GMS 2.3+)
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot BuildBreak(UndertaleInstruction inst, int index, InstructionType instType)
    {
        var snap = new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            ExtendedKind = inst.ExtendedKind,
            Index = index,
        };

        // Break with Int32 type can carry a function reference or an integer argument
        if (inst.Type1 == DataType.Int32)
        {
            if (inst.ValueFunction is { } func)
            {
                snap = snap with { FunctionName = func.Name?.Content };
            }
            else
            {
                // IntArgument stores the full 32-bit value: lower 24 bits = asset ID,
                // upper 8 bits = asset type. Store the full value to avoid false matches
                // between different asset types with the same ID.
                snap = snap with { IntValue = inst.IntArgument };
            }
        }

        return snap;
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private static InstructionSnapshot MakeUnsupported(
        UndertaleInstruction inst, int index, InstructionType instType, string reason)
    {
        return new InstructionSnapshot
        {
            Kind = inst.Kind,
            InstructionType = instType,
            Type1 = inst.Type1,
            IsUnsupported = true,
            UnsupportedReason = reason,
            Index = index,
        };
    }
}
