using Gmmt.Core.Classification;
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
    // GMS2 internal global variables that don't exist in GMS1 runtime.
    // When transplanting code that reads these, we replace the push with a
    // constant 0 (false) to avoid "global variable not set" errors.
    private static readonly HashSet<string> Gms2InternalGlobals = new(StringComparer.Ordinal)
    {
        "__argument_relative",
    };

    // Functions that are safe to create in the target FUNC table even if they
    // don't exist there yet. This includes:
    //  - Script-defined functions (gml_*) — the code is being transplanted
    //  - GMS1 built-in functions — implemented in the runner natively; the FUNC
    //    table entry is just a name reference, the runner resolves by name
    //
    // The ONLY functions we reject are GMS2-only engine functions that have no
    // GMS1 equivalent in the runner.
    private static bool IsScriptFunction(string name) =>
        name.StartsWith("gml_Script_", StringComparison.Ordinal) ||
        name.StartsWith("gml_Object_", StringComparison.Ordinal) ||
        name.StartsWith("gml_Room_", StringComparison.Ordinal) ||
        name.StartsWith("gml_GlobalScript_", StringComparison.Ordinal);

    // GMS2-only function list is now in Gmmt.Core.Classification.Gms2OnlyFunctions (shared).

    /// <summary>
    /// Check if a function can be resolved in GMS1 target — either it already
    /// exists in the FUNC table, or it's a script function being transplanted.
    ///
    /// NOTE: GMS1 built-in functions (random_range, place_meeting, etc.) are NOT
    /// in the FUNC table — the runner resolves them via its internal built-in table.
    /// GMS2 puts ALL functions in FUNC, so GMS2 code that calls built-ins has
    /// FUNC-based references that cannot be transplanted to GMS1 as-is.
    /// </summary>
    private static bool IsFunctionResolvable(string funcName, HashSet<string> targetFunctions)
    {
        // Already in target FUNC table
        if (targetFunctions.Contains(funcName))
            return true;
        // Script functions — being transplanted along with their code
        if (IsScriptFunction(funcName))
            return true;
        // Everything else (built-ins, GMS2-only) — cannot resolve via FUNC table
        return false;
    }

    /// <summary>
    /// Replace bytecode of existing code entries in target with their modded versions.
    /// This is used for entries that the mod modified (not just strings, but logic/bytecode).
    /// Uses the same instruction cloning + reference remapping as TransplantAll.
    ///
    /// Unlike TransplantAll, this uses PERMISSIVE function validation: GMS1 built-in
    /// functions (random_range, place_meeting, etc.) are allowed and added to the
    /// FUNC table. This is safe because:
    /// - The target code entry already exists (runner already has the built-in)
    /// - UTMT needs the function in FUNC list to build the reference chain during
    ///   serialization (SerializeReferenceChain walks Functions list)
    /// - The runner resolves Call instructions by following the reference chain
    ///   to find the function name string, then looks up the built-in by name
    /// </summary>
    public static List<CodeTransplantResult> ReplaceExistingBytecode(
        IReadOnlyList<string> codeNames,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        // Reset per-run state for unwrapping and remapping
        _moddedScriptNames = null;
        _targetScriptIndices = null;
        _totalRemapCount = 0;
        _remapLimit = int.Parse(Environment.GetEnvironmentVariable("GMMT_REMAP_LIMIT") ?? int.MaxValue.ToString());
        _totalUnwrapCount = 0;
        _unwrapLimit = int.Parse(Environment.GetEnvironmentVariable("GMMT_UNWRAP_LIMIT") ?? int.MaxValue.ToString());

        // Build target functions set
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
            // Find in target
            var targetCode = targetData.Code?.FirstOrDefault(c => c.Name?.Content == codeName);
            if (targetCode is null)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.SourceNotFound,
                    Diagnostic = "Code entry not found in target.",
                });
                continue;
            }

            // Find in modded
            var moddedCode = moddedData.Code?.FirstOrDefault(c => c.Name?.Content == codeName);
            if (moddedCode is null)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.SourceNotFound,
                    Diagnostic = "Code entry not found in modded archive.",
                });
                continue;
            }

            // Skip child entries
            if (moddedCode.ParentEntry is not null)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.Unsupported,
                    Diagnostic = "Child code entry — bytecode covered by parent.",
                });
                continue;
            }

            // Skip entries that have child entries in GMS2 — their bytecode
            // includes sub-function code that GMS1 runner can't handle
            if (moddedCode.ChildEntries is not null && moddedCode.ChildEntries.Count > 0)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.Unsupported,
                    Diagnostic = $"Has {moddedCode.ChildEntries.Count} child entries (GMS2.3+ sub-functions).",
                });
                continue;
            }

            // Permissive validation: only reject GMS2-only functions.
            // GMS1 built-ins are allowed — EnsureFunction returns null for them,
            // and RemapCall/RemapPush sets the raw name string ID instead.
            var missingFunc = ValidateFunctionReferencesPermissive(moddedCode.Instructions, targetFunctions);
            if (missingFunc is not null)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.Unsupported,
                    InstructionCount = moddedCode.Instructions?.Count ?? 0,
                    Diagnostic = $"References GMS2-only function '{missingFunc}'.",
                });
                continue;
            }

            try
            {
                var clonedInstructions = CloneInstructions(
                    moddedCode.Instructions, moddedData, targetData, targetFunctions,
                    remapScriptIndices: true);

                targetCode.Replace(clonedInstructions);
                targetCode.LocalsCount = moddedCode.LocalsCount;
                targetCode.ArgumentsCount = moddedCode.ArgumentsCount;

                // Track newly available functions
                if (codeName.StartsWith("gml_", StringComparison.Ordinal))
                    targetFunctions.Add(codeName);

                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.Transplanted,
                    InstructionCount = clonedInstructions.Count,
                });
            }
            catch (Exception ex)
            {
                results.Add(new CodeTransplantResult
                {
                    CodeName = codeName,
                    Status = CodeTransplantStatus.Failed,
                    Diagnostic = $"Exception: {ex.Message}",
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Transplant multiple new code entries from modded to target.
    /// </summary>
    public static List<CodeTransplantResult> TransplantAll(
        IReadOnlyList<string> codeNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        // Reset per-run state
        _nextNonLocalVarId = -1;
        _moddedScriptNames = null;
        _targetScriptIndices = null;
        _totalRemapCount = 0;
        _remapLimit = int.Parse(Environment.GetEnvironmentVariable("GMMT_REMAP_LIMIT") ?? int.MaxValue.ToString());
        _totalUnwrapCount = 0;
        _unwrapLimit = int.Parse(Environment.GetEnvironmentVariable("GMMT_UNWRAP_LIMIT") ?? int.MaxValue.ToString());

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

            if (funcName is not null && !IsFunctionResolvable(funcName, targetFunctions))
            {
                return funcName;
            }
        }

        return null;
    }

    /// <summary>
    /// Permissive version of ValidateFunctionReferences for bytecode replacement.
    /// Only rejects GMS2-only functions. Everything else (GMS1 built-ins, scripts)
    /// is allowed — EnsureFunction will add them to the FUNC table.
    /// </summary>
    private static string? ValidateFunctionReferencesPermissive(
        IReadOnlyList<UndertaleInstruction>? instructions,
        HashSet<string> targetFunctions)
    {
        if (instructions is null) return null;

        foreach (var inst in instructions)
        {
            string? funcName = null;

            if (inst.ValueFunction is { } func)
                funcName = func.Name?.Content;

            if (funcName is not null &&
                !targetFunctions.Contains(funcName) &&
                !IsScriptFunction(funcName) &&
                Gms2OnlyFunctions.Names.Contains(funcName))
            {
                return funcName;
            }
        }

        return null;
    }

    /// <summary>
    /// Clone a list of instructions, remapping all string/variable/function references.
    /// When remapScriptIndices is true, also remaps resource indices in script_execute calls.
    /// This should only be true for ReplaceExistingBytecode (modifying existing entries),
    /// NOT for TransplantAll (new entries where modded indices are used directly).
    /// </summary>
    private static List<UndertaleInstruction> CloneInstructions(
        IReadOnlyList<UndertaleInstruction> srcInstructions,
        UndertaleData moddedData,
        UndertaleData targetData,
        HashSet<string> targetFunctions,
        bool remapScriptIndices = false)
    {
        var result = new List<UndertaleInstruction>(srcInstructions.Count);

        foreach (var src in srcInstructions)
        {
            var clone = CloneInstruction(src, moddedData, targetData, targetFunctions);
            result.Add(clone);
        }

        // Post-pass: remap script indices in script_execute calls.
        // Only for bytecode replacement of existing entries — modded script indices
        // differ from target indices and need remapping.
        if (remapScriptIndices)
        {
            UnwrapMacrosScriptExecute(result, moddedData, targetData);
            RemapScriptExecuteIndices(result, moddedData, targetData);
        }

        return result;
    }

    /// <summary>
    /// Build a script name→index mapping for the given data.
    /// </summary>
    private static Dictionary<string, int> BuildScriptIndexMap(UndertaleData data)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        if (data.Scripts is not null)
        {
            for (int i = 0; i < data.Scripts.Count; i++)
            {
                var name = data.Scripts[i].Name?.Content;
                if (name is not null)
                    map[name] = i;
            }
        }
        return map;
    }

    /// <summary>
    /// Build a reverse script index→name mapping for the given data.
    /// </summary>
    private static Dictionary<int, string> BuildScriptNameMap(UndertaleData data)
    {
        var map = new Dictionary<int, string>();
        if (data.Scripts is not null)
        {
            for (int i = 0; i < data.Scripts.Count; i++)
            {
                var name = data.Scripts[i].Name?.Content;
                if (name is not null)
                    map[i] = name;
            }
        }
        return map;
    }

    // Lazily built maps
    private static Dictionary<int, string>? _moddedScriptNames;
    private static Dictionary<string, int>? _targetScriptIndices;
    private static int _totalUnwrapCount = 0;
    private static int _unwrapLimit = int.MaxValue; // GMMT_UNWRAP_LIMIT for bisecting

    /// <summary>
    /// Get the number of stack values produced by an instruction.
    /// </summary>
    private static int GetStackProduced(UndertaleInstruction inst)
    {
        switch (inst.Kind)
        {
            case Opcode.Push:
            case Opcode.PushI:
            case Opcode.PushLoc:
            case Opcode.PushGlb:
            case Opcode.PushBltn:
                return 1;
            case Opcode.Conv:
                // Conv pops one, pushes one (type conversion in-place)
                return 1;
            case Opcode.Dup:
                // Dup duplicates top of stack
                return 2; // pops 1, pushes 2 (net +1, but we track gross)
            case Opcode.Call:
                // Call consumes args and produces 1 return value
                return 1;
            case Opcode.Add:
            case Opcode.Sub:
            case Opcode.Mul:
            case Opcode.Div:
            case Opcode.Rem:
            case Opcode.Mod:
            case Opcode.And:
            case Opcode.Or:
            case Opcode.Xor:
            case Opcode.Shl:
            case Opcode.Shr:
            case Opcode.Cmp:
                // Binary ops: pop 2, push 1
                return 1;
            case Opcode.Neg:
            case Opcode.Not:
                // Unary ops: pop 1, push 1
                return 1;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Get the number of stack values consumed by an instruction.
    /// </summary>
    private static int GetStackConsumed(UndertaleInstruction inst)
    {
        switch (inst.Kind)
        {
            case Opcode.Push:
            case Opcode.PushI:
            case Opcode.PushLoc:
            case Opcode.PushGlb:
            case Opcode.PushBltn:
                return 0; // Push instructions consume nothing from stack
            case Opcode.Conv:
                return 1;
            case Opcode.Dup:
                return 1;
            case Opcode.Call:
                return inst.ArgumentsCount;
            case Opcode.Add:
            case Opcode.Sub:
            case Opcode.Mul:
            case Opcode.Div:
            case Opcode.Rem:
            case Opcode.Mod:
            case Opcode.And:
            case Opcode.Or:
            case Opcode.Xor:
            case Opcode.Shl:
            case Opcode.Shr:
            case Opcode.Cmp:
                return 2;
            case Opcode.Neg:
            case Opcode.Not:
                return 1;
            case Opcode.Pop:
                return 1;
            case Opcode.Popz:
            case Opcode.Ret:
                return 1;
            case Opcode.Bt:
            case Opcode.Bf:
                return 1;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Detect and unwrap GMS2 'macros' wrapper pattern:
    ///   script_execute(macros, arg1, ..., argN-1, real_script_index)
    /// becomes:
    ///   Call real_script(arg1, ..., argN-1)
    ///
    /// 'macros' is an empty GMS2 compile-time script. In GMS2, this pattern
    /// is used for macro-based dispatch. In GMS1, macros is empty so the call
    /// is a NOP and the real script never executes. We unwrap to restore the
    /// direct call that vanilla GMS1 bytecode uses.
    ///
    /// IMPORTANT: Removing instructions changes byte addresses and therefore
    /// invalidates JumpOffset on all branch instructions (B, Bt, Bf, PushEnv,
    /// PopEnv) that cross the removed region. This method uses a two-pass
    /// approach: first mark all removals + replacements, then rebuild the
    /// instruction list with corrected branch offsets.
    /// </summary>
    private static void UnwrapMacrosScriptExecute(
        List<UndertaleInstruction> instructions,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        _moddedScriptNames ??= BuildScriptNameMap(moddedData);
        _targetScriptIndices ??= BuildScriptIndexMap(targetData);

        // Find the modded index of 'macros' script
        int macrosModdedIndex = -1;
        foreach (var kvp in _moddedScriptNames)
        {
            if (kvp.Value == "macros") { macrosModdedIndex = kvp.Key; break; }
        }
        if (macrosModdedIndex < 0) return; // no macros script in mod

        // --- Pass 1: Identify all unwraps, mark removals and replacements ---

        // Indices to remove (set of instruction indices)
        var toRemove = new HashSet<int>();
        // Replacement Call instructions: index → new instruction
        var replacements = new Dictionary<int, UndertaleInstruction>();

        int unwrapCount = 0;

        for (int i = 0; i < instructions.Count; i++)
        {
            var inst = instructions[i];
            if (inst.Kind != Opcode.Call) continue;
            if (inst.ValueFunction?.Name?.Content != "script_execute") continue;

            int argCount = inst.ArgumentsCount;
            if (argCount < 3) continue; // need at least macros + 1 real arg + real_script

            // Walk backwards to find the first argument push (macros index)
            int stackNeeded = argCount;
            int searchIdx = i - 1;
            int firstPushIdx = -1;

            while (searchIdx >= 0 && stackNeeded > 0)
            {
                var si = instructions[searchIdx];
                int net = GetStackProduced(si) - GetStackConsumed(si);
                stackNeeded -= net;
                if (stackNeeded <= 0)
                {
                    firstPushIdx = searchIdx;
                    break;
                }
                searchIdx--;
            }

            if (firstPushIdx < 0) continue;

            var firstPush = instructions[firstPushIdx];
            if (firstPush.Kind != Opcode.PushI && firstPush.Kind != Opcode.Push) continue;
            if (firstPush.Type1 != DataType.Int16 && firstPush.Type1 != DataType.Int32) continue;

            int firstVal = firstPush.Type1 == DataType.Int16 ? firstPush.ValueShort : firstPush.ValueInt;
            if (firstVal != macrosModdedIndex)
            {
                continue; // not a macros call
            }

            // Find the last argument push before Call (this is real_script_index).
            // It's the push right before the optional Conv before Call.
            int lastPushIdx = i - 1;
            if (lastPushIdx >= 0 && instructions[lastPushIdx].Kind == Opcode.Conv)
                lastPushIdx--;
            if (lastPushIdx < 0) continue;

            var lastPush = instructions[lastPushIdx];
            if (lastPush.Kind != Opcode.PushI && lastPush.Kind != Opcode.Push) continue;
            if (lastPush.Type1 != DataType.Int16 && lastPush.Type1 != DataType.Int32) continue;

            int realScriptModdedIndex = lastPush.Type1 == DataType.Int16 ? lastPush.ValueShort : lastPush.ValueInt;

            // Look up real script name and target function reference
            if (!_moddedScriptNames.TryGetValue(realScriptModdedIndex, out var realScriptName)) continue;

            // Find the target function for the real script.
            // FUNC table entries may use bare name or gml_Script_ prefix.
            UndertaleFunction? targetFunc = null;
            if (targetData.Functions is not null)
            {
                string gmlName = "gml_Script_" + realScriptName;
                foreach (var f in targetData.Functions)
                {
                    var fname = f.Name?.Content;
                    if (fname == realScriptName || fname == gmlName)
                    {
                        targetFunc = f;
                        break;
                    }
                }
            }
            if (targetFunc == null)
            {
                Console.Error.WriteLine($"  [Unwrap] SKIP: function '{realScriptName}' not found in target FUNC table");
                continue;
            }

            // Check unwrap limit (for bisecting crashes)
            if (_totalUnwrapCount >= _unwrapLimit)
            {
                Console.Error.WriteLine($"  [Unwrap] LIMIT reached ({_unwrapLimit}), skipping remaining");
                break;
            }
            _totalUnwrapCount++;

            // Mark first push (macros) and its Conv for removal
            toRemove.Add(firstPushIdx);
            if (firstPushIdx + 1 < instructions.Count && instructions[firstPushIdx + 1].Kind == Opcode.Conv)
                toRemove.Add(firstPushIdx + 1);

            // Mark last push (real_script) and its Conv for removal
            toRemove.Add(lastPushIdx);
            int convBeforeCall = lastPushIdx + 1;
            if (convBeforeCall < i && instructions[convBeforeCall].Kind == Opcode.Conv)
                toRemove.Add(convBeforeCall);

            // Mark Call for replacement: script_execute → real_script, argCount - 2
            // CRITICAL: Must copy ReferenceType from original instruction — it's stored
            // in the FUNC reference chain during serialization. Without it, the chain
            // entry has type=0 which causes the runner to misinterpret the function ref.
            replacements[i] = new UndertaleInstruction
            {
                Kind = Opcode.Call,
                Type1 = inst.Type1,
                Type2 = inst.Type2,
                TypeInst = inst.TypeInst,
                ComparisonKind = inst.ComparisonKind,
                Extra = inst.Extra,
                SwapExtra = inst.SwapExtra,
                ExtendedKind = inst.ExtendedKind,
                ReferenceType = inst.ReferenceType,
                ArgumentsCount = (ushort)(argCount - 2),
                ValueFunction = targetFunc,
            };

            unwrapCount++;
            Console.Error.WriteLine($"  [Unwrap] script_execute(macros, ..., {realScriptName}) → Call {realScriptName}({argCount - 2} args)");
        }

        if (unwrapCount == 0) return;
        Console.Error.WriteLine($"  [Unwrap] Total: {unwrapCount} macros calls unwrapped");

        // --- Pass 2: Apply replacements and NOP-out removed instructions in-place ---
        //
        // CRITICAL: We must NOT remove instructions from the list because
        // JumpOffset values on branch instructions (B, Bt, Bf, PushEnv, PopEnv)
        // are relative offsets in instruction-size units. Removing instructions
        // would shift addresses and corrupt all branches that cross the removed
        // region. Re-computing branch targets is unreliable because CloneInstruction
        // may have changed some instruction sizes (e.g. __argument_relative → PushI 0)
        // making the JumpOffset values inconsistent with cloned instruction sizes.
        //
        // Instead, we replace removed instructions with size-equivalent NOPs:
        // - PushI (1 unit) + Conv (1 unit) → PushI 0 (1 unit) + Popz (1 unit)
        //   Net stack effect: 0 (push then pop), same as removing the push entirely
        //   since the Call's ArgumentsCount is already reduced by 2.
        // - Standalone PushI/Push without Conv → PushI 0 + Popz won't fit in 1 slot.
        //   For a 1-unit instruction, use Popz as a stack-neutral NOP (pops nothing
        //   if stack is empty — actually Popz always pops 1, so use B +1 to skip).
        //   In practice, PushI is always followed by Conv in the macros pattern.

        // Apply Call replacements (same size: Call with ValueFunction → size 2)
        foreach (var (idx, newInst) in replacements)
            instructions[idx] = newInst;

        // Replace removed instructions with NOPs.
        // Process pairs: if idx and idx+1 are both in toRemove, replace with PushI 0 + Popz.
        // This preserves instruction count and sizes → no JumpOffset patching needed.
        var sortedRemove = toRemove.OrderBy(x => x).ToList();
        int ri = 0;
        while (ri < sortedRemove.Count)
        {
            int idx = sortedRemove[ri];
            var orig = instructions[idx];
            uint origSize = orig.CalculateInstructionSize();

            // Check if next removed index is adjacent (PushI + Conv pair)
            if (ri + 1 < sortedRemove.Count && sortedRemove[ri + 1] == idx + 1)
            {
                var origNext = instructions[idx + 1];
                uint pairSize = origSize + origNext.CalculateInstructionSize();

                if (pairSize == 2)
                {
                    // Replace with PushI 0 (1 unit) + Popz (1 unit) = 2 units, net stack 0
                    instructions[idx] = new UndertaleInstruction
                    {
                        Kind = Opcode.PushI,
                        Type1 = DataType.Int16,
                        ValueShort = 0,
                    };
                    instructions[idx + 1] = new UndertaleInstruction
                    {
                        Kind = Opcode.Popz,
                        Type1 = DataType.Variable,
                        // Type2 must be 0 for SingleTypeInstruction (Popz)
                    };
                }
                else
                {
                    // Unexpected pair size — leave as-is and warn
                    Console.Error.WriteLine($"  [Unwrap] WARN: removed pair at {idx},{idx + 1} has unexpected size {pairSize}, leaving as-is");
                }
                ri += 2;
            }
            else
            {
                // Single instruction removal — should not happen in macros pattern
                // but handle gracefully: if it's 1-unit, we can't safely NOP it
                // without changing stack. Just zero it to PushI 0 + Popz won't fit.
                // Use a B +1 (branch forward 1 unit = skip to next instruction).
                if (origSize == 1)
                {
                    instructions[idx] = new UndertaleInstruction
                    {
                        Kind = Opcode.B,
                        JumpOffset = 1, // skip 1 unit = branch to next instruction
                    };
                }
                else
                {
                    Console.Error.WriteLine($"  [Unwrap] WARN: single removed instruction at {idx} has size {origSize}, cannot NOP safely");
                }
                ri += 1;
            }
        }
    }

    /// <summary>
    /// Scan cloned instructions for script_execute call patterns and remap
    /// the script index argument from modded indices to target indices.
    ///
    /// Uses stack depth tracking to find the FIRST argument pushed (script index),
    /// which may be far from the Call when there are multiple arguments.
    /// </summary>
    private static int _totalRemapCount = 0;
    private static int _remapLimit = int.MaxValue; // Set to limit for bisecting

    private static void RemapScriptExecuteIndices(
        List<UndertaleInstruction> instructions,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        // Lazily build maps
        _moddedScriptNames ??= BuildScriptNameMap(moddedData);
        _targetScriptIndices ??= BuildScriptIndexMap(targetData);

        for (int i = 0; i < instructions.Count; i++)
        {
            var inst = instructions[i];

            // Look for Call instruction to script_execute
            if (inst.Kind != Opcode.Call) continue;
            if (inst.ValueFunction?.Name?.Content != "script_execute") continue;

            // script_execute(script_index, arg1, arg2, ...)
            // ArgumentsCount includes the script_index itself.
            // GML pushes arguments LEFT-TO-RIGHT: script_index is pushed FIRST,
            // then arg1, arg2, ... The script_index push is therefore N arguments back
            // from the Call (where N = ArgumentsCount).
            //
            // We walk backwards from Call using stack depth tracking to find the FIRST
            // argument push (the script index). Only the first argument is remapped —
            // remapping other arguments is too risky (false positives from coincidental
            // integer matches).

            int argCount = inst.ArgumentsCount;
            if (argCount < 1) continue;

            // Walk backwards, tracking net stack effect.
            int stackNeeded = argCount;
            int searchIdx = i - 1;
            int scriptPushIdx = -1;

            while (searchIdx >= 0 && stackNeeded > 0)
            {
                var si = instructions[searchIdx];
                int produced = GetStackProduced(si);
                int consumed = GetStackConsumed(si);
                int net = produced - consumed;
                stackNeeded -= net;
                if (stackNeeded <= 0)
                {
                    scriptPushIdx = searchIdx;
                    break;
                }
                searchIdx--;
            }

            if (scriptPushIdx < 0) continue;

            var pushInst = instructions[scriptPushIdx];
            if (pushInst.Kind != Opcode.PushI && pushInst.Kind != Opcode.Push) continue;
            if (pushInst.Type1 != DataType.Int16 && pushInst.Type1 != DataType.Int32) continue;

            int moddedIndex = pushInst.Type1 == DataType.Int16 ? pushInst.ValueShort : pushInst.ValueInt;

            // Bounds check: must be a valid script index in the modded archive
            if (moddedIndex < 0 || moddedIndex >= (moddedData.Scripts?.Count ?? 0))
                continue;

            // Look up the script name in modded archive
            if (!_moddedScriptNames.TryGetValue(moddedIndex, out var scriptName)) continue;

            int newIndex;
            if (_targetScriptIndices!.TryGetValue(scriptName, out var targetIndex))
            {
                newIndex = targetIndex;
            }
            else
            {
                // Script not found in target by name.
                Console.Error.WriteLine($"  [WARN] Script '{scriptName}' (modded index {moddedIndex}) not found in target by name — skipping remap");
                continue;
            }

            if (moddedIndex != newIndex && _totalRemapCount < _remapLimit)
            {
                _totalRemapCount++;
                Console.Error.WriteLine($"  [Remap#{_totalRemapCount}] {scriptName}: {moddedIndex} → {newIndex} (args={argCount})");

                var replacement = new UndertaleInstruction
                {
                    Kind = pushInst.Kind,
                    Type1 = pushInst.Type1,
                    Type2 = pushInst.Type2,
                    TypeInst = pushInst.TypeInst,
                    ComparisonKind = pushInst.ComparisonKind,
                    JumpOffset = pushInst.JumpOffset,
                    Extra = pushInst.Extra,
                    SwapExtra = pushInst.SwapExtra,
                    ExtendedKind = pushInst.ExtendedKind,
                    ReferenceType = pushInst.ReferenceType,
                };
                if (pushInst.Type1 == DataType.Int16)
                    replacement.ValueShort = (short)newIndex;
                else
                    replacement.ValueInt = newIndex;

                instructions[scriptPushIdx] = replacement;
            }
        }
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
                // GMS2 internal globals: replace push with constant 0 (false)
                if (src.ValueVariable?.Name?.Content is { } pushVarName &&
                    Gms2InternalGlobals.Contains(pushVarName))
                {
                    inst.Kind = Opcode.PushI;
                    inst.Type1 = DataType.Int16;
                    inst.TypeInst = InstanceType.Undefined;
                    inst.ReferenceType = 0;
                    inst.ValueShort = 0;
                    break;
                }
                RemapVariable(src, inst, moddedData, targetData);
                break;

            case DataType.Int32:
                if (src.ValueFunction is { } pushFunc)
                {
                    var resolvedPushFunc = EnsureFunction(pushFunc.Name?.Content, targetData, targetFunctions);
                    if (resolvedPushFunc is not null)
                    {
                        inst.ValueFunction = resolvedPushFunc;
                    }
                    else if (pushFunc.Name?.Content is { } pushFuncName)
                    {
                        // Built-in function: set raw name string ID
                        var nameStr = targetData.Strings.MakeString(pushFuncName);
                        inst.ValueInt = targetData.Strings.IndexOf(nameStr);
                    }
                }
                else if (src.ValueVariable is { } pushVar)
                {
                    inst.TypeInst = RemapInstructionInstanceType(src.TypeInst, moddedData, targetData);
                    inst.ValueVariable = EnsureVariable(
                        pushVar.Name?.Content,
                        NormalizeVariableInstanceType(pushVar.InstanceType),
                        targetData);
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
            var name = func.Name?.Content;
            var resolved = EnsureFunction(name, targetData, targetFunctions);
            if (resolved is not null)
            {
                inst.ValueFunction = resolved;
            }
            else if (name is not null)
            {
                // Built-in function not in FUNC list — set raw name string ID.
                // During serialization, UTMT won't touch this instruction (no ValueFunction),
                // so the raw int32 will be written as-is. The runner reads the name string ID
                // from this value to find the built-in function.
                var nameStr = targetData.Strings.MakeString(name);
                var nameStrId = targetData.Strings.IndexOf(nameStr);
                inst.ValueInt = nameStrId;
            }
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
                var resolvedBreakFunc = EnsureFunction(func.Name?.Content, targetData, targetFunctions);
                if (resolvedBreakFunc is not null)
                {
                    inst.ValueFunction = resolvedBreakFunc;
                }
                else if (func.Name?.Content is { } breakFuncName)
                {
                    var nameStr = targetData.Strings.MakeString(breakFuncName);
                    inst.IntArgument = targetData.Strings.IndexOf(nameStr);
                }
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

        inst.TypeInst = RemapInstructionInstanceType(src.TypeInst, moddedData, targetData);

        // Treat room creation references in GMS2 (which are mapped as ReferenceType.Instance and have a room instance ID as TypeInst)
        // as normal Instance variables in GMS1 if we can't map the instance ID.
        if (inst.ReferenceType == VariableType.Instance && inst.TypeInst == InstanceType.Self && src.TypeInst > 0)
        {
            inst.ReferenceType = VariableType.Normal;
        }

        inst.ValueVariable = EnsureVariable(
            srcVar.Name?.Content,
            NormalizeVariableInstanceType(srcVar.InstanceType),
            targetData);
    }

    private static InstanceType NormalizeVariableInstanceType(InstanceType instanceType)
    {
        // Assembler does the same normalization for object/room instance scoped
        // variables: the VARI entry itself should be Self, while the instruction
        // TypeInst carries the actual object/instance target.
        return instanceType > 0 ? InstanceType.Self : instanceType;
    }

    private static InstanceType RemapInstructionInstanceType(
        InstanceType typeInst,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        if (typeInst <= 0)
            return typeInst;

        int moddedObjectIndex = (int)typeInst;
        if (moddedObjectIndex >= 0 && moddedObjectIndex < moddedData.GameObjects.Count)
        {
            var objectName = moddedData.GameObjects[moddedObjectIndex]?.Name?.Content;
            if (objectName is not null && targetData.GameObjects is not null)
            {
                for (int i = 0; i < targetData.GameObjects.Count; i++)
                {
                    if (targetData.GameObjects[i]?.Name?.Content == objectName)
                        return (InstanceType)i;
                }
            }
        }

        // GMS2 room instance IDs (e.g. 111220) are not valid object indexes in
        // the GMS1 target's variable reference encoding. Falling back to Self is
        // safer than serializing a dangling positive TypeInst that the runner
        // later reports as a null variable name.
        return InstanceType.Self;
    }

    // Tracks the next available VarID for non-Local variables (Self/Global/etc.)
    // This is lazily initialized from the existing target data.
    private static int _nextNonLocalVarId = -1;

    /// <summary>
    /// Ensure a variable exists in the target. Variables are safe to create
    /// because they're just named storage slots — the runner doesn't need
    /// pre-defined implementations for them.
    ///
    /// IMPORTANT: In GMS1, VarID for non-Local (Self/Global) variables is an index
    /// into a single runtime array of size VarCount1. Local variables use a
    /// separate per-frame index. We must assign VarIDs correctly to avoid
    /// out-of-bounds access in the runner.
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

        // Lazily compute the next non-Local VarID from existing data
        if (_nextNonLocalVarId < 0 && targetData.Variables is not null)
        {
            _nextNonLocalVarId = 0;
            foreach (var v in targetData.Variables)
            {
                if (v.InstanceType != InstanceType.Local && v.VarID >= _nextNonLocalVarId)
                    _nextNonLocalVarId = v.VarID + 1;
            }
        }

        var nameStr = targetData.Strings.MakeString(name);
        int varId;

        if (instanceType == InstanceType.Local)
        {
            // Local variables: VarID is a per-code-entry local index.
            // For transplanted code we just use 0 — the actual local index
            // is determined by the CodeLocals entries, not by VarID in VARI.
            varId = 0;
        }
        else
        {
            // Non-Local (Self/Global): allocate the next sequential slot
            varId = _nextNonLocalVarId++;
        }

        var newVar = new UndertaleVariable
        {
            Name = nameStr,
            InstanceType = instanceType,
            VarID = varId,
            NameStringID = targetData.Strings.IndexOf(nameStr),
        };

        targetData.Variables?.Add(newVar);

        // Update VarCount to cover the new maximum VarID
        if (instanceType != InstanceType.Local)
        {
            uint needed = (uint)(varId + 1);
            if (needed > targetData.VarCount1)
            {
                targetData.VarCount1 = needed;
                targetData.VarCount2 = needed;
            }
        }

        return newVar;
    }

    /// <summary>
    /// Find or create a function in the target FUNC table.
    /// - If the function already exists in FUNC → returns it.
    /// - If it's a script function (gml_*) → creates a new FUNC entry and returns it.
    /// - Otherwise (GMS1 built-in not in FUNC) → returns null.
    ///   The caller (RemapCall/RemapPush) handles null by setting the raw name
    ///   string ID directly on the instruction, which the runner resolves at runtime.
    /// </summary>
    private static UndertaleFunction? EnsureFunction(
        string? name,
        UndertaleData targetData,
        HashSet<string> targetFunctions)
    {
        if (name is null)
            throw new InvalidOperationException("Cannot remap function with null name.");

        // Look for existing function in FUNC list
        if (targetData.Functions is not null)
        {
            foreach (var f in targetData.Functions)
            {
                if (f.Name?.Content == name)
                    return f;
            }
        }

        // Only create FUNC entries for script-defined functions.
        // Built-in functions must NOT be added to FUNC (causes SIGSEGV).
        if (!IsScriptFunction(name))
            return null;

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
