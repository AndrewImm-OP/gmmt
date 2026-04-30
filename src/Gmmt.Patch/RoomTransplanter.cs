using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleRoom;
using static UndertaleModLib.Models.UndertaleRoom.Layer;

#pragma warning disable CS8601 // Possible null reference assignment — UTMT API accepts null in some places

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single room.
/// </summary>
public sealed record RoomTransplantResult
{
    public required string RoomName { get; init; }
    public required RoomTransplantStatus Status { get; init; }
    public int LayerCount { get; init; }
    public int GameObjectCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum RoomTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new rooms from a modded archive to a target archive.
///
/// Prerequisites (must be transplanted before rooms):
/// - Sprites, Backgrounds, Fonts (for texture/layer references)
/// - Code entries (for CreationCode, PreCreateCode, object event code)
/// - Game objects (for room instance references)
///
/// Strategy:
/// - Copy all scalar room properties.
/// - Clone legacy Backgrounds/Views/GameObjects/Tiles lists.
/// - Clone GMS2 Layers with their polymorphic data subtypes.
/// - Resolve all cross-references (code, objects, sprites, backgrounds, fonts)
///   by name lookup in the target archive.
/// </summary>
public static class RoomTransplanter
{
    public static List<RoomTransplantResult> TransplantAll(
        IReadOnlyList<string> roomNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<RoomTransplantResult>(roomNames.Count);

        foreach (var name in roomNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData);
            results.Add(result);
        }

        return results;
    }

