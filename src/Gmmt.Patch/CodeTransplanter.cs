using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

#pragma warning disable CS8601 // Possible null reference assignment — UTMT API accepts null in some places

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single code entry.
/// </summary>
public sealed record CodeTransplantResult
{
    public required string CodeName { get; init; }
    public required CodeTransplantStatus Status { get; init; }
    public int InstructionCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum CodeTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Unsupported,
    Failed,
}

/// <summary>
/// Transplants new code entries from a modded archive to a target archive.
///
/// Unlike string patching (which modifies existing code entries), transplanting
/// creates entirely new code entries in the target. This is necessary for mods
/// that add new scripts, object events, room creation code, etc.
///
/// Strategy:
/// 1. Validate: scan all instructions to check that every function/variable
///    reference can be resolved in the target. This prevents injecting GMS2-only
///    built-ins into a GMS1 target.
/// 2. Clone each instruction, remapping string/variable/function references.
/// 3. Variables are created if missing (safe — they're just named storage slots).
/// 4. Functions must already exist in the target (creating unknown built-ins
///    will crash the runner).
/// 5. Only transplants root code entries (no parent/child hierarchies).
/// </summary>
public static class CodeTransplanter
{
    // Functions that are safe to create in the target because they are
    // script-defined (gml_Script_*, gml_Object_*, etc.) rather than engine built-ins.
    // Engine built-in functions must already exist in the target FUNC table.
    private static bool IsScriptFunction(string name) =>
        name.StartsWith("gml_Script_", StringComparison.Ordinal) ||
        name.StartsWith("gml_Object_", StringComparison.Ordinal) ||
        name.StartsWith("gml_Room_", StringComparison.Ordinal) ||
        name.StartsWith("gml_GlobalScript_", StringComparison.Ordinal);

    /// <summary>
    /// Transplant multiple new code entries from modded to target.
    /// </summary>
    public static List<CodeTransplantResult> TransplantAll(
        IReadOnlyList<string> codeNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        // Build a set of existing target functions for fast lookup
        var targetFunctions = new HashSet<string>(StringComparer.Ordinal);
        if (targetData.Functions is not null)
        {
            foreach (var f in targetData.Functions)
            {
                if (f.Name?.Content is { } name)
                    targetFunctions.Add(name);
            }
        }

        var results = new List<CodeTransplantResult>(codeNames.Count);

        foreach (var codeName in codeNames)
        {
            var result = TransplantOne(codeName, moddedIdx, targetData, targetFunctions);
            results.Add(result);
        }

        return results;
    }

