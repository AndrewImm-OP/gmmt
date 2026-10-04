using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Result of patching events on a single game object.
/// </summary>
public sealed record ObjectEventPatchResult
{
    public required string ObjectName { get; init; }
    public required ObjectEventPatchStatus Status { get; init; }
    public int EventsAdded { get; init; }
    public int EventsAlreadyPresent { get; init; }
    public int EventsFailed { get; init; }
    public string? Diagnostic { get; init; }
}

public enum ObjectEventPatchStatus
{
    Applied,
    NothingToDo,
    ObjectMissing,
    Failed,
}

/// <summary>
/// Patches event lists on existing game objects. When a mod adds new events to
/// an existing object, the code entries are transplanted separately, but the
/// event→code binding on the object must also be created. This patcher handles
/// that binding.
///
/// Strategy:
/// - Compare event lists between vanilla and modded for each shared object.
/// - For each event present in modded but absent in vanilla, find the corresponding
///   code entry in the target archive and bind it to the object.
/// - Does NOT remove events (too dangerous — could break the game).
/// </summary>
public static class ObjectEventPatcher
{
    /// <summary>
    /// Patch events for all objects that have new events added by the mod.
    /// </summary>
    public static List<ObjectEventPatchResult> PatchAll(
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<ObjectEventPatchResult>();

        foreach (var (name, moddedEntry) in moddedIdx.Objects)
        {
            // Only process objects that exist in both vanilla and modded
            if (!vanillaIdx.Objects.TryGetValue(name, out var vanillaEntry))
                continue;

            var result = PatchOne(name, vanillaEntry.Res, moddedEntry.Res, targetData);
            if (result.Status != ObjectEventPatchStatus.NothingToDo)
                results.Add(result);
        }

        return results;
    }

    private static ObjectEventPatchResult PatchOne(
        string objectName,
        UndertaleGameObject vanillaObj,
        UndertaleGameObject moddedObj,
        UndertaleData targetData)
    {
        // Find target object
        UndertaleGameObject? targetObj = null;
        if (targetData.GameObjects is not null)
        {
            foreach (var obj in targetData.GameObjects)
            {
                if (obj.Name?.Content == objectName)
                {
                    targetObj = obj;
                    break;
                }
            }
        }

        if (targetObj is null)
        {
            return new ObjectEventPatchResult
            {
                ObjectName = objectName,
                Status = ObjectEventPatchStatus.ObjectMissing,
                Diagnostic = "Object not found in target archive.",
            };
        }

        // Collect event signatures from vanilla and modded
        var vanillaEvents = CollectEventSignatures(vanillaObj);
        var moddedEvents = CollectEventSignatures(moddedObj);

        // Find events added by the mod (in modded but not in vanilla)
        var addedEvents = new List<EventSignature>();
        foreach (var sig in moddedEvents)
        {
            if (!vanillaEvents.Contains(sig))
                addedEvents.Add(sig);
        }

        if (addedEvents.Count == 0)
        {
            return new ObjectEventPatchResult
            {
                ObjectName = objectName,
                Status = ObjectEventPatchStatus.NothingToDo,
            };
        }

        int added = 0, alreadyPresent = 0, failed = 0;

        foreach (var sig in addedEvents)
        {
            // Check if target already has this event
            if (TargetHasEvent(targetObj, sig.EventTypeIndex, sig.EventSubtype))
            {
                alreadyPresent++;
                continue;
            }

            // Find the code entry in target
            var codeName = sig.CodeEntryName;
            UndertaleCode? targetCode = null;
            if (targetData.Code is not null && codeName is not null)
            {
                foreach (var c in targetData.Code)
                {
                    if (c.Name?.Content == codeName)
                    {
                        targetCode = c;
                        break;
                    }
                }
            }

            if (targetCode is null)
            {
                // Code entry not found in target — it may not have been transplanted
                // or codeName was null. Either way, skip — binding with null CodeId
                // causes the runner to SEGV.
                failed++;
                continue;
            }

            // Create the event binding
            try
            {
                BindEvent(targetObj, targetData, sig.EventTypeIndex, sig.EventSubtype, targetCode);
                added++;
            }
            catch
            {
                failed++;
            }
        }

        if (added == 0 && failed == 0 && alreadyPresent > 0)
        {
            return new ObjectEventPatchResult
            {
                ObjectName = objectName,
                Status = ObjectEventPatchStatus.NothingToDo,
                EventsAlreadyPresent = alreadyPresent,
            };
        }

        return new ObjectEventPatchResult
        {
            ObjectName = objectName,
            Status = failed > 0 && added == 0 ? ObjectEventPatchStatus.Failed : ObjectEventPatchStatus.Applied,
            EventsAdded = added,
            EventsAlreadyPresent = alreadyPresent,
            EventsFailed = failed,
            Diagnostic = failed > 0 ? $"{failed} event(s) could not be bound (missing code entry in target)" : null,
        };
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────

    private record struct EventSignature(int EventTypeIndex, uint EventSubtype, string? CodeEntryName);

    private static HashSet<EventSignature> CollectEventSignatures(UndertaleGameObject obj)
    {
        var sigs = new HashSet<EventSignature>();

        for (int typeIdx = 0; typeIdx < obj.Events.Count; typeIdx++)
        {
            var subEvents = obj.Events[typeIdx];
            if (subEvents is null) continue;

            foreach (var ev in subEvents)
            {
                string? codeName = null;
                if (ev.Actions.Count > 0)
                {
                    codeName = ev.Actions[0].CodeId?.Name?.Content;
                }
                sigs.Add(new EventSignature(typeIdx, ev.EventSubtype, codeName));
            }
        }

        return sigs;
    }

    private static bool TargetHasEvent(UndertaleGameObject obj, int eventTypeIndex, uint eventSubtype)
    {
        if (eventTypeIndex >= obj.Events.Count) return false;
        var subEvents = obj.Events[eventTypeIndex];
        if (subEvents is null) return false;

        foreach (var ev in subEvents)
        {
            if (ev.EventSubtype == eventSubtype)
                return true;
        }
        return false;
    }

    private static void BindEvent(
        UndertaleGameObject targetObj,
        UndertaleData targetData,
        int eventTypeIndex,
        uint eventSubtype,
        UndertaleCode codeEntry)
    {
        // Ensure event type list exists
        while (targetObj.Events.Count <= eventTypeIndex)
        {
            targetObj.Events.Add(new UndertalePointerList<UndertaleGameObject.Event>());
        }

        var subEvents = targetObj.Events[eventTypeIndex];

        var newEvent = new UndertaleGameObject.Event
        {
            EventSubtype = eventSubtype,
        };

        // Only set CodeId — UTMT serialises version-specific
        // EventAction fields automatically. Hardcoding GMS2 values
        // (LibID, Kind, ExeType …) breaks GMS1 targets (SIGSEGV).
        var action = new UndertaleGameObject.EventAction
        {
            CodeId = codeEntry,
        };

        newEvent.Actions.Add(action);
        subEvents.Add(newEvent);
    }
}
