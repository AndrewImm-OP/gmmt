using Gmmt.Core.Indexing;
using UndertaleModLib.Models;

namespace Gmmt.Diff;

/// <summary>
/// Compares code entries between two archives at the instruction level.
/// Processes root entries and parent entries (which contain child sub-function bytecode).
/// Skips child entries (ParentEntry != null) since their bytecode is covered by the parent.
/// Uses semantic comparison via <see cref="InstructionComparer"/>.
/// </summary>
public static class CodeDiffer
{
    /// <summary>
    /// Diff a single code entry by name.
    /// Skips non-root entries (returns CodeEntryMissing with diagnostic).
    /// </summary>
    public static CodeDiffResult DiffCodeEntry(
        string codeEntryName,
        NameIndex vanillaIdx,
        NameIndex moddedIdx)
    {
        bool vanillaHas = vanillaIdx.Code.TryGetValue(codeEntryName, out var vanillaEntry);
        bool moddedHas = moddedIdx.Code.TryGetValue(codeEntryName, out var moddedEntry);

        // Neither archive has this code entry
        if (!vanillaHas && !moddedHas)
        {
            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.CodeEntryMissing,
                Differences = [],
                Diagnostic = "Code entry not found in either archive.",
            };
        }

        // Only in modded
        if (!vanillaHas)
        {
            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.CodeEntryMissing,
                Differences = [],
                Diagnostic = "Code entry exists only in modded archive (new code).",
                ModdedInstructionCount = moddedEntry.Res.Instructions.Count,
            };
        }

        // Only in vanilla
        if (!moddedHas)
        {
            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.CodeEntryMissing,
                Differences = [],
                Diagnostic = "Code entry exists only in vanilla archive (deleted by mod).",
                VanillaInstructionCount = vanillaEntry.Res.Instructions.Count,
            };
        }

        var vanillaCode = vanillaEntry.Res;
        var moddedCode = moddedEntry.Res;

        // Skip child entries — their bytecode is inside the parent entry.
        // Parent entries (with children) ARE processed since they contain the full bytecode.
        if (IsChildEntry(vanillaCode) || IsChildEntry(moddedCode))
        {
            var reason = IsChildEntry(vanillaCode) && IsChildEntry(moddedCode)
                ? "Child code entry (has parent) — bytecode covered by parent entry."
                : IsChildEntry(vanillaCode)
                    ? "Code entry is a child entry in vanilla archive."
                    : "Code entry is a child entry in modded archive.";

            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.CodeEntryMissing,
                Differences = [],
                Diagnostic = reason,
                VanillaInstructionCount = vanillaCode.Instructions.Count,
                ModdedInstructionCount = moddedCode.Instructions.Count,
            };
        }

        // Build snapshots
        var vanillaSnap = CodeSnapshotBuilder.Build(vanillaCode);
        var moddedSnap = CodeSnapshotBuilder.Build(moddedCode);

        // Check for unsupported instructions
        if (vanillaSnap.HasUnsupported || moddedSnap.HasUnsupported)
        {
            var diffs = CollectDifferences(vanillaSnap, moddedSnap);
            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.Unsupported,
                Differences = diffs,
                VanillaInstructionCount = vanillaSnap.Instructions.Count,
                ModdedInstructionCount = moddedSnap.Instructions.Count,
                Diagnostic = "One or more instructions could not be fully resolved for semantic comparison.",
            };
        }

        // Compare instruction by instruction
        var differences = CollectDifferences(vanillaSnap, moddedSnap);

        if (differences.Count == 0)
        {
            return new CodeDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeDiffStatus.Equal,
                Differences = [],
                VanillaInstructionCount = vanillaSnap.Instructions.Count,
                ModdedInstructionCount = moddedSnap.Instructions.Count,
            };
        }

        return new CodeDiffResult
        {
            CodeEntryName = codeEntryName,
            Status = CodeDiffStatus.Modified,
            Differences = differences,
            VanillaInstructionCount = vanillaSnap.Instructions.Count,
            ModdedInstructionCount = moddedSnap.Instructions.Count,
        };
    }

    /// <summary>
    /// Diff all root code entries between vanilla and modded archives.
    /// Only returns entries that are not Equal.
    /// </summary>
    public static List<CodeDiffResult> DiffAll(NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var results = new List<CodeDiffResult>();
        var allNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in vanillaIdx.Code.Keys) allNames.Add(name);
        foreach (var name in moddedIdx.Code.Keys) allNames.Add(name);

        foreach (var name in allNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Skip child entries early — their bytecode is inside the parent
            bool vanillaHas = vanillaIdx.Code.TryGetValue(name, out var vanillaEntry);
            bool moddedHas = moddedIdx.Code.TryGetValue(name, out var moddedEntry);

            if (vanillaHas && IsChildEntry(vanillaEntry.Res))
                continue;
            if (moddedHas && IsChildEntry(moddedEntry.Res))
                continue;

            var result = DiffCodeEntry(name, vanillaIdx, moddedIdx);

            // Only include non-equal results
            if (result.Status != CodeDiffStatus.Equal)
                results.Add(result);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// A child entry shares bytecode with its parent.
    /// We skip these because the parent entry contains the full instruction stream.
    /// </summary>
    private static bool IsChildEntry(UndertaleCode code)
    {
        return code.ParentEntry is not null;
    }

    /// <summary>
    /// Collect all instruction-level differences between two snapshots.
    /// Handles both same-length and different-length instruction lists.
    /// </summary>
    private static List<InstructionDiff> CollectDifferences(CodeSnapshot vanilla, CodeSnapshot modded)
    {
        var diffs = new List<InstructionDiff>();
        int maxLen = Math.Max(vanilla.Instructions.Count, modded.Instructions.Count);

        for (int i = 0; i < maxLen; i++)
        {
            var v = i < vanilla.Instructions.Count ? vanilla.Instructions[i] : null;
            var m = i < modded.Instructions.Count ? modded.Instructions[i] : null;

            if (v is null && m is not null)
            {
                diffs.Add(new InstructionDiff
                {
                    Index = i,
                    Vanilla = null,
                    Modded = m,
                    Description = $"Added: {m.Kind} (modded has extra instructions)",
                });
            }
            else if (v is not null && m is null)
            {
                diffs.Add(new InstructionDiff
                {
                    Index = i,
                    Vanilla = v,
                    Modded = null,
                    Description = $"Removed: {v.Kind} (vanilla has extra instructions)",
                });
            }
            else if (v is not null && m is not null && !InstructionComparer.AreEqual(v, m))
            {
                var desc = InstructionComparer.DescribeDifference(v, m)
                           ?? $"Instructions differ at index {i}";

                diffs.Add(new InstructionDiff
                {
                    Index = i,
                    Vanilla = v,
                    Modded = m,
                    Description = desc,
                });
            }
        }

        return diffs;
    }
}
