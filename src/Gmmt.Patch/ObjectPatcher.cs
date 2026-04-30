using Gmmt.Core.Indexing;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Applies scalar property patches to game objects in a target archive.
/// Sprite and parent references are resolved by name via NameIndex.
/// Never transfers raw resource IDs. Refuses with diagnostics on failure.
/// </summary>
public static class ObjectPatcher
{
    /// <summary>
    /// Apply a single object patch request. Mutates the target data in-place.
    /// </summary>
    public static ObjectPatchResult Apply(ObjectPatchRequest request, NameIndex targetIdx)
    {
        if (request.Patches.Count == 0)
        {
            return new ObjectPatchResult
            {
                ObjectName = request.ObjectName,
                Status = ObjectPatchStatus.NothingToDo,
                Properties = [],
                Diagnostic = "No property patches specified.",
            };
        }

        if (!targetIdx.Objects.TryGetValue(request.ObjectName, out var entry))
        {
            return new ObjectPatchResult
            {
                ObjectName = request.ObjectName,
                Status = ObjectPatchStatus.ObjectMissing,
                Properties = [],
                Diagnostic = $"Object '{request.ObjectName}' not found in target archive.",
            };
        }

        var obj = entry.Res;
        var results = new List<PropertyPatchResult>(request.Patches.Count);

        foreach (var patch in request.Patches)
        {
            var result = ApplyProperty(obj, patch, targetIdx);
            results.Add(result);
        }

        int applied = results.Count(r => r.Status == PropertyPatchStatus.Applied);
        int alreadyEqual = results.Count(r => r.Status == PropertyPatchStatus.AlreadyEqual);
        int failed = results.Count(r =>
            r.Status is PropertyPatchStatus.ResourceNotFound or PropertyPatchStatus.Skipped);

        ObjectPatchStatus status;
        if (failed == results.Count)
            status = ObjectPatchStatus.Failed;
        else if (applied == 0 && alreadyEqual > 0 && failed == 0)
            status = ObjectPatchStatus.Applied; // all already equal counts as success
        else if (failed > 0)
            status = ObjectPatchStatus.PartiallyApplied;
        else
            status = ObjectPatchStatus.Applied;

        return new ObjectPatchResult
        {
            ObjectName = request.ObjectName,
            Status = status,
            Properties = results,
        };
    }

    /// <summary>
    /// Apply a list of object patch requests. Mutates the target data in-place.
    /// </summary>
    public static List<ObjectPatchResult> ApplyAll(
        IReadOnlyList<ObjectPatchRequest> requests,
        NameIndex targetIdx)
    {
        var results = new List<ObjectPatchResult>(requests.Count);
        foreach (var request in requests)
        {
            results.Add(Apply(request, targetIdx));
        }
        return results;
    }

    // ─────────────────────────────────────────────────────────
    // Per-property dispatch
    // ─────────────────────────────────────────────────────────

    private static PropertyPatchResult ApplyProperty(
        UndertaleGameObject obj,
        ObjectPropertyPatch patch,
        NameIndex targetIdx)
    {
        return patch.Property switch
        {
            "Visible" => ApplyBool(patch, obj.Visible, v => obj.Visible = v),
            "Solid" => ApplyBool(patch, obj.Solid, v => obj.Solid = v),
            "Persistent" => ApplyBool(patch, obj.Persistent, v => obj.Persistent = v),
            "Depth" => ApplyInt(patch, obj.Depth, v => obj.Depth = v),
            "SpriteIndex" => ApplySprite(patch, obj, targetIdx),
            "ParentId" => ApplyParent(patch, obj, targetIdx),
            _ => new PropertyPatchResult
            {
                Property = patch.Property,
                Status = PropertyPatchStatus.Skipped,
                OldValue = "?",
                NewValue = patch.Value?.ToString() ?? "(null)",
                Diagnostic = $"Unknown property '{patch.Property}'.",
            },
        };
    }

    // ─────────────────────────────────────────────────────────
    // Typed applicators
    // ─────────────────────────────────────────────────────────

    private static PropertyPatchResult ApplyBool(
        ObjectPropertyPatch patch, bool current, Action<bool> setter)
    {
        if (patch.Value is not bool newVal)
        {
            return new PropertyPatchResult
            {
                Property = patch.Property,
                Status = PropertyPatchStatus.Skipped,
                OldValue = current.ToString(),
                NewValue = patch.Value?.ToString() ?? "(null)",
                Diagnostic = $"Expected bool value for '{patch.Property}', got {patch.Value?.GetType().Name ?? "null"}.",
            };
        }

        var oldStr = current.ToString();
        if (current == newVal)
        {
            return new PropertyPatchResult
            {
                Property = patch.Property,
                Status = PropertyPatchStatus.AlreadyEqual,
                OldValue = oldStr,
                NewValue = newVal.ToString(),
            };
        }

        setter(newVal);
        return new PropertyPatchResult
        {
            Property = patch.Property,
            Status = PropertyPatchStatus.Applied,
            OldValue = oldStr,
            NewValue = newVal.ToString(),
        };
    }

