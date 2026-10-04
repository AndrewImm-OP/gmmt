using Gmmt.Core.Indexing;
using UndertaleModLib.Models;

namespace Gmmt.Diff;

/// <summary>
/// Identifies resources that exist only in the modded archive (not in vanilla)
/// and therefore need to be transplanted wholesale to the target, as well as
/// resources that exist in both but have been modified by the mod.
/// </summary>
public static class ResourceTransplantDiffer
{
    /// <summary>
    /// Compute the transplant manifest by comparing vanilla and modded name indices.
    /// Resources present in modded but absent from vanilla are candidates for transplant.
    /// Resources present in both but modified are candidates for update.
    /// </summary>
    public static TransplantManifest ComputeManifest(
        NameIndex vanillaIdx,
        NameIndex moddedIdx)
    {
        var newSprites = FindNew(vanillaIdx.Sprites, moddedIdx.Sprites);
        var modifiedSprites = FindModifiedSprites(vanillaIdx, moddedIdx);
        var newCode = FindNew(vanillaIdx.Code, moddedIdx.Code);
        var newObjects = FindNew(vanillaIdx.Objects, moddedIdx.Objects);
        var newRooms = FindNew(vanillaIdx.Rooms, moddedIdx.Rooms);
        var newSounds = FindNew(vanillaIdx.Sounds, moddedIdx.Sounds);
        var modifiedSounds = FindModifiedSounds(vanillaIdx, moddedIdx);
        var newScripts = FindNew(vanillaIdx.Scripts, moddedIdx.Scripts);
        var newFonts = FindNew(vanillaIdx.Fonts, moddedIdx.Fonts);
        var modifiedFonts = FindModifiedFonts(vanillaIdx, moddedIdx);
        var newBackgrounds = FindNew(vanillaIdx.Backgrounds, moddedIdx.Backgrounds);
        var modifiedBackgrounds = FindModifiedBackgrounds(vanillaIdx, moddedIdx);

        // Find embedded texture indices referenced by new sprites
        var newTexIndices = FindNewEmbeddedTextureIndices(newSprites, moddedIdx);

        return new TransplantManifest
        {
            NewSprites = newSprites,
            ModifiedSprites = modifiedSprites,
            ModifiedBackgrounds = modifiedBackgrounds,
            ModifiedFonts = modifiedFonts,
            ModifiedSounds = modifiedSounds,
            NewCodeEntries = newCode,
            NewObjects = newObjects,
            NewRooms = newRooms,
            NewSounds = newSounds,
            NewScripts = newScripts,
            NewFonts = newFonts,
            NewBackgrounds = newBackgrounds,
            NewEmbeddedTextureIndices = newTexIndices,
        };
    }

