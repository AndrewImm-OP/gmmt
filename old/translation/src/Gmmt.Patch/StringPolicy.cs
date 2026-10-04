using UndertaleModLib.Models;

namespace Gmmt.Patch;

public static class StringPolicy
{
    public static StringDisposition Decide(
        UndertaleString currentString,
        string newContent,
        int totalRefsToCurrentString,
        bool allRefsChangingToSameContent)
    {
        return ShouldModifyInPlace(totalRefsToCurrentString, allRefsChangingToSameContent)
            ? StringDisposition.ModifyInPlace
            : StringDisposition.CloneAndRepoint;
    }

    public static bool ShouldModifyInPlace(
        int totalRefsToCurrentString,
        bool allRefsChangingToSameContent)
    {
        if (totalRefsToCurrentString <= 1)
            return true;

        if (allRefsChangingToSameContent)
            return true;

        return false;
    }
}
