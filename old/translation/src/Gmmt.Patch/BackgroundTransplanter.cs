using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single background/tileset.
/// </summary>
public sealed record BackgroundTransplantResult
{
    public required string BackgroundName { get; init; }
    public required BackgroundTransplantStatus Status { get; init; }
    public string? Diagnostic { get; init; }
}

public enum BackgroundTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

public sealed record BackgroundUpdateResult
{
    public required string BackgroundName { get; init; }
    public required BackgroundUpdateStatus Status { get; init; }
    public string? Diagnostic { get; init; }
}

public enum BackgroundUpdateStatus
{
    Updated,
    TargetNotFound,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new backgrounds (tilesets in GMS2) from a modded archive to a target.
///
/// Strategy:
/// - Copy all scalar properties (flags, GMS2 tile dimensions, etc.).
/// - Clone the background's texture page item (TPAG) and embedded texture
///   using the shared texture cloning logic from SpriteTransplanter.
/// - Copy GMS2 tile ID lists if present.
/// </summary>
public static class BackgroundTransplanter
{
    public static List<BackgroundTransplantResult> TransplantAll(
        IReadOnlyList<string> backgroundNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<BackgroundTransplantResult>(backgroundNames.Count);

        foreach (var name in backgroundNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap);
            results.Add(result);
        }

        return results;
    }

    private static BackgroundTransplantResult TransplantOne(
        string bgName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        // Check if already exists in target
        if (targetData.Backgrounds is not null)
        {
            foreach (var bg in targetData.Backgrounds)
            {
                if (bg.Name?.Content == bgName)
                {
                    return new BackgroundTransplantResult
                    {
                        BackgroundName = bgName,
                        Status = BackgroundTransplantStatus.AlreadyExists,
                        Diagnostic = "Background already exists in target archive.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Backgrounds.TryGetValue(bgName, out var moddedEntry))
        {
            return new BackgroundTransplantResult
            {
                BackgroundName = bgName,
                Status = BackgroundTransplantStatus.SourceNotFound,
                Diagnostic = "Background not found in modded archive.",
            };
        }

        var srcBg = moddedEntry.Res;

        try
        {
            var newBg = new UndertaleBackground
            {
                Name = targetData.Strings.MakeString(bgName),
                Transparent = srcBg.Transparent,
                Smooth = srcBg.Smooth,
                Preload = srcBg.Preload,
                GMS2UnknownAlways2 = srcBg.GMS2UnknownAlways2,
                GMS2TileWidth = srcBg.GMS2TileWidth,
                GMS2TileHeight = srcBg.GMS2TileHeight,
                GMS2OutputBorderX = srcBg.GMS2OutputBorderX,
                GMS2OutputBorderY = srcBg.GMS2OutputBorderY,
                GMS2TileColumns = srcBg.GMS2TileColumns,
                GMS2ItemsPerTileCount = srcBg.GMS2ItemsPerTileCount,
                GMS2TileCount = srcBg.GMS2TileCount,
                GMS2UnknownAlwaysZero = srcBg.GMS2UnknownAlwaysZero,
                GMS2FrameLength = srcBg.GMS2FrameLength,
            };

            // Clone texture page item
            if (srcBg.Texture is not null)
            {
                newBg.Texture = SpriteTransplanter.CloneTpagItem(
                    srcBg.Texture, moddedIdx.Data, targetData,
                    textureCloneMap, tpagCloneMap);
            }

            // Clone GMS2 tile IDs
            if (srcBg.GMS2TileIds is not null)
            {
                foreach (var srcTileId in srcBg.GMS2TileIds)
                {
                    newBg.GMS2TileIds.Add(new UndertaleBackground.TileID { ID = srcTileId.ID });
                }
            }

            targetData.Backgrounds.Add(newBg);

            return new BackgroundTransplantResult
            {
                BackgroundName = bgName,
                Status = BackgroundTransplantStatus.Transplanted,
            };
        }
        catch (Exception ex)
        {
            return new BackgroundTransplantResult
            {
                BackgroundName = bgName,
                Status = BackgroundTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Update existing (modified) backgrounds
    // ═══════════════════════════════════════════════════════════════

    public static List<BackgroundUpdateResult> UpdateAll(
        IReadOnlyList<string> bgNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<BackgroundUpdateResult>(bgNames.Count);
        foreach (var name in bgNames)
            results.Add(UpdateOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap));
        return results;
    }

    private static BackgroundUpdateResult UpdateOne(
        string bgName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        UndertaleBackground? targetBg = null;
        if (targetData.Backgrounds is not null)
        {
            foreach (var bg in targetData.Backgrounds)
            {
                if (bg.Name?.Content == bgName) { targetBg = bg; break; }
            }
        }

        if (targetBg is null)
            return new BackgroundUpdateResult { BackgroundName = bgName, Status = BackgroundUpdateStatus.TargetNotFound };

        if (!moddedIdx.Backgrounds.TryGetValue(bgName, out var moddedEntry))
            return new BackgroundUpdateResult { BackgroundName = bgName, Status = BackgroundUpdateStatus.SourceNotFound };

        var src = moddedEntry.Res;

        try
        {
            targetBg.Transparent = src.Transparent;
            targetBg.Smooth = src.Smooth;
            targetBg.Preload = src.Preload;
            targetBg.GMS2UnknownAlways2 = src.GMS2UnknownAlways2;
            targetBg.GMS2TileWidth = src.GMS2TileWidth;
            targetBg.GMS2TileHeight = src.GMS2TileHeight;
            targetBg.GMS2OutputBorderX = src.GMS2OutputBorderX;
            targetBg.GMS2OutputBorderY = src.GMS2OutputBorderY;
            targetBg.GMS2TileColumns = src.GMS2TileColumns;
            targetBg.GMS2ItemsPerTileCount = src.GMS2ItemsPerTileCount;
            targetBg.GMS2TileCount = src.GMS2TileCount;
            targetBg.GMS2UnknownAlwaysZero = src.GMS2UnknownAlwaysZero;
            targetBg.GMS2FrameLength = src.GMS2FrameLength;

            if (src.Texture is not null)
            {
                targetBg.Texture = SpriteTransplanter.CloneTpagItem(
                    src.Texture, moddedIdx.Data, targetData,
                    textureCloneMap, tpagCloneMap);
            }

            // Replace tile IDs
            targetBg.GMS2TileIds?.Clear();
            if (src.GMS2TileIds is not null && targetBg.GMS2TileIds is not null)
            {
                foreach (var srcId in src.GMS2TileIds)
                    targetBg.GMS2TileIds.Add(new UndertaleBackground.TileID { ID = srcId.ID });
            }

            return new BackgroundUpdateResult { BackgroundName = bgName, Status = BackgroundUpdateStatus.Updated };
        }
        catch (Exception ex)
        {
            return new BackgroundUpdateResult
            {
                BackgroundName = bgName,
                Status = BackgroundUpdateStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }
}