    private static List<string> FindNew<T>(
        Dictionary<string, (T, int)> vanillaDict,
        Dictionary<string, (T, int)> moddedDict)
    {
        var names = new List<string>();
        foreach (var name in moddedDict.Keys)
        {
            if (!vanillaDict.ContainsKey(name))
                names.Add(name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Identifies sprites that exist in both vanilla and modded archives but have been
    /// modified. Compares frame count, dimensions, origin, TPAG sub-regions, and
    /// collision masks to detect changes.
    /// </summary>
    private static List<string> FindModifiedSprites(
        NameIndex vanillaIdx,
        NameIndex moddedIdx)
    {
        var modified = new List<string>();

        foreach (var (name, moddedEntry) in moddedIdx.Sprites)
        {
            if (!vanillaIdx.Sprites.TryGetValue(name, out var vanillaEntry))
                continue; // new sprite, handled separately

            var vanSpr = vanillaEntry.Res;
            var modSpr = moddedEntry.Res;

            if (IsSpriteModified(vanSpr, modSpr))
                modified.Add(name);
        }

        modified.Sort(StringComparer.Ordinal);
        return modified;
    }

    /// <summary>
    /// Determines if a sprite has been modified by comparing structural properties.
    /// We compare metadata, frame count, per-frame TPAG sub-regions, and collision masks.
    /// We do NOT compare raw pixel data (too expensive) — TPAG coordinate changes are
    /// sufficient to detect redrawn/replaced sprites.
    /// </summary>
    private static bool IsSpriteModified(UndertaleSprite vanilla, UndertaleSprite modded)
    {
        // Dimensions changed
        if (vanilla.Width != modded.Width || vanilla.Height != modded.Height)
            return true;

        // Origin changed
        if (vanilla.OriginX != modded.OriginX || vanilla.OriginY != modded.OriginY)
            return true;

        // Margins changed
        if (vanilla.MarginLeft != modded.MarginLeft || vanilla.MarginRight != modded.MarginRight ||
            vanilla.MarginTop != modded.MarginTop || vanilla.MarginBottom != modded.MarginBottom)
            return true;

        // BBox mode changed
        if (vanilla.BBoxMode != modded.BBoxMode)
            return true;

        // Playback speed changed
        if (vanilla.GMS2PlaybackSpeed != modded.GMS2PlaybackSpeed ||
            vanilla.GMS2PlaybackSpeedType != modded.GMS2PlaybackSpeedType)
            return true;

        // Frame count changed
        int vanillaFrameCount = vanilla.Textures?.Count ?? 0;
        int moddedFrameCount = modded.Textures?.Count ?? 0;
        if (vanillaFrameCount != moddedFrameCount)
            return true;

        // Per-frame TPAG sub-region comparison
        if (vanilla.Textures is not null && modded.Textures is not null)
        {
            for (int i = 0; i < vanillaFrameCount; i++)
            {
                var vanTpag = vanilla.Textures[i]?.Texture;
                var modTpag = modded.Textures[i]?.Texture;

                if (vanTpag is null && modTpag is null) continue;
                if (vanTpag is null || modTpag is null) return true;

                if (vanTpag.SourceX != modTpag.SourceX ||
                    vanTpag.SourceY != modTpag.SourceY ||
                    vanTpag.SourceWidth != modTpag.SourceWidth ||
                    vanTpag.SourceHeight != modTpag.SourceHeight ||
                    vanTpag.TargetX != modTpag.TargetX ||
                    vanTpag.TargetY != modTpag.TargetY ||
                    vanTpag.TargetWidth != modTpag.TargetWidth ||
                    vanTpag.TargetHeight != modTpag.TargetHeight ||
                    vanTpag.BoundingWidth != modTpag.BoundingWidth ||
                    vanTpag.BoundingHeight != modTpag.BoundingHeight)
                {
                    return true;
                }
            }
        }

        // Collision mask count changed
        int vanillaMaskCount = vanilla.CollisionMasks?.Count ?? 0;
        int moddedMaskCount = modded.CollisionMasks?.Count ?? 0;
        if (vanillaMaskCount != moddedMaskCount)
            return true;

        // Per-mask data comparison
        if (vanilla.CollisionMasks is not null && modded.CollisionMasks is not null)
        {
            for (int i = 0; i < vanillaMaskCount; i++)
            {
                var vanMask = vanilla.CollisionMasks[i];
                var modMask = modded.CollisionMasks[i];

                if (vanMask?.Data is null && modMask?.Data is null) continue;
                if (vanMask?.Data is null || modMask?.Data is null) return true;
                if (vanMask.Width != modMask.Width || vanMask.Height != modMask.Height)
                    return true;
                if (!vanMask.Data.AsSpan().SequenceEqual(modMask.Data.AsSpan()))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds indices of embedded textures in the modded archive that are referenced
    /// by new sprites (sprites that don't exist in vanilla). These textures may need
    /// to be transplanted along with the sprites.
    /// </summary>
    private static List<int> FindNewEmbeddedTextureIndices(
        IReadOnlyList<string> newSpriteNames,
        NameIndex moddedIdx)
    {
        var texIndices = new HashSet<int>();
        var moddedTextures = moddedIdx.Data.EmbeddedTextures;

        foreach (var name in newSpriteNames)
        {
            if (!moddedIdx.Sprites.TryGetValue(name, out var entry))
                continue;

            var sprite = entry.Res;
            if (sprite.Textures is null) continue;

            foreach (var texEntry in sprite.Textures)
            {
                if (texEntry?.Texture?.TexturePage is { } embTex)
                {
                    int idx = moddedTextures.IndexOf(embTex);
                    if (idx >= 0)
                        texIndices.Add(idx);
                }
            }
        }

        var sorted = texIndices.ToList();
        sorted.Sort();
        return sorted;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Modified background detection
    // ═══════════════════════════════════════════════════════════════

    private static List<string> FindModifiedBackgrounds(
        NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var modified = new List<string>();
        foreach (var (name, moddedEntry) in moddedIdx.Backgrounds)
        {
            if (!vanillaIdx.Backgrounds.TryGetValue(name, out var vanillaEntry))
                continue;
            if (IsBackgroundModified(vanillaEntry.Res, moddedEntry.Res))
                modified.Add(name);
        }
        modified.Sort(StringComparer.Ordinal);
        return modified;
    }

    private static bool IsBackgroundModified(UndertaleBackground vanilla, UndertaleBackground modded)
    {
        // GMS2 tile dimensions
        if (vanilla.GMS2TileWidth != modded.GMS2TileWidth ||
            vanilla.GMS2TileHeight != modded.GMS2TileHeight ||
            vanilla.GMS2TileColumns != modded.GMS2TileColumns ||
            vanilla.GMS2TileCount != modded.GMS2TileCount ||
            vanilla.GMS2ItemsPerTileCount != modded.GMS2ItemsPerTileCount ||
            vanilla.GMS2OutputBorderX != modded.GMS2OutputBorderX ||
            vanilla.GMS2OutputBorderY != modded.GMS2OutputBorderY ||
            vanilla.GMS2FrameLength != modded.GMS2FrameLength)
            return true;

        // TPAG comparison
        var vanTpag = vanilla.Texture;
        var modTpag = modded.Texture;
        if (vanTpag is null && modTpag is null) return false;
        if (vanTpag is null || modTpag is null) return true;

        if (vanTpag.SourceX != modTpag.SourceX ||
            vanTpag.SourceY != modTpag.SourceY ||
            vanTpag.SourceWidth != modTpag.SourceWidth ||
            vanTpag.SourceHeight != modTpag.SourceHeight ||
            vanTpag.TargetX != modTpag.TargetX ||
            vanTpag.TargetY != modTpag.TargetY ||
            vanTpag.TargetWidth != modTpag.TargetWidth ||
            vanTpag.TargetHeight != modTpag.TargetHeight ||
            vanTpag.BoundingWidth != modTpag.BoundingWidth ||
            vanTpag.BoundingHeight != modTpag.BoundingHeight)
            return true;

        // GMS2 tile ID comparison
        int vanTileCount = vanilla.GMS2TileIds?.Count ?? 0;
        int modTileCount = modded.GMS2TileIds?.Count ?? 0;
        if (vanTileCount != modTileCount) return true;
        if (vanilla.GMS2TileIds is not null && modded.GMS2TileIds is not null)
        {
            for (int i = 0; i < vanTileCount; i++)
            {
                if (vanilla.GMS2TileIds[i].ID != modded.GMS2TileIds[i].ID)
                    return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Modified font detection
    // ═══════════════════════════════════════════════════════════════

    private static List<string> FindModifiedFonts(
        NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var modified = new List<string>();
        foreach (var (name, moddedEntry) in moddedIdx.Fonts)
        {
            if (!vanillaIdx.Fonts.TryGetValue(name, out var vanillaEntry))
                continue;
            if (IsFontModified(vanillaEntry.Res, moddedEntry.Res))
                modified.Add(name);
        }
        modified.Sort(StringComparer.Ordinal);
        return modified;
    }

    private static bool IsFontModified(UndertaleFont vanilla, UndertaleFont modded)
    {
        if (vanilla.EmSize != modded.EmSize ||
            vanilla.Bold != modded.Bold ||
            vanilla.Italic != modded.Italic ||
            vanilla.RangeStart != modded.RangeStart ||
            vanilla.RangeEnd != modded.RangeEnd ||
            vanilla.AntiAliasing != modded.AntiAliasing ||
            vanilla.ScaleX != modded.ScaleX ||
            vanilla.ScaleY != modded.ScaleY ||
            vanilla.Ascender != modded.Ascender ||
            vanilla.LineHeight != modded.LineHeight)
            return true;

        int vanGlyphCount = vanilla.Glyphs?.Count ?? 0;
        int modGlyphCount = modded.Glyphs?.Count ?? 0;
        if (vanGlyphCount != modGlyphCount) return true;

        // TPAG comparison
        var vanTpag = vanilla.Texture;
        var modTpag = modded.Texture;
        if (vanTpag is null != (modTpag is null)) return true;
        if (vanTpag is not null && modTpag is not null)
        {
            if (vanTpag.SourceWidth != modTpag.SourceWidth ||
                vanTpag.SourceHeight != modTpag.SourceHeight ||
                vanTpag.SourceX != modTpag.SourceX ||
                vanTpag.SourceY != modTpag.SourceY)
                return true;
        }

        // Per-glyph comparison
        if (vanilla.Glyphs is not null && modded.Glyphs is not null)
        {
            for (int i = 0; i < vanGlyphCount; i++)
            {
                var vg = vanilla.Glyphs[i];
                var mg = modded.Glyphs[i];
                if (vg.Character != mg.Character ||
                    vg.SourceX != mg.SourceX ||
                    vg.SourceY != mg.SourceY ||
                    vg.SourceWidth != mg.SourceWidth ||
                    vg.SourceHeight != mg.SourceHeight ||
                    vg.Shift != mg.Shift ||
                    vg.Offset != mg.Offset)
                    return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Modified sound detection
    // ═══════════════════════════════════════════════════════════════

    private static List<string> FindModifiedSounds(
        NameIndex vanillaIdx, NameIndex moddedIdx)
    {
        var modified = new List<string>();
        foreach (var (name, moddedEntry) in moddedIdx.Sounds)
        {
            if (!vanillaIdx.Sounds.TryGetValue(name, out var vanillaEntry))
                continue;
            if (IsSoundModified(vanillaEntry.Res, moddedEntry.Res))
                modified.Add(name);
        }
        modified.Sort(StringComparer.Ordinal);
        return modified;
    }

    private static bool IsSoundModified(UndertaleSound vanilla, UndertaleSound modded)
    {
        if (vanilla.Flags != modded.Flags ||
            vanilla.Volume != modded.Volume ||
            vanilla.Pitch != modded.Pitch)
            return true;

        // Embedded audio data comparison
        if (vanilla.AudioFile is not null && modded.AudioFile is not null)
        {
            var vanData = vanilla.AudioFile.Data;
            var modData = modded.AudioFile.Data;
            if (vanData is null != (modData is null)) return true;
            if (vanData is not null && modData is not null)
            {
                if (vanData.Length != modData.Length) return true;
                if (!vanData.AsSpan().SequenceEqual(modData.AsSpan())) return true;
            }
        }
        else if (vanilla.AudioFile is null != (modded.AudioFile is null))
        {
            return true;
        }

        return false;
    }
}
