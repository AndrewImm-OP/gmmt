using Gmmt.Core.Loading;

namespace Gmmt.Core.Compatibility;

/// <summary>
/// Checks compatibility between vanilla Windows, modded Windows, and vanilla Linux archives.
/// Produces a report with errors (hard blockers) and warnings (proceed with caution).
/// </summary>
public static class CompatChecker
{
    /// <summary>
    /// Full three-way compatibility check for the translate pipeline.
    /// </summary>
    public static CompatReport CheckTranslate(
        ArchiveMetadata vanillaWin,
        ArchiveMetadata moddedWin,
        ArchiveMetadata vanillaUnx)
    {
        var r = new CompatReport();

        // --- Hard errors ---

        if (vanillaWin.IsYYC)
            r.AddError("YYC001",
                "Vanilla Windows archive is YYC-compiled (no bytecode). Mod translation requires VM-compiled archives.");

        if (vanillaUnx.IsYYC)
            r.AddError("YYC002",
                "Vanilla Linux archive is YYC-compiled (no bytecode). Mod translation requires VM-compiled archives.");

        if (vanillaWin.BytecodeVersion != vanillaUnx.BytecodeVersion)
            r.AddError("BC001",
                $"Bytecode version mismatch: Windows={vanillaWin.BytecodeVersion}, Linux={vanillaUnx.BytecodeVersion}.");

        if (vanillaWin.BytecodeVersion != moddedWin.BytecodeVersion)
            r.AddError("BC002",
                $"Mod changed bytecode version: vanilla={vanillaWin.BytecodeVersion}, modded={moddedWin.BytecodeVersion}.");

        if (vanillaWin.BytecodeVersion < 15)
            r.AddError("BC003",
                $"Bytecode version {vanillaWin.BytecodeVersion} is below minimum supported (15). Only bytecode 15+ is supported.");

        if (vanillaWin.Major != vanillaUnx.Major || vanillaWin.Minor != vanillaUnx.Minor)
            r.AddError("VER001",
                $"Major engine version mismatch: Windows={vanillaWin.Major}.{vanillaWin.Minor}, " +
                $"Linux={vanillaUnx.Major}.{vanillaUnx.Minor}.");

        // --- Warnings ---

        if (vanillaWin.GameName != vanillaUnx.GameName)
            r.AddWarning("GAME001",
                $"Game name differs: Windows='{vanillaWin.GameName}', Linux='{vanillaUnx.GameName}'. " +
                "Verify these are the same game.");

        if (vanillaWin.Release != vanillaUnx.Release || vanillaWin.Build != vanillaUnx.Build)
            r.AddWarning("VER002",
                $"Build version differs: Windows={vanillaWin.Release}.{vanillaWin.Build}, " +
                $"Linux={vanillaUnx.Release}.{vanillaUnx.Build}.");

        if (moddedWin.IsGMS2 && !vanillaUnx.IsGMS2)
            r.AddWarning("FMT001",
                "Modded archive uses GMS2 resources while the Linux target is GMS1. " +
                "gmmt currently cannot fully transplant every new GMS2 resource type, " +
                "so large content mods may only translate partially.");

        CheckResourceCountDrift(vanillaWin, vanillaUnx, "Windows", "Linux", r);
        CheckModScope(vanillaWin, moddedWin, r);

        // --- Info ---

        r.AddInfo("META001",
            $"Windows: {vanillaWin.GameName} v{vanillaWin.VersionString} BC{vanillaWin.BytecodeVersion}");
        r.AddInfo("META002",
            $"Modded:  {moddedWin.GameName} v{moddedWin.VersionString} BC{moddedWin.BytecodeVersion}");
        r.AddInfo("META003",
            $"Linux:   {vanillaUnx.GameName} v{vanillaUnx.VersionString} BC{vanillaUnx.BytecodeVersion}");

        return r;
    }

    /// <summary>
    /// Two-way compatibility check for the compare-names command.
    /// </summary>
    public static CompatReport CheckPair(ArchiveMetadata a, ArchiveMetadata b)
    {
        var r = new CompatReport();

        if (a.BytecodeVersion != b.BytecodeVersion)
            r.AddWarning("BC001",
                $"Bytecode version differs: A={a.BytecodeVersion}, B={b.BytecodeVersion}.");

        if (a.Major != b.Major || a.Minor != b.Minor)
            r.AddWarning("VER001",
                $"Engine version differs: A={a.Major}.{a.Minor}, B={b.Major}.{b.Minor}.");

        if (a.GameName != b.GameName)
            r.AddWarning("GAME001",
                $"Game name differs: A='{a.GameName}', B='{b.GameName}'.");

        CheckResourceCountDrift(a, b, "A", "B", r);

        return r;
    }

    private static void CheckResourceCountDrift(
        ArchiveMetadata a, ArchiveMetadata b,
        string labelA, string labelB,
        CompatReport r)
    {
        Chk("Code", a.CodeCount, b.CodeCount);
        Chk("Object", a.ObjectCount, b.ObjectCount);
        Chk("Room", a.RoomCount, b.RoomCount);
        Chk("Sprite", a.SpriteCount, b.SpriteCount);
        Chk("Sound", a.SoundCount, b.SoundCount);
        Chk("String", a.StringCount, b.StringCount);
        Chk("Font", a.FontCount, b.FontCount);
        Chk("Shader", a.ShaderCount, b.ShaderCount);
        Chk("Script", a.ScriptCount, b.ScriptCount);
        Chk("Background", a.BackgroundCount, b.BackgroundCount);
        Chk("Function", a.FunctionCount, b.FunctionCount);
        Chk("Variable", a.VariableCount, b.VariableCount);

        void Chk(string name, int ca, int cb)
        {
            if (ca != cb)
                r.AddWarning("CNT001",
                    $"{name} count differs: {labelA}={ca}, {labelB}={cb}.");
        }
    }

    private static void CheckModScope(ArchiveMetadata vanilla, ArchiveMetadata modded, CompatReport r)
    {
        int addedCode = modded.CodeCount - vanilla.CodeCount;
        int addedObj = modded.ObjectCount - vanilla.ObjectCount;
        int addedRoom = modded.RoomCount - vanilla.RoomCount;
        int addedStr = modded.StringCount - vanilla.StringCount;

        if (addedCode > 50)
            r.AddWarning("SCOPE001", $"Mod adds {addedCode} code entries. Large mod — increased risk.");
        if (addedObj > 20)
            r.AddWarning("SCOPE002", $"Mod adds {addedObj} objects. Complex mod — verify carefully.");
        if (addedRoom > 5)
            r.AddWarning("SCOPE003", $"Mod adds {addedRoom} rooms. Complex mod — verify carefully.");
        if (addedStr > 200)
            r.AddWarning("SCOPE004", $"Mod adds {addedStr} strings. Large content mod.");
    }
}
