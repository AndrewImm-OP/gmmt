using Gmmt.Core.Compatibility;
using Gmmt.Core.Indexing;
using Gmmt.Core.Loading;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Core.Classification;

/// <summary>
/// Pre-flight classifier that analyzes a mod's portability before translation.
///
/// Runs lightweight scans (no full instruction-by-instruction diff) to determine
/// whether automatic translation will produce a useful result.
///
/// Inputs: three loaded archives (vanilla Windows, modded Windows, target Linux)
/// with name indices and compatibility report.
///
/// Output: <see cref="ModClassification"/> with verdict, breakdown, and issues.
/// </summary>
public static class ModClassifier
{
    /// <summary>
    /// Classify the mod's portability.
    /// </summary>
    /// <param name="vanillaMeta">Metadata of the vanilla Windows archive.</param>
    /// <param name="moddedMeta">Metadata of the modded Windows archive.</param>
    /// <param name="targetMeta">Metadata of the target Linux archive.</param>
    /// <param name="vanillaIdx">Name index of the vanilla archive.</param>
    /// <param name="moddedIdx">Name index of the modded archive.</param>
    /// <param name="targetIdx">Name index of the target archive.</param>
    /// <param name="compat">Compatibility report from CompatChecker.</param>
    public static ModClassification Classify(
        ArchiveMetadata vanillaMeta,
        ArchiveMetadata moddedMeta,
        ArchiveMetadata targetMeta,
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        NameIndex targetIdx,
        CompatReport compat)
    {
        var issues = new List<ClassificationIssue>();
        bool targetIsGms1 = !targetMeta.IsGMS2;
        bool moddedIsGms2 = moddedMeta.IsGMS2;

        // ─────────────────────────────────────────────────────
        // Phase 1: Compute resource manifest (fast — name comparisons only)
        // ─────────────────────────────────────────────────────

        var newSprites = CountNew(vanillaIdx.Sprites, moddedIdx.Sprites);
        var newSounds = CountNew(vanillaIdx.Sounds, moddedIdx.Sounds);
        var newFonts = CountNew(vanillaIdx.Fonts, moddedIdx.Fonts);
        var newBackgrounds = CountNew(vanillaIdx.Backgrounds, moddedIdx.Backgrounds);
        var newCode = FindNewNames(vanillaIdx.Code, moddedIdx.Code);
        var newObjects = CountNew(vanillaIdx.Objects, moddedIdx.Objects);
        var newScripts = CountNew(vanillaIdx.Scripts, moddedIdx.Scripts);
        var newRooms = CountNew(vanillaIdx.Rooms, moddedIdx.Rooms);

        // Modified resources (approximated by existence + instruction count diff for code)
        var modifiedCodeNames = FindModifiedCodeNames(vanillaIdx, moddedIdx);

        // ─────────────────────────────────────────────────────
        // Phase 2: Scan new code for GMS2-only signals
        // ─────────────────────────────────────────────────────

        int newCodeWithGms2 = 0;
        int newCodeWithChildren = 0;
        int newCodePortable = 0;
        var gms2FuncCategories = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var codeName in newCode)
        {
            if (!moddedIdx.Code.TryGetValue(codeName, out var entry))
                continue;

            var code = entry.Res;
            bool hasGms2Ref = false;
            bool hasChildren = code.ChildEntries is not null && code.ChildEntries.Count > 0;

            if (hasChildren)
                newCodeWithChildren++;

            if (targetIsGms1)
            {
                var gms2Refs = ScanForGms2OnlyRefs(code);
                if (gms2Refs.Count > 0)
                {
                    hasGms2Ref = true;
                    newCodeWithGms2++;
                    foreach (var funcName in gms2Refs)
                    {
                        var cat = Gms2OnlyFunctions.GetCategory(funcName) ?? "GMS2-Only";
                        gms2FuncCategories[cat] = gms2FuncCategories.GetValueOrDefault(cat) + 1;
                    }
                }
            }

            if (!hasGms2Ref && !hasChildren)
                newCodePortable++;
        }

        // ─────────────────────────────────────────────────────
        // Phase 3: Scan modified code for GMS2-only signals
        // ─────────────────────────────────────────────────────

