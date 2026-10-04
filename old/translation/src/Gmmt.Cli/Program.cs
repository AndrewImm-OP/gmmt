using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Gmmt.Core;
using Gmmt.Core.Classification;
using Gmmt.Core.Compatibility;
using Gmmt.Core.Indexing;
using Gmmt.Core.Loading;
using Gmmt.Pipeline;
using Gmmt.VanillaLibrary;
using Gmmt.XDelta;
using UndertaleModLib;

namespace Gmmt.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("GMMT CLI");

        var translateCommand = new Command("translate-xdelta", "Translate an xdelta patch");
        var patchOption = new Argument<string>("patch", "Path to the xdelta patch file");
        var targetOption = new Argument<string>("target", "Path to the target game.unx file");
        var outputOption = new Option<string>(new[] { "-o", "--output" }, "Path to the output file");
        var vanillaOption = new Option<string>(new[] { "-v", "--vanilla" }, "Path to the vanilla game.unx file (optional, uses library if not specified)");
        var reportOption = new Option<string>(new[] { "-r", "--report" }, "Path to output a JSON report");
        var skipDiffPatchesOption = new Option<bool>("--skip-diff-patches", "Skip shared-resource diff patches (useful for room/resource isolation runs)");
        var dryRunOption = new Option<bool>("--dry-run", "Compute plan without writing patched archive");
        var planOption = new Option<string>("--plan", "Path to write the patch plan JSON (implies --dry-run)");

        translateCommand.AddArgument(patchOption);
        translateCommand.AddArgument(targetOption);
        translateCommand.AddOption(outputOption);
        translateCommand.AddOption(vanillaOption);
        translateCommand.AddOption(reportOption);
        translateCommand.AddOption(skipDiffPatchesOption);
        translateCommand.AddOption(dryRunOption);
        translateCommand.AddOption(planOption);

        translateCommand.SetHandler(async (string patch, string target, string output, string vanilla, string report, bool skipDiffPatches, bool dryRun, string plan) =>
        {
            var logger = new ConsoleLogger();
            var pipeline = new TranslationPipeline(logger);

            // --plan implies --dry-run
            if (!string.IsNullOrWhiteSpace(plan))
                dryRun = true;

            var options = new TranslateXDeltaOptions
            {
                PatchFilePath = patch,
                TargetFilePath = target,
                OutputPath = output,
                VanillaPath = vanilla,
                ReportPath = report,
                SkipDiffPatches = skipDiffPatches,
                DryRun = dryRun,
                PlanPath = string.IsNullOrWhiteSpace(plan) ? null : plan,
            };

            var result = await pipeline.RunXDeltaTranslateAsync(options);

            if (result.Success)
            {
                Console.WriteLine("Translation successful.");
                // Exit 0 for success (Portable/MostlyPortable verdict)
            }
            else
            {
                Console.WriteLine($"Translation failed: {result.ErrorMessage}");
                // NotPortable abort → 2, other failures → 3
                Environment.ExitCode = result.ErrorMessage?.Contains("Pre-flight classification") == true ? 2 : 3;
            }
        }, patchOption, targetOption, outputOption, vanillaOption, reportOption, skipDiffPatchesOption, dryRunOption, planOption);

        rootCommand.AddCommand(translateCommand);

        // ── classify command ──────────────────────────────────────────
        var classifyCommand = new Command("classify", "Pre-flight classification: analyze mod portability without patching");
        var classifyPatchArg = new Argument<string>("patch", "Path to the xdelta patch file");
        var classifyTargetArg = new Argument<string>("target", "Path to the target game.unx file");
        var classifyVanillaOpt = new Option<string>(new[] { "-v", "--vanilla" }, "Path to vanilla .win (optional, uses library)");
        var classifyLibraryOpt = new Option<string>(new[] { "-l", "--library" }, "Path to vanilla library");
        var classifyJsonOpt = new Option<string>(new[] { "--json" }, "Write classification result as JSON to this path");

        classifyCommand.AddArgument(classifyPatchArg);
        classifyCommand.AddArgument(classifyTargetArg);
        classifyCommand.AddOption(classifyVanillaOpt);
        classifyCommand.AddOption(classifyLibraryOpt);
        classifyCommand.AddOption(classifyJsonOpt);

        classifyCommand.SetHandler((string patch, string target, string vanilla, string library, string json) =>
        {
            RunClassify(patch, target, vanilla, library, json);
        }, classifyPatchArg, classifyTargetArg, classifyVanillaOpt, classifyLibraryOpt, classifyJsonOpt);

        rootCommand.AddCommand(classifyCommand);

        var inspectCodeCommand = new Command("inspect-code", "Inspect bytecode of a specific script/object event");
        var archiveOption = new Argument<string>("archive", "Path to the .unx archive");
        var codeNameOption = new Argument<string>("codeName", "Name of the code entry (e.g. gml_Script_action_move_to)");
        inspectCodeCommand.AddArgument(archiveOption);
        inspectCodeCommand.AddArgument(codeNameOption);
        
        inspectCodeCommand.SetHandler((string archive, string codeName) =>
        {
            Console.WriteLine($"Loading {archive}...");
            using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read);
            var data = UndertaleIO.Read(stream, null, null);
            
            var code = data.Code.FirstOrDefault(c => c.Name?.Content == codeName);
            if (code == null)
            {
                Console.WriteLine($"Code entry '{codeName}' not found.");
                return;
            }

            Console.WriteLine($"--- Inspecting {codeName} ---");
            Console.WriteLine($"LocalsCount: {code.LocalsCount}, ArgumentsCount: {code.ArgumentsCount}");
            for (int i = 0; i < code.Instructions.Count; i++)
            {
                var inst = code.Instructions[i];
                var val = inst.ValueShort != 0 ? inst.ValueShort.ToString() : inst.ValueInt != 0 ? inst.ValueInt.ToString() : inst.ValueDouble != 0 ? inst.ValueDouble.ToString() : "0";
                var varName = inst.ValueVariable?.Name?.Content ?? "null";
                var varId = inst.ValueVariable?.VarID.ToString() ?? "-1";
                var instType = inst.ValueVariable?.InstanceType.ToString() ?? "-1";
                var nameStrId = inst.ValueVariable?.NameStringID.ToString() ?? "-1";
                var funcName = inst.ValueString?.Resource?.Content ?? "null"; // simplistic
                var valFunc = inst.ValueFunction?.Name?.Content ?? "null";

                Console.WriteLine($"[{i,4}] {inst.Kind,-20} T1:{inst.Type1,-10} T2:{inst.Type2,-10} TInst:{inst.TypeInst,-6} " +
                                  $"Ref:{inst.ReferenceType,-10} Val:{val,-6} Var:{varName} (ID:{varId}, InstType:{instType}, NameStrID:{nameStrId}) Str:{funcName} VFunc:{valFunc}");
            }
        }, archiveOption, codeNameOption);
        
        rootCommand.AddCommand(inspectCodeCommand);

        var inspectVarsCommand = new Command("inspect-vars", "Inspect variables in an archive");
        var varsArchiveArg = new Argument<string>("archive", "Path to the .unx archive");
        var varsFilterOpt = new Option<string?>(new[] { "-f", "--filter" }, () => null, "Filter variable name (substring match)");
        inspectVarsCommand.AddArgument(varsArchiveArg);
        inspectVarsCommand.AddOption(varsFilterOpt);
        
        inspectVarsCommand.SetHandler((string archive, string? filter) =>
        {
            Console.WriteLine($"Loading {archive}...");
            using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read);
            var data = UndertaleIO.Read(stream, null, null);
            
            Console.WriteLine($"Total variables: {data.Variables?.Count ?? 0}");
            Console.WriteLine($"VarCount1: {data.VarCount1}, VarCount2: {data.VarCount2}");
            Console.WriteLine();
            
            if (data.Variables == null) return;
            
            int badCount = 0;
            for (int i = 0; i < data.Variables.Count; i++)
            {
                var v = data.Variables[i];
                var name = v.Name?.Content ?? "(null)";
                bool matchFilter = string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                bool isBad = v.Name == null || v.NameStringID == 0 && name != (data.Strings.Count > 0 ? data.Strings[0].Content : "");
                
                if (matchFilter || isBad)
                {
                    var marker = isBad ? " *** BAD ***" : "";
                    Console.WriteLine($"[{i,5}] VarID:{v.VarID,-6} InstType:{v.InstanceType,-10} NameStrID:{v.NameStringID,-6} Name:{name}{marker}");
                }
                if (isBad) badCount++;
            }
            Console.WriteLine($"\nTotal bad variables (null name or NameStrID=0 mismatch): {badCount}");
        }, varsArchiveArg, varsFilterOpt);
        
        rootCommand.AddCommand(inspectVarsCommand);

        var inspectFuncsCommand = new Command("inspect-funcs", "Inspect functions in an archive");
        var funcsArchiveArg = new Argument<string>("archive", "Path to the archive");
        var funcsFilterOpt = new Option<string?>(new[] { "-f", "--filter" }, () => null, "Filter function name");
        inspectFuncsCommand.AddArgument(funcsArchiveArg);
        inspectFuncsCommand.AddOption(funcsFilterOpt);

        inspectFuncsCommand.SetHandler((string archive, string? filter) =>
        {
            Console.WriteLine($"Loading {archive}...");
            using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read);
            var data = UndertaleIO.Read(stream, null, null);

            Console.WriteLine($"Total functions in FUNC list: {data.Functions?.Count ?? 0}");

            if (data.Functions == null) return;

            for (int i = 0; i < data.Functions.Count; i++)
            {
                var f = data.Functions[i];
                var name = f.Name?.Content ?? "(null)";
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine($"[{i,4}] Name:{name} Occurrences:{f.Occurrences} NameStrID:{f.NameStringID}");
            }
        }, funcsArchiveArg, funcsFilterOpt);

        rootCommand.AddCommand(inspectFuncsCommand);

        var inspectScriptsCommand = new Command("inspect-scripts", "Inspect scripts in an archive");
        var scriptsArchiveArg = new Argument<string>("archive", "Path to the archive");
        var scriptsFilterOpt = new Option<string?>(new[] { "-f", "--filter" }, () => null, "Filter script name");
        var scriptsIndexOpt = new Option<int?>(new[] { "-i", "--index" }, () => null, "Show specific script index");
        inspectScriptsCommand.AddArgument(scriptsArchiveArg);
        inspectScriptsCommand.AddOption(scriptsFilterOpt);
        inspectScriptsCommand.AddOption(scriptsIndexOpt);

        inspectScriptsCommand.SetHandler((string archive, string? filter, int? index) =>
        {
            using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read);
            var data = UndertaleIO.Read(stream, null, null);
            Console.WriteLine($"Scripts count: {data.Scripts?.Count ?? 0}");
            if (data.Scripts == null) return;
            for (int i = 0; i < data.Scripts.Count; i++)
            {
                var s = data.Scripts[i];
                var name = s.Name?.Content ?? "(null)";
                var codeName = s.Code?.Name?.Content ?? "(null)";
                if (index.HasValue && i != index.Value) continue;
                if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (!index.HasValue && filter == null && i > 100) { Console.WriteLine("... (use -f or -i to filter)"); break; }
                Console.WriteLine($"[{i,4}] Name:{name} Code:{codeName}");
            }
        }, scriptsArchiveArg, scriptsFilterOpt, scriptsIndexOpt);

        rootCommand.AddCommand(inspectScriptsCommand);

        return await rootCommand.InvokeAsync(args);
    }

    // ═══════════════════════════════════════════════════════════
    //  Standalone classify command
    // ═══════════════════════════════════════════════════════════

    static void RunClassify(string patchPath, string targetPath, string? vanillaPath, string? libraryPath, string? jsonPath)
    {
        // ── Step 1: Resolve vanilla archive and reconstruct modded via xdelta ──

        if (!XDeltaRunner.IsAvailable())
        {
            WriteError("xdelta3 binary not found. Install xdelta3 or add it to PATH.");
            Environment.ExitCode = 3;
            return;
        }

        string? resolvedVanillaPath = vanillaPath;
        string? tempModdedPath = null;

        try
        {
            if (resolvedVanillaPath is null)
            {
                // Try vanilla library
                var lib = new VanillaLibraryManager(libraryPath);
                var candidates = lib.GetCandidates().ToList();
                Console.WriteLine($"Vanilla library: {lib.LibraryPath} ({candidates.Count} candidate(s))");

                foreach (var candidate in candidates)
                {
                    var candidatePath = lib.GetArchivePath(candidate.Hash);
                    if (!File.Exists(candidatePath)) continue;

                    tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-classify-{Guid.NewGuid():N}.win");
                    var xResult = XDeltaRunner.Apply(candidatePath, patchPath, tempModdedPath);
                    if (xResult.Success)
                    {
                        resolvedVanillaPath = candidatePath;
                        Console.WriteLine($"  Matched: {candidate.ShortHash} ({candidate.GameName} {candidate.VersionString})");
                        break;
                    }
                    CleanupTemp(tempModdedPath);
                    tempModdedPath = null;
                }

                if (resolvedVanillaPath is null)
                {
                    WriteError("No vanilla archive matched the xdelta patch. Use --vanilla to specify.");
                    Environment.ExitCode = 3;
                    return;
                }
            }
            else
            {
                tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-classify-{Guid.NewGuid():N}.win");
                var xResult = XDeltaRunner.Apply(resolvedVanillaPath, patchPath, tempModdedPath);
                if (!xResult.Success)
                {
                    WriteError($"xdelta failed: {xResult.StdErr}");
                    Environment.ExitCode = 3;
                    return;
                }
            }

            // ── Step 2: Load archives ──

            Console.WriteLine("Loading archives...");
            var vanilla = ArchiveLoader.Load(resolvedVanillaPath);
            var modded = ArchiveLoader.Load(tempModdedPath!);
            var target = ArchiveLoader.Load(targetPath);

            // ── Step 3: Build indices ──

            Console.WriteLine("Building name indices...");
            var vanillaIdx = NameIndex.Build(vanilla.Data);
            var moddedIdx = NameIndex.Build(modded.Data);
            var targetIdx = NameIndex.Build(target.Data);

            // ── Step 4: Classify ──

            var compat = CompatChecker.CheckTranslate(vanilla.Metadata, modded.Metadata, target.Metadata);
            var classification = ModClassifier.Classify(
                vanilla.Metadata, modded.Metadata, target.Metadata,
                vanillaIdx, moddedIdx, targetIdx, compat);

            // ── Step 5: Output ──

            PrintClassification(classification);

            if (jsonPath is not null)
            {
                WriteClassificationJson(classification, jsonPath);
                Console.WriteLine($"\nJSON written to: {jsonPath}");
            }

            Environment.ExitCode = classification.Verdict switch
            {
                PortabilityVerdict.NotPortable => 2,
                PortabilityVerdict.PatchableOnly => 1,
                _ => 0, // Portable, MostlyPortable
            };
        }
        finally
        {
            CleanupTemp(tempModdedPath);
        }
    }

    static void PrintClassification(ModClassification c)
    {
        var verdictColor = c.Verdict switch
        {
            PortabilityVerdict.Portable => ConsoleColor.Green,
            PortabilityVerdict.MostlyPortable => ConsoleColor.Yellow,
            PortabilityVerdict.PatchableOnly => ConsoleColor.DarkYellow,
            PortabilityVerdict.NotPortable => ConsoleColor.Red,
            _ => ConsoleColor.White,
        };

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.ForegroundColor = verdictColor;
        Console.WriteLine($"  VERDICT: {c.Verdict}");
        Console.ResetColor();
        Console.WriteLine($"  {c.Summary}");
        Console.WriteLine($"  Estimated auto-portable: {c.EstimatedSuccessPercent}%");
        Console.WriteLine("═══════════════════════════════════════════════════════════");

        var b = c.Breakdown;
        Console.WriteLine();
        Console.WriteLine("  ── Breakdown ──");
        Console.WriteLine($"  New resources:      {b.TotalNewResources} (spr:{b.NewSpriteCount} snd:{b.NewSoundCount} fnt:{b.NewFontCount} bg:{b.NewBackgroundCount} obj:{b.NewObjectCount} scr:{b.NewScriptCount} room:{b.NewRoomCount})");
        Console.WriteLine($"  Modified resources: {b.TotalModifiedResources} (spr:{b.ModifiedSpriteCount} snd:{b.ModifiedSoundCount} fnt:{b.ModifiedFontCount} bg:{b.ModifiedBackgroundCount})");
        Console.WriteLine($"  New code:           {b.NewCodeEntryCount} ({b.NewCodePortableCount} portable, {b.NewCodeUnsupportedCount} unsupported)");
        Console.WriteLine($"  Modified code:      {b.ModifiedCodeEntryCount} ({b.ModifiedCodeWithGms2OnlyRefsCount} GMS2-only, {b.ModifiedCodeWithChildEntriesCount} child entries)");
        Console.WriteLine($"  String delta:       {b.StringCountDelta:+#;-#;0}");

        var significantIssues = c.Issues.Where(i => i.Severity != ClassificationSeverity.Info).ToList();
        if (significantIssues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ── Issues ──");
            foreach (var issue in significantIssues)
            {
                var color = issue.Severity switch
                {
                    ClassificationSeverity.Blocker => ConsoleColor.Red,
                    ClassificationSeverity.Major => ConsoleColor.Yellow,
                    ClassificationSeverity.Minor => ConsoleColor.Cyan,
                    _ => ConsoleColor.Gray,
                };
                var prefix = issue.Severity switch
                {
                    ClassificationSeverity.Blocker => "BLOCKER",
                    ClassificationSeverity.Major => "MAJOR  ",
                    ClassificationSeverity.Minor => "MINOR  ",
                    _ => "INFO   ",
                };

                Console.ForegroundColor = color;
                Console.Write($"  [{prefix}]");
                Console.ResetColor();
                Console.WriteLine($" {issue.Code}: {issue.Message}");

                if (issue.Detail is not null)
                    Console.WriteLine($"            {issue.Detail}");
            }
        }

        // Info items
        var infoItems = c.Issues.Where(i => i.Severity == ClassificationSeverity.Info).ToList();
        if (infoItems.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  ── Info ──");
            foreach (var info in infoItems)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  {info.Code}: {info.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
    }

    static void WriteClassificationJson(ModClassification c, string path)
    {
        var obj = new
        {
            verdict = c.Verdict.ToString(),
            summary = c.Summary,
            estimatedSuccessPercent = c.EstimatedSuccessPercent,
            shouldAbort = c.ShouldAbort,
            breakdown = new
            {
                newSprites = c.Breakdown.NewSpriteCount,
                newSounds = c.Breakdown.NewSoundCount,
                newFonts = c.Breakdown.NewFontCount,
                newBackgrounds = c.Breakdown.NewBackgroundCount,
                modifiedSprites = c.Breakdown.ModifiedSpriteCount,
                modifiedSounds = c.Breakdown.ModifiedSoundCount,
                modifiedFonts = c.Breakdown.ModifiedFontCount,
                modifiedBackgrounds = c.Breakdown.ModifiedBackgroundCount,
                newCodeEntries = c.Breakdown.NewCodeEntryCount,
                newCodePortable = c.Breakdown.NewCodePortableCount,
                newCodeUnsupported = c.Breakdown.NewCodeUnsupportedCount,
                newCodeWithChildEntries = c.Breakdown.NewCodeWithChildEntriesCount,
                newCodeWithGms2OnlyRefs = c.Breakdown.NewCodeWithGms2OnlyRefsCount,
                modifiedCodeEntries = c.Breakdown.ModifiedCodeEntryCount,
                modifiedCodeWithChildEntries = c.Breakdown.ModifiedCodeWithChildEntriesCount,
                modifiedCodeWithGms2OnlyRefs = c.Breakdown.ModifiedCodeWithGms2OnlyRefsCount,
                newObjects = c.Breakdown.NewObjectCount,
                newScripts = c.Breakdown.NewScriptCount,
                newRooms = c.Breakdown.NewRoomCount,
                stringCountDelta = c.Breakdown.StringCountDelta,
                totalNewResources = c.Breakdown.TotalNewResources,
                totalModifiedResources = c.Breakdown.TotalModifiedResources,
                estimatedPortablePercent = c.Breakdown.EstimatedPortablePercent,
            },
            issues = c.Issues.Select(i => new
            {
                severity = i.Severity.ToString(),
                code = i.Code,
                category = i.Category,
                message = i.Message,
                detail = i.Detail,
            }).ToArray(),
        };

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }

    static void CleanupTemp(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { /* best effort */ }
    }
}

class ConsoleLogger : IPipelineLogger
{
    public void Log(string message) => Console.WriteLine(message);
    public void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARNING: {message}");
        Console.ResetColor();
    }
    public void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }
}
