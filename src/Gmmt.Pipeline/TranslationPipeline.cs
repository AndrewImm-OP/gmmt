using Gmmt.Core.Compatibility;
using Gmmt.Core.Indexing;
using Gmmt.Core.Loading;
using Gmmt.Diff;
using Gmmt.Patch;
using Gmmt.Report;
using Gmmt.VanillaLibrary;
using Gmmt.XDelta;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Pipeline;

/// <summary>
/// Progress/log callback for pipeline operations.
/// </summary>
public interface IPipelineLogger
{
    void Log(string message);
    void LogWarning(string message);
    void LogError(string message);
}

/// <summary>
/// Options for the xdelta translation pipeline.
/// </summary>
public sealed class TranslateXDeltaOptions
{
    public required string PatchFilePath { get; init; }
    public required string TargetFilePath { get; init; }
    public string? OutputPath { get; init; }
    public string? VanillaPath { get; init; }
    public string? LibraryPath { get; init; }
    public string? ReportPath { get; init; }
    public bool DryRun { get; init; }
    public bool Force { get; init; }
    public bool Backup { get; init; }
}

/// <summary>
/// Result of the translation pipeline.
/// </summary>
public sealed class TranslationResult
{
    public required bool Success { get; init; }
    public required PatchReport Report { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Shared translation pipeline logic used by both CLI and GUI.
/// Extracted from Program.cs RunTranslateXDelta and RunTranslateCore.
/// </summary>
public sealed class TranslationPipeline
{
    private readonly IPipelineLogger _logger;

    public TranslationPipeline(IPipelineLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Run the full xdelta translation pipeline.
    /// </summary>
    public async Task<TranslationResult> RunXDeltaTranslateAsync(
        TranslateXDeltaOptions options,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RunXDeltaTranslate(options, cancellationToken), cancellationToken);
    }

    private TranslationResult RunXDeltaTranslate(
        TranslateXDeltaOptions options,
        CancellationToken ct)
    {
        var reportBuilder = new PatchReportBuilder
        {
            TargetPath = options.TargetFilePath
        };

        var finalOutputPath = options.OutputPath ?? options.TargetFilePath + ".patched";
        string? tempModdedPath = null;

        try
        {
            // Check xdelta availability
            ct.ThrowIfCancellationRequested();
            if (!XDeltaRunner.IsAvailable())
            {
                var msg = "xdelta3 binary not found. Install xdelta3 or add it to PATH.";
                _logger.LogError(msg);
                reportBuilder.AddError("System", "xdelta3", msg);
                return Fail(reportBuilder, msg, options.ReportPath);
            }

            _logger.Log($"Using xdelta3: {XDeltaRunner.GetBinaryPath()}");

            // Find/use vanilla archive
            string? usedVanillaPath = null;
            VanillaEntry? usedVanillaEntry = null;

            ct.ThrowIfCancellationRequested();

            if (options.VanillaPath is not null)
            {
                if (!File.Exists(options.VanillaPath))
                {
                    var msg = $"Vanilla archive not found: {options.VanillaPath}";
                    _logger.LogError(msg);
                    reportBuilder.AddError("System", "vanilla-missing", msg);
                    return Fail(reportBuilder, msg, options.ReportPath);
                }

                tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-modded-{Guid.NewGuid():N}.win");
                _logger.Log($"Using specified vanilla: {options.VanillaPath}");
                _logger.Log("Applying xdelta patch...");

                var xdeltaResult = XDeltaRunner.Apply(options.VanillaPath, options.PatchFilePath, tempModdedPath);
                if (!xdeltaResult.Success)
                {
                    CleanupTempFile(tempModdedPath);
                    tempModdedPath = null;

                    var msg = $"xdelta failed with the specified vanilla archive. stderr: {xdeltaResult.StdErr}";
                    _logger.LogError(msg);
                    reportBuilder.AddError("XDelta", "base-mismatch", msg);
                    return Fail(reportBuilder, msg, options.ReportPath);
                }

                usedVanillaPath = options.VanillaPath;
            }
            else
            {
                var library = new VanillaLibraryManager(options.LibraryPath);
                var candidates = library.GetCandidates().ToList();

                _logger.Log($"Vanilla library: {library.LibraryPath}");
                _logger.Log($"Found {candidates.Count} candidate(s)");

                if (candidates.Count == 0)
                {
                    var msg = "No vanilla archives in library. Add one with vanilla-library add.";
                    _logger.LogError(msg);
                    reportBuilder.AddError("System", "vanilla-library-empty", msg);
                    return Fail(reportBuilder, msg, options.ReportPath);
                }

                tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-modded-{Guid.NewGuid():N}.win");
                int candidatesTried = 0;

                foreach (var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();

                    var candidatePath = library.GetArchivePath(candidate.Hash);
                    if (!File.Exists(candidatePath))
                    {
                        _logger.Log($"  Skipping {candidate.ShortHash}: archive file missing");
                        continue;
                    }

                    candidatesTried++;
                    _logger.Log($"  Trying {candidate.ShortHash} ({candidate.GameName} {candidate.VersionString})...");

                    var result = XDeltaRunner.Apply(candidatePath, options.PatchFilePath, tempModdedPath);
                    if (result.Success)
                    {
                        _logger.Log($"  Success! Using {candidate.ShortHash}");
                        usedVanillaPath = candidatePath;
                        usedVanillaEntry = candidate;
                        break;
                    }
                    else
                    {
                        _logger.Log($"    xdelta failed: {Truncate(result.StdErr.Replace('\n', ' '), 80)}");
                        reportBuilder.AddInfo("XDelta", $"candidate-{candidate.ShortHash}",
                            $"Candidate rejected: {Truncate(result.StdErr.Replace('\n', ' '), 100)}");
                        CleanupTempFile(tempModdedPath);
                    }
                }

                if (usedVanillaPath is null)
                {
                    tempModdedPath = null;
                    var msg = $"No vanilla archive matched the xdelta patch after trying {candidatesTried} candidate(s).";
                    _logger.LogError(msg);
                    reportBuilder.AddError("System", "no-matching-vanilla", msg);
                    return Fail(reportBuilder, msg, options.ReportPath);
                }
            }

            reportBuilder.VanillaPath = usedVanillaPath;
            reportBuilder.ModdedPath = "(reconstructed from xdelta)";
            reportBuilder.AddInfo("XDelta", "reconstruction",
                $"Modded archive reconstructed from {(usedVanillaEntry is not null ? usedVanillaEntry.ShortHash : Path.GetFileName(usedVanillaPath))}");

            // Load archives
            ct.ThrowIfCancellationRequested();
            _logger.Log("Loading vanilla archive...");
            var vanilla = ArchiveLoader.Load(usedVanillaPath, msg => _logger.Log($"  {msg}"));

            ct.ThrowIfCancellationRequested();
            _logger.Log("Loading reconstructed modded archive...");
            var modded = ArchiveLoader.Load(tempModdedPath!, msg => _logger.Log($"  {msg}"));

            ct.ThrowIfCancellationRequested();
            _logger.Log("Loading target archive...");
            var target = ArchiveLoader.Load(options.TargetFilePath, msg => _logger.Log($"  {msg}"));

            // Compatibility check
            ct.ThrowIfCancellationRequested();
            var compat = CompatChecker.CheckTranslate(vanilla.Metadata, modded.Metadata, target.Metadata);
            bool hasErrors = false;

            foreach (var issue in compat.Issues)
            {
                var prefix = issue.Severity switch
                {
                    CompatSeverity.Error => "ERROR",
                    CompatSeverity.Warning => "WARN",
                    _ => "INFO",
                };
                _logger.Log($"  [{prefix}] {issue.Code}: {issue.Message}");

                if (issue.Severity == CompatSeverity.Error)
                {
                    hasErrors = true;
                    reportBuilder.AddError("Compatibility", issue.Code, issue.Message);
                }
                else
                {
                    reportBuilder.AddInfo("Compatibility", issue.Code, issue.Message);
                }
            }

            if (hasErrors && !options.Force)
            {
                var msg = "Compatibility errors detected. Use force to proceed.";
                _logger.LogError(msg);
                return Fail(reportBuilder, msg, options.ReportPath);
            }

            // Run translation core
            ct.ThrowIfCancellationRequested();
            return RunTranslateCore(vanilla, modded, target, finalOutputPath, reportBuilder,
                options.ReportPath, options.DryRun, options.Backup, options.Force, ct);
        }
        finally
        {
            CleanupTempFile(tempModdedPath);
        }
    }

    private TranslationResult RunTranslateCore(
        ArchiveLoader.LoadResult vanilla,
        ArchiveLoader.LoadResult modded,
        ArchiveLoader.LoadResult target,
        string outputPath,
        PatchReportBuilder reportBuilder,
        string? reportPath,
        bool dryRun,
        bool backup,
        bool force,
        CancellationToken ct)
    {
        _logger.Log("Building name indices...");
        var vanillaIdx = NameIndex.Build(vanilla.Data);
        var moddedIdx = NameIndex.Build(modded.Data);
        var targetIdx = NameIndex.Build(target.Data);

        ct.ThrowIfCancellationRequested();

        // ─────────────────────────────────────────────────────────
         // Phase 0: Transplant new resources from modded → target
        // Order matters: sprites → sounds → backgrounds → fonts → code → scripts → objects → rooms
        // ─────────────────────────────────────────────────────────
        _logger.Log("Computing transplant manifest...");
        var manifest = ResourceTransplantDiffer.ComputeManifest(vanillaIdx, moddedIdx);
        _logger.Log(
            $"  New: sprites={manifest.NewSprites.Count}, code={manifest.NewCodeEntries.Count}, " +
            $"objects={manifest.NewObjects.Count}, rooms={manifest.NewRooms.Count}, " +
            $"sounds={manifest.NewSounds.Count}, scripts={manifest.NewScripts.Count}, " +
            $"fonts={manifest.NewFonts.Count}, backgrounds={manifest.NewBackgrounds.Count}");
        _logger.Log(
            $"  Modified: sprites={manifest.ModifiedSprites.Count}, " +
            $"backgrounds={manifest.ModifiedBackgrounds.Count}, " +
            $"fonts={manifest.ModifiedFonts.Count}, sounds={manifest.ModifiedSounds.Count}");

        bool hasTransplantWork = true;
        // bool hasTransplantWork = manifest.TotalNewResources > 0 ||
        //     manifest.ModifiedSprites.Count > 0 ||
        //     manifest.ModifiedBackgrounds.Count > 0 ||
        //     manifest.ModifiedFonts.Count > 0 ||
        //     manifest.ModifiedSounds.Count > 0;

        if (hasTransplantWork)
        {
            // Shared texture clone maps for sprites, fonts, and backgrounds
            var textureCloneMap = new Dictionary<int, UndertaleEmbeddedTexture>();
            var tpagCloneMap = new Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem>(
                ReferenceEqualityComparer.Instance);

            // ── 1. Transplant sprites (with textures and TPAG items) ──
            if (false && manifest.NewSprites.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewSprites.Count} sprites...");
                var spriteResults = SpriteTransplanter.TransplantAll(
                    manifest.NewSprites, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                foreach (var r in spriteResults)
                {
                    switch (r.Status)
                    {
                        case SpriteTransplantStatus.Transplanted:
                            reportBuilder.AddApplied("Sprite", r.SpriteName,
                                $"Transplanted ({r.FrameCount} frames)",
                                null, ReportConfidence.High);
                            break;
                        case SpriteTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Sprite", r.SpriteName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case SpriteTransplantStatus.Failed:
                            reportBuilder.AddError("Sprite", r.SpriteName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Sprite", r.SpriteName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
            }

            // ── 1b. Update modified sprites (existing in both vanilla and modded) ──
            if (false && manifest.ModifiedSprites.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Updating {manifest.ModifiedSprites.Count} modified sprites...");
                var updateResults = SpriteTransplanter.UpdateAll(
                    manifest.ModifiedSprites, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                int updated = 0, failed = 0;
                foreach (var r in updateResults)
                {
                    switch (r.Status)
                    {
                        case SpriteUpdateStatus.Updated:
                            updated++;
                            reportBuilder.AddApplied("Sprite", r.SpriteName,
                                $"Updated ({r.FrameCount} frames)",
                                null, ReportConfidence.High);
                            break;
                        case SpriteUpdateStatus.TargetNotFound:
                            reportBuilder.AddSkipped("Sprite", r.SpriteName,
                                "Not found in target archive", r.Diagnostic);
                            break;
                        case SpriteUpdateStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Sprite", r.SpriteName,
                                r.Diagnostic ?? "Update failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Sprite", r.SpriteName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Sprite update: {updated} ok, {failed} failed");
            }

            // ── 2. Transplant sounds ──
            if (false && manifest.NewSounds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewSounds.Count} sounds...");
                var soundResults = SoundTransplanter.TransplantAll(
                    manifest.NewSounds, moddedIdx, target.Data);

                int transplanted = 0, failed = 0;
                foreach (var r in soundResults)
                {
                    switch (r.Status)
                    {
                        case SoundTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Sound", r.SoundName,
                                "Transplanted", null, ReportConfidence.High);
                            break;
                        case SoundTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Sound", r.SoundName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case SoundTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Sound", r.SoundName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Sound", r.SoundName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Sound transplant: {transplanted} ok, {failed} failed");
            }

            // ── 2b. Update modified sounds ──
            if (false && manifest.ModifiedSounds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Updating {manifest.ModifiedSounds.Count} modified sounds...");
                var updateResults = SoundTransplanter.UpdateAll(
                    manifest.ModifiedSounds, moddedIdx, target.Data);

                int updated = 0, failed = 0;
                foreach (var r in updateResults)
                {
                    switch (r.Status)
                    {
                        case SoundUpdateStatus.Updated:
                            updated++;
                            reportBuilder.AddApplied("Sound", r.SoundName,
                                "Updated", null, ReportConfidence.High);
                            break;
                        case SoundUpdateStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Sound", r.SoundName,
                                r.Diagnostic ?? "Update failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Sound", r.SoundName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Sound update: {updated} ok, {failed} failed");
            }

            // ── 3. Transplant backgrounds (tilesets) ──
            if (false && manifest.NewBackgrounds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewBackgrounds.Count} backgrounds...");
                var bgResults = BackgroundTransplanter.TransplantAll(
                    manifest.NewBackgrounds, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                int transplanted = 0, failed = 0;
                foreach (var r in bgResults)
                {
                    switch (r.Status)
                    {
                        case BackgroundTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Background", r.BackgroundName,
                                "Transplanted", null, ReportConfidence.High);
                            break;
                        case BackgroundTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Background", r.BackgroundName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case BackgroundTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Background", r.BackgroundName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Background", r.BackgroundName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Background transplant: {transplanted} ok, {failed} failed");
            }

            // ── 3b. Update modified backgrounds ──
            if (false && manifest.ModifiedBackgrounds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Updating {manifest.ModifiedBackgrounds.Count} modified backgrounds...");
                var updateResults = BackgroundTransplanter.UpdateAll(
                    manifest.ModifiedBackgrounds, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                int updated = 0, failed = 0;
                foreach (var r in updateResults)
                {
                    switch (r.Status)
                    {
                        case BackgroundUpdateStatus.Updated:
                            updated++;
                            reportBuilder.AddApplied("Background", r.BackgroundName,
                                "Updated", null, ReportConfidence.High);
                            break;
                        case BackgroundUpdateStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Background", r.BackgroundName,
                                r.Diagnostic ?? "Update failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Background", r.BackgroundName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Background update: {updated} ok, {failed} failed");
            }

            // ── 4. Transplant fonts ──
            if (false && manifest.NewFonts.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewFonts.Count} fonts...");
                var fontResults = FontTransplanter.TransplantAll(
                    manifest.NewFonts, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                int transplanted = 0, failed = 0;
                foreach (var r in fontResults)
                {
                    switch (r.Status)
                    {
                        case FontTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Font", r.FontName,
                                $"Transplanted ({r.GlyphCount} glyphs)",
                                null, ReportConfidence.High);
                            break;
                        case FontTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Font", r.FontName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case FontTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Font", r.FontName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Font", r.FontName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Font transplant: {transplanted} ok, {failed} failed");
            }

            // ── 4b. Update modified fonts ──
            if (false && manifest.ModifiedFonts.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Updating {manifest.ModifiedFonts.Count} modified fonts...");
                var updateResults = FontTransplanter.UpdateAll(
                    manifest.ModifiedFonts, moddedIdx, target.Data,
                    textureCloneMap, tpagCloneMap);

                int updated = 0, failed = 0;
                foreach (var r in updateResults)
                {
                    switch (r.Status)
                    {
                        case FontUpdateStatus.Updated:
                            updated++;
                            reportBuilder.AddApplied("Font", r.FontName,
                                $"Updated ({r.GlyphCount} glyphs)", null, ReportConfidence.High);
                            break;
                        case FontUpdateStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Font", r.FontName,
                                r.Diagnostic ?? "Update failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Font", r.FontName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Font update: {updated} ok, {failed} failed");
            }

            // ── 5. Transplant code entries ──
            if (false && manifest.NewCodeEntries.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewCodeEntries.Count} code entries...");
                var codeTransplantResults = CodeTransplanter.TransplantAll(
                    manifest.NewCodeEntries, moddedIdx, target.Data);

                int transplanted = 0, unsupported = 0, failed = 0;
                foreach (var r in codeTransplantResults)
                {
                    switch (r.Status)
                    {
                        case CodeTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Code", r.CodeName,
                                $"Transplanted ({r.InstructionCount} instructions)",
                                null, ReportConfidence.High);
                            break;
                        case CodeTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Code", r.CodeName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case CodeTransplantStatus.Unsupported:
                            unsupported++;
                            reportBuilder.AddUnsupported("Code", r.CodeName,
                                r.Diagnostic ?? "Unsupported for transplant");
                            break;
                        case CodeTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Code", r.CodeName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Code", r.CodeName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Code transplant: {transplanted} ok, {unsupported} unsupported, {failed} failed");
            }

            // ── 6. Transplant scripts ──
            if (false && manifest.NewScripts.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewScripts.Count} scripts...");
                var scriptResults = ScriptTransplanter.TransplantAll(
                    manifest.NewScripts, moddedIdx, target.Data);

                int transplanted = 0, failed = 0;
                foreach (var r in scriptResults)
                {
                    switch (r.Status)
                    {
                        case ScriptTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Script", r.ScriptName,
                                "Transplanted", null, ReportConfidence.High);
                            break;
                        case ScriptTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Script", r.ScriptName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case ScriptTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Script", r.ScriptName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Script", r.ScriptName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Script transplant: {transplanted} ok, {failed} failed");
            }

            // ── 7. Transplant game objects ──
            if (false && manifest.NewObjects.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewObjects.Count} game objects...");
                var objectTransplantResults = GameObjectTransplanter.TransplantAll(
                    manifest.NewObjects, moddedIdx, target.Data);

                int transplanted = 0, failed = 0;
                foreach (var r in objectTransplantResults)
                {
                    switch (r.Status)
                    {
                        case GameObjectTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Object", r.ObjectName,
                                $"Transplanted ({r.EventCount} events)",
                                null, ReportConfidence.High);
                            break;
                        case GameObjectTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Object", r.ObjectName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case GameObjectTransplantStatus.Failed:
                            failed++;
                            reportBuilder.AddError("Object", r.ObjectName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Object", r.ObjectName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Object transplant: {transplanted} ok, {failed} failed");
            }

            // ── 8. Transplant rooms (depends on all above) ──
            if (manifest.NewRooms.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                _logger.Log($"Transplanting {manifest.NewRooms.Count} rooms...");
                var roomResults = RoomTransplanter.TransplantAll(
                    manifest.NewRooms, moddedIdx, target.Data);

                int transplanted = 0, failed = 0;
                foreach (var r in roomResults)
                {
                    switch (r.Status)
                    {
                        case RoomTransplantStatus.Transplanted:
                            transplanted++;
                            reportBuilder.AddApplied("Room", r.RoomName,
                                $"Transplanted ({r.LayerCount} layers, {r.GameObjectCount} instances)",
                                null, ReportConfidence.High);
                            break;
                        case RoomTransplantStatus.AlreadyExists:
                            reportBuilder.AddSkipped("Room", r.RoomName,
                                "Already exists in target", r.Diagnostic);
                            break;
                        case RoomTransplantStatus.Failed:
                            failed++;
                            if (failed <= 3)
                                _logger.LogWarning($"  Room '{r.RoomName}' failed: {r.Diagnostic}");
                            reportBuilder.AddError("Room", r.RoomName,
                                r.Diagnostic ?? "Transplant failed");
                            break;
                        default:
                            reportBuilder.AddSkipped("Room", r.RoomName,
                                r.Diagnostic ?? $"Status: {r.Status}");
                            break;
                    }
                }
                _logger.Log($"  Room transplant: {transplanted} ok, {failed} failed");
            }

            // Rebuild target name index after transplants (new resources were added)
            ct.ThrowIfCancellationRequested();
            _logger.Log("Rebuilding target name index after transplants...");
            targetIdx = NameIndex.Build(target.Data);
        }

        // ─────────────────────────────────────────────────────────
        // Phase 0b: Patch event bindings on existing objects
        // If a mod adds new events to existing objects, the code entries
        // are transplanted above, but the event→code binding on the object
        // must also be created.
        // ─────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();
        _logger.Log("Patching object event bindings...");
        var eventPatchResults = true
            ? ObjectEventPatcher.PatchAll(vanillaIdx, moddedIdx, target.Data)
            : new List<ObjectEventPatchResult>();

        if (eventPatchResults.Count > 0)
        {
            int totalAdded = 0, totalFailed = 0;
            foreach (var r in eventPatchResults)
            {
                totalAdded += r.EventsAdded;
                totalFailed += r.EventsFailed;

                switch (r.Status)
                {
                    case ObjectEventPatchStatus.Applied:
                        reportBuilder.AddApplied("ObjectEvent", r.ObjectName,
                            $"{r.EventsAdded} event(s) bound",
                            r.Diagnostic, ReportConfidence.High);
                        break;
                    case ObjectEventPatchStatus.Failed:
                        reportBuilder.AddError("ObjectEvent", r.ObjectName,
                            r.Diagnostic ?? "Event binding failed");
                        break;
                    case ObjectEventPatchStatus.ObjectMissing:
                        reportBuilder.AddSkipped("ObjectEvent", r.ObjectName,
                            "Object not found in target");
                        break;
                }
            }
            _logger.Log($"  Event bindings: {totalAdded} added on {eventPatchResults.Count} object(s), {totalFailed} failed");
        }
        else
        {
            _logger.Log("  No new event bindings needed.");
        }

        // ─────────────────────────────────────────────────────────
        // Phase 1: Compute diffs for existing (shared) resources
        // ─────────────────────────────────────────────────────────
        ct.ThrowIfCancellationRequested();

        // Compute diffs
        _logger.Log("Computing string diffs...");
        var stringDiffs = StringDiffAlgorithm.DiffAll(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing code diffs...");
        var codeDiffs = CodeDiffer.DiffAll(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing object diffs...");
        var objectDiffs = ObjectDiffer.DiffAll(vanillaIdx, moddedIdx);

        var codeEntriesWithStringOnlyDiffs = new HashSet<string>(StringComparer.Ordinal);

        // Convert diffs to patch requests
        ct.ThrowIfCancellationRequested();
        _logger.Log("Building patch requests...");

        // String patches
        var stringRequests = new List<StringPatchRequest>();
        foreach (var diff in stringDiffs)
        {
            if (diff.Status == CodeStringDiffStatus.CodeEntryMissing)
            {
                reportBuilder.AddSkipped("String", diff.CodeEntryName,
                    "Code entry missing in modded archive", diff.Diagnostic);
                continue;
            }

            foreach (var change in diff.Changes)
            {
                // Skip only truly ambiguous matches (multiple candidates, no way to pick)
                if (change.Confidence == StringMatchConfidence.Ambiguous)
                {
                    reportBuilder.AddSkipped("String", $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        $"Ambiguous match (multiple candidates)",
                        $"old='{Truncate(change.OldContent, 60)}' new='{Truncate(change.NewContent, 60)}'");
                    continue;
                }

                if (!change.VanillaOrdinal.HasValue)
                {
                    reportBuilder.AddSkipped("String", $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        "No vanilla ordinal",
                        $"new='{Truncate(change.NewContent, 60)}'");
                    continue;
                }

                // Log lower confidence matches as risky but still apply them
                if (change.Confidence == StringMatchConfidence.ContentMatchOnly)
                {
                    reportBuilder.AddRisky("String", $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        $"Content-only match (ordinal shifted)",
                        $"old='{Truncate(change.OldContent, 60)}' new='{Truncate(change.NewContent, 60)}'");
                }

                var context = new Gmmt.Patch.ContextFingerprint(
                    change.ModdedContext.Preceding,
                    change.ModdedContext.Following);

                stringRequests.Add(new StringPatchRequest(
                    diff.CodeEntryName,
                    change.VanillaOrdinal.Value,
                    change.OldContent,
                    change.NewContent,
                    context));
            }
        }

        // Code patches
        var codeRequests = new List<CodePatchRequest>();
        foreach (var diff in codeDiffs)
        {
            if (diff.Status == CodeDiffStatus.CodeEntryMissing)
            {
                reportBuilder.AddSkipped("Code", diff.CodeEntryName,
                    "Code entry missing", diff.Diagnostic);
                continue;
            }

            if (diff.Status == CodeDiffStatus.Unsupported)
            {
                reportBuilder.AddUnsupported("Code", diff.CodeEntryName,
                    "Unsupported code entry (unresolvable instructions)", diff.Diagnostic);
                continue;
            }

            if (diff.Status != CodeDiffStatus.Modified)
                continue;

            // Classify instruction differences
            int stringDiffCount = 0;
            int nonStringDiffCount = 0;
            foreach (var d in diff.Differences)
            {
                bool isStringOnly = d.Vanilla is not null &&
                    d.Modded is not null &&
                    d.Vanilla.Kind == d.Modded.Kind &&
                    d.Vanilla.StringValue != d.Modded.StringValue;

                if (isStringOnly)
                    stringDiffCount++;
                else
                    nonStringDiffCount++;
            }

            if (nonStringDiffCount > 0)
            {
                // Log non-string changes as risky — they won't be patched by the code patcher,
                // but string changes in this entry are still handled by the string patcher.
                reportBuilder.AddRisky("Code", diff.CodeEntryName,
                    $"{nonStringDiffCount} non-string instruction change(s) not transferred " +
                    $"(logic/numeric changes); {stringDiffCount} string change(s) handled via string patcher",
                    $"{diff.Differences.Count} total instruction difference(s)");

                // Don't add to codeRequests — code patcher would reject it due to structural mismatch.
                // String changes are handled by the string diff/patch path which runs independently.
                continue;
            }

            codeEntriesWithStringOnlyDiffs.Add(diff.CodeEntryName);

            var moddedCode = modded.Data.Code?.FirstOrDefault(c => c.Name?.Content == diff.CodeEntryName);
            if (moddedCode is null)
            {
                reportBuilder.AddSkipped("Code", diff.CodeEntryName,
                    "Code entry not found in modded archive");
                continue;
            }

            var snapshot = CodeSnapshotBuilder.Build(moddedCode);
            codeRequests.Add(new CodePatchRequest(diff.CodeEntryName, snapshot));
        }

        // Object patches
        var objectRequests = new List<ObjectPatchRequest>();
        foreach (var diff in objectDiffs)
        {
            if (diff.Status == ObjectDiffStatus.Missing)
            {
                reportBuilder.AddSkipped("Object", diff.ObjectName,
                    "Object missing", diff.Diagnostic);
                continue;
            }

            if (diff.Status != ObjectDiffStatus.Modified)
                continue;

            var patches = new List<ObjectPropertyPatch>();
            foreach (var propDiff in diff.Differences)
            {
                patches.Add(new ObjectPropertyPatch
                {
                    Property = propDiff.Property,
                    Value = propDiff.ModdedValue
                });
            }

            objectRequests.Add(new ObjectPatchRequest
            {
                ObjectName = diff.ObjectName,
                Patches = patches
            });
        }

        // Apply patches
        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {stringRequests.Count} string patches...");
        var stringResults = StringPatcher.ApplyMany(target.Data, stringRequests);

        var codeEntriesWithAppliedStrings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in stringResults)
        {
            var resourceName = $"{r.CodeName}[{r.Ordinal}]";
            var confidence = r.Confidence switch
            {
                StringPatchConfidence.Exact => ReportConfidence.High,
                StringPatchConfidence.ContextMatched => ReportConfidence.Medium,
                StringPatchConfidence.Heuristic => ReportConfidence.Low,
                _ => ReportConfidence.Unknown
            };

            switch (r.Status)
            {
                case StringPatchStatus.Applied:
                    reportBuilder.AddApplied("String", resourceName,
                        $"'{Truncate(r.OldContent ?? "", 40)}' -> '{Truncate(r.NewContent ?? "", 40)}'",
                        $"Disposition: {r.Disposition}", confidence);
                    codeEntriesWithAppliedStrings.Add(r.CodeName);
                    break;
                case StringPatchStatus.Skipped:
                    reportBuilder.AddSkipped("String", resourceName,
                        r.Message ?? "Skipped", null, confidence);
                    break;
                default:
                    reportBuilder.AddError("String", resourceName,
                        r.Message ?? $"Failed with status {r.Status}", null, confidence);
                    break;
            }
        }

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {codeRequests.Count} code patches...");
        var codeResults = CodePatcher.ApplyMany(target.Data, codeRequests);

        foreach (var r in codeResults)
        {
            if (r.Status == CodePatchStatus.Skipped &&
                (r.Message?.Contains("No differences") == true || r.Message?.Contains("already match") == true) &&
                codeEntriesWithStringOnlyDiffs.Contains(r.CodeName) &&
                codeEntriesWithAppliedStrings.Contains(r.CodeName))
            {
                reportBuilder.AddInfo("Code", r.CodeName,
                    "String changes handled via string patcher",
                    $"InstructionCount: {r.InstructionCount}");
                continue;
            }

            switch (r.Status)
            {
                case CodePatchStatus.Applied:
                    reportBuilder.AddApplied("Code", r.CodeName,
                        r.Message ?? "Applied",
                        $"InstructionCount: {r.InstructionCount}",
                        ReportConfidence.High);
                    break;
                case CodePatchStatus.Skipped:
                    reportBuilder.AddSkipped("Code", r.CodeName,
                        r.Message ?? "Skipped",
                        $"InstructionCount: {r.InstructionCount}");
                    break;
                default:
                    reportBuilder.AddError("Code", r.CodeName,
                        r.Message ?? $"Failed with status {r.Status}",
                        $"InstructionCount: {r.InstructionCount}");
                    break;
            }
        }

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {objectRequests.Count} object patches...");
        foreach (var req in objectRequests)
        {
            var result = ObjectPatcher.Apply(req, targetIdx);

            switch (result.Status)
            {
                case ObjectPatchStatus.Applied:
                case ObjectPatchStatus.PartiallyApplied:
                    var propsApplied = result.Properties
                        .Where(p => p.Status == PropertyPatchStatus.Applied)
                        .Select(p => $"{p.Property}: {p.OldValue}->{p.NewValue}");
                    reportBuilder.AddApplied("Object", result.ObjectName,
                        string.Join(", ", propsApplied),
                        result.Diagnostic,
                        result.Status == ObjectPatchStatus.Applied ? ReportConfidence.High : ReportConfidence.Medium);
                    break;
                case ObjectPatchStatus.NothingToDo:
                    reportBuilder.AddSkipped("Object", result.ObjectName,
                        "Nothing to do (values already equal)", result.Diagnostic);
                    break;
                case ObjectPatchStatus.ObjectMissing:
                    reportBuilder.AddSkipped("Object", result.ObjectName,
                        "Object not found in target archive", result.Diagnostic);
                    break;
                default:
                    reportBuilder.AddError("Object", result.ObjectName,
                        result.Diagnostic ?? $"Failed with status {result.Status}");
                    break;
            }
        }

        // Write output
        var tempReport = reportBuilder.Build();
        int totalApplied = tempReport.AppliedCount;
        string? writtenOutputPath = null;

        if (!dryRun && totalApplied > 0)
        {
            if (backup && File.Exists(outputPath))
            {
                var backupPath = outputPath + ".bak";
                _logger.Log($"Creating backup: {backupPath}");
                try
                {
                    File.Copy(outputPath, backupPath, overwrite: true);
                    reportBuilder.AddInfo("System", "backup", $"Backup created: {backupPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to create backup: {ex.Message}");
                    reportBuilder.AddInfo("System", "backup-failed", $"Backup failed: {ex.Message}");
                }
            }

            ct.ThrowIfCancellationRequested();
            _logger.Log($"Writing translated archive to {outputPath}...");
            using var stream = File.Create(outputPath);
            UndertaleIO.Write(stream, target.Data, msg => _logger.Log($"  [utmt] {msg}"));
            reportBuilder.OutputPath = outputPath;
            writtenOutputPath = outputPath;
            _logger.Log("Done.");
        }
        else if (dryRun)
        {
            _logger.Log("Dry run - output file not written.");
            reportBuilder.AddInfo("System", "dry-run", "Dry run mode enabled, no output written");
        }
        else
        {
            _logger.Log("No changes applied - output file not written.");
        }

        // Write JSON report if requested
        var report = reportBuilder.Build();
        if (reportPath is not null)
        {
            ReportJsonWriter.WriteToFile(report, reportPath);
            _logger.Log($"JSON report written to {reportPath}");
        }

        return new TranslationResult
        {
            Success = true,
            Report = report,
            OutputPath = writtenOutputPath
        };
    }

    private TranslationResult Fail(
        PatchReportBuilder reportBuilder,
        string errorMessage,
        string? reportPath)
    {
        var report = reportBuilder.Build();
        if (reportPath is not null)
        {
            ReportJsonWriter.WriteToFile(report, reportPath);
            _logger.Log($"JSON report written to {reportPath}");
        }

        return new TranslationResult
        {
            Success = false,
            Report = report,
            ErrorMessage = errorMessage
        };
    }

    private static void CleanupTempFile(string? tempPath)
    {
        if (tempPath is not null && File.Exists(tempPath))
        {
            try { File.Delete(tempPath); }
            catch { /* best effort */ }
        }
    }

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return s[..(maxLen - 3)] + "...";
    }
}
