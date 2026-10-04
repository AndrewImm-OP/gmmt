using Gmmt.Core.Indexing;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

/// <summary>
/// Result of transplanting a single script.
/// </summary>
public sealed record ScriptTransplantResult
{
    public required string ScriptName { get; init; }
    public required ScriptTransplantStatus Status { get; init; }
    public string? Diagnostic { get; init; }
}

public enum ScriptTransplantStatus
{
    Transplanted,
    AlreadyExists,
    SourceNotFound,
    Failed,
}

/// <summary>
/// Transplants new scripts from a modded archive to a target archive.
///
/// Scripts are thin wrappers: a name, a reference to a code entry, and an
/// IsConstructor flag. The referenced code entry must already exist in the
/// target (transplanted by CodeTransplanter beforehand).
/// </summary>
public static class ScriptTransplanter
{
    public static List<ScriptTransplantResult> TransplantAll(
        IReadOnlyList<string> scriptNames,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        var results = new List<ScriptTransplantResult>(scriptNames.Count);

        foreach (var name in scriptNames)
        {
            var result = TransplantOne(name, moddedIdx, targetData);
            results.Add(result);
        }

        return results;
    }

    private static ScriptTransplantResult TransplantOne(
        string scriptName,
        NameIndex moddedIdx,
        UndertaleData targetData)
    {
        // Check if already exists in target
        if (targetData.Scripts is not null)
        {
            foreach (var s in targetData.Scripts)
            {
                if (s.Name?.Content == scriptName)
                {
                    return new ScriptTransplantResult
                    {
                        ScriptName = scriptName,
                        Status = ScriptTransplantStatus.AlreadyExists,
                        Diagnostic = "Script already exists in target archive.",
                    };
                }
            }
        }

        // Find in modded
        if (!moddedIdx.Scripts.TryGetValue(scriptName, out var moddedEntry))
        {
            return new ScriptTransplantResult
            {
                ScriptName = scriptName,
                Status = ScriptTransplantStatus.SourceNotFound,
                Diagnostic = "Script not found in modded archive.",
            };
        }

        var srcScript = moddedEntry.Res;

        try
        {
            var newScript = new UndertaleScript
            {
                Name = targetData.Strings.MakeString(scriptName),
                IsConstructor = srcScript.IsConstructor,
            };

            // Resolve code reference in target
            if (srcScript.Code is not null)
            {
                var codeName = srcScript.Code.Name?.Content;
                if (codeName is not null && targetData.Code is not null)
                {
                    foreach (var code in targetData.Code)
                    {
                        if (code.Name?.Content == codeName)
                        {
                            newScript.Code = code;
                            break;
                        }
                    }
                    
                    if (newScript.Code is null)
                    {
                        return new ScriptTransplantResult
                        {
                            ScriptName = scriptName,
                            Status = ScriptTransplantStatus.Failed,
                            Diagnostic = $"Code entry '{codeName}' not found in target (likely unsupported)."
                        };
                    }
                }
                else
                {
                    return new ScriptTransplantResult
                    {
                        ScriptName = scriptName,
                        Status = ScriptTransplantStatus.Failed,
                        Diagnostic = $"Script has Code but no resolvable name."
                    };
                }
            }

            targetData.Scripts.Add(newScript);

            return new ScriptTransplantResult
            {
                ScriptName = scriptName,
                Status = ScriptTransplantStatus.Transplanted,
            };
        }
        catch (Exception ex)
        {
            return new ScriptTransplantResult
            {
                ScriptName = scriptName,
                Status = ScriptTransplantStatus.Failed,
                Diagnostic = $"Exception: {ex.Message}",
            };
        }
    }
}
