using Gmmt.Core.Indexing;
using UndertaleModLib.Models;

namespace Gmmt.Diff;

/// <summary>
/// Compares game objects between two archives on supported scalar properties.
/// Sprite and parent references are resolved by name, never raw ID.
/// No event, physics, texture-mask, or instance diffing.
/// </summary>
public static class ObjectDiffer
{
    /// <summary>
    /// Diff a single game object by name.
    /// </summary>
    public static ObjectDiffResult DiffObject(
        string objectName,
        NameIndex vanillaIdx,
        NameIndex moddedIdx)
    {
        bool vanillaHas = vanillaIdx.Objects.TryGetValue(objectName, out var vanillaEntry);
        bool moddedHas = moddedIdx.Objects.TryGetValue(objectName, out var moddedEntry);

        if (!vanillaHas && !moddedHas)
        {
            return new ObjectDiffResult
            {
                ObjectName = objectName,
                Status = ObjectDiffStatus.Missing,
                Differences = [],
                Diagnostic = "Object not found in either archive.",
            };
        }

        if (!vanillaHas)
        {
            return new ObjectDiffResult
            {
                ObjectName = objectName,
                Status = ObjectDiffStatus.Missing,
                Differences = [],
                Diagnostic = "Object exists only in modded archive (new object).",
            };
        }

        if (!moddedHas)
        {
            return new ObjectDiffResult
            {
                ObjectName = objectName,
                Status = ObjectDiffStatus.Missing,
                Differences = [],
                Diagnostic = "Object exists only in vanilla archive (deleted by mod).",
            };
        }

        var diffs = CompareScalars(vanillaEntry.Res, moddedEntry.Res);

        return new ObjectDiffResult
        {
            ObjectName = objectName,
            Status = diffs.Count > 0 ? ObjectDiffStatus.Modified : ObjectDiffStatus.Equal,
            Differences = diffs,
        };
    }

    /// <summary>
    /// Diff all game objects present in either archive.
    /// Returns only non-equal results.
    /// </summary>
    public static List<ObjectDiffResult> DiffAll(NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in vanillaIdx.Objects.Keys) allNames.Add(name);
        foreach (var name in moddedIdx.Objects.Keys) allNames.Add(name);

        var results = new List<ObjectDiffResult>();

        foreach (var name in allNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            var result = DiffObject(name, vanillaIdx, moddedIdx);
            if (result.Status != ObjectDiffStatus.Equal)
                results.Add(result);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────
    // Scalar property comparison
    // ─────────────────────────────────────────────────────────

    private static List<ObjectPropertyDiff> CompareScalars(
        UndertaleGameObject vanilla,
        UndertaleGameObject modded)
    {
        var diffs = new List<ObjectPropertyDiff>();

        CompareBool(diffs, "Visible", vanilla.Visible, modded.Visible);
        CompareBool(diffs, "Solid", vanilla.Solid, modded.Solid);
        CompareInt(diffs, "Depth", vanilla.Depth, modded.Depth);
        CompareBool(diffs, "Persistent", vanilla.Persistent, modded.Persistent);

        // SpriteIndex — resolve by sprite name, never raw ID
        var vanillaSprite = vanilla.Sprite?.Name?.Content;
        var moddedSprite = modded.Sprite?.Name?.Content;
        CompareNullableString(diffs, "SpriteIndex", vanillaSprite, moddedSprite);

        // ParentId — resolve by object name, never raw ID
        var vanillaParent = vanilla.ParentId?.Name?.Content;
        var moddedParent = modded.ParentId?.Name?.Content;
        CompareNullableString(diffs, "ParentId", vanillaParent, moddedParent);

        return diffs;
    }

    private static void CompareBool(List<ObjectPropertyDiff> diffs, string prop, bool a, bool b)
    {
        if (a != b)
        {
            diffs.Add(new ObjectPropertyDiff
            {
                Property = prop,
                VanillaValue = a.ToString(),
                ModdedValue = b.ToString(),
            });
        }
    }

    private static void CompareInt(List<ObjectPropertyDiff> diffs, string prop, int a, int b)
    {
        if (a != b)
        {
            diffs.Add(new ObjectPropertyDiff
            {
                Property = prop,
                VanillaValue = a.ToString(),
                ModdedValue = b.ToString(),
            });
        }
    }

    private static void CompareNullableString(List<ObjectPropertyDiff> diffs, string prop, string? a, string? b)
    {
        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            diffs.Add(new ObjectPropertyDiff
            {
                Property = prop,
                VanillaValue = a ?? "(none)",
                ModdedValue = b ?? "(none)",
            });
        }
    }
}
