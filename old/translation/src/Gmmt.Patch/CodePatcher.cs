using Gmmt.Diff;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

public static class CodePatcher
{
    public static CodePatchResult ApplySingle(
        UndertaleData data,
        CodePatchRequest request)
    {
        return ApplyMany(data, [request])[0];
    }

    public static IReadOnlyList<CodePatchResult> ApplyMany(
        UndertaleData data,
        IReadOnlyList<CodePatchRequest> requests)
    {
        var results = new CodePatchResult[requests.Count];
        var refCounts = StringRefCounter.CountAllReferences(data);

        for (int i = 0; i < requests.Count; i++)
        {
            results[i] = ApplyOne(data, requests[i], refCounts);
        }

        return results;
    }

    private static CodePatchResult ApplyOne(
        UndertaleData data,
        CodePatchRequest request,
        Dictionary<UndertaleString, int> refCounts)
    {
        var code = data.Code?.FirstOrDefault(c => c.Name?.Content == request.CodeName);
        if (code is null)
        {
            return new CodePatchResult(
                Status: CodePatchStatus.NotFound,
                CodeName: request.CodeName,
                InstructionCount: 0,
                Message: "Code entry not found");
        }

        // Skip child entries — their bytecode is inside the parent entry.
        // Parent entries (with children) are allowed: they contain the full instruction stream
        // including all child sub-functions, so patching the parent covers everything.
        if (code.ParentEntry is not null)
        {
            return new CodePatchResult(
                Status: CodePatchStatus.Unsupported,
                CodeName: request.CodeName,
                InstructionCount: code.Instructions?.Count ?? 0,
                Message: "Code entry has ParentEntry — child entries are covered by parent patch");
        }

        var instructions = code.Instructions;
        if (instructions is null)
        {
            return new CodePatchResult(
                Status: CodePatchStatus.Unsupported,
                CodeName: request.CodeName,
                InstructionCount: 0,
                Message: "Code entry has no instructions");
        }

        var snapshot = request.Snapshot;

        if (snapshot.HasUnsupported)
        {
            return new CodePatchResult(
                Status: CodePatchStatus.Unsupported,
                CodeName: request.CodeName,
                InstructionCount: instructions.Count,
                Message: "Snapshot contains unsupported instructions");
        }

        // Validate structure and collect string patches.
        // If instruction counts match, do strict 1:1 comparison.
        // If counts differ, fall back to best-effort string-only matching.
        var stringPatches = new List<(int Index, UndertaleInstruction Instr, string NewContent)>();
        bool structureMismatch = false;

        if (instructions.Count == snapshot.Instructions.Count)
        {
            // Strict path: 1:1 instruction comparison
            for (int idx = 0; idx < instructions.Count; idx++)
            {
                var instr = instructions[idx];
                var snap = snapshot.Instructions[idx];

                if (!StructureMatches(instr, snap, out _))
                {
                    structureMismatch = true;
                    break;
                }

                var currentStr = instr.ValueString?.Resource;
                if (snap.StringValue is not null)
                {
                    if (currentStr is null)
                    {
                        structureMismatch = true;
                        break;
                    }

                    if (currentStr.Content != snap.StringValue)
                    {
                        stringPatches.Add((idx, instr, snap.StringValue));
                    }
                }
            }
        }
        else
        {
            structureMismatch = true;
        }

        // If strict matching failed, try best-effort: match string instructions by ordinal position.
        // Collect all push.s string instructions from both target and snapshot, pair them by ordinal,
        // and patch any that differ.
        if (structureMismatch)
        {
            stringPatches.Clear();

            var targetStrings = new List<(int Index, UndertaleInstruction Instr, string Content)>();
            for (int idx = 0; idx < instructions.Count; idx++)
            {
                var instr = instructions[idx];
                if (instr.Kind == UndertaleInstruction.Opcode.Push &&
                    instr.Type1 == UndertaleInstruction.DataType.String &&
                    instr.ValueString?.Resource?.Content is { } content)
                {
                    targetStrings.Add((idx, instr, content));
                }
            }

            var snapshotStrings = new List<(int Index, string Content)>();
            for (int idx = 0; idx < snapshot.Instructions.Count; idx++)
            {
                var snap = snapshot.Instructions[idx];
                if (snap.Kind == UndertaleInstruction.Opcode.Push &&
                    snap.Type1 == UndertaleInstruction.DataType.String &&
                    snap.StringValue is not null)
                {
                    snapshotStrings.Add((idx, snap.StringValue));
                }
            }

            // Pair by ordinal position and collect differences
            int pairCount = Math.Min(targetStrings.Count, snapshotStrings.Count);
            for (int i = 0; i < pairCount; i++)
            {
                if (targetStrings[i].Content != snapshotStrings[i].Content)
                {
                    stringPatches.Add((targetStrings[i].Index, targetStrings[i].Instr, snapshotStrings[i].Content));
                }
            }
        }

        if (stringPatches.Count == 0)
        {
            return new CodePatchResult(
                Status: CodePatchStatus.Skipped,
                CodeName: request.CodeName,
                InstructionCount: instructions.Count,
                Message: structureMismatch
                    ? "Structure mismatch but no string differences found via best-effort matching"
                    : "No differences found");
        }

        // Apply string patches
        foreach (var (idx, instr, newContent) in stringPatches)
        {
            var strRef = instr.ValueString;
            if (strRef?.Resource is null)
                continue;

            var currentStr = strRef.Resource;
            refCounts.TryGetValue(currentStr, out var totalRefs);

            // Use same policy as StringPatcher: modify in place if single ref
            if (totalRefs <= 1)
            {
                currentStr.Content = newContent;
            }
            else
            {
                var newString = data.Strings.MakeString(newContent);
                strRef.Resource = newString;
            }
        }

        var mode = structureMismatch ? "best-effort" : "strict";
        return new CodePatchResult(
            Status: CodePatchStatus.Applied,
            CodeName: request.CodeName,
            InstructionCount: instructions.Count,
            Message: $"Applied {stringPatches.Count} string change(s) [{mode}]");
    }

