using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleGameObject;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single game object.
/// </summary>
public sealed record GameObjectTransplantResult
{
    public required string ObjectName { get; init; }
    public required GameObjectTransplantStatus Status { get; init; }
    public int EventCount { get; init; }
    public int SkippedActionCount { get; init; }
    public string? Diagnostic { get; init; }
}

public enum GameObjectTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new game objects from a modded archive to a target archive.
/// This includes copying scalar properties, sprite references, parent references,
/// and all events (with their code entry references).
///
/// Prerequisites:
/// - Sprites must be transplanted BEFORE objects (so SpriteIndex can be resolved).
/// - Code entries must be transplanted BEFORE objects (so event code refs can be resolved).
/// - Objects must be transplanted in dependency order (parents before children),
///   or parents that don't exist yet will be set to null.
/// </summary>
public static class GameObjectTransplanter
{
    /// <summary>
    /// Transplant multiple new game objects. Objects are processed in the given order.
    /// A second pass resolves ParentId references for newly transplanted objects.
    /// </summary>
    public static List<GameObjectTransplantResult> TransplantAll(
        IReadOnlyList<string> objectNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<GameObjectTransplantResult>(objectNames.Count);

        // First pass: create all objects (ParentId left null for now)
        var createdObjects = new Dictionary<string, UndertaleGameObject>(StringComparer.Ordinal);

        foreach (var name in objectNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData, out var newObj);
            results.Add(result);

            if (result.Status == GameObjectTransplantStatus.Transplanted && newObj is not null)
                createdObjects[name] = newObj;
        }

        // Second pass: resolve ParentId for newly created objects
        foreach (var name in objectNames)
        {
            if (!createdObjects.TryGetValue(name, out var obj)) continue;
            if (!moddedIdx.Objects.TryGetValue(name, out var moddedEntry)) continue;

            var moddedParentName = moddedEntry.Res.ParentId?.Name?.Content;
            if (moddedParentName is null) continue;

            // Try to find parent in target
            foreach (var targetObj in targetData.GameObjects)
            {
                if (targetObj.Name?.Content == moddedParentName)
                {
                    obj.ParentId = targetObj;
                    break;
                }
            }
        }

