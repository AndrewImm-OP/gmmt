using Gmmt.Core.Indexing;
using UndertaleModLib.Models;

namespace Gmmt.Diff;

/// <summary>
/// Computes string-level diffs between two versions of a code entry.
/// Uses ordinal-based alignment (same count) or LCS alignment (different count).
/// </summary>
public static class StringDiffAlgorithm
{
    /// <summary>
    /// Diff string references for a single code entry, identified by name.
    /// </summary>
    public static CodeStringDiffResult DiffCodeEntry(
        string codeEntryName,
        NameIndex vanillaIdx,
        NameIndex moddedIdx)
    {
        bool vanillaHas = vanillaIdx.Code.TryGetValue(codeEntryName, out var vanillaEntry);
        bool moddedHas = moddedIdx.Code.TryGetValue(codeEntryName, out var moddedEntry);

        if (!vanillaHas && !moddedHas)
        {
            return new CodeStringDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeStringDiffStatus.CodeEntryMissing,
                Changes = [],
                Diagnostic = "Code entry not found in either archive.",
                VanillaStringRefCount = 0,
                ModdedStringRefCount = 0,
            };
        }

        if (!vanillaHas)
        {
            return new CodeStringDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeStringDiffStatus.CodeEntryMissing,
                Changes = [],
                Diagnostic = "Code entry exists only in modded archive (new code — not a string diff).",
                VanillaStringRefCount = 0,
                ModdedStringRefCount = moddedHas ? StringReferenceExtractor.Extract(moddedEntry.Res).Count : 0,
            };
        }

        if (!moddedHas)
        {
            return new CodeStringDiffResult
            {
                CodeEntryName = codeEntryName,
                Status = CodeStringDiffStatus.CodeEntryMissing,
                Changes = [],
                Diagnostic = "Code entry exists only in vanilla archive (deleted by mod).",
                VanillaStringRefCount = StringReferenceExtractor.Extract(vanillaEntry.Res).Count,
                ModdedStringRefCount = 0,
            };
        }

        var vanillaRefs = StringReferenceExtractor.Extract(vanillaEntry.Res);
        var moddedRefs = StringReferenceExtractor.Extract(moddedEntry.Res);

        if (vanillaRefs.Count == moddedRefs.Count)
            return DiffSameCount(codeEntryName, vanillaRefs, moddedRefs);
        else
            return DiffDifferentCount(codeEntryName, vanillaRefs, moddedRefs);
    }

    /// <summary>
    /// Diff all code entries between vanilla and modded, returning only those with changes.
    /// </summary>
    public static List<CodeStringDiffResult> DiffAll(NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var results = new List<CodeStringDiffResult>();

        // Check all code entries present in either archive
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in vanillaIdx.Code.Keys) allNames.Add(name);
        foreach (var name in moddedIdx.Code.Keys) allNames.Add(name);

        foreach (var name in allNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            // Skip child entries — their string refs are a subset of the parent's
            // instruction stream and will be covered when the parent is processed.
            // Processing them separately would cause double-patching.
            bool vanillaHas = vanillaIdx.Code.TryGetValue(name, out var vanillaEntry);
            bool moddedHas = moddedIdx.Code.TryGetValue(name, out var moddedEntry);

            if (vanillaHas && vanillaEntry.Res.ParentEntry is not null)
                continue;
            if (moddedHas && moddedEntry.Res.ParentEntry is not null)
                continue;

            var result = DiffCodeEntry(name, vanillaIdx, moddedIdx);
            // Only include if there are actual changes or a non-Ok status
            if (result.Changes.Count > 0 || result.Status != CodeStringDiffStatus.Ok)
                results.Add(result);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────
    // Same-count alignment: positional match by ordinal
    // ─────────────────────────────────────────────────────────

    private static CodeStringDiffResult DiffSameCount(
        string codeEntryName,
        List<StringRef> vanillaRefs,
        List<StringRef> moddedRefs)
    {
        var changes = new List<StringChange>();
        bool hasAmbiguous = false;

        for (int i = 0; i < vanillaRefs.Count; i++)
        {
            var v = vanillaRefs[i];
            var m = moddedRefs[i];

            if (v.Content == m.Content)
                continue;

            var vCtx = StringReferenceExtractor.GetFingerprint(vanillaRefs, i);
            var mCtx = StringReferenceExtractor.GetFingerprint(moddedRefs, i);

            var confidence = FingerprintMatches(vCtx, mCtx)
                ? StringMatchConfidence.ExactOrdinalAndContext
                : StringMatchConfidence.ExactOrdinalOnly;

            changes.Add(new StringChange
            {
                CodeEntryName = codeEntryName,
                ModdedOrdinal = m.Ordinal,
                VanillaOrdinal = v.Ordinal,
                OldContent = v.Content,
                NewContent = m.Content,
                ModdedContext = mCtx,
                Confidence = confidence,
            });
        }

        return new CodeStringDiffResult
        {
            CodeEntryName = codeEntryName,
            Status = hasAmbiguous ? CodeStringDiffStatus.Ambiguous : CodeStringDiffStatus.Ok,
            Changes = changes,
            VanillaStringRefCount = vanillaRefs.Count,
            ModdedStringRefCount = moddedRefs.Count,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Different-count alignment: LCS on string content
    // ─────────────────────────────────────────────────────────

    private static CodeStringDiffResult DiffDifferentCount(
        string codeEntryName,
        List<StringRef> vanillaRefs,
        List<StringRef> moddedRefs)
    {
        var alignment = ComputeLcsAlignment(vanillaRefs, moddedRefs);
        var changes = new List<StringChange>();
        bool hasAmbiguous = false;
        bool hasUnresolved = false;

        foreach (var (vanIdx, modIdx) in alignment)
        {
            if (vanIdx.HasValue && modIdx.HasValue)
            {
                // Aligned pair — check for content change
                var v = vanillaRefs[vanIdx.Value];
                var m = moddedRefs[modIdx.Value];

                if (v.Content != m.Content)
                {
                    var mCtx = StringReferenceExtractor.GetFingerprint(moddedRefs, modIdx.Value);

                    // In count-mismatch mode, ordinals may not correspond.
                    // Use ContentMatchOnly confidence since the alignment is heuristic.
                    changes.Add(new StringChange
                    {
                        CodeEntryName = codeEntryName,
                        ModdedOrdinal = m.Ordinal,
                        VanillaOrdinal = v.Ordinal,
                        OldContent = v.Content,
                        NewContent = m.Content,
                        ModdedContext = mCtx,
                        Confidence = StringMatchConfidence.ContentMatchOnly,
                    });
                }
            }
            else if (vanIdx.HasValue && !modIdx.HasValue)
            {
                // Vanilla string ref with no modded counterpart — removed by mod.
                // Not a "string change" — this is a structural code change.
                // We note it but don't produce a StringChange (code diff handles it).
            }
            else if (!vanIdx.HasValue && modIdx.HasValue)
            {
                // Modded string ref with no vanilla counterpart — added by mod.
                // Same: structural code change, not a string content change.
            }
        }

        // Check for ambiguity: if LCS aligned a vanilla ref to a modded ref
        // but the vanilla content appears multiple times, the alignment may be wrong
        if (changes.Count > 0)
        {
            var vanillaContentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in vanillaRefs)
                vanillaContentCounts[r.Content] = vanillaContentCounts.GetValueOrDefault(r.Content) + 1;

            foreach (var change in changes)
            {
                if (vanillaContentCounts.GetValueOrDefault(change.OldContent) > 1)
                {
                    hasAmbiguous = true;
                    // Downgrade confidence
                    changes[changes.IndexOf(change)] = change with
                    {
                        Confidence = StringMatchConfidence.Ambiguous,
                    };
                }
            }
        }

        var status = CodeStringDiffStatus.Ok;
        string? diagnostic = null;

        if (hasAmbiguous)
        {
            status = CodeStringDiffStatus.Ambiguous;
            diagnostic = $"String ref count differs ({vanillaRefs.Count}→{moddedRefs.Count}) " +
                         "and some alignments are ambiguous due to duplicate content.";
        }
        else if (vanillaRefs.Count != moddedRefs.Count && changes.Count == 0)
        {
            // Count differs but no content changes detected — purely structural
            status = CodeStringDiffStatus.Ok;
        }
        else if (vanillaRefs.Count != moddedRefs.Count)
        {
            // Count differs with changes — flag for review
            status = CodeStringDiffStatus.CountMismatchUnresolved;
            diagnostic = $"String ref count differs ({vanillaRefs.Count}→{moddedRefs.Count}). " +
                         "Changes detected via LCS alignment but may not be reliable.";
        }

        return new CodeStringDiffResult
        {
            CodeEntryName = codeEntryName,
            Status = status,
            Changes = changes,
            Diagnostic = diagnostic,
            VanillaStringRefCount = vanillaRefs.Count,
            ModdedStringRefCount = moddedRefs.Count,
        };
    }

    // ─────────────────────────────────────────────────────────
    // LCS alignment
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Computes an alignment between two string-ref sequences using
    /// Longest Common Subsequence on string content.
    /// Returns pairs of (vanillaIndex?, moddedIndex?).
    /// Both null never occurs. Exactly one null means unmatched.
    /// </summary>
    private static List<(int? VanIdx, int? ModIdx)> ComputeLcsAlignment(
        List<StringRef> a,
        List<StringRef> b)
    {
        int m = a.Count;
        int n = b.Count;

        // Build LCS table
        var dp = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (a[i - 1].Content == b[j - 1].Content)
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to find alignment
        var result = new List<(int?, int?)>();
        int ai = m, bi = n;

        // We build the alignment in reverse, then reverse at the end
        var reversed = new List<(int?, int?)>();

        while (ai > 0 && bi > 0)
        {
            if (a[ai - 1].Content == b[bi - 1].Content)
            {
                // Matched pair
                reversed.Add((ai - 1, bi - 1));
                ai--;
                bi--;
            }
            else if (dp[ai - 1, bi] >= dp[ai, bi - 1])
            {
                // Vanilla entry unmatched (present in vanilla, absent in modded)
                reversed.Add((ai - 1, null));
                ai--;
            }
            else
            {
                // Modded entry unmatched (present in modded, absent in vanilla)
                reversed.Add((null, bi - 1));
                bi--;
            }
        }

        // Remaining vanilla entries
        while (ai > 0)
        {
            reversed.Add((ai - 1, null));
            ai--;
        }

        // Remaining modded entries
        while (bi > 0)
        {
            reversed.Add((null, bi - 1));
            bi--;
        }

        reversed.Reverse();
        return reversed;
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Check if two fingerprints are compatible. Null fields are wildcards.
    /// </summary>
    private static bool FingerprintMatches(ContextFingerprint vanilla, ContextFingerprint modded)
    {
        // Both preceding must match (or one is null = first position)
        if (vanilla.Preceding is not null && modded.Preceding is not null
            && vanilla.Preceding != modded.Preceding)
            return false;

        // Both following must match (or one is null = last position)
        if (vanilla.Following is not null && modded.Following is not null
            && vanilla.Following != modded.Following)
            return false;

        return true;
    }
}
