using Gmmt.Core.Classification;
using Gmmt.Core.Compatibility;
using Gmmt.Core.Indexing;
using Gmmt.Core.Loading;
using Gmmt.Diff;
using Gmmt.Patch;
using Gmmt.Report;
using Gmmt.VanillaLibrary;
using Gmmt.XDelta;
using System.Linq;
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
    public bool SkipDiffPatches { get; init; }
    /// <summary>If set, write the patch plan JSON to this path (dry-run mode).</summary>
    public string? PlanPath { get; init; }
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
    /// <summary>Pre-flight mod classification, if computed.</summary>
    public ModClassification? Classification { get; init; }
    /// <summary>Patch plan, populated in --dry-run mode.</summary>
    public PatchPlan? Plan { get; init; }
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

            // Resolve vanilla archive and reconstruct modded via xdelta
            ct.ThrowIfCancellationRequested();
            var vanillaResolution = ResolveVanillaArchive(options, reportBuilder, ct);
            if (!vanillaResolution.Success)
                return Fail(reportBuilder, vanillaResolution.ErrorMessage!, options.ReportPath);

            tempModdedPath = vanillaResolution.TempModdedPath;
            var usedVanillaPath = vanillaResolution.VanillaPath!;

            reportBuilder.VanillaPath = usedVanillaPath;
            reportBuilder.ModdedPath = "(reconstructed from xdelta)";
            reportBuilder.AddInfo("XDelta", "reconstruction",
                $"Modded archive reconstructed from {(vanillaResolution.VanillaEntry is not null ? vanillaResolution.VanillaEntry.ShortHash : Path.GetFileName(usedVanillaPath))}");

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
                options.ReportPath, options.PlanPath, options.DryRun, options.Backup, options.Force,
                options.SkipDiffPatches, ct);
        }
        finally
        {
            CleanupTempFile(tempModdedPath);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Vanilla archive resolution
    // ═══════════════════════════════════════════════════════════════

    private sealed class VanillaResolution
    {
        public bool Success { get; init; }
        public string? VanillaPath { get; init; }
        public string? TempModdedPath { get; init; }
        public VanillaEntry? VanillaEntry { get; init; }
        public string? ErrorMessage { get; init; }
    }

    private VanillaResolution ResolveVanillaArchive(
        TranslateXDeltaOptions options,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        if (options.VanillaPath is not null)
            return ResolveExplicitVanilla(options, reportBuilder);

        return ResolveVanillaFromLibrary(options, reportBuilder, ct);
    }

    private VanillaResolution ResolveExplicitVanilla(
        TranslateXDeltaOptions options,
        PatchReportBuilder reportBuilder)
    {
        if (!File.Exists(options.VanillaPath))
        {
            var msg = $"Vanilla archive not found: {options.VanillaPath}";
            _logger.LogError(msg);
            reportBuilder.AddError("System", "vanilla-missing", msg);
            return new VanillaResolution { Success = false, ErrorMessage = msg };
        }

        var tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-modded-{Guid.NewGuid():N}.win");
        _logger.Log($"Using specified vanilla: {options.VanillaPath}");
        _logger.Log("Applying xdelta patch...");

        var xdeltaResult = XDeltaRunner.Apply(options.VanillaPath, options.PatchFilePath, tempModdedPath);
        if (!xdeltaResult.Success)
        {
            CleanupTempFile(tempModdedPath);
            var msg = $"xdelta failed with the specified vanilla archive. stderr: {xdeltaResult.StdErr}";
            _logger.LogError(msg);
            reportBuilder.AddError("XDelta", "base-mismatch", msg);
            return new VanillaResolution { Success = false, ErrorMessage = msg };
        }

        return new VanillaResolution
        {
            Success = true,
            VanillaPath = options.VanillaPath,
            TempModdedPath = tempModdedPath
        };
    }

    private VanillaResolution ResolveVanillaFromLibrary(
        TranslateXDeltaOptions options,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
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
            return new VanillaResolution { Success = false, ErrorMessage = msg };
        }

        var tempModdedPath = Path.Combine(Path.GetTempPath(), $"gmmt-modded-{Guid.NewGuid():N}.win");
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
                return new VanillaResolution
                {
                    Success = true,
                    VanillaPath = candidatePath,
                    TempModdedPath = tempModdedPath,
                    VanillaEntry = candidate
                };
            }

            _logger.Log($"    xdelta failed: {Truncate(result.StdErr.Replace('\n', ' '), 80)}");
            reportBuilder.AddInfo("XDelta", $"candidate-{candidate.ShortHash}",
                $"Candidate rejected: {Truncate(result.StdErr.Replace('\n', ' '), 100)}");
            CleanupTempFile(tempModdedPath);
        }

        CleanupTempFile(tempModdedPath);
        var msg2 = $"No vanilla archive matched the xdelta patch after trying {candidatesTried} candidate(s).";
        _logger.LogError(msg2);
        reportBuilder.AddError("System", "no-matching-vanilla", msg2);
        return new VanillaResolution { Success = false, ErrorMessage = msg2 };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Translation core
    // ═══════════════════════════════════════════════════════════════

    private TranslationResult RunTranslateCore(
        ArchiveLoader.LoadResult vanilla,
        ArchiveLoader.LoadResult modded,
        ArchiveLoader.LoadResult target,
        string outputPath,
        PatchReportBuilder reportBuilder,
        string? reportPath,
        string? planPath,
        bool dryRun,
        bool backup,
        bool force,
        bool skipDiffPatches,
        CancellationToken ct)
    {
        _logger.Log("Building name indices...");
        var vanillaIdx = NameIndex.Build(vanilla.Data);
        var moddedIdx = NameIndex.Build(modded.Data);
        var targetIdx = NameIndex.Build(target.Data);

        ct.ThrowIfCancellationRequested();

        // ─────────────────────────────────────────────────────────
        // Phase -1: Pre-flight mod classification
        // ─────────────────────────────────────────────────────────
        _logger.Log("Running pre-flight mod classification...");
        var classification = ModClassifier.Classify(
            vanilla.Metadata, modded.Metadata, target.Metadata,
            vanillaIdx, moddedIdx, targetIdx,
            CompatChecker.CheckTranslate(vanilla.Metadata, modded.Metadata, target.Metadata));

        LogClassification(classification);
        reportBuilder.AddInfo("Classification", "verdict", classification.Summary);

        foreach (var issue in classification.Issues)
        {
            if (issue.Severity == ClassificationSeverity.Blocker)
                reportBuilder.AddError("Classification", issue.Code, issue.Message);
            else if (issue.Severity == ClassificationSeverity.Major)
                reportBuilder.AddRisky("Classification", issue.Code, issue.Message);
            else if (issue.Severity == ClassificationSeverity.Minor)
                reportBuilder.AddInfo("Classification", issue.Code, issue.Message);
        }

        if (classification.ShouldAbort && !force)
        {
            var msg = $"Pre-flight classification: {classification.Verdict}. " +
                      "Automatic translation will produce broken output. Use --force to override.";
            _logger.LogError(msg);
            return Fail(reportBuilder, msg, reportPath);
        }

        if (classification.Verdict == PortabilityVerdict.NotPortable && force)
        {
            _logger.LogWarning("⚠ Proceeding despite NotPortable verdict (--force). Output will likely be broken.");
        }

        ct.ThrowIfCancellationRequested();

        // ─────────────────────────────────────────────────────────
        // Dry-run: compute plan without patching
        // ─────────────────────────────────────────────────────────
        if (dryRun)
        {
            return BuildDryRunResult(vanilla, modded, target, vanillaIdx, moddedIdx,
                classification, reportBuilder, reportPath, planPath, ct);
        }

        // ─────────────────────────────────────────────────────────
        // Phase 0: Transplant new resources from modded → target
        // ─────────────────────────────────────────────────────────
        targetIdx = TransplantResources(modded, target, vanillaIdx, moddedIdx, targetIdx, reportBuilder, ct);

        // ─────────────────────────────────────────────────────────
        // Phase 0b: Patch event bindings on existing objects
        // ─────────────────────────────────────────────────────────
        PatchEventBindings(vanillaIdx, moddedIdx, target.Data, reportBuilder, ct);

        // ─────────────────────────────────────────────────────────
        // Phase 1: Compute diffs and apply patches for shared resources
        // ─────────────────────────────────────────────────────────
        // Phase 1a: Bytecode replacement for modified code entries
        // (lighter than full diff patches — only needs code diffs, not string diffs)
        {
            ct.ThrowIfCancellationRequested();
            _logger.Log("Computing code diffs for bytecode replacement...");
            var codeDiffs = CodeDiffer.DiffAll(vanillaIdx, moddedIdx);
            var bytecodeReplaceNames = new List<string>();
            foreach (var diff in codeDiffs)
            {
                if (diff.Status != CodeDiffStatus.Modified) continue;
                bool hasNonString = false;
                foreach (var d in diff.Differences)
                {
                    bool isStringOnly = d.Vanilla is not null && d.Modded is not null &&
                        d.Vanilla.Kind == d.Modded.Kind && d.Vanilla.StringValue != d.Modded.StringValue;
                    if (!isStringOnly) { hasNonString = true; break; }
                }
                if (hasNonString) bytecodeReplaceNames.Add(diff.CodeEntryName);
            }

            var skipBytecodeReplace = Environment.GetEnvironmentVariable("GMMT_SKIP_BYTECODE_REPLACE") == "1";

            // GMMT_REPLACE_EXCLUDE=name1,name2,... — exclude specific entries from bytecode replacement
            // Useful when modded bytecode for an init script (e.g. SCR_GAMESTART) breaks game logic
            // and we want to keep vanilla bytecode while still applying replace to everything else.
            var excludeEnv = Environment.GetEnvironmentVariable("GMMT_REPLACE_EXCLUDE");
            var excludeSet = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(excludeEnv))
            {
                foreach (var name in excludeEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    excludeSet.Add(name);
            }

            // GMMT_REPLACE_LIMIT=N — replace only the first N entries (for bisecting which replace breaks the game).
            int replaceLimit = int.MaxValue;
            var limitEnv = Environment.GetEnvironmentVariable("GMMT_REPLACE_LIMIT");
            if (!string.IsNullOrWhiteSpace(limitEnv) && int.TryParse(limitEnv, out var parsedLimit))
                replaceLimit = parsedLimit;

            if (excludeSet.Count > 0)
            {
                int beforeCount = bytecodeReplaceNames.Count;
                bytecodeReplaceNames = bytecodeReplaceNames.Where(n => !excludeSet.Contains(n)).ToList();
                int excluded = beforeCount - bytecodeReplaceNames.Count;
                if (excluded > 0)
                    _logger.Log($"Excluding {excluded} entries from bytecode replacement (GMMT_REPLACE_EXCLUDE).");
            }

            if (replaceLimit < bytecodeReplaceNames.Count)
            {
                _logger.Log($"Limiting bytecode replacement to first {replaceLimit} entries (GMMT_REPLACE_LIMIT={replaceLimit}).");
                bytecodeReplaceNames = bytecodeReplaceNames.Take(replaceLimit).ToList();
            }

            if (bytecodeReplaceNames.Count > 0 && !skipBytecodeReplace)
            {
                _logger.Log($"Replacing bytecode for {bytecodeReplaceNames.Count} modified code entries...");
                var replaceResults = CodeTransplanter.ReplaceExistingBytecode(
                    bytecodeReplaceNames, modded.Data, target.Data);
                int replOk = 0, replFail = 0, replUnsup = 0;
                foreach (var r in replaceResults)
                {
                    switch (r.Status)
                    {
                        case CodeTransplantStatus.Transplanted: replOk++; break;
                        case CodeTransplantStatus.Unsupported:
                            replUnsup++;
                            reportBuilder.AddRisky("CodeReplace", r.CodeName,
                                $"Bytecode replacement unsupported: {r.Diagnostic}");
                            break;
                        default:
                            replFail++;
                            reportBuilder.AddRisky("CodeReplace", r.CodeName,
                                $"Bytecode replacement failed: {r.Diagnostic}");
                            break;
                    }
                }
                _logger.Log($"  Bytecode replacement: {replOk} ok, {replUnsup} unsupported, {replFail} failed");
            }
            else if (bytecodeReplaceNames.Count > 0)
            {
                _logger.Log($"Skipping bytecode replacement for {bytecodeReplaceNames.Count} entries (GMMT_SKIP_BYTECODE_REPLACE=1).");
            }
        }

        if (skipDiffPatches)
        {
            _logger.Log("Skipping diff patches (--skip-diff-patches).");
            reportBuilder.AddInfo("Pipeline", "skip-diff-patches",
                "Shared-resource diff patches were skipped for this run.");
        }
        else
        {
            ApplyDiffPatches(vanilla, modded, target, vanillaIdx, moddedIdx, targetIdx, reportBuilder, ct);
        }

        // ─────────────────────────────────────────────────────────
        // Phase 2: Write output
        // ─────────────────────────────────────────────────────────
        return WriteOutput(target, outputPath, reportBuilder, reportPath, dryRun, backup, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Dry-run: build plan without patching
    // ═══════════════════════════════════════════════════════════════

    private TranslationResult BuildDryRunResult(
        ArchiveLoader.LoadResult vanilla,
        ArchiveLoader.LoadResult modded,
        ArchiveLoader.LoadResult target,
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        ModClassification classification,
        PatchReportBuilder reportBuilder,
        string? reportPath,
        string? planPath,
        CancellationToken ct)
    {
        _logger.Log("DRY RUN — computing plan without patching...");

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing transplant manifest...");
        var manifest = ResourceTransplantDiffer.ComputeManifest(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing code diffs...");
        var codeDiffs = CodeDiffer.DiffAll(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing string diffs...");
        var stringDiffs = StringDiffAlgorithm.DiffAll(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Computing object diffs...");
        var objectDiffs = ObjectDiffer.DiffAll(vanillaIdx, moddedIdx);

        ct.ThrowIfCancellationRequested();
        _logger.Log("Building patch plan...");
        bool targetIsGms1 = !target.Metadata.IsGMS2;
        var plan = PatchPlanBuilder.Build(
            classification, manifest, codeDiffs, stringDiffs, objectDiffs,
            moddedIdx, target.Data, targetIsGms1);

        // Log plan summary
        _logger.Log("");
        _logger.Log("═══════════════════════════════════════════════════════════");
        _logger.Log($"  PATCH PLAN (dry run)");
        _logger.Log($"  Actions: {plan.ActionCount} ({plan.HighConfidenceCount} high, {plan.MediumConfidenceCount} medium, {plan.LowConfidenceCount} low confidence)");
        _logger.Log($"  Skipped: {plan.SkipCount}");
        _logger.Log($"  Code replacements: {plan.CodeReplaceCount}");
        _logger.Log($"  String patches: {plan.StringPatchCount}");
        _logger.Log($"  Transplants: {plan.TransplantCount}");
        _logger.Log($"  Updates: {plan.UpdateCount}");
        _logger.Log("═══════════════════════════════════════════════════════════");

        // Write plan JSON if path specified
        if (planPath is not null)
        {
            WritePlanJson(plan, planPath);
            _logger.Log($"Plan JSON written to: {planPath}");
        }

        // Also write report if requested
        if (reportPath is not null)
        {
            var report = reportBuilder.Build();
            ReportJsonWriter.WriteToFile(report, reportPath);
            _logger.Log($"Report written to: {reportPath}");
        }

        _logger.Log("\nDry run complete. No files were modified.");

        return new TranslationResult
        {
            Success = true,
            Report = reportBuilder.Build(),
            Classification = classification,
            Plan = plan,
        };
    }

    private static void WritePlanJson(PatchPlan plan, string path)
    {
        var obj = new
        {
            classification = new
            {
                verdict = plan.Classification.Verdict.ToString(),
                estimatedSuccessPercent = plan.Classification.EstimatedSuccessPercent,
                summary = plan.Classification.Summary,
            },
            summary = new
            {
                totalActions = plan.ActionCount,
                totalSkipped = plan.SkipCount,
                highConfidence = plan.HighConfidenceCount,
                mediumConfidence = plan.MediumConfidenceCount,
                lowConfidence = plan.LowConfidenceCount,
                codeReplacements = plan.CodeReplaceCount,
                stringPatches = plan.StringPatchCount,
                transplants = plan.TransplantCount,
                updates = plan.UpdateCount,
            },
            actions = plan.Actions.Select(a => new
            {
                kind = a.Kind.ToString(),
                resource = a.Resource,
                confidence = a.Confidence.ToString(),
                description = a.Description,
                detail = a.Detail,
            }).ToArray(),
            skipped = plan.Skipped.Select(s => new
            {
                kind = s.Kind.ToString(),
                resource = s.Resource,
                reason = s.Reason,
                detail = s.Detail,
            }).ToArray(),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(obj,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 0: Resource transplantation
    // ═══════════════════════════════════════════════════════════════

    private NameIndex TransplantResources(
        ArchiveLoader.LoadResult modded,
        ArchiveLoader.LoadResult target,
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        NameIndex targetIdx,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
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

        bool hasTransplantWork = manifest.TotalNewResources > 0 ||
            manifest.ModifiedSprites.Count > 0 ||
            manifest.ModifiedBackgrounds.Count > 0 ||
            manifest.ModifiedFonts.Count > 0 ||
            manifest.ModifiedSounds.Count > 0;

        if (!hasTransplantWork)
            return targetIdx;

        // Shared texture clone maps for sprites, fonts, and backgrounds
        var textureCloneMap = new Dictionary<int, UndertaleEmbeddedTexture>();
        var tpagCloneMap = new Dictionary<UndertaleTexturePageItem, UndertaleTexturePageItem>(
            ReferenceEqualityComparer.Instance);

        // ── 1. Transplant sprites (with textures and TPAG items) ──
        TransplantAndReport(manifest.NewSprites, "Sprite", "sprites",
            names => SpriteTransplanter.TransplantAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == SpriteTransplantStatus.Transplanted,
            r => r.Status == SpriteTransplantStatus.AlreadyExists,
            r => r.Status == SpriteTransplantStatus.Failed,
            r => r.SpriteName,
            r => r.Diagnostic,
            r => r.Status == SpriteTransplantStatus.Transplanted ? $"Transplanted ({r.FrameCount} frames)" : null,
            reportBuilder, ct);

        // ── 1b. Update modified sprites ──
        UpdateAndReport(manifest.ModifiedSprites, "Sprite", "sprites",
            names => SpriteTransplanter.UpdateAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == SpriteUpdateStatus.Updated,
            r => r.Status == SpriteUpdateStatus.Failed,
            r => r.SpriteName,
            r => r.Diagnostic,
            r => r.Status == SpriteUpdateStatus.Updated ? $"Updated ({r.FrameCount} frames)" : null,
            reportBuilder, ct);

        // ── 2. Transplant sounds ──
        TransplantAndReport(manifest.NewSounds, "Sound", "sounds",
            names => SoundTransplanter.TransplantAll(names, moddedIdx, target.Data),
            r => r.Status == SoundTransplantStatus.Transplanted,
            r => r.Status == SoundTransplantStatus.AlreadyExists,
            r => r.Status == SoundTransplantStatus.Failed,
            r => r.SoundName,
            r => r.Diagnostic,
            r => null,
            reportBuilder, ct);

        // ── 2b. Update modified sounds ──
        UpdateAndReport(manifest.ModifiedSounds, "Sound", "sounds",
            names => SoundTransplanter.UpdateAll(names, moddedIdx, target.Data),
            r => r.Status == SoundUpdateStatus.Updated,
            r => r.Status == SoundUpdateStatus.Failed,
            r => r.SoundName,
            r => r.Diagnostic,
            r => null,
            reportBuilder, ct);

        // ── 3. Transplant backgrounds (tilesets) ──
        TransplantAndReport(manifest.NewBackgrounds, "Background", "backgrounds",
            names => BackgroundTransplanter.TransplantAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == BackgroundTransplantStatus.Transplanted,
            r => r.Status == BackgroundTransplantStatus.AlreadyExists,
            r => r.Status == BackgroundTransplantStatus.Failed,
            r => r.BackgroundName,
            r => r.Diagnostic,
            r => null,
            reportBuilder, ct);

        // ── 3b. Update modified backgrounds ──
        UpdateAndReport(manifest.ModifiedBackgrounds, "Background", "backgrounds",
            names => BackgroundTransplanter.UpdateAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == BackgroundUpdateStatus.Updated,
            r => r.Status == BackgroundUpdateStatus.Failed,
            r => r.BackgroundName,
            r => r.Diagnostic,
            r => null,
            reportBuilder, ct);

        // ── 4. Transplant fonts ──
        TransplantAndReport(manifest.NewFonts, "Font", "fonts",
            names => FontTransplanter.TransplantAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == FontTransplantStatus.Transplanted,
            r => r.Status == FontTransplantStatus.AlreadyExists,
            r => r.Status == FontTransplantStatus.Failed,
            r => r.FontName,
            r => r.Diagnostic,
            r => r.Status == FontTransplantStatus.Transplanted ? $"Transplanted ({r.GlyphCount} glyphs)" : null,
            reportBuilder, ct);

        // ── 4b. Update modified fonts ──
        UpdateAndReport(manifest.ModifiedFonts, "Font", "fonts",
            names => FontTransplanter.UpdateAll(names, moddedIdx, target.Data, textureCloneMap, tpagCloneMap),
            r => r.Status == FontUpdateStatus.Updated,
            r => r.Status == FontUpdateStatus.Failed,
            r => r.FontName,
            r => r.Diagnostic,
            r => r.Status == FontUpdateStatus.Updated ? $"Updated ({r.GlyphCount} glyphs)" : null,
            reportBuilder, ct);

        // ── Diagnostic: texture transplant summary ──
        if (textureCloneMap.Count > 0)
            RunTextureDiagnostics(target.Data, modded.Data, textureCloneMap);

        // ── 5. Transplant code entries ──
        TransplantCodeEntries(manifest.NewCodeEntries, moddedIdx, target.Data, reportBuilder, ct);

        // ── 6. Transplant scripts ──
        TransplantAndReport(manifest.NewScripts, "Script", "scripts",
            names => ScriptTransplanter.TransplantAll(names, moddedIdx, target.Data),
            r => r.Status == ScriptTransplantStatus.Transplanted,
            r => r.Status == ScriptTransplantStatus.AlreadyExists,
            r => r.Status == ScriptTransplantStatus.Failed,
            r => r.ScriptName,
            r => r.Diagnostic,
            r => null,
            reportBuilder, ct);

        // ── 7. Transplant game objects ──
        TransplantAndReport(manifest.NewObjects, "Object", "game objects",
            names => GameObjectTransplanter.TransplantAll(names, moddedIdx, target.Data),
            r => r.Status == GameObjectTransplantStatus.Transplanted,
            r => r.Status == GameObjectTransplantStatus.AlreadyExists,
            r => r.Status == GameObjectTransplantStatus.Failed,
            r => r.ObjectName,
            r => r.Diagnostic,
            r => r.Status == GameObjectTransplantStatus.Transplanted
                ? $"Transplanted ({r.EventCount} actions" +
                  (r.SkippedActionCount > 0 ? $", skipped {r.SkippedActionCount}" : "") + ")"
                : null,
            reportBuilder, ct);

        // ── 8. Transplant rooms (depends on all above) ──
        TransplantRooms(manifest.NewRooms, moddedIdx, target.Data, reportBuilder, ct);

        // Rebuild target name index after transplants (new resources were added)
        ct.ThrowIfCancellationRequested();
        _logger.Log("Rebuilding target name index after transplants...");
        return NameIndex.Build(target.Data);
    }

    /// <summary>
    /// Generic transplant reporting: runs the transplant operation and reports results.
    /// Eliminates the repeated switch/case blocks for each resource type.
    /// </summary>
    private void TransplantAndReport<TResult>(
        IReadOnlyList<string> names,
        string resourceType,
        string displayName,
        Func<IReadOnlyList<string>, IReadOnlyList<TResult>> transplantFunc,
        Func<TResult, bool> isTransplanted,
        Func<TResult, bool> isAlreadyExists,
        Func<TResult, bool> isFailed,
        Func<TResult, string> getName,
        Func<TResult, string?> getDiagnostic,
        Func<TResult, string?> getSuccessDetail,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        if (names.Count == 0) return;

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Transplanting {names.Count} {displayName}...");
        var results = transplantFunc(names);

        int transplanted = 0, failed = 0;
        foreach (var r in results)
        {
            var name = getName(r);
            var diagnostic = getDiagnostic(r);

            if (isTransplanted(r))
            {
                transplanted++;
                reportBuilder.AddApplied(resourceType, name,
                    getSuccessDetail(r) ?? "Transplanted", null, ReportConfidence.High);
            }
            else if (isAlreadyExists(r))
            {
                reportBuilder.AddSkipped(resourceType, name,
                    "Already exists in target", diagnostic);
            }
            else if (isFailed(r))
            {
                failed++;
                reportBuilder.AddError(resourceType, name,
                    diagnostic ?? "Transplant failed");
            }
            else
            {
                reportBuilder.AddSkipped(resourceType, name,
                    diagnostic ?? $"Status: unknown");
            }
        }
        _logger.Log($"  {resourceType} transplant: {transplanted} ok, {failed} failed");
    }

    /// <summary>
    /// Generic update reporting: runs the update operation and reports results.
    /// </summary>
    private void UpdateAndReport<TResult>(
        IReadOnlyList<string> names,
        string resourceType,
        string displayName,
        Func<IReadOnlyList<string>, IReadOnlyList<TResult>> updateFunc,
        Func<TResult, bool> isUpdated,
        Func<TResult, bool> isFailed,
        Func<TResult, string> getName,
        Func<TResult, string?> getDiagnostic,
        Func<TResult, string?> getSuccessDetail,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        if (names.Count == 0) return;

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Updating {names.Count} modified {displayName}...");
        var results = updateFunc(names);

        int updated = 0, failed = 0;
        foreach (var r in results)
        {
            var name = getName(r);
            var diagnostic = getDiagnostic(r);

            if (isUpdated(r))
            {
                updated++;
                reportBuilder.AddApplied(resourceType, name,
                    getSuccessDetail(r) ?? "Updated", null, ReportConfidence.High);
            }
            else if (isFailed(r))
            {
                failed++;
                reportBuilder.AddError(resourceType, name,
                    diagnostic ?? "Update failed");
            }
            else
            {
                reportBuilder.AddSkipped(resourceType, name,
                    diagnostic ?? $"Status: unknown");
            }
        }
        _logger.Log($"  {resourceType} update: {updated} ok, {failed} failed");
    }

    /// <summary>
    /// Code entries have an extra "Unsupported" status, so they use a dedicated method.
    /// </summary>
    private void TransplantCodeEntries(
        IReadOnlyList<string> names,
        NameIndex moddedIdx,
        UndertaleData targetData,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        if (names.Count == 0) return;

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Transplanting {names.Count} code entries...");
        var results = CodeTransplanter.TransplantAll(names, moddedIdx, targetData);

        int transplanted = 0, unsupported = 0, failed = 0;
        foreach (var r in results)
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

    /// <summary>
    /// Rooms have a special logging rule (only first 3 failures logged as warnings).
    /// </summary>
    private void TransplantRooms(
        IReadOnlyList<string> names,
        NameIndex moddedIdx,
        UndertaleData targetData,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        if (names.Count == 0) return;

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Transplanting {names.Count} rooms...");
        var results = RoomTransplanter.TransplantAll(names, moddedIdx, targetData);

        int transplanted = 0, failed = 0;
        foreach (var r in results)
        {
            switch (r.Status)
            {
                case RoomTransplantStatus.Transplanted:
                    transplanted++;
                    reportBuilder.AddApplied("Room", r.RoomName,
                        $"Transplanted ({r.LayerCount} layers, {r.GameObjectCount} instances, " +
                        $"roomOrder={(r.AddedToRoomOrder ? "added" : "existing")}, " +
                        $"maxObj={r.MaxInstanceId}, maxTile={r.MaxTileId}" +
                        (r.SkippedCreationCode ? ", skipped creation code" : "") + ")",
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

    // ═══════════════════════════════════════════════════════════════
    //  Texture diagnostics
    // ═══════════════════════════════════════════════════════════════

    private void RunTextureDiagnostics(
        UndertaleData targetData,
        UndertaleData moddedData,
        Dictionary<int, UndertaleEmbeddedTexture> textureCloneMap)
    {
        _logger.Log($"  [DIAG] Texture clone map: {textureCloneMap.Count} textures cloned from modded→target");
        _logger.Log($"  [DIAG] Target TXTR count: {targetData.EmbeddedTextures.Count}");
        _logger.Log($"  [DIAG] Target TPAG count: {targetData.TexturePageItems.Count}");
        _logger.Log($"  [DIAG] Target SPRT count: {targetData.Sprites.Count}");
        _logger.Log($"  [DIAG] Target GMS version: Major={targetData.GeneralInfo?.Major}, Minor={targetData.GeneralInfo?.Minor}");
        _logger.Log($"  [DIAG] Modded GMS version: Major={moddedData.GeneralInfo?.Major}, Minor={moddedData.GeneralInfo?.Minor}");
        _logger.Log($"  [DIAG] TGIN present: {targetData.TextureGroupInfo != null} (count={targetData.TextureGroupInfo?.Count ?? 0})");
        _logger.Log($"  [DIAG] IsTPAG4ByteAligned: {targetData.IsTPAG4ByteAligned}");

        // Dump details of each cloned texture
        foreach (var (srcIdx, clonedTex) in textureCloneMap)
        {
            var img = clonedTex.TextureData?.Image;
            int clonedTexIdx = targetData.EmbeddedTextures.IndexOf(clonedTex);
            _logger.Log(
                $"  [DIAG] TXTR[modded:{srcIdx}→target:{clonedTexIdx}]: " +
                $"Scaled={clonedTex.Scaled}, " +
                $"GeneratedMips={clonedTex.GeneratedMips}, " +
                $"TextureWidth={clonedTex.TextureWidth}, " +
                $"TextureHeight={clonedTex.TextureHeight}, " +
                $"IndexInGroup={clonedTex.IndexInGroup}, " +
                $"ImageFormat={img?.Format}, " +
                $"ImageW={img?.Width}, ImageH={img?.Height}, " +
                $"DataLen={img?.ToSpan().Length}");
        }

        // Validate TPAG references
        int tpagErrors = 0;
        foreach (var tpag in targetData.TexturePageItems)
        {
            if (tpag.TexturePage is null)
            {
                _logger.LogWarning($"  [DIAG] TPAG item has null TexturePage!");
                tpagErrors++;
                continue;
            }
            int texIdx = targetData.EmbeddedTextures.IndexOf(tpag.TexturePage);
            if (texIdx < 0)
            {
                _logger.LogWarning($"  [DIAG] TPAG item references EmbeddedTexture NOT in target list!");
                tpagErrors++;
            }
        }
        _logger.Log(tpagErrors > 0
            ? $"  [DIAG] {tpagErrors} TPAG reference error(s) found!"
            : $"  [DIAG] All TPAG references valid.");

        // Validate sprite→TPAG references
        int sprtErrors = 0;
        foreach (var spr in targetData.Sprites)
        {
            if (spr.Textures is null) continue;
            foreach (var texEntry in spr.Textures)
            {
                if (texEntry?.Texture is null) continue;
                int tpagIdx = targetData.TexturePageItems.IndexOf(texEntry.Texture);
                if (tpagIdx < 0)
                {
                    _logger.LogWarning($"  [DIAG] Sprite '{spr.Name?.Content}' references TPAG NOT in target list!");
                    sprtErrors++;
                }
            }
        }
        _logger.Log(sprtErrors > 0
            ? $"  [DIAG] {sprtErrors} sprite→TPAG reference error(s) found!"
            : $"  [DIAG] All sprite→TPAG references valid.");

        // Check Scaled values consistency
        var scaledValues = targetData.EmbeddedTextures
            .Select(t => t.Scaled)
            .Distinct()
            .ToList();
        _logger.Log($"  [DIAG] Scaled values in target TXTR: [{string.Join(", ", scaledValues)}]");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 0b: Event bindings
    // ═══════════════════════════════════════════════════════════════

    private void PatchEventBindings(
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        UndertaleData targetData,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _logger.Log("Patching object event bindings...");
        var results = ObjectEventPatcher.PatchAll(vanillaIdx, moddedIdx, targetData);

        if (results.Count == 0)
        {
            _logger.Log("  No new event bindings needed.");
            return;
        }

        int totalAdded = 0, totalFailed = 0;
        foreach (var r in results)
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
        _logger.Log($"  Event bindings: {totalAdded} added on {results.Count} object(s), {totalFailed} failed");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 1: Diff computation and patch application
    // ═══════════════════════════════════════════════════════════════

    private void ApplyDiffPatches(
        ArchiveLoader.LoadResult vanilla,
        ArchiveLoader.LoadResult modded,
        ArchiveLoader.LoadResult target,
        NameIndex vanillaIdx,
        NameIndex moddedIdx,
        NameIndex targetIdx,
        PatchReportBuilder reportBuilder,
        CancellationToken ct)
    {
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

        // Convert diffs to patch requests
        ct.ThrowIfCancellationRequested();
        _logger.Log("Building patch requests...");

        var codeEntriesWithStringOnlyDiffs = new HashSet<string>(StringComparer.Ordinal);
        var stringRequests = BuildStringPatchRequests(stringDiffs, reportBuilder);
        var codeRequests = BuildCodePatchRequests(codeDiffs, modded.Data, codeEntriesWithStringOnlyDiffs, reportBuilder);
        var objectRequests = BuildObjectPatchRequests(objectDiffs, reportBuilder);

        // Apply patches
        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {stringRequests.Count} string patches...");
        var stringResults = StringPatcher.ApplyMany(target.Data, stringRequests);
        var codeEntriesWithAppliedStrings = ReportStringResults(stringResults, reportBuilder);

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {codeRequests.Count} code patches...");
        var codeResults = CodePatcher.ApplyMany(target.Data, codeRequests);
        ReportCodeResults(codeResults, codeEntriesWithStringOnlyDiffs, codeEntriesWithAppliedStrings, reportBuilder);

        ct.ThrowIfCancellationRequested();
        _logger.Log($"Applying {objectRequests.Count} object patches...");
        ApplyObjectPatches(objectRequests, targetIdx, reportBuilder);
    }

    // ─────────────────────────────────────────────────────────
    // Diff → patch request builders
    // ─────────────────────────────────────────────────────────

    private List<StringPatchRequest> BuildStringPatchRequests(
        IReadOnlyList<CodeStringDiffResult> stringDiffs,
        PatchReportBuilder reportBuilder)
    {
        var requests = new List<StringPatchRequest>();

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

                if (change.Confidence == StringMatchConfidence.ContentMatchOnly)
                {
                    reportBuilder.AddRisky("String", $"{diff.CodeEntryName}[{change.ModdedOrdinal}]",
                        $"Content-only match (ordinal shifted)",
                        $"old='{Truncate(change.OldContent, 60)}' new='{Truncate(change.NewContent, 60)}'");
                }

                var context = new Gmmt.Patch.ContextFingerprint(
                    change.ModdedContext.Preceding,
                    change.ModdedContext.Following);

                requests.Add(new StringPatchRequest(
                    diff.CodeEntryName,
                    change.VanillaOrdinal.Value,
                    change.OldContent,
                    change.NewContent,
                    context));
            }
        }

        return requests;
    }

    private List<CodePatchRequest> BuildCodePatchRequests(
        IReadOnlyList<CodeDiffResult> codeDiffs,
        UndertaleData moddedData,
        HashSet<string> codeEntriesWithStringOnlyDiffs,
        PatchReportBuilder reportBuilder)
    {
        var requests = new List<CodePatchRequest>();

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
                // Bytecode replacement handled in Phase 1a; skip here
                continue;
            }

            codeEntriesWithStringOnlyDiffs.Add(diff.CodeEntryName);

            var moddedCode = moddedData.Code?.FirstOrDefault(c => c.Name?.Content == diff.CodeEntryName);
            if (moddedCode is null)
            {
                reportBuilder.AddSkipped("Code", diff.CodeEntryName,
                    "Code entry not found in modded archive");
                continue;
            }

            var snapshot = CodeSnapshotBuilder.Build(moddedCode);
            requests.Add(new CodePatchRequest(diff.CodeEntryName, snapshot));
        }

        return requests;
    }

    private static List<ObjectPatchRequest> BuildObjectPatchRequests(
        IReadOnlyList<ObjectDiffResult> objectDiffs,
        PatchReportBuilder reportBuilder)
    {
        var requests = new List<ObjectPatchRequest>();

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

            requests.Add(new ObjectPatchRequest
            {
                ObjectName = diff.ObjectName,
                Patches = patches
            });
        }

        return requests;
    }

    // ─────────────────────────────────────────────────────────
    // Patch result reporters
    // ─────────────────────────────────────────────────────────

    private static HashSet<string> ReportStringResults(
        IReadOnlyList<StringPatchResult> results,
        PatchReportBuilder reportBuilder)
    {
        var codeEntriesWithAppliedStrings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in results)
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

        return codeEntriesWithAppliedStrings;
    }

    private static void ReportCodeResults(
        IReadOnlyList<CodePatchResult> results,
        HashSet<string> codeEntriesWithStringOnlyDiffs,
        HashSet<string> codeEntriesWithAppliedStrings,
        PatchReportBuilder reportBuilder)
    {
        foreach (var r in results)
        {
            // Suppress noise: code entries where the only changes were strings
            // already handled by the string patcher
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
    }

    private static void ApplyObjectPatches(
        List<ObjectPatchRequest> requests,
        NameIndex targetIdx,
        PatchReportBuilder reportBuilder)
    {
        foreach (var req in requests)
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
    }

    // ═══════════════════════════════════════════════════════════════
    //  Phase 2: Output writing
    // ═══════════════════════════════════════════════════════════════

    private TranslationResult WriteOutput(
        ArchiveLoader.LoadResult target,
        string outputPath,
        PatchReportBuilder reportBuilder,
        string? reportPath,
        bool dryRun,
        bool backup,
        CancellationToken ct)
    {
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

    // ═══════════════════════════════════════════════════════════════
    //  Shared utilities
    // ═══════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════
    //  Classification logging
    // ═══════════════════════════════════════════════════════════════

    private void LogClassification(ModClassification classification)
    {
        var verdictStr = classification.Verdict switch
        {
            PortabilityVerdict.Portable => "✅ PORTABLE",
            PortabilityVerdict.MostlyPortable => "⚠️  MOSTLY PORTABLE",
            PortabilityVerdict.PatchableOnly => "⚠ PATCHABLE ONLY",
            PortabilityVerdict.NotPortable => "❌ NOT PORTABLE",
            _ => "?"
        };

        _logger.Log("");
        _logger.Log("═══════════════════════════════════════════════════════════");
        _logger.Log($"  PRE-FLIGHT CLASSIFICATION: {verdictStr}");
        _logger.Log($"  {classification.Summary}");
        _logger.Log($"  Estimated auto-portable: {classification.EstimatedSuccessPercent}%");
        _logger.Log("═══════════════════════════════════════════════════════════");

        var b = classification.Breakdown;
        _logger.Log($"  Resources: {b.TotalNewResources} new, {b.TotalModifiedResources} modified");
        _logger.Log($"  Code: {b.NewCodeEntryCount} new ({b.NewCodePortableCount} portable, {b.NewCodeUnsupportedCount} unsupported)");
        _logger.Log($"  Code: {b.ModifiedCodeEntryCount} modified ({b.ModifiedCodeWithGms2OnlyRefsCount} with GMS2-only refs, {b.ModifiedCodeWithChildEntriesCount} with child entries)");

        if (b.NewRoomCount > 0)
            _logger.Log($"  Rooms: {b.NewRoomCount} new (risky)");

        // Log non-info issues
        var significantIssues = classification.Issues
            .Where(i => i.Severity != ClassificationSeverity.Info)
            .ToList();

        if (significantIssues.Count > 0)
        {
            _logger.Log("");
            foreach (var issue in significantIssues)
            {
                var prefix = issue.Severity switch
                {
                    ClassificationSeverity.Blocker => "❌ BLOCKER",
                    ClassificationSeverity.Major => "⚠ MAJOR",
                    ClassificationSeverity.Minor => "ℹ MINOR",
                    _ => "  INFO"
                };

                if (issue.Severity == ClassificationSeverity.Blocker)
                    _logger.LogError($"  [{prefix}] {issue.Code}: {issue.Message}");
                else if (issue.Severity == ClassificationSeverity.Major)
                    _logger.LogWarning($"  [{prefix}] {issue.Code}: {issue.Message}");
                else
                    _logger.Log($"  [{prefix}] {issue.Code}: {issue.Message}");

                if (issue.Detail is not null)
                    _logger.Log($"           {issue.Detail}");
            }
        }

        _logger.Log("");
    }

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return s[..(maxLen - 3)] + "...";
    }
}