    private static bool StructureMatches(UndertaleInstruction instr, InstructionSnapshot snap, out string mismatch)
    {
        if (instr.Kind != snap.Kind)
        {
            mismatch = $"opcode mismatch: {instr.Kind} vs {snap.Kind}";
            return false;
        }

        if (instr.Type1 != snap.Type1)
        {
            mismatch = $"Type1 mismatch: {instr.Type1} vs {snap.Type1}";
            return false;
        }

        if (instr.Type2 != snap.Type2)
        {
            mismatch = $"Type2 mismatch: {instr.Type2} vs {snap.Type2}";
            return false;
        }

        if (snap.ComparisonKind.HasValue && instr.ComparisonKind != snap.ComparisonKind.Value)
        {
            mismatch = $"ComparisonKind mismatch: {instr.ComparisonKind} vs {snap.ComparisonKind}";
            return false;
        }

        if (snap.JumpOffset.HasValue && instr.JumpOffset != snap.JumpOffset.Value)
        {
            mismatch = $"JumpOffset mismatch: {instr.JumpOffset} vs {snap.JumpOffset}";
            return false;
        }

        if (snap.ArgumentsCount.HasValue && instr.ArgumentsCount != snap.ArgumentsCount.Value)
        {
            mismatch = $"ArgumentsCount mismatch: {instr.ArgumentsCount} vs {snap.ArgumentsCount}";
            return false;
        }

        if (snap.Extra.HasValue && instr.Extra != snap.Extra.Value)
        {
            mismatch = $"Extra mismatch: {instr.Extra} vs {snap.Extra}";
            return false;
        }

        if (snap.ExtendedKind.HasValue && instr.ExtendedKind != snap.ExtendedKind.Value)
        {
            mismatch = $"ExtendedKind mismatch: {instr.ExtendedKind} vs {snap.ExtendedKind}";
            return false;
        }

        // Check variable name match
        if (snap.VariableName is not null)
        {
            var varName = instr.ValueVariable?.Name?.Content;
            if (varName != snap.VariableName)
            {
                mismatch = $"variable name mismatch: '{varName}' vs '{snap.VariableName}'";
                return false;
            }
        }

        // Check function name match
        if (snap.FunctionName is not null)
        {
            var funcName = instr.ValueFunction?.Name?.Content;
            if (funcName != snap.FunctionName)
            {
                mismatch = $"function name mismatch: '{funcName}' vs '{snap.FunctionName}'";
                return false;
            }
        }

        // Check integer value match
        if (snap.IntValue.HasValue)
        {
            long? instrInt = snap.Type1 switch
            {
                UndertaleInstruction.DataType.Int16 => instr.ValueShort,
                UndertaleInstruction.DataType.Int32 => instr.ValueInt,
                UndertaleInstruction.DataType.Int64 => instr.ValueLong,
                _ => null
            };

            if (instrInt != snap.IntValue.Value)
            {
                mismatch = $"integer value mismatch: {instrInt} vs {snap.IntValue}";
                return false;
            }
        }

        // Check double value match
        if (snap.DoubleValue.HasValue && instr.ValueDouble != snap.DoubleValue.Value)
        {
            mismatch = $"double value mismatch: {instr.ValueDouble} vs {snap.DoubleValue}";
            return false;
        }

        // Check boolean value match
        if (snap.BoolValue.HasValue)
        {
            var instrBool = instr.ValueInt != 0;
            if (instrBool != snap.BoolValue.Value)
            {
                mismatch = $"boolean value mismatch: {instrBool} vs {snap.BoolValue}";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }
}
