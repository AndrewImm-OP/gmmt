using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single font.
/// </summary>
public sealed record FontTransplantResult
{
    public required string FontName { get; init; }
    public required FontTransplantStatus Status { get; init; }
    public int GlyphCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum FontTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

public sealed record FontUpdateResult
{
    public required string FontName { get; init; }
    public required FontUpdateStatus Status { get; init; }
    public int GlyphCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum FontUpdateStatus
{
    Updated,
    TargetNotFound,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new fonts from a modded archive to a target archive.
///
/// Strategy:
/// - Copy all scalar font properties.
/// - Clone the font's texture page item (TPAG) and embedded texture using
///   the shared texture cloning logic from SpriteTransplanter.
/// - Deep-clone all glyphs including kerning data.
/// </summary>
public static class FontTransplanter
{
    public static List<FontTransplantResult> TransplantAll(
        IReadOnlyList<string> fontNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<FontTransplantResult>(fontNames.Count);

        foreach (var name in fontNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap);
            results.Add(result);
        }

        return results;
    }

    private static FontTransplantResult TransplantOne(
        string fontName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        // Check if already exists in target
        if (targetData.Fonts is not null)
        {
            foreach (var f in targetData.Fonts)
            {
                if (f.Name?.Content == fontName)
                {
                    return new FontTransplantResult
                    {
                        FontName = fontName,
                        Status = FontTransplantStatus.AlreadyExists,
                        Diagnostic = "Font already exists in target archive.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Fonts.TryGetValue(fontName, out var moddedEntry))
        {
            return new FontTransplantResult
            {
                FontName = fontName,
                Status = FontTransplantStatus.SourceNotFound,
                Diagnostic = "Font not found in modded archive.",
            };
        }

        var srcFont = moddedEntry.Res;

        try
        {
            var newFont = new UndertaleFont
            {
                Name = targetData.Strings.MakeString(fontName),
                EmSizeIsFloat = srcFont.EmSizeIsFloat,
                EmSize = srcFont.EmSize,
                Bold = srcFont.Bold,
                Italic = srcFont.Italic,
                RangeStart = srcFont.RangeStart,
                Charset = srcFont.Charset,
                AntiAliasing = srcFont.AntiAliasing,
                RangeEnd = srcFont.RangeEnd,
                ScaleX = srcFont.ScaleX,
                ScaleY = srcFont.ScaleY,
                Ascender = srcFont.Ascender,
                AscenderOffset = srcFont.AscenderOffset,
                SDFSpread = srcFont.SDFSpread,
                LineHeight = srcFont.LineHeight,
            };

            if (srcFont.DisplayName?.Content is { } displayName)
                newFont.DisplayName = targetData.Strings.MakeString(displayName);

            // Clone texture page item
            if (srcFont.Texture is not null)
            {
                newFont.Texture = SpriteTransplanter.CloneTpagItem(
                    srcFont.Texture, moddedIdx.Data, targetData,
                    textureCloneMap, tpagCloneMap);
            }

            // Clone glyphs
            int glyphCount = 0;
            if (srcFont.Glyphs is not null)
            {
                foreach (var srcGlyph in srcFont.Glyphs)
                {
                    var newGlyph = new UndertaleFont.Glyph
                    {
                        Character = srcGlyph.Character,
                        SourceX = srcGlyph.SourceX,
                        SourceY = srcGlyph.SourceY,
                        SourceWidth = srcGlyph.SourceWidth,
                        SourceHeight = srcGlyph.SourceHeight,
                        Shift = srcGlyph.Shift,
                        Offset = srcGlyph.Offset,
                    };

                    // Clone kerning data
                    if (srcGlyph.Kerning is not null)
                    {
                        foreach (var srcKern in srcGlyph.Kerning)
                        {
                            newGlyph.Kerning.Add(new UndertaleFont.Glyph.GlyphKerning
                            {
                                Character = srcKern.Character,
                                ShiftModifier = srcKern.ShiftModifier,
                            });
                        }
                    }

                    newFont.Glyphs.Add(newGlyph);
                    glyphCount++;
                }
            }

            targetData.Fonts.Add(newFont);

            return new FontTransplantResult
            {
                FontName = fontName,
                Status = FontTransplantStatus.Transplanted,
                GlyphCount = glyphCount,
            };
        }
        catch (Exception ex)
        {
            return new FontTransplantResult
            {
                FontName = fontName,
                Status = FontTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Update existing (modified) fonts
    // ═══════════════════════════════════════════════════════════════

    public static List<FontUpdateResult> UpdateAll(
        IReadOnlyList<string> fontNames,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        var results = new List<FontUpdateResult>(fontNames.Count);
        foreach (var name in fontNames)
            results.Add(UpdateOne(name, moddedIdx, targetData, textureCloneMap, tpagCloneMap));
        return results;
    }

    private static FontUpdateResult UpdateOne(
        string fontName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap,
        Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem> tpagCloneMap)
    {
        UndertaleFont? targetFont = null;
        if (targetData.Fonts is not null)
        {
            foreach (var f in targetData.Fonts)
            {
                if (f.Name?.Content == fontName) { targetFont = f; break; }
            }
        }

        if (targetFont is null)
            return new FontUpdateResult { FontName = fontName, Status = FontUpdateStatus.TargetNotFound };

        if (!moddedIdx.Fonts.TryGetValue(fontName, out var moddedEntry))
            return new FontUpdateResult { FontName = fontName, Status = FontUpdateStatus.SourceNotFound };

        var src = moddedEntry.Res;

        try
        {
            targetFont.EmSizeIsFloat = src.EmSizeIsFloat;
            targetFont.EmSize = src.EmSize;
            targetFont.Bold = src.Bold;
            targetFont.Italic = src.Italic;
            targetFont.RangeStart = src.RangeStart;
            targetFont.Charset = src.Charset;
            targetFont.AntiAliasing = src.AntiAliasing;
            targetFont.RangeEnd = src.RangeEnd;
            targetFont.ScaleX = src.ScaleX;
            targetFont.ScaleY = src.ScaleY;
            targetFont.Ascender = src.Ascender;
            targetFont.AscenderOffset = src.AscenderOffset;
            targetFont.SDFSpread = src.SDFSpread;
            targetFont.LineHeight = src.LineHeight;

            if (src.DisplayName?.Content is { } displayName)
                targetFont.DisplayName = targetData.Strings.MakeString(displayName);

            // Replace texture page item
            if (src.Texture is not null)
            {
                targetFont.Texture = SpriteTransplanter.CloneTpagItem(
                    src.Texture, moddedIdx.Data, targetData,
                    textureCloneMap, tpagCloneMap);
            }

            // Replace glyphs
            targetFont.Glyphs.Clear();
            int glyphCount = 0;
            if (src.Glyphs is not null)
            {
                foreach (var srcGlyph in src.Glyphs)
                {
                    var newGlyph = new UndertaleFont.Glyph
                    {
                        Character = srcGlyph.Character,
                        SourceX = srcGlyph.SourceX,
                        SourceY = srcGlyph.SourceY,
                        SourceWidth = srcGlyph.SourceWidth,
                        SourceHeight = srcGlyph.SourceHeight,
                        Shift = srcGlyph.Shift,
                        Offset = srcGlyph.Offset,
                    };

                    if (srcGlyph.Kerning is not null)
                    {
                        foreach (var srcKern in srcGlyph.Kerning)
                        {
                            newGlyph.Kerning.Add(new UndertaleFont.Glyph.GlyphKerning
                            {
                                Character = srcKern.Character,
                                ShiftModifier = srcKern.ShiftModifier,
                            });
                        }
                    }

                    targetFont.Glyphs.Add(newGlyph);
                    glyphCount++;
                }
            }

            return new FontUpdateResult
            {
                FontName = fontName,
                Status = FontUpdateStatus.Updated,
                GlyphCount = glyphCount,
            };
        }
        catch (Exception ex)
        {
            return new FontUpdateResult
            {
                FontName = fontName,
                Status = FontUpdateStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }
}
