using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

public static class StringPatcher
{
    public static StringPatchResult ApplySingle(
        UndertaleData data,
        StringPatchRequest request)
    {
        return ApplyMany(data, [request])[0];
    }

    public static IReadOnlyList<StringPatchResult> ApplyMany(
        UndertaleData data,
        IReadOnlyList<StringPatchRequest> requests)
    {
        var results = new StringPatchResult[requests.Count];
        var refCounts = StringRefCounter.CountAllReferences(data);

        // Group requests by target UndertaleString to determine allRefsChangingToSameContent
        var stringToRequests = new Dictionary<UndertaleString, List<(int Index, StringPatchRequest Request)>>(
            StringRefCounter.UndertaleStringIdentityComparer.Instance);

        // First pass: resolve targets and group by string identity
        var resolvedTargets = new (UndertaleCode? Code, List<UndertaleString> Strings, int TargetIndex, UndertaleString? Target)?[requests.Count];

        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var resolved = ResolveTarget(data, request);
            resolvedTargets[i] = resolved;

            if (resolved?.Target is not null)
            {
                if (!stringToRequests.TryGetValue(resolved.Value.Target, out var list))
                {
                    list = [];
                    stringToRequests[resolved.Value.Target] = list;
                }
                list.Add((i, request));
            }
        }

        // Second pass: apply patches
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            var resolved = resolvedTargets[i];

            if (resolved is null)
            {
                results[i] = MakeResult(request, StringPatchStatus.NotFound, null, null, "Code entry not found");
                continue;
            }

            var (code, strings, targetIndex, target) = resolved.Value;

            if (target is null)
            {
                results[i] = MakeResult(request, StringPatchStatus.NotFound, null, null, "Ordinal out of range");
                continue;
            }

            // Validate old content
            if (target.Content != request.OldContent)
            {
                results[i] = MakeResult(request, StringPatchStatus.ContextMismatch, null, null,
                    $"Old content mismatch: expected '{request.OldContent}', found '{target.Content}'");
                continue;
            }

            // Validate context
            var contextResult = ValidateContext(strings, targetIndex, request.Context);
            if (!contextResult.Valid)
            {
                results[i] = MakeResult(request, StringPatchStatus.ContextMismatch, null, null, contextResult.Message);
                continue;
            }

            // Determine disposition
            refCounts.TryGetValue(target, out var totalRefs);
            var allSameContent = AllRequestsWantSameContent(stringToRequests, target, request.NewContent);
            var disposition = StringPolicy.Decide(target, request.NewContent, totalRefs, allSameContent);

            // Apply patch
            if (disposition == StringDisposition.ModifyInPlace)
            {
                target.Content = request.NewContent;
            }
            else
            {
                var newString = data.Strings.MakeString(request.NewContent);
                RepointInstruction(code!, targetIndex, newString);
            }

            var confidence = request.Context.Prev is not null || request.Context.Next is not null
                ? StringPatchConfidence.ContextMatched
                : StringPatchConfidence.Exact;

            results[i] = new StringPatchResult(
                Status: StringPatchStatus.Applied,
                Confidence: confidence,
                Disposition: disposition,
                CodeName: request.CodeName,
                Ordinal: request.Ordinal,
                OldContent: request.OldContent,
                NewContent: request.NewContent,
                Message: null);
        }

        return results;
    }

    private static (UndertaleCode? Code, List<UndertaleString> Strings, int TargetIndex, UndertaleString? Target)? ResolveTarget(
        UndertaleData data,
        StringPatchRequest request)
    {
        var code = data.Code?.FirstOrDefault(c => c.Name?.Content == request.CodeName);
        if (code is null)
            return null;

        var strings = ExtractStringReferences(code);
        var targetIndex = request.Ordinal;

        if (targetIndex < 0 || targetIndex >= strings.Count)
            return (code, strings, targetIndex, null);

        return (code, strings, targetIndex, strings[targetIndex]);
    }

    private static List<UndertaleString> ExtractStringReferences(UndertaleCode code)
    {
        var strings = new List<UndertaleString>();

        if (code.Instructions is null)
            return strings;

        foreach (var instr in code.Instructions)
        {
            var str = instr.ValueString?.Resource;
            if (str is not null)
            {
                strings.Add(str);
            }
        }

        return strings;
    }

    private static (bool Valid, string? Message) ValidateContext(
        List<UndertaleString> strings,
        int targetIndex,
        ContextFingerprint context)
    {
        if (context.Prev is not null)
        {
            if (targetIndex == 0)
                return (false, "Context mismatch: no previous string exists");

            var prevContent = strings[targetIndex - 1].Content;
            if (prevContent != context.Prev)
                return (false, $"Context mismatch: prev expected '{context.Prev}', found '{prevContent}'");
        }

        if (context.Next is not null)
        {
            if (targetIndex >= strings.Count - 1)
                return (false, "Context mismatch: no next string exists");

            var nextContent = strings[targetIndex + 1].Content;
            if (nextContent != context.Next)
                return (false, $"Context mismatch: next expected '{context.Next}', found '{nextContent}'");
        }

        return (true, null);
    }

    private static bool AllRequestsWantSameContent(
        Dictionary<UndertaleString, List<(int Index, StringPatchRequest Request)>> stringToRequests,
        UndertaleString target,
        string newContent)
    {
        if (!stringToRequests.TryGetValue(target, out var requests))
            return true;

        foreach (var (_, req) in requests)
        {
            if (req.NewContent != newContent)
                return false;
        }

        return true;
    }

    private static void RepointInstruction(UndertaleCode code, int ordinal, UndertaleString newString)
    {
        if (code.Instructions is null)
            return;

        int currentOrdinal = 0;
        foreach (var instr in code.Instructions)
        {
            var strRef = instr.ValueString;
            if (strRef?.Resource is not null)
            {
                if (currentOrdinal == ordinal)
                {
                    strRef.Resource = newString;
                    return;
                }
                currentOrdinal++;
            }
        }
    }

    private static StringPatchResult MakeResult(
        StringPatchRequest request,
        StringPatchStatus status,
        StringDisposition? disposition,
        StringPatchConfidence? confidence,
        string? message)
    {
        return new StringPatchResult(
            Status: status,
            Confidence: confidence ?? StringPatchConfidence.Low,
            Disposition: disposition,
            CodeName: request.CodeName,
            Ordinal: request.Ordinal,
            OldContent: request.OldContent,
            NewContent: status == StringPatchStatus.Applied ? request.NewContent : null,
            Message: message);
    }
}