    private static RoomTransplantResult TransplantOne(
        string roomName,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        // Check if already exists in target
        if (targetData.Rooms is not null)
        {
            foreach (var r in targetData.Rooms)
            {
                if (r.Name?.Content == roomName)
                {
                    return new RoomTransplantResult
                    {
                        RoomName = roomName,
                        Status = RoomTransplantStatus.AlreadyExists,
                        Diagnostic = "Room already exists in target archive.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Rooms.TryGetValue(roomName, out var moddedEntry))
        {
            return new RoomTransplantResult
            {
                RoomName = roomName,
                Status = RoomTransplantStatus.SourceNotFound,
                Diagnostic = "Room not found in modded archive.",
            };
        }

        var srcRoom = moddedEntry.Res;

        try
        {
            var newRoom = new UndertaleRoom
            {
                Name = targetData.Strings.MakeString(roomName),
                Width = srcRoom.Width,
                Height = srcRoom.Height,
                Speed = srcRoom.Speed,
                Persistent = srcRoom.Persistent,
                BackgroundColor = srcRoom.BackgroundColor,
                DrawBackgroundColor = srcRoom.DrawBackgroundColor,
                Flags = srcRoom.Flags,
                World = srcRoom.World,
                Top = srcRoom.Top,
                Left = srcRoom.Left,
                Right = srcRoom.Right,
                Bottom = srcRoom.Bottom,
                GravityX = srcRoom.GravityX,
                GravityY = srcRoom.GravityY,
                MetersPerPixel = srcRoom.MetersPerPixel,
            };

            if (srcRoom.Caption?.Content is { } caption)
                newRoom.Caption = targetData.Strings.MakeString(caption);

            // Resolve CreationCode
            try
            {
                newRoom.CreationCodeId = ResolveCode(srcRoom.CreationCodeId, targetData, allowMissing: false);
            }
            catch (ArgumentException ex)
            {
                return new RoomTransplantResult
                {
                    RoomName = roomName,
                    Status = RoomTransplantStatus.Failed,
                    Diagnostic = ex.Message
                };
            }

            // Clone legacy backgrounds (8 slots)
            int gameObjectCount = 0;
            CloneLegacyBackgrounds(srcRoom, newRoom, targetData);
            CloneLegacyViews(srcRoom, newRoom, targetData);

            // For GMS2 rooms with layers, instances are placed via layers.
            // Only clone legacy GameObjects if no layers exist (GMS1 room).
            bool hasLayers = srcRoom.Layers is not null && srcRoom.Layers.Count > 0;
            if (!hasLayers)
                gameObjectCount += CloneLegacyGameObjects(srcRoom, newRoom, targetData);

            CloneLegacyTiles(srcRoom, newRoom, targetData);

            // Clone GMS2 layers
            int layerCount = 0;
            if (srcRoom.Layers is not null)
            {
                foreach (var srcLayer in srcRoom.Layers)
                {
                    var newLayer = CloneLayer(srcLayer, newRoom, targetData);
                    if (newLayer is not null)
                    {
                        newRoom.Layers.Add(newLayer);
                        layerCount++;
                    }
                }
            }

            targetData.Rooms.Add(newRoom);

            return new RoomTransplantResult
            {
                RoomName = roomName,
                Status = RoomTransplantStatus.Transplanted,
                LayerCount = layerCount,
                GameObjectCount = gameObjectCount,
            };
        }
        catch (Exception ex)
        {
            return new RoomTransplantResult
            {
                RoomName = roomName,
                Status = RoomTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.GetType().Name}: {ex.Message} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}",
            };
        }
    }

    // ── Legacy backgrounds ──────────────────────────────────────

    private static void CloneLegacyBackgrounds(
        UndertaleRoom srcRoom, UndertaleRoom newRoom, UndertaleData targetData)
    {
        if (srcRoom.Backgrounds is null) return;

        // newRoom.Backgrounds is auto-initialized. Match count to source.
        while (newRoom.Backgrounds.Count < srcRoom.Backgrounds.Count)
            newRoom.Backgrounds.Add(new Background());

        for (int i = 0; i < srcRoom.Backgrounds.Count; i++)
        {
            var src = srcRoom.Backgrounds[i];
            if (src is null) continue;

            var dst = newRoom.Backgrounds[i];
            dst.Enabled = src.Enabled;
            dst.Foreground = src.Foreground;
            dst.X = src.X;
            dst.Y = src.Y;
            dst.TiledHorizontally = src.TiledHorizontally;
            dst.TiledVertically = src.TiledVertically;
            dst.SpeedX = src.SpeedX;
            dst.SpeedY = src.SpeedY;
            dst.Stretch = src.Stretch;

            dst.BackgroundDefinition = ResolveBackground(src.BackgroundDefinition, targetData);
        }
    }

    // ── Legacy views ────────────────────────────────────────────

    private static void CloneLegacyViews(
        UndertaleRoom srcRoom, UndertaleRoom newRoom, UndertaleData targetData)
    {
        if (srcRoom.Views is null) return;

        while (newRoom.Views.Count < srcRoom.Views.Count)
            newRoom.Views.Add(new View());

        for (int i = 0; i < srcRoom.Views.Count; i++)
        {
            var src = srcRoom.Views[i];
            if (src is null) continue;

            var dst = newRoom.Views[i];
            dst.Enabled = src.Enabled;
            dst.ViewX = src.ViewX;
            dst.ViewY = src.ViewY;
            dst.ViewWidth = src.ViewWidth;
            dst.ViewHeight = src.ViewHeight;
            dst.PortX = src.PortX;
            dst.PortY = src.PortY;
            dst.PortWidth = src.PortWidth;
            dst.PortHeight = src.PortHeight;
            dst.BorderX = src.BorderX;
            dst.BorderY = src.BorderY;
            dst.SpeedX = src.SpeedX;
            dst.SpeedY = src.SpeedY;

            dst.ObjectId = ResolveGameObject(src.ObjectId, targetData);
        }
    }

    // ── Legacy game objects (room instances) ────────────────────

    private static int CloneLegacyGameObjects(
        UndertaleRoom srcRoom, UndertaleRoom newRoom, UndertaleData targetData)
    {
        if (srcRoom.GameObjects is null) return 0;
        int count = 0;

        foreach (var src in srcRoom.GameObjects)
        {
            if (src is null) continue;

            var dst = new UndertaleRoom.GameObject
            {
                X = src.X,
                Y = src.Y,
                InstanceID = src.InstanceID,
                ScaleX = src.ScaleX,
                ScaleY = src.ScaleY,
                Color = src.Color,
                Rotation = src.Rotation,
                ImageSpeed = src.ImageSpeed,
                ImageIndex = src.ImageIndex,
            };

            dst.ObjectDefinition = ResolveGameObject(src.ObjectDefinition, targetData);
            dst.CreationCode = ResolveCode(src.CreationCode, targetData, allowMissing: false);
            dst.PreCreateCode = ResolveCode(src.PreCreateCode, targetData, allowMissing: false);

            newRoom.GameObjects.Add(dst);
            count++;
        }

        return count;
    }

    // ── Legacy tiles ────────────────────────────────────────────

    private static void CloneLegacyTiles(
        UndertaleRoom srcRoom, UndertaleRoom newRoom, UndertaleData targetData)
    {
        if (srcRoom.Tiles is null) return;

        foreach (var src in srcRoom.Tiles)
        {
            if (src is null) continue;

            var dst = new Tile
            {
                X = src.X,
                Y = src.Y,
                SourceX = src.SourceX,
                SourceY = src.SourceY,
                Width = src.Width,
                Height = src.Height,
                TileDepth = src.TileDepth,
                InstanceID = src.InstanceID,
                ScaleX = src.ScaleX,
                ScaleY = src.ScaleY,
                Color = src.Color,
            };

            // GMS1 uses BackgroundDefinition, GMS2 uses SpriteDefinition
            dst.BackgroundDefinition = ResolveBackground(src.BackgroundDefinition, targetData);
            dst.SpriteDefinition = ResolveSprite(src.SpriteDefinition, targetData);

            newRoom.Tiles.Add(dst);
        }
    }

    // ── GMS2 Layers ─────────────────────────────────────────────

    private static Layer? CloneLayer(
        Layer srcLayer, UndertaleRoom newRoom, UndertaleData targetData)
    {
        var newLayer = new Layer
        {
            ParentRoom = newRoom,
            LayerName = targetData.Strings.MakeString(srcLayer.LayerName?.Content ?? ""),
            LayerId = srcLayer.LayerId,
            LayerType = srcLayer.LayerType,
            LayerDepth = srcLayer.LayerDepth,
            XOffset = srcLayer.XOffset,
            YOffset = srcLayer.YOffset,
            HSpeed = srcLayer.HSpeed,
            VSpeed = srcLayer.VSpeed,
            IsVisible = srcLayer.IsVisible,
        };

        // Effect properties (GMS 2022.1+)
        newLayer.EffectEnabled = srcLayer.EffectEnabled;
        if (srcLayer.EffectType?.Content is { } effectType)
            newLayer.EffectType = targetData.Strings.MakeString(effectType);

        if (srcLayer.EffectProperties is not null)
        {
            newLayer.EffectProperties = new UndertaleSimpleList<EffectProperty>();
            foreach (var srcProp in srcLayer.EffectProperties)
            {
                newLayer.EffectProperties.Add(new EffectProperty
                {
                    Kind = srcProp.Kind,
                    Name = targetData.Strings.MakeString(srcProp.Name?.Content ?? ""),
                    Value = targetData.Strings.MakeString(srcProp.Value?.Content ?? ""),
                });
            }
        }

        // Clone layer data based on type
        newLayer.Data = srcLayer.LayerType switch
        {
            LayerType.Instances => CloneInstancesData(srcLayer.InstancesData, newRoom, targetData),
            LayerType.Tiles => CloneTilesData(srcLayer.TilesData, newLayer, targetData),
            LayerType.Background => CloneBackgroundData(srcLayer.BackgroundData, newLayer, targetData),
            LayerType.Assets => CloneAssetsData(srcLayer.AssetsData, targetData),
            LayerType.Effect => CloneEffectData(srcLayer.EffectData, targetData),
            _ => null, // Path, Path2, or unknown — no data payload
        };

        return newLayer;
    }

    private static LayerInstancesData? CloneInstancesData(
        LayerInstancesData? srcData, UndertaleRoom newRoom, UndertaleData targetData)
    {
        if (srcData is null) return null;

        var newData = new LayerInstancesData();

        // Copy room instances from the source layer's Instances collection
        if (srcData.Instances is not null)
        {
            foreach (var srcInst in srcData.Instances)
            {
                if (srcInst is null) continue;

                var dst = new UndertaleRoom.GameObject
                {
                    X = srcInst.X,
                    Y = srcInst.Y,
                    InstanceID = srcInst.InstanceID,
                    ScaleX = srcInst.ScaleX,
                    ScaleY = srcInst.ScaleY,
                    Color = srcInst.Color,
                    Rotation = srcInst.Rotation,
                    ImageSpeed = srcInst.ImageSpeed,
                    ImageIndex = srcInst.ImageIndex,
                };

                dst.ObjectDefinition = ResolveGameObject(srcInst.ObjectDefinition, targetData);
                dst.CreationCode = ResolveCode(srcInst.CreationCode, targetData, allowMissing: false);
                dst.PreCreateCode = ResolveCode(srcInst.PreCreateCode, targetData, allowMissing: false);

                // In GMS2, room instances live in the main GameObjects list
                // and the layer just references them
                newRoom.GameObjects.Add(dst);
                newData.Instances.Add(dst);
            }
        }

        return newData;
    }

    private static LayerTilesData? CloneTilesData(
        LayerTilesData? srcData, Layer newLayer, UndertaleData targetData)
    {
        if (srcData is null) return null;

        var newData = new LayerTilesData
        {
            ParentLayer = newLayer,
            TilesX = srcData.TilesX,
            TilesY = srcData.TilesY,
        };

        newData.Background = ResolveBackground(srcData.Background, targetData);

        // Clone tile data (2D array of uint)
        if (srcData.TileData is not null)
        {
            newData.TileData = new uint[srcData.TileData.Length][];
            for (int i = 0; i < srcData.TileData.Length; i++)
            {
                if (srcData.TileData[i] is not null)
                    newData.TileData[i] = (uint[])srcData.TileData[i].Clone();
            }
        }

        return newData;
    }

    private static LayerBackgroundData? CloneBackgroundData(
        LayerBackgroundData? srcData, Layer newLayer, UndertaleData targetData)
    {
        if (srcData is null) return null;

        var newData = new LayerBackgroundData
        {
            ParentLayer = newLayer,
            Visible = srcData.Visible,
            Foreground = srcData.Foreground,
            TiledHorizontally = srcData.TiledHorizontally,
            TiledVertically = srcData.TiledVertically,
            Stretch = srcData.Stretch,
            Color = srcData.Color,
            FirstFrame = srcData.FirstFrame,
            AnimationSpeed = srcData.AnimationSpeed,
            AnimationSpeedType = srcData.AnimationSpeedType,
        };

        newData.Sprite = ResolveSprite(srcData.Sprite, targetData);

        return newData;
    }

    private static LayerAssetsData? CloneAssetsData(
        LayerAssetsData? srcData, UndertaleData targetData)
    {
        if (srcData is null) return null;

        var newData = new LayerAssetsData();

        // Clone legacy tiles within assets layer
        if (srcData.LegacyTiles is not null)
        {
            newData.LegacyTiles = new UndertalePointerList<Tile>();
            foreach (var src in srcData.LegacyTiles)
            {
                if (src is null) continue;
                var dst = new Tile
                {
                    X = src.X,
                    Y = src.Y,
                    SourceX = src.SourceX,
                    SourceY = src.SourceY,
                    Width = src.Width,
                    Height = src.Height,
                    TileDepth = src.TileDepth,
                    InstanceID = src.InstanceID,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Color = src.Color,
                };
                dst.BackgroundDefinition = ResolveBackground(src.BackgroundDefinition, targetData);
                dst.SpriteDefinition = ResolveSprite(src.SpriteDefinition, targetData);
                newData.LegacyTiles.Add(dst);
            }
        }

        // Clone sprite instances
        if (srcData.Sprites is not null)
        {
            newData.Sprites = new UndertalePointerList<SpriteInstance>();
            foreach (var src in srcData.Sprites)
            {
                if (src is null) continue;
                var dst = new SpriteInstance
                {
                    Name = targetData.Strings.MakeString(src.Name?.Content ?? ""),
                    X = src.X,
                    Y = src.Y,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Color = src.Color,
                    AnimationSpeed = src.AnimationSpeed,
                    AnimationSpeedType = src.AnimationSpeedType,
                    FrameIndex = src.FrameIndex,
                    Rotation = src.Rotation,
                };
                dst.Sprite = ResolveSprite(src.Sprite, targetData);
                newData.Sprites.Add(dst);
            }
        }

        // Clone sequence instances (GMS 2.3+)
        if (srcData.Sequences is not null)
        {
            newData.Sequences = new UndertalePointerList<SequenceInstance>();
            foreach (var src in srcData.Sequences)
            {
                if (src is null) continue;
                var dst = new SequenceInstance
                {
                    Name = targetData.Strings.MakeString(src.Name?.Content ?? ""),
                    X = src.X,
                    Y = src.Y,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Color = src.Color,
                    AnimationSpeed = src.AnimationSpeed,
                    AnimationSpeedType = src.AnimationSpeedType,
                    FrameIndex = src.FrameIndex,
                    Rotation = src.Rotation,
                };
                // Sequence references — resolve by name if possible
                dst.Sequence = ResolveSequence(src.Sequence, targetData);
                newData.Sequences.Add(dst);
            }
        }

        // Clone NineSlices (removed in 2.3.2, but copy if present)
        if (srcData.NineSlices is not null)
        {
            newData.NineSlices = new UndertalePointerList<SpriteInstance>();
            foreach (var src in srcData.NineSlices)
            {
                if (src is null) continue;
                var dst = new SpriteInstance
                {
                    Name = targetData.Strings.MakeString(src.Name?.Content ?? ""),
                    X = src.X,
                    Y = src.Y,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Color = src.Color,
                    AnimationSpeed = src.AnimationSpeed,
                    AnimationSpeedType = src.AnimationSpeedType,
                    FrameIndex = src.FrameIndex,
                    Rotation = src.Rotation,
                };
                dst.Sprite = ResolveSprite(src.Sprite, targetData);
                newData.NineSlices.Add(dst);
            }
        }

        // Clone particle system instances (2023.2+)
        if (srcData.ParticleSystems is not null)
        {
            newData.ParticleSystems = new UndertalePointerList<ParticleSystemInstance>();
            foreach (var src in srcData.ParticleSystems)
            {
                if (src is null) continue;
                var dst = new ParticleSystemInstance
                {
                    Name = targetData.Strings.MakeString(src.Name?.Content ?? ""),
                    X = src.X,
                    Y = src.Y,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Color = src.Color,
                    Rotation = src.Rotation,
                };
                dst.ParticleSystem = ResolveParticleSystem(src.ParticleSystem, targetData);
                newData.ParticleSystems.Add(dst);
            }
        }

        // Clone text item instances (2024.6+)
        if (srcData.TextItems is not null)
        {
            newData.TextItems = new UndertalePointerList<TextItemInstance>();
            foreach (var src in srcData.TextItems)
            {
                if (src is null) continue;
                var dst = new TextItemInstance
                {
                    Name = targetData.Strings.MakeString(src.Name?.Content ?? ""),
                    X = src.X,
                    Y = src.Y,
                    ScaleX = src.ScaleX,
                    ScaleY = src.ScaleY,
                    Rotation = src.Rotation,
                    Color = src.Color,
                    OriginX = src.OriginX,
                    OriginY = src.OriginY,
                    Alignment = src.Alignment,
                    CharSpacing = src.CharSpacing,
                    LineSpacing = src.LineSpacing,
                    FrameWidth = src.FrameWidth,
                    FrameHeight = src.FrameHeight,
                    Wrap = src.Wrap,
                };
                if (src.Text?.Content is { } text)
                    dst.Text = targetData.Strings.MakeString(text);
                dst.Font = ResolveFont(src.Font, targetData);
                newData.TextItems.Add(dst);
            }
        }

        return newData;
    }

    private static LayerEffectData? CloneEffectData(
        LayerEffectData? srcData, UndertaleData targetData)
    {
        if (srcData is null) return null;

        var newData = new LayerEffectData();

        if (srcData.EffectType?.Content is { } effectType)
            newData.EffectType = targetData.Strings.MakeString(effectType);

        if (srcData.Properties is not null)
        {
            newData.Properties = new UndertaleSimpleList<EffectProperty>();
            foreach (var srcProp in srcData.Properties)
            {
                newData.Properties.Add(new EffectProperty
                {
                    Kind = srcProp.Kind,
                    Name = targetData.Strings.MakeString(srcProp.Name?.Content ?? ""),
                    Value = targetData.Strings.MakeString(srcProp.Value?.Content ?? ""),
                });
            }
        }

        return newData;
    }

    // ── Cross-reference resolution helpers ──────────────────────

    private static UndertaleCode? ResolveCode(UndertaleCode? srcCode, UndertaleData targetData, bool allowMissing = true)
    {
        var name = srcCode?.Name?.Content;
        if (name is null || targetData.Code is null) return null;

        foreach (var code in targetData.Code)
        {
            if (code.Name?.Content == name)
                return code;
        }
        
        if (!allowMissing)
        {
            Console.WriteLine($"WARNING: Code entry {name} not found.");
            return null;
        }
            throw new ArgumentException($"Code entry '{name}' not found in target.");
            
        return null;
    }

    private static UndertaleGameObject? ResolveGameObject(
        UndertaleGameObject? srcObj, UndertaleData targetData)
    {
        var name = srcObj?.Name?.Content;
        if (name is null) return null;

        foreach (var obj in targetData.GameObjects)
        {
            if (obj.Name?.Content == name)
                return obj;
        }
        return null;
    }

    private static UndertaleSprite? ResolveSprite(
        UndertaleSprite? srcSprite, UndertaleData targetData)
    {
        var name = srcSprite?.Name?.Content;
        if (name is null) return null;

        foreach (var spr in targetData.Sprites)
        {
            if (spr.Name?.Content == name)
                return spr;
        }
        return null;
    }

    private static UndertaleBackground? ResolveBackground(
        UndertaleBackground? srcBg, UndertaleData targetData)
    {
        var name = srcBg?.Name?.Content;
        if (name is null || targetData.Backgrounds is null) return null;

        foreach (var bg in targetData.Backgrounds)
        {
            if (bg.Name?.Content == name)
                return bg;
        }
        return null;
    }

    private static UndertaleFont? ResolveFont(
        UndertaleFont? srcFont, UndertaleData targetData)
    {
        var name = srcFont?.Name?.Content;
        if (name is null || targetData.Fonts is null) return null;

        foreach (var font in targetData.Fonts)
        {
            if (font.Name?.Content == name)
                return font;
        }
        return null;
    }

    private static UndertaleSequence? ResolveSequence(
        UndertaleSequence? srcSeq, UndertaleData targetData)
    {
        var name = srcSeq?.Name?.Content;
        if (name is null || targetData.Sequences is null) return null;

        foreach (var seq in targetData.Sequences)
        {
            if (seq.Name?.Content == name)
                return seq;
        }
        return null;
    }

    private static UndertaleParticleSystem? ResolveParticleSystem(
        UndertaleParticleSystem? srcPs, UndertaleData targetData)
    {
        var name = srcPs?.Name?.Content;
        if (name is null || targetData.ParticleSystems is null) return null;

        foreach (var ps in targetData.ParticleSystems)
        {
            if (ps.Name?.Content == name)
                return ps;
        }
        return null;
    }
}
