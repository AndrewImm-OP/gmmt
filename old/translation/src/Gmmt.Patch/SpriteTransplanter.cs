using System.Buffers.Binary;
using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single sprite.
/// </summary>
public sealed record SpriteTransplantResult
{
    public required string SpriteName { get; init; }
    public required SpriteTransplantStatus Status { get; init; }
    public int FrameCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum SpriteTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Result of updating a single modified sprite.
/// </summary>
public sealed record SpriteUpdateResult
{
    public required string SpriteName { get; init; }
    public required SpriteUpdateStatus Status { get; init; }
    public int FrameCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum SpriteUpdateStatus
{
    Updated,
    TargetNotFound,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants sprites (with their TPAG items and embedded textures) from a
/// modded archive to a target archive. This enables the target archive to reference
/// sprites that were added by the mod.
///
/// Strategy:
/// - For each new sprite, copy all its texture page items to the target.
/// - Each TPAG references an embedded texture (texture page atlas). If that atlas
///   is not yet cloned to the target, clone the entire atlas.
/// - Create a new UndertaleSprite in the target with the cloned TPAG refs.
/// - Copy all sprite metadata (dimensions, origin, collision masks, etc.).
///
/// Embedded texture identity:
/// We track which modded-archive texture pages have been cloned by their index
/// in the modded archive's EmbeddedTextures list. If two sprites share a texture
/// page, the same target clone is reused.
/// </summary>
public static class SpriteTransplanter
{
    /// <summary>
    /// Transplant multiple sprites from modded to target.
    /// Sprites are processed in order; shared texture pages are cloned once.
    /// </summary>
    public static List<SpriteTransplantResult> TransplantAll(
        IReadOnlyList<string> spriteNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var textureCloneMap = new Dictionary<int, UndertaleEmbeddedTexture>();
        var tpagCloneMap = new Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem>(
            ReferenceEqualityComparer.Instance);

        return TransplantAll(spriteNames, moddedIdx, targetData, textureCloneMap, tpagCloneMap);
    }

    /// <summary>
    /// Transplant multiple sprites from modded to target, using shared texture
    /// clone maps that can be reused across sprite/font/background transplanters.
    /// </summary>
    public static List<SpriteTransplantResult> TransplantAll(
        IReadOnlyList<string> spriteNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<SpriteTransplantResult>(spriteNames.Count);

        foreach (var name in spriteNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap);
            results.Add(result);
        }

        return results;
    }

    private static SpriteTransplantResult TransplantOne(
        string spriteName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        // Check if already in target
        foreach (var spr in targetData.Sprites)
        {
            if (spr.Name?.Content == spriteName)
            {
                return new SpriteTransplantResult
                {
                    SpriteName = spriteName,
                    Status = SpriteTransplantStatus.AlreadyExists,
                    Diagnostic = "Sprite already exists in target archive.",
                };
            }
        }

        // Find in modded
        if (!moddedIdx.Sprites.TryGetValue(spriteName, out var moddedEntry))
        {
            return new SpriteTransplantResult
            {
                SpriteName = spriteName,
                Status = SpriteTransplantStatus.SourceNotFound,
                Diagnostic = "Sprite not found in modded archive.",
            };
        }

        var srcSprite = moddedEntry.Res;

        try
        {
            // Create new sprite in target
            var newSprite = new UndertaleSprite
            {
                Name = targetData.Strings.MakeString(spriteName),
                Width = srcSprite.Width,
                Height = srcSprite.Height,
                MarginLeft = srcSprite.MarginLeft,
                MarginRight = srcSprite.MarginRight,
                MarginBottom = srcSprite.MarginBottom,
                MarginTop = srcSprite.MarginTop,
                Transparent = srcSprite.Transparent,
                Smooth = srcSprite.Smooth,
                Preload = srcSprite.Preload,
                BBoxMode = srcSprite.BBoxMode,
                SepMasks = srcSprite.SepMasks,
                OriginX = srcSprite.OriginX,
                OriginY = srcSprite.OriginY,
                GMS2PlaybackSpeed = srcSprite.GMS2PlaybackSpeed,
                GMS2PlaybackSpeedType = srcSprite.GMS2PlaybackSpeedType,
            };

            // Clone texture frames (TPAG items)
            int frameCount = 0;
            if (srcSprite.Textures is not null)
            {
                foreach (var srcTexEntry in srcSprite.Textures)
                {
                    if (srcTexEntry?.Texture is null)
                    {
                        // Null frame — add placeholder
                        newSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = null! });
                        frameCount++;
                        continue;
                    }

                    var clonedTpag = CloneTpagItem(
                        srcTexEntry.Texture, moddedIdx.Data, targetData,
                        textureCloneMap, tpagCloneMap);

                    newSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = clonedTpag });
                    frameCount++;
                }
            }