    private static PropertyPatchResult ApplyInt(
        ObjectPropertyPatch patch, int current, Action<int> setter)
    {
        if (patch.Value is not int newVal)
        {
            // Try converting from other numeric types / string
            if (patch.Value is long l)
                newVal = (int)l;
            else if (patch.Value is string s && int.TryParse(s, out var parsed))
                newVal = parsed;
            else
            {
                return new PropertyPatchResult
                {
                    Property = patch.Property,
                    Status = PropertyPatchStatus.Skipped,
                    OldValue = current.ToString(),
                    NewValue = patch.Value?.ToString() ?? "(null)",
                    Diagnostic = $"Expected int value for '{patch.Property}', got {patch.Value?.GetType().Name ?? "null"}.",
                };
            }
        }

        var oldStr = current.ToString();
        if (current == newVal)
        {
            return new PropertyPatchResult
            {
                Property = patch.Property,
                Status = PropertyPatchStatus.AlreadyEqual,
                OldValue = oldStr,
                NewValue = newVal.ToString(),
            };
        }

        setter(newVal);
        return new PropertyPatchResult
        {
            Property = patch.Property,
            Status = PropertyPatchStatus.Applied,
            OldValue = oldStr,
            NewValue = newVal.ToString(),
        };
    }

    private static PropertyPatchResult ApplySprite(
        ObjectPropertyPatch patch,
        UndertaleGameObject obj,
        NameIndex targetIdx)
    {
        var oldName = obj.Sprite?.Name?.Content ?? "(none)";

        // null clears the sprite
        if (patch.Value is null)
        {
            if (obj.Sprite is null)
            {
                return new PropertyPatchResult
                {
                    Property = "SpriteIndex",
                    Status = PropertyPatchStatus.AlreadyEqual,
                    OldValue = oldName,
                    NewValue = "(none)",
                };
            }

            obj.Sprite = null!;
            return new PropertyPatchResult
            {
                Property = "SpriteIndex",
                Status = PropertyPatchStatus.Applied,
                OldValue = oldName,
                NewValue = "(none)",
            };
        }

        if (patch.Value is not string spriteName)
        {
            return new PropertyPatchResult
            {
                Property = "SpriteIndex",
                Status = PropertyPatchStatus.Skipped,
                OldValue = oldName,
                NewValue = patch.Value.ToString() ?? "(null)",
                Diagnostic = $"Expected string (sprite name) for SpriteIndex, got {patch.Value.GetType().Name}.",
            };
        }

        if (!targetIdx.Sprites.TryGetValue(spriteName, out var spriteEntry))
        {
            return new PropertyPatchResult
            {
                Property = "SpriteIndex",
                Status = PropertyPatchStatus.ResourceNotFound,
                OldValue = oldName,
                NewValue = spriteName,
                Diagnostic = $"Sprite '{spriteName}' not found in target archive.",
            };
        }

        if (string.Equals(oldName, spriteName, StringComparison.Ordinal))
        {
            return new PropertyPatchResult
            {
                Property = "SpriteIndex",
                Status = PropertyPatchStatus.AlreadyEqual,
                OldValue = oldName,
                NewValue = spriteName,
            };
        }

        obj.Sprite = spriteEntry.Res;
        return new PropertyPatchResult
        {
            Property = "SpriteIndex",
            Status = PropertyPatchStatus.Applied,
            OldValue = oldName,
            NewValue = spriteName,
        };
    }

    private static PropertyPatchResult ApplyParent(
        ObjectPropertyPatch patch,
        UndertaleGameObject obj,
        NameIndex targetIdx)
    {
        var oldName = obj.ParentId?.Name?.Content ?? "(none)";

        // null clears the parent
        if (patch.Value is null)
        {
            if (obj.ParentId is null)
            {
                return new PropertyPatchResult
                {
                    Property = "ParentId",
                    Status = PropertyPatchStatus.AlreadyEqual,
                    OldValue = oldName,
                    NewValue = "(none)",
                };
            }

            obj.ParentId = null!;
            return new PropertyPatchResult
            {
                Property = "ParentId",
                Status = PropertyPatchStatus.Applied,
                OldValue = oldName,
                NewValue = "(none)",
            };
        }

        if (patch.Value is not string parentName)
        {
            return new PropertyPatchResult
            {
                Property = "ParentId",
                Status = PropertyPatchStatus.Skipped,
                OldValue = oldName,
                NewValue = patch.Value.ToString() ?? "(null)",
                Diagnostic = $"Expected string (object name) for ParentId, got {patch.Value.GetType().Name}.",
            };
        }

        if (!targetIdx.Objects.TryGetValue(parentName, out var parentEntry))
        {
            return new PropertyPatchResult
            {
                Property = "ParentId",
                Status = PropertyPatchStatus.ResourceNotFound,
                OldValue = oldName,
                NewValue = parentName,
                Diagnostic = $"Object '{parentName}' not found in target archive.",
            };
        }

        if (string.Equals(oldName, parentName, StringComparison.Ordinal))
        {
            return new PropertyPatchResult
            {
                Property = "ParentId",
                Status = PropertyPatchStatus.AlreadyEqual,
                OldValue = oldName,
                NewValue = parentName,
            };
        }

        obj.ParentId = parentEntry.Res;
        return new PropertyPatchResult
        {
            Property = "ParentId",
            Status = PropertyPatchStatus.Applied,
            OldValue = oldName,
            NewValue = parentName,
        };
    }
}
