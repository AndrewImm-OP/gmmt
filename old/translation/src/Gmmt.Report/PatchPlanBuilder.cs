using Gmmt.Core.Classification;
using Gmmt.Core.Indexing;
using Gmmt.Diff;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Report;

/// <summary>
/// Builds a <see cref="PatchPlan"/> from classification + diffs + manifest.
/// This is the "what would happen" without actually patching anything.
/// Used by --dry-run mode.
/// </summary>
public sealed class PatchPlanBuilder
{
    private readonly List<PlanAction> _actions = [];
    private readonly List<PlanSkip> _skipped = [];

    /// <summary>
    /// Build the complete plan.
    /// </summary>
    public static PatchPlan Build(
        ModClassification classification,
        TransplantManifest manifest,
        IReadOnlyList<CodeDiffResult> codeDiffs,
        IReadOnlyList<CodeStringDiffResult> stringDiffs,
        IReadOnlyList<ObjectDiffResult> objectDiffs,
        NameIndex moddedIdx,
        UndertaleData targetData,
        bool targetIsGms1)
    {
        var builder = new PatchPlanBuilder();

        builder.PlanTransplants(manifest, moddedIdx, targetData, targetIsGms1);
        builder.PlanCodeReplacements(codeDiffs, moddedIdx, targetIsGms1);
        builder.PlanStringPatches(stringDiffs);
        builder.PlanObjectPatches(objectDiffs);

        return new PatchPlan
        {
            Classification = classification,
            Actions = builder._actions.AsReadOnly(),
            Skipped = builder._skipped.AsReadOnly(),
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Resource transplants
    // ═══════════════════════════════════════════════════════════

    private void PlanTransplants(
        TransplantManifest manifest,
        NameIndex moddedIdx,
        UndertaleData targetData,
        bool targetIsGms1)
    {
        // New sprites
        foreach (var name in manifest.NewSprites)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantSprite,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Transplant new sprite with textures",
                Detail = GetSpriteDetail(name, moddedIdx),
            });
        }