            // Clone collision masks
            if (srcSprite.CollisionMasks is not null)
            {
                foreach (var srcMask in srcSprite.CollisionMasks)
                {
                    if (srcMask?.Data is not null)
                    {
                        var newMask = new UndertaleSprite.MaskEntry
                        {
                            Data = (byte[])srcMask.Data.Clone(),
                            Width = srcMask.Width,
                            Height = srcMask.Height,
                        };
                        newSprite.CollisionMasks.Add(newMask);
                    }
                }
            }

            targetData.Sprites.Add(newSprite);

            return new SpriteTransplantResult
            {
                SpriteName = spriteName,
                Status = SpriteTransplantStatus.Transplanted,
                FrameCount = frameCount,
            };
        }
        catch (Exception ex)
        {
            return new SpriteTransplantResult
            {
                SpriteName = spriteName,
                Status = SpriteTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Update existing (modified) sprites
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Update multiple existing sprites in the target archive with modded versions.
    /// For each sprite, replaces frames (TPAG items), metadata, and collision masks.
    /// </summary>
    public static List<SpriteUpdateResult> UpdateAll(
        IReadOnlyList<string> spriteNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<SpriteUpdateResult>(spriteNames.Count);

        foreach (var name in spriteNames)
        {
            var result = UpdateOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap);
            results.Add(result);
        }

        return results;
    }

    private static SpriteUpdateResult UpdateOne(
        string spriteName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        // Find in target
        UndertaleSprite? targetSprite = null;
        foreach (var spr in targetData.Sprites)
        {
            if (spr.Name?.Content == spriteName)
            {
                targetSprite = spr;
                break;
            }
        }

        if (targetSprite is null)
        {
            return new SpriteUpdateResult
            {
                SpriteName = spriteName,
                Status = SpriteUpdateStatus.TargetNotFound,
                Diagnostic = "Sprite not found in target archive.",
            };
        }

        // Find in modded
        if (!moddedIdx.Sprites.TryGetValue(spriteName, out var moddedEntry))
        {
            return new SpriteUpdateResult
            {
                SpriteName = spriteName,
                Status = SpriteUpdateStatus.SourceNotFound,
                Diagnostic = "Sprite not found in modded archive.",
            };
        }

        var srcSprite = moddedEntry.Res;

        try
        {
            // Update metadata
            targetSprite.Width = srcSprite.Width;
            targetSprite.Height = srcSprite.Height;
            targetSprite.MarginLeft = srcSprite.MarginLeft;
            targetSprite.MarginRight = srcSprite.MarginRight;
            targetSprite.MarginBottom = srcSprite.MarginBottom;
            targetSprite.MarginTop = srcSprite.MarginTop;
            targetSprite.Transparent = srcSprite.Transparent;
            targetSprite.Smooth = srcSprite.Smooth;
            targetSprite.Preload = srcSprite.Preload;
            targetSprite.BBoxMode = srcSprite.BBoxMode;
            targetSprite.SepMasks = srcSprite.SepMasks;
            targetSprite.OriginX = srcSprite.OriginX;
            targetSprite.OriginY = srcSprite.OriginY;
            targetSprite.GMS2PlaybackSpeed = srcSprite.GMS2PlaybackSpeed;
            targetSprite.GMS2PlaybackSpeedType = srcSprite.GMS2PlaybackSpeedType;

            // Replace texture frames — clear old, add new cloned ones
            targetSprite.Textures.Clear();
            int frameCount = 0;

            if (srcSprite.Textures is not null)
            {
                foreach (var srcTexEntry in srcSprite.Textures)
                {
                    if (srcTexEntry?.Texture is null)
                    {
                        targetSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = null! });
                        frameCount++;
                        continue;
                    }

                    var clonedTpag = CloneTpagItem(
                        srcTexEntry.Texture, moddedIdx.Data, targetData,
                        textureCloneMap, tpagCloneMap);

                    targetSprite.Textures.Add(new UndertaleSprite.TextureEntry { Texture = clonedTpag });
                    frameCount++;
                }
            }

            // Replace collision masks
            targetSprite.CollisionMasks.Clear();
            if (srcSprite.CollisionMasks is not null)
            {
                foreach (var srcMask in srcSprite.CollisionMasks)
                {
                    if (srcMask?.Data is not null)
                    {
                        var newMask = new UndertaleSprite.MaskEntry
                        {
                            Data = (byte[])srcMask.Data.Clone(),
                            Width = srcMask.Width,
                            Height = srcMask.Height,
                        };
                        targetSprite.CollisionMasks.Add(newMask);
                    }
                }
            }

            return new SpriteUpdateResult
            {
                SpriteName = spriteName,
                Status = SpriteUpdateStatus.Updated,
                FrameCount = frameCount,
            };
        }
        catch (Exception ex)
        {
            return new SpriteUpdateResult
            {
                SpriteName = spriteName,
                Status = SpriteUpdateStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Shared texture/TPAG cloning
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Clone a TPAG item from the modded archive to the target archive.
    /// If the item has already been cloned (shared across sprites), reuse the clone.
    /// The referenced embedded texture is also cloned if not already done.
    /// </summary>
    internal static UndertaleTexturePageItem CloneTpagItem(
        UndertaleTexturePageItem srcTpag,
        UndertaleData moddedData,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        if (tpagCloneMap.TryGetValue(srcTpag, out var existing))
            return existing;

        // Ensure the embedded texture page is cloned
        UndertaleEmbeddedTexture? targetTexPage = null;
        if (srcTpag.TexturePage is not null)
        {
            int srcTexIdx = moddedData.EmbeddedTextures.IndexOf(srcTpag.TexturePage);
            if (srcTexIdx >= 0)
            {
                targetTexPage = GetOrCloneEmbeddedTexture(
                    srcTexIdx, moddedData, targetData, textureCloneMap);
            }
        }

        var newTpag = new UndertaleTexturePageItem
        {
            SourceX = srcTpag.SourceX,
            SourceY = srcTpag.SourceY,
            SourceWidth = srcTpag.SourceWidth,
            SourceHeight = srcTpag.SourceHeight,
            TargetX = srcTpag.TargetX,
            TargetY = srcTpag.TargetY,
            TargetWidth = srcTpag.TargetWidth,
            TargetHeight = srcTpag.TargetHeight,
            BoundingWidth = srcTpag.BoundingWidth,
            BoundingHeight = srcTpag.BoundingHeight,
            TexturePage = targetTexPage!,
        };

        targetData.TexturePageItems.Add(newTpag);
        tpagCloneMap[srcTpag] = newTpag;

        return newTpag;
    }

    /// <summary>
    /// Get or clone an embedded texture from modded to target by modded index.
    /// The entire texture atlas is cloned (pixel data and all).
    /// If the source uses QOI/BZ2 format and the target is GMS1, converts to PNG.
    /// </summary>
    internal static UndertaleEmbeddedTexture GetOrCloneEmbeddedTexture(
        int moddedTexIdx,
        UndertaleData moddedData,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap)
    {
        if (textureCloneMap.TryGetValue(moddedTexIdx, out var existing))
            return existing;

        var srcTex = moddedData.EmbeddedTextures[moddedTexIdx];

        var newTex = new UndertaleEmbeddedTexture
        {
            Scaled = srcTex.Scaled,
            GeneratedMips = srcTex.GeneratedMips,
            TextureWidth = srcTex.TextureWidth,
            TextureHeight = srcTex.TextureHeight,
            IndexInGroup = srcTex.IndexInGroup,
        };

        // Clone the texture data (pixel data) via deep copy.
        // IMPORTANT: We must NOT share the GMImage reference between source and
        // target archives. Sharing causes corruption when UndertaleModLib
        // serializes one archive while both reference the same backing byte[].
        if (srcTex.TextureData?.Image is not null)
        {
            var srcImage = srcTex.TextureData.Image;
            bool targetIsGMS1 = (targetData.GeneralInfo?.Major ?? 1) < 2;
            bool sourceNeedsConversion = srcImage.Format is
                GMImage.ImageFormat.Qoi or GMImage.ImageFormat.Bz2Qoi;

            GMImage targetImage;
            if (targetIsGMS1 && sourceNeedsConversion)
            {
                // Convert QOI/BZ2 → PNG for GMS1 compatibility (already produces a new instance)
                targetImage = srcImage.ConvertToPng();
            }
            else
            {
                // Deep-copy the image so the target archive owns independent pixel data
                targetImage = DeepCloneImage(srcImage);
            }

            newTex.TextureData = new UndertaleEmbeddedTexture.TexData
            {
                Image = targetImage,
            };
        }

        targetData.EmbeddedTextures.Add(newTex);
        textureCloneMap[moddedTexIdx] = newTex;

        // Register in TextureGroupInfo if present (GMS 2.2.1+ only; GMS1 has no TGIN chunk).
        var tginList = targetData.TextureGroupInfo;
        if (tginList is { Count: > 0 })
        {
            var tg = tginList[0];
            tg.TexturePages?.Add(
                new UndertaleResourceById<UndertaleEmbeddedTexture, UndertaleChunkTXTR>(newTex));
        }

        return newTex;
    }

    /// <summary>
    /// Create a deep copy of a <see cref="GMImage"/> by duplicating its backing byte array.
    /// GMImage is documented as immutable, but its internal byte[] can be shared by reference.
    /// When two UndertaleData instances reference the same GMImage, serialization of one
    /// can corrupt the other. This method ensures full data isolation.
    /// </summary>
    internal static GMImage DeepCloneImage(GMImage source)
    {
        switch (source.Format)
        {
            case GMImage.ImageFormat.Png:
            {
                // ToSpan() returns the raw _data for PNG — copy it and reconstruct
                byte[] copy = source.ToSpan().ToArray();
                return GMImage.FromPng(copy);
            }

            case GMImage.ImageFormat.Qoi:
            {
                // ToSpan() returns the raw _data for QOI — copy it and reconstruct
                byte[] copy = source.ToSpan().ToArray();
                return GMImage.FromQoi(copy);
            }

            case GMImage.ImageFormat.Bz2Qoi:
            {
                // For BZ2+QOI, ToSpan(gm2022_5) writes the full on-disk representation
                // including the header. We need to re-parse it the same way
                // FromBinaryReader would, but we can take a shortcut:
                // serialize with gm2022_5=true (which includes the uncompressed size field)
                // and then extract the components.
                //
                // BZ2+QOI on-disk layout (gm2022_5=true):
                //   bytes 0-3:  magic "2zoq"
                //   bytes 4-5:  width  (int16 LE)
                //   bytes 6-7:  height (int16 LE)
                //   bytes 8-11: uncompressedSize (int32 LE)
                //   bytes 12+:  compressed BZ2 data
                //
                // BZ2+QOI on-disk layout (gm2022_5=false):
                //   bytes 0-3:  magic "2zoq"
                //   bytes 4-5:  width  (int16 LE)
                //   bytes 6-7:  height (int16 LE)
                //   bytes 8+:   compressed BZ2 data

                // Try with gm2022_5=true first to preserve uncompressed size if available
                ReadOnlySpan<byte> fullSpan = source.ToSpan(gm2022_5: true);
                byte[] fullCopy = fullSpan.ToArray();

                // Parse header
                int width = BinaryPrimitives.ReadInt16LittleEndian(fullCopy.AsSpan(4, 2));
                int height = BinaryPrimitives.ReadInt16LittleEndian(fullCopy.AsSpan(6, 2));
                int uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(fullCopy.AsSpan(8, 4));

                // Extract compressed data (skip 12-byte header)
                byte[] compressedData = fullCopy.AsSpan(12).ToArray();

                return GMImage.FromBz2Qoi(compressedData, width, height, uncompressedSize);
            }

            case GMImage.ImageFormat.RawBgra:
            {
                // Raw format — copy the pixel data
                byte[] copy = source.GetRawImageData().ToArray();
                // Reconstruct through the public constructor that creates a blank image,
                // then we need to copy data into it
                var clone = new GMImage(source.Width, source.Height);
                copy.CopyTo(clone.GetRawImageData());
                return clone;
            }

            default:
                throw new InvalidOperationException($"Unknown GMImage format: {source.Format}");
        }
    }
}