    private static CodeTransplantResult TransplantOne(
        string codeName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        // Check if already exists in target
        if (targetData.Code is not null)
        {
            foreach (var c in targetData.Code)
            {
                if (c.Name?.Content == codeName)
                {
                    return new CodeTransplantResult
                    {
                        CodeName = codeName,
                        Status = CodeTransplantStatus.AlreadyExists,
                        Diagnostic = "Code entry already exists in target.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Code.TryGetValue(codeName, out var moddedEntry))
        {
            return new CodeTransplantResult
            {
                CodeName = codeName,
                Status = CodeTransplantStatus.SourceNotFound,
                Diagnostic = "Code entry not found in modded archive.",
            };
        }

        var srcCode = moddedEntry.Res;

        // Skip child entries — they share bytecode with the parent entry.
        // Parent entries (with children) are allowed: they contain the full instruction stream.
        if (srcCode.ParentEntry is not null)
        {
            return new CodeTransplantResult
            {
                CodeName = codeName,
                Status = CodeTransplantStatus.Unsupported,
                Diagnostic = "Child code entry (has parent) — bytecode covered by parent entry.",
            };
        }

        // Validation pass: check all function references are resolvable
        var missingFunc = ValidateFunctionReferences(srcCode.Instructions, targetFunctions);
        if (missingFunc is not null)
        {
            return new CodeTransplantResult
            {
                CodeName = codeName,
                Status = CodeTransplantStatus.Unsupported,
                InstructionCount = srcCode.Instructions?.Count ?? 0,
                Diagnostic = $"References unresolvable function '{missingFunc}' (not in target).",
            };
        }

        try
        {
            // Create the code entry using UTMT's helper (also creates CodeLocals)
            var newCode = UndertaleCode.CreateEmptyEntry(targetData, codeName);
            newCode.LocalsCount = srcCode.LocalsCount;
            newCode.ArgumentsCount = srcCode.ArgumentsCount;

            // Clone instructions with reference remapping
            var clonedInstructions = CloneInstructions(
                srcCode.Instructions, moddedIdx.Data, targetData, targetFunctions);

            newCode.Replace(clonedInstructions);

            // Handle child entries if any (GMS2.3+ sub-functions)
            if (srcCode.ChildEntries is not null && srcCode.ChildEntries.Count > 0)
            {
                foreach (var srcChild in srcCode.ChildEntries)
                {
                    var childName = srcChild.Name?.Content;
                    if (childName is null) continue;

                    var childCode = UndertaleCode.CreateEmptyEntry(targetData, childName);
                    childCode.LocalsCount = srcChild.LocalsCount;
                    childCode.ArgumentsCount = srcChild.ArgumentsCount;
                    childCode.ParentEntry = newCode;
                    childCode.Offset = srcChild.Offset;

                    newCode.ChildEntries.Add(childCode);
                }
            }

            // Track newly available functions (this code entry's name is now a callable)
            if (codeName.StartsWith("gml_", StringComparison.Ordinal))
                targetFunctions.Add(codeName);

            return new CodeTransplantResult
            {
                CodeName = codeName,
                Status = CodeTransplantStatus.Transplanted,
                InstructionCount = clonedInstructions.Count,
            };
        }
        catch (Exception ex)
        {
            return new CodeTransplantResult
            {
                CodeName = codeName,
                Status = CodeTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Scan all instructions for function references that can't be resolved
    /// in the target archive. Returns the first missing function name, or null if all ok.
    /// Script-defined functions (gml_*) are allowed to be created.
    /// Engine built-ins must already exist.
    /// </summary>
    private static string? ValidateFunctionReferences(
        IReadOnlyList<UndertaleInstruction>? instructions,
        HashSet<string> targetFunctions)
    {
        if (instructions is null) return null;

        foreach (var inst in instructions)
        {
            string? funcName = null;

            // Check ValueFunction (Call, Break, Push.Int32 with func ref)
            if (inst.ValueFunction is { } func)
                funcName = func.Name?.Content;

            if (funcName is not null &&
                !targetFunctions.Contains(funcName) &&
                !IsScriptFunction(funcName))
            {
                return funcName;
            }
        }

        return null;
    }

    /// <summary>
    /// Clone a list of instructions, remapping all string/variable/function references.
    /// </summary>
    private static List<UndertaleInstruction> CloneInstructions(
        IReadOnlyList<UndertaleInstruction> srcInstructions,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        var result = new List<UndertaleInstruction>(srcInstructions.Count);

        foreach (var src in srcInstructions)
        {
            var clone = CloneInstruction(src, moddedData, targetData, targetFunctions);
            result.Add(clone);
        }

        return result;
    }

    private static UndertaleInstruction CloneInstruction(
        UndertaleInstruction src,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        var inst = new UndertaleInstruction
        {
            Kind = src.Kind,
            Type1 = src.Type1,
            Type2 = src.Type2,
            TypeInst = src.TypeInst,
            ComparisonKind = src.ComparisonKind,
            JumpOffset = src.JumpOffset,
            ArgumentsCount = src.ArgumentsCount,
            Extra = src.Extra,
            SwapExtra = src.SwapExtra,
            ExtendedKind = src.ExtendedKind,
            ReferenceType = src.ReferenceType,
        };

        var instType = GetInstructionType(src.Kind);

        switch (instType)
        {
            case InstructionType.PushInstruction:
                RemapPush(src, inst, moddedData, targetData, targetFunctions);
                break;

            case InstructionType.PopInstruction:
                RemapPop(src, inst, moddedData, targetData);
                break;

            case InstructionType.CallInstruction:
                RemapCall(src, inst, moddedData, targetData, targetFunctions);
                break;

            case InstructionType.BreakInstruction:
                RemapBreak(src, inst, moddedData, targetData, targetFunctions);
                break;

            default:
                // SingleType, DoubleType, Comparison, Goto — no references to remap
                inst.ValueShort = src.ValueShort;
                break;
        }

        return inst;
    }

    private static void RemapPush(
        UndertaleInstruction src,
        UndertaleInstruction inst,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        switch (src.Type1)
        {
            case DataType.String:
                var srcStr = src.ValueString?.Resource?.Content;
                if (srcStr is not null)
                {
                    var targetStr = targetData.Strings.MakeString(srcStr);
                    inst.ValueString = new UndertaleResourceById<UndertaleString, UndertaleChunkSTRG>(targetStr);
                }
                break;

            case DataType.Variable:
                RemapVariable(src, inst, moddedData, targetData);
                break;

            case DataType.Int32:
                if (src.ValueFunction is { } pushFunc)
                {
                    inst.ValueFunction = EnsureFunction(pushFunc.Name?.Content, targetData, targetFunctions);
                }
                else if (src.ValueVariable is { } pushVar)
                {
                    inst.ValueVariable = EnsureVariable(
                        pushVar.Name?.Content, pushVar.InstanceType, targetData);
                }
                else
                {
                    inst.ValueInt = src.ValueInt;
                }
                break;

            case DataType.Int16:
                inst.ValueShort = src.ValueShort;
                break;

            case DataType.Int64:
                inst.ValueLong = src.ValueLong;
                break;

            case DataType.Double:
                inst.ValueDouble = src.ValueDouble;
                break;

            case DataType.Boolean:
                inst.ValueInt = src.ValueInt;
                break;

            default:
                inst.ValueInt = src.ValueInt;
                break;
        }
    }

    private static void RemapPop(
        UndertaleInstruction src,
        UndertaleInstruction inst,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        if (src.Type1 == DataType.Int16)
        {
            inst.SwapExtra = src.SwapExtra;
            return;
        }

        RemapVariable(src, inst, moddedData, targetData);
    }

    private static void RemapCall(
        UndertaleInstruction src,
        UndertaleInstruction inst,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        if (src.ValueFunction is { } func)
        {
            inst.ValueFunction = EnsureFunction(func.Name?.Content, targetData, targetFunctions);
        }
    }

    private static void RemapBreak(
        UndertaleInstruction src,
        UndertaleInstruction inst,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        if (src.Type1 == DataType.Int32)
        {
            if (src.ValueFunction is { } func)
            {
                inst.ValueFunction = EnsureFunction(func.Name?.Content, targetData, targetFunctions);
            }
            else
            {
                inst.IntArgument = src.IntArgument;
            }
        }
    }

    private static void RemapVariable(
        UndertaleInstruction src,
        UndertaleInstruction inst,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        var srcVar = src.ValueVariable;
        if (srcVar is null) return;

        inst.ValueVariable = EnsureVariable(
            srcVar.Name?.Content, srcVar.InstanceType, targetData);
    }

    /// <summary>
    /// Ensure a variable exists in the target. Variables are safe to create
    /// because they're just named storage slots — the runner doesn't need
    /// pre-defined implementations for them.
    /// </summary>
    private static UndertaleVariable EnsureVariable(
        string? name,
        InstanceType instanceType,
        UndertaleData targetData)
    {
        if (name is null)
            throw new InvalidOperationException("Cannot remap variable with null name.");

        if (targetData.Variables is not null)
        {
            foreach (var v in targetData.Variables)
            {
                if (v.Name?.Content == name && v.InstanceType == instanceType)
                    return v;
            }
        }

        var nameStr = targetData.Strings.MakeString(name);
        var newVar = new UndertaleVariable
        {
            Name = nameStr,
            InstanceType = instanceType,
            VarID = targetData.Variables?.Count ?? 0,
        };

        targetData.Variables?.Add(newVar);
        return newVar;
    }

    /// <summary>
    /// Find or create a function in the target. Only script-defined functions
    /// (gml_*) are created on demand. Engine built-ins must already exist.
    /// The validation pass ensures we never reach this for missing built-ins.
    /// </summary>
    private static UndertaleFunction EnsureFunction(
        string? name,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        if (name is null)
            throw new InvalidOperationException("Cannot remap function with null name.");

        // Look for existing function
        if (targetData.Functions is not null)
        {
            foreach (var f in targetData.Functions)
            {
                if (f.Name?.Content == name)
                    return f;
            }
        }

        // Only create if it's a script function (safe to add)
        var nameStr = targetData.Strings.MakeString(name);
        var newFunc = new UndertaleFunction
        {
            Name = nameStr,
        };

        targetData.Functions?.Add(newFunc);
        targetFunctions.Add(name);
        return newFunc;
    }
}