        return results;
    }

    private static GameObjectTransplantResult TransplantOne(
        string objectName,
        NameIndex moddedIdx,
        UndertaleData targetData,
        out UndertaleGameObject? createdObject)
    {
        createdObject = null;

        // Check if already exists
        foreach (var obj in targetData.GameObjects)
        {
            if (obj.Name?.Content == objectName)
            {

                return new GameObjectTransplantResult
                {
                    ObjectName = objectName,
                    Status = GameObjectTransplantStatus.AlreadyExists,
                    Diagnostic = "Object already exists in target archive.",
                };
            }
        }

        // Find in modded
        if (!moddedIdx.Objects.TryGetValue(objectName, out var moddedEntry))
        {

            return new GameObjectTransplantResult
            {
                ObjectName = objectName,
                Status = GameObjectTransplantStatus.SourceNotFound,
                Diagnostic = "Object not found in modded archive.",
            };
        }

        var srcObj = moddedEntry.Res;

        try
        {
            var newObj = new UndertaleGameObject
            {
                Name = targetData.Strings.MakeString(objectName),
                Visible = srcObj.Visible,
                Solid = srcObj.Solid,
                Depth = srcObj.Depth,
                Persistent = srcObj.Persistent,
                UsesPhysics = srcObj.UsesPhysics,
                IsSensor = srcObj.IsSensor,
                CollisionShape = srcObj.CollisionShape,
                Density = srcObj.Density,
                Restitution = srcObj.Restitution,
                Group = srcObj.Group,
                LinearDamping = srcObj.LinearDamping,
                AngularDamping = srcObj.AngularDamping,
                Friction = srcObj.Friction,
                Awake = srcObj.Awake,
                Kinematic = srcObj.Kinematic,
            };

            // Resolve sprite reference in target
            var spriteName = srcObj.Sprite?.Name?.Content;
            if (spriteName is not null)
            {
                foreach (var spr in targetData.Sprites)
                {
                    if (spr.Name?.Content == spriteName)
                    {
                        newObj.Sprite = spr;
                        break;
                    }
                }
            }

            // Resolve texture mask reference in target
            var texMaskName = srcObj.TextureMaskId?.Name?.Content;
            if (texMaskName is not null)
            {
                foreach (var spr in targetData.Sprites)
                {
                    if (spr.Name?.Content == texMaskName)
                    {
                        newObj.TextureMaskId = spr;
                        break;
                    }
                }
            }

            // ParentId is resolved in the second pass (after all objects are created)

            // Clone events. For GMS1 safety, transplant objects even when some
            // GMS2-added event code is unsupported; skip only those actions/events.
            int eventCount = 0;
            int skippedActionCount = 0;
            if (srcObj.Events is not null)
            {
                for (int eventTypeIdx = 0; eventTypeIdx < srcObj.Events.Count && eventTypeIdx < newObj.Events.Count; eventTypeIdx++)
                {
                    var srcEventList = srcObj.Events[eventTypeIdx];
                    if (srcEventList is null) continue;

                    foreach (var srcEvent in srcEventList)
                    {
                        uint eventSubtype = srcEvent.EventSubtype;
                        if (eventTypeIdx == (int)EventType.Collision)
                        {
                            var resolvedSubtype = ResolveCollisionSubtype(srcEvent.EventSubtype, moddedIdx.Data, targetData);
                            if (resolvedSubtype is null)
                            {
                                skippedActionCount += srcEvent.Actions?.Count ?? 0;
                                continue;
                            }
                            eventSubtype = resolvedSubtype.Value;
                        }

                        var newEvent = new Event
                        {
                            EventSubtype = eventSubtype,
                        };

                        if (srcEvent.Actions is not null)
                        {
                            foreach (var srcAction in srcEvent.Actions)
                            {
                                // Only set CodeId — UTMT handles version-specific
                                // serialisation of EventAction fields automatically.
                                // Copying GMS2-specific fields (LibID, Kind, ExeType …)
                                // into a GMS1 target causes the runner to SIGSEGV.
                                var newAction = new EventAction();

                                if (srcAction.CodeId is not null)
                                {
                                    var codeEntryName = srcAction.CodeId.Name?.Content;
                                    if (codeEntryName is null || targetData.Code is null)
                                    {
                                        skippedActionCount++;
                                        continue;
                                    }

                                    foreach (var code in targetData.Code)
                                    {
                                        if (code.Name?.Content == codeEntryName)
                                        {
                                            newAction.CodeId = code;
                                            break;
                                        }
                                    }

                                    if (newAction.CodeId is null)
                                    {
                                        skippedActionCount++;
                                        continue;
                                    }
                                }

                                newEvent.Actions.Add(newAction);
                                eventCount++;
                            }
                        }

                        if (newEvent.Actions.Count > 0)
                            newObj.Events[eventTypeIdx].Add(newEvent);
                    }
                }
            }

            // Clone physics vertices
            if (srcObj.PhysicsVertices is not null)
            {
                foreach (var srcVert in srcObj.PhysicsVertices)
                {
                    newObj.PhysicsVertices.Add(new UndertalePhysicsVertex
                    {
                        X = srcVert.X,
                        Y = srcVert.Y,
                    });
                }
            }

            targetData.GameObjects.Add(newObj);
            createdObject = newObj;


            return new GameObjectTransplantResult
            {
                ObjectName = objectName,
                Status = GameObjectTransplantStatus.Transplanted,
                EventCount = eventCount,
                SkippedActionCount = skippedActionCount,
                Diagnostic = skippedActionCount > 0 ? $"Skipped {skippedActionCount} unsupported/missing action(s)." : null,
            };
        }
        catch (Exception ex)
        {

            return new GameObjectTransplantResult
            {
                ObjectName = objectName,
                Status = GameObjectTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    private static uint? ResolveCollisionSubtype(
        uint moddedSubtype,
        UndertaleData moddedData,
        UndertaleData targetData)
    {
        if (moddedSubtype >= moddedData.GameObjects.Count)
            return null;

        var moddedCollisionObjectName = moddedData.GameObjects[(int)moddedSubtype]?.Name?.Content;
        if (moddedCollisionObjectName is null)
            return null;

        for (int i = 0; i < targetData.GameObjects.Count; i++)
        {
            if (targetData.GameObjects[i]?.Name?.Content == moddedCollisionObjectName)
                return (uint)i;
        }

        return null;
    }
}
