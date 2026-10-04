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
    public uint MaxInstanceId { get; init; }
    public uint MaxTileId { get; init; }
    public bool AddedToRoomOrder { get; init; }
    public bool SkippedCreationCode { get; init; }
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
        bool targetIsGMS1 = (targetData.GeneralInfo?.Major ?? 1) < 2;

        try
        {
            var newRoom = new UndertaleRoom
            {
                Name = targetData.Strings.MakeString(roomName),
                Width = srcRoom.Width,
                Height = srcRoom.Height,
                Speed = targetIsGMS1 && srcRoom.Speed == 0 ? 30u : srcRoom.Speed,
                Persistent = srcRoom.Persistent,
                BackgroundColor = srcRoom.BackgroundColor,
                DrawBackgroundColor = srcRoom.DrawBackgroundColor,
                Flags = SanitizeFlags(srcRoom.Flags, targetIsGMS1),
                World = targetIsGMS1 ? false : srcRoom.World,
                Top = targetIsGMS1 ? 0 : srcRoom.Top,
                Left = targetIsGMS1 ? 0 : srcRoom.Left,
                Right = targetIsGMS1 ? srcRoom.Width : srcRoom.Right,
                Bottom = targetIsGMS1 ? srcRoom.Height : srcRoom.Bottom,
                GravityX = targetIsGMS1 ? 0 : srcRoom.GravityX,
                GravityY = targetIsGMS1 ? 10 : srcRoom.GravityY,
                MetersPerPixel = targetIsGMS1 ? 0.1f : srcRoom.MetersPerPixel,
            };

            newRoom.Caption = targetData.Strings.MakeString(srcRoom.Caption?.Content ?? "");

            // Resolve CreationCode. When converting GMS2 rooms to GMS1, unsupported
            // room creation code often contains layer_* calls that have no legacy
            // equivalent after layer→legacy conversion. Skip it instead of dropping
            // the whole room.
            var resolvedCreationCode = ResolveCode(srcRoom.CreationCodeId, targetData, allowMissing: targetIsGMS1);
            bool skippedCreationCode = srcRoom.CreationCodeId is not null && resolvedCreationCode is null;
            newRoom.CreationCodeId = resolvedCreationCode;

            // Clone legacy backgrounds (8 slots)
            int gameObjectCount = 0;
            CloneLegacyBackgrounds(srcRoom, newRoom, targetData);
            CloneLegacyViews(srcRoom, newRoom, targetData);

            bool hasLayers = srcRoom.Layers is not null && srcRoom.Layers.Count > 0;

            int layerCount = 0;

            if (hasLayers && targetIsGMS1)
            {
                // GMS2→GMS1 conversion: extract layer data into legacy structures.
                // GMS1 runner does not understand Layers — do NOT add them to newRoom.
                gameObjectCount += ConvertLayersToLegacy(srcRoom, newRoom, targetData);
            }
            else if (hasLayers)
            {
                // GMS2→GMS2: clone layers as-is
                if (!hasLayers)
                    gameObjectCount += CloneLegacyGameObjects(srcRoom, newRoom, targetData);

                CloneLegacyTiles(srcRoom, newRoom, targetData);

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
            }
            else
            {
                // GMS1 source room (no layers): clone legacy structures
                gameObjectCount += CloneLegacyGameObjects(srcRoom, newRoom, targetData);
                CloneLegacyTiles(srcRoom, newRoom, targetData);
            }

            if (targetData.Rooms is null)
                throw new InvalidOperationException("Target archive has no ROOM chunk.");

            targetData.Rooms.Add(newRoom);

            bool addedToRoomOrder = AddToRoomOrder(newRoom, targetData);
            var (maxInstanceId, maxTileId) = UpdateRoomInstanceCounters(targetData);

            return new RoomTransplantResult
            {
                RoomName = roomName,
                Status = RoomTransplantStatus.Transplanted,
                LayerCount = layerCount,
                GameObjectCount = gameObjectCount,
                MaxInstanceId = maxInstanceId,
                MaxTileId = maxTileId,
                AddedToRoomOrder = addedToRoomOrder,
                SkippedCreationCode = skippedCreationCode,
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

    private static RoomEntryFlags SanitizeFlags(RoomEntryFlags flags, bool targetIsGMS1)
    {
        if (!targetIsGMS1)
            return flags;

        const RoomEntryFlags gms1Flags =
            RoomEntryFlags.EnableViews |
            RoomEntryFlags.ShowColor |
            RoomEntryFlags.DoNotClearDisplayBuffer;

        return flags & gms1Flags;
    }

    private static bool AddToRoomOrder(UndertaleRoom room, UndertaleData targetData)
    {
        var roomOrder = targetData.GeneralInfo?.RoomOrder;
        if (roomOrder is null)
            return false;

        foreach (var existingRef in roomOrder)
        {
            var existingRoom = existingRef?.Resource;
            if (ReferenceEquals(existingRoom, room) || existingRoom?.Name?.Content == room.Name?.Content)
                return false;
        }

        roomOrder.Add(new UndertaleResourceById<UndertaleRoom, UndertaleChunkROOM>(room));
        return true;
    }

    private static (uint maxInstanceId, uint maxTileId) UpdateRoomInstanceCounters(UndertaleData targetData)
    {
        uint maxInstanceId = 0;
        uint maxTileId = 0;

        if (targetData.Rooms is not null)
        {
            foreach (var room in targetData.Rooms)
            {
                if (room?.GameObjects is not null)
                {
                    foreach (var obj in room.GameObjects)
                    {
                        if (obj is not null && obj.InstanceID > maxInstanceId)
                            maxInstanceId = obj.InstanceID;
                    }
                }

                if (room?.Tiles is not null)
                {
                    foreach (var tile in room.Tiles)
                    {
                        if (tile is not null && tile.InstanceID > maxTileId)
                            maxTileId = tile.InstanceID;
                    }
                }
            }
        }

        if (targetData.GeneralInfo is not null)
        {
            targetData.GeneralInfo.LastObj = Math.Max(targetData.GeneralInfo.LastObj, NextFreeId(maxInstanceId));
            targetData.GeneralInfo.LastTile = Math.Max(targetData.GeneralInfo.LastTile, NextFreeId(maxTileId));
        }

        return (maxInstanceId, maxTileId);
    }

    private static uint NextFreeId(uint maxUsedId) =>
        maxUsedId == uint.MaxValue ? uint.MaxValue : maxUsedId + 1;

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

            var backgroundDefinition = ResolveBackground(src.BackgroundDefinition, targetData);
            if (dst.Enabled && backgroundDefinition is null)
            {
                // GMS1 runner is not robust around enabled room backgrounds
                // with an invalid BGND reference. Disable instead of writing -1.
                dst.Enabled = false;
            }
            dst.BackgroundDefinition = backgroundDefinition;
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

            var followObject = ResolveGameObject(src.ObjectId, targetData);
            if (dst.Enabled && src.ObjectId is not null && followObject is null)
            {
                // A view following a missing object is safer disabled in GMS1.
                dst.Enabled = false;
            }
            dst.ObjectId = followObject;
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
                // GMS1 room instances do not serialize ImageSpeed/ImageIndex.
                // Keep deterministic defaults instead of carrying GMS2 editor-only values.
                ImageSpeed = 1,
                ImageIndex = 0,
            };

            dst.ObjectDefinition = ResolveRequiredGameObject(src.ObjectDefinition, targetData, "legacy room instance");
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
            bool targetIsGMS1 = (targetData.GeneralInfo?.Major ?? 1) < 2;
            if (targetIsGMS1)
            {
                dst.BackgroundDefinition =
                    ResolveBackground(src.BackgroundDefinition, targetData) ??
                    FindOrCreateBackgroundFromSprite(src.SpriteDefinition, targetData);

                if (dst.BackgroundDefinition is null)
                    continue;
            }
            else
            {
                // GMS1 uses BackgroundDefinition, GMS2 uses SpriteDefinition
                dst.BackgroundDefinition = ResolveBackground(src.BackgroundDefinition, targetData);
                dst.SpriteDefinition = ResolveSprite(src.SpriteDefinition, targetData);
            }

            newRoom.Tiles.Add(dst);
        }
    }

    // ── GMS2→GMS1 layer conversion ────────────────────────────

    /// <summary>
    /// Converts GMS2 Layer data into GMS1 legacy room structures.
    /// GMS1 runner does not understand Layers — this extracts:
    ///   - Instances layers → legacy GameObjects
    ///   - Background layers → legacy Backgrounds (8-slot array)
    ///   - Tiles layers are logged but not converted (GMS2 uses tilemaps,
    ///     GMS1 uses individual tile objects — no lossless conversion).
    /// </summary>
    private static int ConvertLayersToLegacy(
        UndertaleRoom srcRoom, UndertaleRoom newRoom, UndertaleData targetData)
    {
        int gameObjectCount = 0;
        int bgSlot = 0;

        // Also clone any legacy tiles that may exist alongside layers
        CloneLegacyTiles(srcRoom, newRoom, targetData);

        if (srcRoom.Layers is null) return 0;

        foreach (var srcLayer in srcRoom.Layers)
        {
            switch (srcLayer.LayerType)
            {
                case LayerType.Instances:
                    if (srcLayer.InstancesData?.Instances is not null)
                    {
                        foreach (var srcInst in srcLayer.InstancesData.Instances)
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
                                // GMS1 room instances do not serialize ImageSpeed/ImageIndex.
                                // Keep deterministic defaults instead of carrying GMS2 editor-only values.
                                ImageSpeed = 1,
                                ImageIndex = 0,
                            };

                            dst.ObjectDefinition = ResolveRequiredGameObject(srcInst.ObjectDefinition, targetData, $"layer '{srcLayer.LayerName?.Content ?? srcLayer.LayerId.ToString()}' instance");
                            dst.CreationCode = ResolveCode(srcInst.CreationCode, targetData, allowMissing: false);
                            dst.PreCreateCode = ResolveCode(srcInst.PreCreateCode, targetData, allowMissing: false);

                            newRoom.GameObjects.Add(dst);
                            gameObjectCount++;
                        }
                    }
                    break;

                case LayerType.Background:
                    if (srcLayer.BackgroundData is not null)
                    {
                        var srcBg = srcLayer.BackgroundData;
                        var backgroundDefinition =
                            FindOrCreateBackgroundFromSprite(srcBg.Sprite, targetData);

                        if (backgroundDefinition is null)
                        {
                            ApplyBackgroundColorLayer(srcBg, newRoom);
                            break;
                        }

                        if (bgSlot >= 8)
                            break;

                        while (newRoom.Backgrounds.Count <= bgSlot)
                            newRoom.Backgrounds.Add(new Background());

                        var bg = newRoom.Backgrounds[bgSlot];

                        bg.Enabled = srcBg.Visible;
                        bg.Foreground = srcBg.Foreground;
                        bg.TiledHorizontally = srcBg.TiledHorizontally;
                        bg.TiledVertically = srcBg.TiledVertically;
                        bg.Stretch = srcBg.Stretch;
                        bg.X = (int)srcLayer.XOffset;
                        bg.Y = (int)srcLayer.YOffset;
                        bg.SpeedX = (int)srcLayer.HSpeed;
                        bg.SpeedY = (int)srcLayer.VSpeed;
                        bg.BackgroundDefinition = backgroundDefinition;

                        bgSlot++;
                    }
                    break;

                case LayerType.Tiles:
                    // GMS2 tile layers use tilemaps (TilesX × TilesY grid of uint IDs).
                    // GMS1 uses individual Tile objects — no direct lossless conversion.
                    // Skip for now; tiles from legacy Tiles list are already cloned above.
                    break;

                case LayerType.Assets:
                    // Assets layer can contain sprite instances and legacy tiles.
                    // Extract legacy tiles from assets layers into the room's Tiles list.
                    if (srcLayer.AssetsData?.LegacyTiles is not null)
                    {
                        foreach (var src in srcLayer.AssetsData.LegacyTiles)
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
                            dst.BackgroundDefinition =
                                ResolveBackground(src.BackgroundDefinition, targetData) ??
                                FindOrCreateBackgroundFromSprite(src.SpriteDefinition, targetData);

                            if (dst.BackgroundDefinition is null)
                                continue;

                            newRoom.Tiles.Add(dst);
                        }
                    }
                    break;

                default:
                    // Effect, Path, etc. — no GMS1 equivalent, skip
                    break;
            }
        }

        return gameObjectCount;
    }

    private static void ApplyBackgroundColorLayer(
        LayerBackgroundData srcBg, UndertaleRoom newRoom)
    {
        if (!srcBg.Visible)
            return;

        newRoom.DrawBackgroundColor = true;
        newRoom.BackgroundColor = 0xFF000000 | (srcBg.Color & 0x00FFFFFF);
        newRoom.Flags |= RoomEntryFlags.ShowColor;
    }

    /// <summary>
    /// Try to find a background resource by name (for GMS2→GMS1 sprite→background mapping).
    /// Returns null-wrapped result for use with ResolveBackground.
    /// </summary>
    private static UndertaleBackground? FindBackgroundByName(string name, UndertaleData targetData)
    {
        if (targetData.Backgrounds is null) return null;
        foreach (var bg in targetData.Backgrounds)
        {
            if (bg.Name?.Content == name)
                return bg;
        }
        return null;
    }

    private static UndertaleBackground? FindOrCreateBackgroundFromSprite(
        UndertaleSprite? srcSprite, UndertaleData targetData)
    {
        var spriteName = srcSprite?.Name?.Content;
        if (spriteName is null || targetData.Backgrounds is null)
            return null;

        var existing = FindBackgroundByName(spriteName, targetData);
        if (existing is not null)
            return existing;

        var targetSprite = ResolveSprite(srcSprite, targetData);
        var texture = targetSprite?.Textures?.FirstOrDefault()?.Texture;
        if (texture is null)
            return null;

        var background = new UndertaleBackground
        {
            Name = targetData.Strings.MakeString(spriteName),
            Transparent = true,
            Smooth = false,
            Preload = false,
            Texture = texture,
        };

        targetData.Backgrounds.Add(background);
        return background;
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

        if (allowMissing)
        {
            Console.WriteLine($"WARNING: Code entry {name} not found.");
            return null;
        }

        throw new ArgumentException($"Code entry '{name}' not found in target.");
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

    private static UndertaleGameObject ResolveRequiredGameObject(
        UndertaleGameObject? srcObj,
        UndertaleData targetData,
        string context)
    {
        var name = srcObj?.Name?.Content;
        if (name is null)
            throw new ArgumentException($"Missing object reference in {context}.");

        var resolved = ResolveGameObject(srcObj, targetData);
        if (resolved is null)
            throw new ArgumentException($"Object '{name}' referenced by {context} was not found in target.");

        return resolved;
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
