using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single sound.
/// </summary>
public sealed record SoundTransplantResult
{
    public required string SoundName { get; init; }
    public required SoundTransplantStatus Status { get; init; }
    public string? Diagnostic { get; init; }
}

public enum SoundTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

public sealed record SoundUpdateResult
{
    public required string SoundName { get; init; }
    public required SoundUpdateStatus Status { get; init; }
    public string? Diagnostic { get; init; }
}

public enum SoundUpdateStatus
{
    Updated,
    TargetNotFound,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new sounds from a modded archive to a target archive.
///
/// Strategy:
/// - Copy the UndertaleSound entry with all scalar properties.
/// - If the sound has embedded audio (GroupID == 0 and AudioFile is set),
///   clone the UndertaleEmbeddedAudio entry into the target.
/// - If the sound references an external audio group (GroupID != 0),
///   transplant the sound entry but set AudioID/GroupID to match the target
///   audio group structure. The actual audio data in external .dat files must
///   be handled separately by the caller.
/// - AudioGroup references are resolved by name in the target. If the group
///   doesn't exist in the target, it is created.
/// </summary>
public static class SoundTransplanter
{
    public static List<SoundTransplantResult> TransplantAll(
        IReadOnlyList<string> soundNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<SoundTransplantResult>(soundNames.Count);

        foreach (var name in soundNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData);
            results.Add(result);
        }

        return results;
    }

    private static SoundTransplantResult TransplantOne(
        string soundName,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        // Check if already exists in target
        if (targetData.Sounds is not null)
        {
            foreach (var s in targetData.Sounds)
            {
                if (s.Name?.Content == soundName)
                {
                    return new SoundTransplantResult
                    {
                        SoundName = soundName,
                        Status = SoundTransplantStatus.AlreadyExists,
                        Diagnostic = "Sound already exists in target archive.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Sounds.TryGetValue(soundName, out var moddedEntry))
        {
            return new SoundTransplantResult
            {
                SoundName = soundName,
                Status = SoundTransplantStatus.SourceNotFound,
                Diagnostic = "Sound not found in modded archive.",
            };
        }

        var srcSound = moddedEntry.Res;

        try
        {
            var newSound = new UndertaleSound
            {
                Name = targetData.Strings.MakeString(soundName),
                Flags = srcSound.Flags,
                Effects = srcSound.Effects,
                Volume = srcSound.Volume,
                Pitch = srcSound.Pitch,
                Preload = srcSound.Preload,
                AudioLength = srcSound.AudioLength,
            };

            // Clone string properties
            if (srcSound.Type?.Content is { } typeStr)
                newSound.Type = targetData.Strings.MakeString(typeStr);

            if (srcSound.File?.Content is { } fileStr)
                newSound.File = targetData.Strings.MakeString(fileStr);

            // Resolve or create audio group in target
            var srcGroupName = srcSound.AudioGroup?.Name?.Content;
            if (srcGroupName is not null)
            {
                var targetGroup = FindOrCreateAudioGroup(srcGroupName, targetData);
                newSound.AudioGroup = targetGroup;
                newSound.GroupID = targetData.AudioGroups.IndexOf(targetGroup);
            }
            else
            {
                newSound.GroupID = srcSound.GroupID;
            }

            // Handle embedded audio
            if (srcSound.GroupID == 0 && srcSound.AudioFile is not null)
            {
                // Inline audio — clone the embedded audio entry
                var newAudio = new UndertaleEmbeddedAudio();
                if (srcSound.AudioFile.Data is not null)
                    newAudio.Data = (byte[])srcSound.AudioFile.Data.Clone();

                targetData.EmbeddedAudio.Add(newAudio);
                newSound.AudioFile = newAudio;
                newSound.AudioID = targetData.EmbeddedAudio.IndexOf(newAudio);
            }
            else
            {
                // External audio group — keep the AudioID so external .dat files
                // can be copied alongside
                newSound.AudioID = srcSound.AudioID;
            }

            targetData.Sounds.Add(newSound);

            return new SoundTransplantResult
            {
                SoundName = soundName,
                Status = SoundTransplantStatus.Transplanted,
            };
        }
        catch (Exception ex)
        {
            return new SoundTransplantResult
            {
                SoundName = soundName,
                Status = SoundTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Update existing (modified) sounds
    // ═══════════════════════════════════════════════════════════════

    public static List<SoundUpdateResult> UpdateAll(
        IReadOnlyList<string> soundNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<SoundUpdateResult>(soundNames.Count);
        foreach (var name in soundNames)
            results.Add(UpdateOne(name, moddedIdx, targetData));
        return results;
    }

    private static SoundUpdateResult UpdateOne(
        string soundName,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        UndertaleSound? targetSound = null;
        if (targetData.Sounds is not null)
        {
            foreach (var s in targetData.Sounds)
            {
                if (s.Name?.Content == soundName) { targetSound = s; break; }
            }
        }

        if (targetSound is null)
            return new SoundUpdateResult { SoundName = soundName, Status = SoundUpdateStatus.TargetNotFound };

        if (!moddedIdx.Sounds.TryGetValue(soundName, out var moddedEntry))
            return new SoundUpdateResult { SoundName = soundName, Status = SoundUpdateStatus.SourceNotFound };

        var src = moddedEntry.Res;

        try
        {
            targetSound.Flags = src.Flags;
            targetSound.Effects = src.Effects;
            targetSound.Volume = src.Volume;
            targetSound.Pitch = src.Pitch;
            targetSound.Preload = src.Preload;
            targetSound.AudioLength = src.AudioLength;

            if (src.Type?.Content is { } typeStr)
                targetSound.Type = targetData.Strings.MakeString(typeStr);
            if (src.File?.Content is { } fileStr)
                targetSound.File = targetData.Strings.MakeString(fileStr);

            // Update embedded audio data
            if (src.GroupID == 0 && src.AudioFile is not null)
            {
                if (targetSound.AudioFile is not null && src.AudioFile.Data is not null)
                {
                    // Replace data in existing embedded audio entry
                    targetSound.AudioFile.Data = (byte[])src.AudioFile.Data.Clone();
                }
                else if (src.AudioFile.Data is not null)
                {
                    // Create new embedded audio entry
                    var newAudio = new UndertaleEmbeddedAudio
                    {
                        Data = (byte[])src.AudioFile.Data.Clone()
                    };
                    targetData.EmbeddedAudio.Add(newAudio);
                    targetSound.AudioFile = newAudio;
                    targetSound.AudioID = targetData.EmbeddedAudio.IndexOf(newAudio);
                }
            }

            return new SoundUpdateResult { SoundName = soundName, Status = SoundUpdateStatus.Updated };
        }
        catch (Exception ex)
        {
            return new SoundUpdateResult
            {
                SoundName = soundName,
                Status = SoundUpdateStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }

    private static UndertaleAudioGroup FindOrCreateAudioGroup(
        string groupName, UndertaleData targetData)
    {
        if (targetData.AudioGroups is not null)
        {
            foreach (var g in targetData.AudioGroups)
            {
                if (g.Name?.Content == groupName)
                    return g;
            }
        }

        var newGroup = new UndertaleAudioGroup
        {
            Name = targetData.Strings.MakeString(groupName),
        };
        targetData.AudioGroups.Add(newGroup);
        return newGroup;
    }
}