        // Modified sprites
        foreach (var name in manifest.ModifiedSprites)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.UpdateSprite,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Update sprite texture data",
            });
        }

        // New sounds
        foreach (var name in manifest.NewSounds)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantSound,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Transplant new sound",
            });
        }

        // Modified sounds
        foreach (var name in manifest.ModifiedSounds)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.UpdateSound,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Update sound data",
            });
        }

        // New backgrounds
        foreach (var name in manifest.NewBackgrounds)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantBackground,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Transplant new background/tileset",
            });
        }

        // Modified backgrounds
        foreach (var name in manifest.ModifiedBackgrounds)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.UpdateBackground,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Update background/tileset data",
            });
        }

        // New fonts
        foreach (var name in manifest.NewFonts)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantFont,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Transplant new font",
                Detail = GetFontDetail(name, moddedIdx),
            });
        }

        // Modified fonts
        foreach (var name in manifest.ModifiedFonts)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.UpdateFont,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Update font data",
            });
        }

        // New scripts
        foreach (var name in manifest.NewScripts)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantScript,
                Resource = name,
                Confidence = PlanConfidence.High,
                Description = "Transplant new script entry",
            });
        }

        // New objects
        foreach (var name in manifest.NewObjects)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantObject,
                Resource = name,
                Confidence = PlanConfidence.Medium,
                Description = "Transplant new game object with events",
            });
        }

        // New rooms
        foreach (var name in manifest.NewRooms)
        {
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.TransplantRoom,
                Resource = name,
                Confidence = PlanConfidence.Low,
                Description = "Transplant new room (risky: layers, instances, creation code)",
            });
        }

        // New code entries
        foreach (var name in manifest.NewCodeEntries)
        {
            if (!moddedIdx.Code.TryGetValue(name, out var entry))
                continue;

            var code = entry.Res;
            bool hasChildren = code.ChildEntries is not null && code.ChildEntries.Count > 0;
            bool hasGms2Refs = targetIsGms1 && HasGms2OnlyRefs(code);

            if (hasChildren || hasGms2Refs)
            {
                var reason = hasChildren
                    ? $"GMS2.3+ sub-functions ({code.ChildEntries!.Count} child entries)"
                    : "References GMS2-only functions";
                _skipped.Add(new PlanSkip
                {
                    Kind = PlanActionKind.TransplantCode,
                    Resource = name,
                    Reason = reason,
                });
            }
            else
            {
                _actions.Add(new PlanAction
                {
                    Kind = PlanActionKind.TransplantCode,
                    Resource = name,
                    Confidence = PlanConfidence.High,
                    Description = $"Transplant new code entry ({code.Instructions?.Count ?? 0} instructions)",
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Code replacements (bytecode-level changes)
    // ═══════════════════════════════════════════════════════════

    private void PlanCodeReplacements(
        IReadOnlyList<CodeDiffResult> codeDiffs,
        NameIndex moddedIdx,
        bool targetIsGms1)
    {
        foreach (var diff in codeDiffs)
        {
            if (diff.Status == CodeDiffStatus.CodeEntryMissing)
            {
                // New code handled in transplants, deleted code is a no-op
                continue;
            }

            if (diff.Status == CodeDiffStatus.Unsupported)
            {
                _skipped.Add(new PlanSkip
                {
                    Kind = PlanActionKind.ReplaceCode,
                    Resource = diff.CodeEntryName,
                    Reason = "Unsupported instructions — cannot compare semantically",
                    Detail = diff.Diagnostic,
                });
                continue;
            }

            if (diff.Status != CodeDiffStatus.Modified)
                continue;

            // Classify: string-only vs bytecode change
            bool hasNonString = false;
            int stringDiffCount = 0;
            foreach (var d in diff.Differences)
            {
                bool isStringOnly = d.Vanilla is not null && d.Modded is not null &&
                    d.Vanilla.Kind == d.Modded.Kind && d.Vanilla.StringValue != d.Modded.StringValue;
                if (isStringOnly) stringDiffCount++;
                else hasNonString = true;
            }

            if (!hasNonString)
            {
                // String-only changes handled by string patcher, no bytecode replace needed
                continue;
            }

            // Check transplantability
            if (moddedIdx.Code.TryGetValue(diff.CodeEntryName, out var moddedEntry))
            {
                var code = moddedEntry.Res;
                bool hasChildren = code.ChildEntries is not null && code.ChildEntries.Count > 0;
                bool hasGms2Refs = targetIsGms1 && HasGms2OnlyRefs(code);

                if (hasChildren)
                {
                    _skipped.Add(new PlanSkip
                    {
                        Kind = PlanActionKind.ReplaceCode,
                        Resource = diff.CodeEntryName,
                        Reason = $"GMS2.3+ sub-functions ({code.ChildEntries!.Count} child entries)",
                    });
                    continue;
                }

                if (hasGms2Refs)
                {
                    _skipped.Add(new PlanSkip
                    {
                        Kind = PlanActionKind.ReplaceCode,
                        Resource = diff.CodeEntryName,
                        Reason = "References GMS2-only functions",
                    });
                    continue;
                }
            }

            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.ReplaceCode,
                Resource = diff.CodeEntryName,
                Confidence = PlanConfidence.High,
                Description = $"Replace bytecode ({diff.Differences.Count} instruction differences)",
                Detail = $"vanilla:{diff.VanillaInstructionCount} → modded:{diff.ModdedInstructionCount} instructions",
            });
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  String patches
    // ═══════════════════════════════════════════════════════════

    private void PlanStringPatches(IReadOnlyList<CodeStringDiffResult> stringDiffs)
    {
        foreach (var diff in stringDiffs)
        {
            if (diff.Status == CodeStringDiffStatus.CodeEntryMissing)
                continue;

            foreach (var change in diff.Changes)
            {
                if (change.Confidence == StringMatchConfidence.Ambiguous)
                {
                    _skipped.Add(new PlanSkip
                    {
                        Kind = PlanActionKind.PatchString,
                        Resource = $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        Reason = "Ambiguous match — multiple candidates",
                        Detail = $"'{Truncate(change.OldContent, 50)}' → '{Truncate(change.NewContent, 50)}'",
                    });
                    continue;
                }

                if (!change.VanillaOrdinal.HasValue)
                {
                    _skipped.Add(new PlanSkip
                    {
                        Kind = PlanActionKind.PatchString,
                        Resource = $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        Reason = "No vanilla ordinal — cannot locate target string",
                    });
                    continue;
                }

                var confidence = change.Confidence switch
                {
                    StringMatchConfidence.ExactOrdinalAndContext => PlanConfidence.High,
                    StringMatchConfidence.ExactOrdinalOnly => PlanConfidence.Medium,
                    StringMatchConfidence.ContentMatchOnly => PlanConfidence.Low,
                    _ => PlanConfidence.Unknown,
                };

                _actions.Add(new PlanAction
                {
                    Kind = PlanActionKind.PatchString,
                    Resource = $"{diff.CodeEntryName}[{change.VanillaOrdinal}]",
                    Confidence = confidence,
                    Description = $"'{Truncate(change.OldContent, 40)}' → '{Truncate(change.NewContent, 40)}'",
                    Detail = $"match: {change.Confidence}",
                });
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Object property patches
    // ═══════════════════════════════════════════════════════════

    private void PlanObjectPatches(IReadOnlyList<ObjectDiffResult> objectDiffs)
    {
        foreach (var diff in objectDiffs)
        {
            if (diff.Status == ObjectDiffStatus.Missing)
            {
                // New objects handled in transplants, deleted objects are no-op
                continue;
            }

            if (diff.Status != ObjectDiffStatus.Modified)
                continue;

            var props = string.Join(", ", diff.Differences.Select(d => d.Property));
            _actions.Add(new PlanAction
            {
                Kind = PlanActionKind.PatchObjectProperties,
                Resource = diff.ObjectName,
                Confidence = PlanConfidence.High,
                Description = $"Patch {diff.Differences.Count} properties: {props}",
            });
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static bool HasGms2OnlyRefs(UndertaleCode code)
    {
        if (code.Instructions is null) return false;
        foreach (var inst in code.Instructions)
        {
            if (inst.ValueFunction?.Name?.Content is { } fn && Gms2OnlyFunctions.Names.Contains(fn))
                return true;
        }
        return false;
    }

    private static string? GetSpriteDetail(string name, NameIndex idx)
    {
        if (!idx.Sprites.TryGetValue(name, out var entry)) return null;
        var spr = entry.Res;
        return $"{spr.Textures?.Count ?? 0} frames, {spr.Width}x{spr.Height}";
    }

    private static string? GetFontDetail(string name, NameIndex idx)
    {
        if (!idx.Fonts.TryGetValue(name, out var entry)) return null;
        return $"{entry.Res.Glyphs?.Count ?? 0} glyphs";
    }

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return s[..(maxLen - 3)] + "...";
    }
}
