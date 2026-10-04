using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Gmmt.Runtime;

public sealed class ArchiveInput : IDisposable
{
    public string Path { get; }
    public Dictionary<string, string> InputHashes { get; }
    public string? BundledRunnerPath { get; }
    public string[] ExtraAssetPaths { get; }
    private readonly bool temporary;
    private readonly string? temporaryDir;

    private ArchiveInput(string path, bool temporary, Dictionary<string, string> hashes,
        string? bundledRunner = null, string[]? extraAssetPaths = null, string? temporaryDir = null)
    {
        Path = path;
        this.temporary = temporary;
        InputHashes = hashes;
        BundledRunnerPath = bundledRunner;
        ExtraAssetPaths = extraAssetPaths ?? Array.Empty<string>();
        this.temporaryDir = temporaryDir;
    }

    public static async Task<ArchiveInput> PrepareAsync(string input, string? vanilla = null, bool patch = false, IEnumerable<string>? alternateBaselines = null)
    {
        input = System.IO.Path.GetFullPath(input);
        var hashes = new Dictionary<string, string> { [input] = RuntimeCatalog.Hash(input) };

        if (input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gmmt-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                using var zip = ZipFile.OpenRead(input);
                ZipInput.Validate(zip);
                string? bundledRunner = null;
                var extraAssets = new List<string>();

                var runnerEntry = zip.Entries.FirstOrDefault(e => e.Name.Equals("runner", StringComparison.OrdinalIgnoreCase) && e.Length > 50_000);
                if (runnerEntry != null)
                {
                    var runnerPath = System.IO.Path.Combine(tempDir, "runner");
                    runnerPath = ZipInput.Extract(runnerEntry, tempDir);
                    _ = RuntimeCatalog.InspectElf(runnerPath);
                    if (OperatingSystem.IsLinux())
                    {
                        File.SetUnixFileMode(runnerPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                    bundledRunner = runnerPath;
                }

                // Extract assets if any
                var assetEntries = zip.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Name)).ToList();
                if (assetEntries.Count > 0)
                {
                    var assetsDir = System.IO.Path.Combine(tempDir, "assets");
                    Directory.CreateDirectory(assetsDir);
                    foreach (var entry in assetEntries)
                    {
                        ZipInput.Extract(entry, tempDir);
                    }
                    extraAssets.Add(assetsDir);
                }

                var patchEntries = zip.Entries.Where(e => e.Name.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)).ToArray();
                var archiveEntries = zip.Entries.Where(e => e.Name.EndsWith(".unx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".win", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (patchEntries.Length > 1 || archiveEntries.Length > 1 || (patchEntries.Length != 0 && archiveEntries.Length != 0))
                    throw new InvalidDataException("ZIP contains multiple mod inputs. Extract and choose the intended file.");
                var patchEntry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase));
                var archiveEntry = zip.Entries.FirstOrDefault(e => (e.Name.EndsWith(".unx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".win", StringComparison.OrdinalIgnoreCase)) && e.Length > 50_000);

                if (patchEntry != null)
                {
                    var extractedPatch = ZipInput.Extract(patchEntry, tempDir);
                    hashes[extractedPatch] = RuntimeCatalog.Hash(extractedPatch);

                    var patchedOutput = await ApplyPatchWithFallbackAsync(extractedPatch, vanilla, hashes, alternateBaselines);
                    return new ArchiveInput(patchedOutput, true, hashes, bundledRunner, extraAssets.ToArray(), tempDir);
                }

                if (archiveEntry != null)
                {
                    var extractedArchive = ZipInput.Extract(archiveEntry, tempDir);
                    return new ArchiveInput(extractedArchive, false, hashes, bundledRunner, extraAssets.ToArray(), tempDir);
                }

                throw new InvalidDataException("Zip archive does not contain a compatible GameMaker patch (.xdelta) or data archive (.win/.unx).");
            }
            catch
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                throw;
            }
        }

        string? dirRunner = null;
        var inputDir = System.IO.Path.GetDirectoryName(input);
        if (inputDir != null)
        {
            var potentialRunner = System.IO.Path.Combine(inputDir, "runner");
            if (File.Exists(potentialRunner) && new FileInfo(potentialRunner).Length > 50_000)
            {
                dirRunner = potentialRunner;
            }
        }

        if (!patch && !input.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveInput(input, false, hashes, dirRunner);
        }

        var patchedPath = await ApplyPatchWithFallbackAsync(input, vanilla, hashes, alternateBaselines);
        return new ArchiveInput(patchedPath, true, hashes, dirRunner);
    }

    private static async Task<string> ApplyPatchWithFallbackAsync(string patchFile, string? vanilla, Dictionary<string, string> hashes, IEnumerable<string>? alternateBaselines)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(vanilla))
        {
            candidates.Add(System.IO.Path.GetFullPath(vanilla));
        }

        if (alternateBaselines != null) candidates.AddRange(alternateBaselines.Select(System.IO.Path.GetFullPath));

        // Add common fallback candidates on user's system
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonLinuxUnx = System.IO.Path.Combine(home, ".local/share/Steam/steamapps/common/Undertale/assets/game.unx");
        if (File.Exists(commonLinuxUnx) && !candidates.Contains(commonLinuxUnx)) candidates.Add(commonLinuxUnx);

        var commonDepotWin = System.IO.Path.Combine(home, ".local/share/Steam/ubuntu12_32/steamapps/content/app_391540/depot_391541/data.win");
        if (File.Exists(commonDepotWin) && !candidates.Contains(commonDepotWin)) candidates.Add(commonDepotWin);

        var altDepotWin = System.IO.Path.Combine(home, ".local/share/Steam/steamapps/content/app_391540/depot_391541/data.win");
        if (File.Exists(altDepotWin) && !candidates.Contains(altDepotWin)) candidates.Add(altDepotWin);

        if (candidates.Count == 0)
        {
            throw new ArgumentException("A clean Undertale vanilla archive (data.win or game.unx) is required for xdelta patching.");
        }

        string lastError = "";
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(candidate)) continue;
            var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gmmt-" + Guid.NewGuid().ToString("N") + ".win");
            try
            {
                var baselineHash = RuntimeCatalog.Hash(candidate);
                var start = new ProcessStartInfo("xdelta3")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var arg in new[] { "-d", "-s", candidate, patchFile, output }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Could not start xdelta3");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                    throw new IOException("xdelta3 timed out");
                }
                await stdout;
                var error = await stderr;
                if (process.ExitCode == 0)
                {
                    if (RuntimeCatalog.Hash(candidate) != baselineHash) throw new IOException("Baseline changed while applying patch.");
                    hashes[candidate] = baselineHash;
                    return output;
                }
                lastError = error;
                if (File.Exists(output)) File.Delete(output);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (File.Exists(output)) File.Delete(output);
                lastError = ex.Message;
            }
        }

        throw new IOException($"xdelta3 failed to match any baseline archive: {lastError}");
    }

    public void Dispose()
    {
        if (temporary && File.Exists(Path)) File.Delete(Path);
        if (temporaryDir != null && Directory.Exists(temporaryDir))
        {
            try { Directory.Delete(temporaryDir, true); } catch { }
        }
    }
}