        int modifiedCodeWithGms2 = 0;
        int modifiedCodeWithChildren = 0;

        foreach (var codeName in modifiedCodeNames)
        {
            if (!moddedIdx.Code.TryGetValue(codeName, out var entry))
                continue;

            var code = entry.Res;

            if (code.ChildEntries is not null && code.ChildEntries.Count > 0)
                modifiedCodeWithChildren++;

            if (targetIsGms1)
            {
                var gms2Refs = ScanForGms2OnlyRefs(code);
                if (gms2Refs.Count > 0)
                {
                    modifiedCodeWithGms2++;
                    foreach (var funcName in gms2Refs)
                    {
                        var cat = Gms2OnlyFunctions.GetCategory(funcName) ?? "GMS2-Only";
                        gms2FuncCategories[cat] = gms2FuncCategories.GetValueOrDefault(cat) + 1;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────────────
        // Phase 4: Collect issues
        // ─────────────────────────────────────────────────────

        // -- Blockers --

        if (vanillaMeta.IsYYC)
            issues.Add(MakeIssue(ClassificationSeverity.Blocker, "YYC001", "Runtime",
                "Vanilla Windows archive is YYC-compiled (no bytecode). Mod translation requires VM-compiled archives."));

        if (targetMeta.IsYYC)
            issues.Add(MakeIssue(ClassificationSeverity.Blocker, "YYC002", "Runtime",
                "Target Linux archive is YYC-compiled (no bytecode). Mod translation requires VM-compiled archives."));

        if (vanillaMeta.BytecodeVersion < 15)
            issues.Add(MakeIssue(ClassificationSeverity.Blocker, "BC001", "Runtime",
                $"Bytecode version {vanillaMeta.BytecodeVersion} is below minimum supported (15)."));

        if (vanillaMeta.BytecodeVersion != targetMeta.BytecodeVersion)
            issues.Add(MakeIssue(ClassificationSeverity.Blocker, "BC002", "Runtime",
                $"Bytecode version mismatch: vanilla={vanillaMeta.BytecodeVersion}, target={targetMeta.BytecodeVersion}."));

        // GMS2-only function dominance check
        int totalNewAndModifiedCode = newCode.Count + modifiedCodeNames.Count;
        int totalUnsupportedCode = newCodeWithGms2 + newCodeWithChildren + modifiedCodeWithGms2 + modifiedCodeWithChildren;

        if (targetIsGms1 && totalNewAndModifiedCode > 5 && totalUnsupportedCode > 0)
        {
            double unsupportedRatio = (double)totalUnsupportedCode / totalNewAndModifiedCode;

            if (unsupportedRatio > 0.8)
            {
                issues.Add(MakeIssue(ClassificationSeverity.Blocker, "GMS2DOM", "GMS2 Compatibility",
                    $">{(int)(unsupportedRatio * 100)}% of changed code entries ({totalUnsupportedCode}/{totalNewAndModifiedCode}) " +
                    "use GMS2-only runtime features. Automatic translation will produce a non-functional output.",
                    FormatGms2Categories(gms2FuncCategories)));
            }
        }

        // -- Majors --

        if (targetIsGms1 && moddedIsGms2)
        {
            // GMS2→GMS1 is only a problem if the mod actually touches code with GMS2-only features.
            // Pure string/resource mods are fine even across format versions.
            if (totalUnsupportedCode > 0)
            {
                issues.Add(MakeIssue(ClassificationSeverity.Major, "FMT001", "Format",
                    $"Modded archive is GMS2, target is GMS1. " +
                    $"{totalUnsupportedCode} changed code entries use GMS2-only constructs that cannot be auto-translated.",
                    FormatGms2Categories(gms2FuncCategories)));
            }
            else
            {
                issues.Add(MakeIssue(ClassificationSeverity.Minor, "FMT001", "Format",
                    "Modded archive is GMS2, target is GMS1. " +
                    "No GMS2-only code detected in changed entries — resource/string changes should translate fine."));
            }
        }

        if (newCodeWithGms2 > 0 && totalNewAndModifiedCode > 0)
        {
            double ratio = (double)newCodeWithGms2 / newCode.Count;
            var severity = ratio > 0.5 ? ClassificationSeverity.Major : ClassificationSeverity.Minor;
            issues.Add(MakeIssue(severity, "GMS2NEW", "GMS2 Compatibility",
                $"{newCodeWithGms2} of {newCode.Count} new code entries reference GMS2-only functions.",
                FormatGms2Categories(gms2FuncCategories)));
        }

        if (newCodeWithChildren > 0)
        {
            var severity = newCodeWithChildren > newCode.Count / 2 ? ClassificationSeverity.Major : ClassificationSeverity.Minor;
            issues.Add(MakeIssue(severity, "CHILD001", "GMS2.3+ Sub-functions",
                $"{newCodeWithChildren} of {newCode.Count} new code entries have child entries (GMS2.3+ sub-functions). " +
                "These cannot be transplanted to a pre-2.3 target."));
        }

        if (modifiedCodeWithChildren > 0)
        {
            issues.Add(MakeIssue(ClassificationSeverity.Major, "CHILD002", "GMS2.3+ Sub-functions",
                $"{modifiedCodeWithChildren} of {modifiedCodeNames.Count} modified code entries have child entries. " +
                "Bytecode replacement will be skipped for these."));
        }

        if (modifiedCodeWithGms2 > 0)
        {
            issues.Add(MakeIssue(ClassificationSeverity.Major, "GMS2MOD", "GMS2 Compatibility",
                $"{modifiedCodeWithGms2} of {modifiedCodeNames.Count} modified code entries reference GMS2-only functions."));
        }

        if (newRooms > 0)
        {
            var severity = newRooms > 3 ? ClassificationSeverity.Major : ClassificationSeverity.Minor;
            issues.Add(MakeIssue(severity, "ROOM001", "Rooms",
                $"Mod adds {newRooms} new room(s). Room transplantation is risky " +
                "(layer structure, instance placement, creation code may not transfer correctly)."));
        }

        // -- Minors --

        if (modifiedCodeNames.Count > 50)
        {
            issues.Add(MakeIssue(ClassificationSeverity.Minor, "SCOPE001", "Scope",
                $"Mod modifies {modifiedCodeNames.Count} existing code entries. Large scope increases risk."));
        }

        int stringDelta = moddedMeta.StringCount - vanillaMeta.StringCount;
        if (Math.Abs(stringDelta) > 200)
        {
            issues.Add(MakeIssue(ClassificationSeverity.Minor, "STR001", "Strings",
                $"String count delta: {stringDelta:+#;-#;0}. Large delta increases ambiguous match risk."));
        }

        // Resource count drift between vanilla Windows and target Linux
        int codeCountDrift = Math.Abs(vanillaMeta.CodeCount - targetMeta.CodeCount);
        if (codeCountDrift > 20)
        {
            issues.Add(MakeIssue(ClassificationSeverity.Minor, "DRIFT001", "Platform Drift",
                $"Code entry count differs by {codeCountDrift} between vanilla Windows and target Linux. " +
                "Some code patches may fail to find their target."));
        }

        // -- Info --

        issues.Add(MakeIssue(ClassificationSeverity.Info, "META001", "Summary",
            $"Vanilla: {vanillaMeta.GameName} v{vanillaMeta.VersionString} BC{vanillaMeta.BytecodeVersion} " +
            $"({(vanillaMeta.IsGMS2 ? "GMS2" : "GMS1")})"));

        issues.Add(MakeIssue(ClassificationSeverity.Info, "META002", "Summary",
            $"Modded: {moddedMeta.GameName} v{moddedMeta.VersionString} BC{moddedMeta.BytecodeVersion} " +
            $"({(moddedMeta.IsGMS2 ? "GMS2" : "GMS1")})"));

        issues.Add(MakeIssue(ClassificationSeverity.Info, "META003", "Summary",
            $"Target: {targetMeta.GameName} v{targetMeta.VersionString} BC{targetMeta.BytecodeVersion} " +
            $"({(targetMeta.IsGMS2 ? "GMS2" : "GMS1")})"));

        int totalChanges = newCode.Count + modifiedCodeNames.Count +
            newSprites + newSounds + newFonts + newBackgrounds + newObjects + newScripts + newRooms;

        issues.Add(MakeIssue(ClassificationSeverity.Info, "SCOPE000", "Summary",
            $"Total mod scope: {totalChanges} changes " +
            $"(code: {newCode.Count} new + {modifiedCodeNames.Count} modified, " +
            $"resources: {newSprites} spr + {newSounds} snd + {newFonts} fnt + {newBackgrounds} bg + " +
            $"{newObjects} obj + {newScripts} scr + {newRooms} room)"));

        // ─────────────────────────────────────────────────────
        // Phase 5: Compute verdict
        // ─────────────────────────────────────────────────────

        var breakdown = new ClassificationBreakdown
        {
            NewSpriteCount = newSprites,
            NewSoundCount = newSounds,
            NewFontCount = newFonts,
            NewBackgroundCount = newBackgrounds,
            NewCodeEntryCount = newCode.Count,
            NewCodePortableCount = newCodePortable,
            NewCodeUnsupportedCount = newCodeWithGms2 + newCodeWithChildren,
            NewCodeWithChildEntriesCount = newCodeWithChildren,
            NewCodeWithGms2OnlyRefsCount = newCodeWithGms2,
            ModifiedCodeEntryCount = modifiedCodeNames.Count,
            ModifiedCodeWithChildEntriesCount = modifiedCodeWithChildren,
            ModifiedCodeWithGms2OnlyRefsCount = modifiedCodeWithGms2,
            NewObjectCount = newObjects,
            NewScriptCount = newScripts,
            NewRoomCount = newRooms,
            StringCountDelta = stringDelta,
            // Modified resources not computed in pre-flight (requires structural comparison)
        };

        var verdict = ComputeVerdict(issues, breakdown);
        var summary = BuildSummary(verdict, breakdown, issues);

        // Sort issues: blockers first, then majors, minors, info
        issues.Sort((a, b) => a.Severity.CompareTo(b.Severity));

        return new ModClassification
        {
            Verdict = verdict,
            Summary = summary,
            Breakdown = breakdown,
            Issues = issues.AsReadOnly(),
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Verdict computation
    // ═══════════════════════════════════════════════════════════

    private static PortabilityVerdict ComputeVerdict(
        List<ClassificationIssue> issues,
        ClassificationBreakdown breakdown)
    {
        bool hasBlocker = issues.Any(i => i.Severity == ClassificationSeverity.Blocker);
        int majorCount = issues.Count(i => i.Severity == ClassificationSeverity.Major);
        int minorCount = issues.Count(i => i.Severity == ClassificationSeverity.Minor);

        if (hasBlocker)
            return PortabilityVerdict.NotPortable;

        // Many majors or high percentage of unsupported code → PatchableOnly
        if (majorCount >= 3)
            return PortabilityVerdict.PatchableOnly;

        int totalCode = breakdown.NewCodeEntryCount + breakdown.ModifiedCodeEntryCount;
        int unsupportedCode = breakdown.NewCodeUnsupportedCount +
            breakdown.ModifiedCodeWithChildEntriesCount + breakdown.ModifiedCodeWithGms2OnlyRefsCount;

        if (totalCode > 0 && unsupportedCode > 0)
        {
            double unsupportedRatio = (double)unsupportedCode / totalCode;
            if (unsupportedRatio > 0.3)
                return PortabilityVerdict.PatchableOnly;
        }

        if (majorCount > 0)
            return PortabilityVerdict.MostlyPortable;

        if (minorCount > 2)
            return PortabilityVerdict.MostlyPortable;

        return PortabilityVerdict.Portable;
    }

    private static string BuildSummary(
        PortabilityVerdict verdict,
        ClassificationBreakdown breakdown,
        List<ClassificationIssue> issues)
    {
        var pct = breakdown.EstimatedPortablePercent;
        var totalChanges = breakdown.TotalNewResources + breakdown.NewCodeEntryCount + breakdown.ModifiedCodeEntryCount;

        var headline = verdict switch
        {
            PortabilityVerdict.Portable =>
                $"PORTABLE — All {totalChanges} changes are auto-translatable (~{pct}%).",

            PortabilityVerdict.MostlyPortable =>
                $"MOSTLY PORTABLE — Most changes translatable (~{pct}%).",

            PortabilityVerdict.PatchableOnly =>
                $"PATCHABLE ONLY — ~{pct}% auto-translatable. Manual porting needed.",

            PortabilityVerdict.NotPortable =>
                "NOT PORTABLE — Automatic translation will produce broken output.",

            _ => "Unknown verdict.",
        };

        // Append top reasons (blockers + majors) so the user sees WHY
        var reasons = issues
            .Where(i => i.Severity is ClassificationSeverity.Blocker or ClassificationSeverity.Major)
            .Take(3)
            .Select(i => $"{i.Code}: {i.Message}")
            .ToList();

        if (reasons.Count == 0)
            return headline;

        return headline + " Reason: " + string.Join("; ", reasons);
    }

    // ═══════════════════════════════════════════════════════════
    //  Lightweight code scanning
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Scan a code entry's instructions for references to GMS2-only functions.
    /// Returns the set of GMS2-only function names found.
    /// Fast: just iterates instructions and checks ValueFunction names.
    /// </summary>
    private static HashSet<string> ScanForGms2OnlyRefs(UndertaleCode code)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        if (code.Instructions is null)
            return found;

        foreach (var inst in code.Instructions)
        {
            string? funcName = inst.ValueFunction?.Name?.Content;
            if (funcName is not null && Gms2OnlyFunctions.Names.Contains(funcName))
                found.Add(funcName);
        }

        return found;
    }

    /// <summary>
    /// Find code entry names that exist in both vanilla and modded but appear modified.
    /// Uses a fast heuristic: instruction count differs.
    /// This misses code entries where only string content changed (same instruction count),
    /// but that's fine — string-only changes are always portable.
    /// </summary>
    private static List<string> FindModifiedCodeNames(NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var modified = new List<string>();

        foreach (var (name, moddedEntry) in moddedIdx.Code)
        {
            if (!vanillaIdx.Code.TryGetValue(name, out var vanillaEntry))
                continue; // new entry, not modified

            // Skip child entries
            if (moddedEntry.Res.ParentEntry is not null)
                continue;

            var vanillaCode = vanillaEntry.Res;
            var moddedCode = moddedEntry.Res;

            // Heuristic: instruction count differs → likely modified
            int vanillaCount = vanillaCode.Instructions?.Count ?? 0;
            int moddedCount = moddedCode.Instructions?.Count ?? 0;

            if (vanillaCount != moddedCount)
            {
                modified.Add(name);
                continue;
            }

            // Same instruction count but child entries added/removed
            int vanillaChildren = vanillaCode.ChildEntries?.Count ?? 0;
            int moddedChildren = moddedCode.ChildEntries?.Count ?? 0;

            if (vanillaChildren != moddedChildren)
            {
                modified.Add(name);
                continue;
            }
        }

        return modified;
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static List<string> FindNewNames<T>(
        Dictionary<string, (T, int)> vanillaDict,
        Dictionary<string, (T, int)> moddedDict)
    {
        var names = new List<string>();
        foreach (var name in moddedDict.Keys)
        {
            if (!vanillaDict.ContainsKey(name))
                names.Add(name);
        }
        return names;
    }

    private static int CountNew<T>(
        Dictionary<string, (T, int)> vanillaDict,
        Dictionary<string, (T, int)> moddedDict)
    {
        int count = 0;
        foreach (var name in moddedDict.Keys)
        {
            if (!vanillaDict.ContainsKey(name))
                count++;
        }
        return count;
    }

    private static ClassificationIssue MakeIssue(
        ClassificationSeverity severity, string code, string category, string message, string? detail = null)
    {
        return new ClassificationIssue
        {
            Severity = severity,
            Code = code,
            Category = category,
            Message = message,
            Detail = detail,
        };
    }

    private static string FormatGms2Categories(Dictionary<string, int> categories)
    {
        if (categories.Count == 0) return "";
        var parts = categories
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}: {kv.Value} ref(s)");
        return string.Join(", ", parts);
    }
}
