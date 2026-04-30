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

            // Clone events
            int eventCount = 0;
            if (srcObj.Events is not null)
            {
                for (int eventTypeIdx = 0; eventTypeIdx < srcObj.Events.Count && eventTypeIdx < newObj.Events.Count; eventTypeIdx++)
                {
                    var srcEventList = srcObj.Events[eventTypeIdx];
                    if (srcEventList is null) continue;

                    foreach (var srcEvent in srcEventList)
                    {
                        var newEvent = new Event
                        {
                            EventSubtype = srcEvent.EventSubtype,
                        };

                        if (srcEvent.Actions is not null)
                        {
                            foreach (var srcAction in srcEvent.Actions)
                            {
                                var newAction = new EventAction
                                {
                                    LibID = srcAction.LibID,
                                    ID = srcAction.ID,
                                    Kind = srcAction.Kind,
                                    UseRelative = srcAction.UseRelative,
                                    IsQuestion = srcAction.IsQuestion,
                                    UseApplyTo = srcAction.UseApplyTo,
                                    ExeType = srcAction.ExeType,
                                    ActionName = targetData.Strings.MakeString(
                                        srcAction.ActionName?.Content ?? ""),
                                    ArgumentCount = srcAction.ArgumentCount,
                                    Who = srcAction.Who,
                                    Relative = srcAction.Relative,
                                    IsNot = srcAction.IsNot,
                                    UnknownAlwaysZero = srcAction.UnknownAlwaysZero,
                                };

                                // Resolve code reference in target
                                if (srcAction.CodeId is not null)
                                {
                                    var codeEntryName = srcAction.CodeId.Name?.Content;
                                    if (codeEntryName is not null && targetData.Code is not null)
                                    {
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
                                            
            return new GameObjectTransplantResult
                                            {
                                                ObjectName = objectName,
                                                Status = GameObjectTransplantStatus.Failed,
                                                Diagnostic = $"Code entry '{codeEntryName}' for event {eventTypeIdx} not found in target."
                                            };
                                        }
                                    }
                                    else
                                    {
                                         
            return new GameObjectTransplantResult
                                         {
                                             ObjectName = objectName,
                                             Status = GameObjectTransplantStatus.Failed,
                                             Diagnostic = $"Action has CodeId but no resolvable name in event {eventTypeIdx}."
                                         };
                                    }
                                }

                                newEvent.Actions.Add(newAction);
                                eventCount++;
                            }
                        }

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
}
