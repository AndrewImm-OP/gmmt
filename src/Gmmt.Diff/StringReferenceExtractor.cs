using UndertaleModLib.Models;

namespace Gmmt.Diff;

/// <summary>
/// Extracts the ordered sequence of string references from a code entry's instructions.
/// Only instructions that push a UndertaleString value are included.
/// The result is an ordered list indexed by ordinal (0-based position among string refs).
/// </summary>
public static class StringReferenceExtractor
{
    /// <summary>
    /// Extract all string-referencing instructions from a code entry, in instruction order.
    /// </summary>
    public static List<StringRef> Extract(UndertaleCode code)
    {
        var refs = new List<StringRef>();
        int ordinal = 0;

        for (int i = 0; i < code.Instructions.Count; i++)
        {
            var inst = code.Instructions[i];
            if (inst.ValueString?.Resource is UndertaleString str)
            {
                refs.Add(new StringRef(str.Content, ordinal, i));
                ordinal++;
            }
        }

        return refs;
    }

    /// <summary>
    /// Build a ContextFingerprint for the string ref at the given ordinal.
    /// </summary>
    public static ContextFingerprint GetFingerprint(IReadOnlyList<StringRef> refs, int ordinal)
    {
        string? preceding = ordinal > 0 ? refs[ordinal - 1].Content : null;
        string? following = ordinal < refs.Count - 1 ? refs[ordinal + 1].Content : null;
        return new ContextFingerprint(preceding, following);
    }
}
