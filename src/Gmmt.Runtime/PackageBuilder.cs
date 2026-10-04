using System.Text.Json;

namespace Gmmt.Runtime;

public static class PackageBuilder
{
    public static string Create(PackagePlan plan, string output, IReadOnlyList<string> assetDirectories,
        IReadOnlyList<string>? libraryDirectories = null, bool nativeExtensionsReviewed = false,
        Dictionary<string, string>? inputHashes = null)
    {
        if (!plan.CanPackage) throw new InvalidOperationException(string.Join("\n", plan.Blockers));
        if (plan.Archive.NativeExtensions.Length != 0 && !nativeExtensionsReviewed)
            throw new InvalidOperationException("Native extensions detected. Supply Linux dependencies and explicitly acknowledge their review.");
        var runner = plan.Runner!;
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; choose a new package directory.");
        var assets = assetDirectories.Select(Path.GetFullPath).ToArray();
        var libraries = (libraryDirectories ?? []).Select(Path.GetFullPath).ToArray();
        foreach (var directory in assets.Concat(libraries))
        {
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
            if (IsInside(output, directory)) throw new IOException("Output must be outside all input resource directories.");
        }
        var parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".gmmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var assetOutput = Path.Combine(staging, "assets");
            Directory.CreateDirectory(assetOutput);
            var skipped = new List<string>();
            foreach (var source in assets) CopyTree(source, assetOutput, true, skipped);
            foreach (var source in libraries) CopyTree(source, Path.Combine(staging, "lib"), false, skipped);
            File.Copy(plan.Archive.Metadata.FilePath, Path.Combine(assetOutput, "game.unx"), false);
            File.Copy(runner.RunnerPath, Path.Combine(staging, "runner"), false);
            if (RuntimeCatalog.Hash(Path.Combine(assetOutput, "game.unx")) != plan.Archive.Sha256)
                throw new IOException("Input archive changed after planning.");
            if (RuntimeCatalog.Hash(Path.Combine(staging, "runner")) != runner.RunnerSha256)
                throw new IOException("Runner changed after planning.");
            var runtime = runner.SteamRuntimeScript;
            var launcher = "#!/bin/sh\nset -eu\ncd -- \"$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\"\n";
            if (runtime != null)
                launcher += "runtime=${GMMT_STEAM_RUNTIME:-" + Quote(runtime) + "}\n"
                    + "if [ ! -f \"$runtime\" ]; then echo 'Steam runtime script not found; set GMMT_STEAM_RUNTIME' >&2; exit 1; fi\n"
                    + "selected=$(\"$runtime\" --print-steam-runtime-library-paths)\n"
                    + "export LD_LIBRARY_PATH=\"$PWD/lib:$selected${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}\"\n";
            else launcher += "export LD_LIBRARY_PATH=\"$PWD/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}\"\n";
            // Calling the loader works on filesystems where copied executable bits cannot be set.
            var loader = runner.Elf.Machine switch
            { 3 => "/lib/ld-linux.so.2", 62 => "/lib64/ld-linux-x86-64.so.2", 183 => "/lib/ld-linux-aarch64.so.1", _ => "/lib/ld-linux-armhf.so.3" };
            launcher += "if [ -x ./runner ]; then exec ./runner \"$@\"; fi\n"
                + "if [ -x " + Quote(loader) + " ]; then exec " + Quote(loader) + " ./runner \"$@\"; fi\n"
                + "echo 'Runner is not executable and ELF loader is unavailable; check permissions and multilib' >&2\nexit 1\n";
            File.WriteAllText(Path.Combine(staging, "launch.sh"), launcher);
            if (OperatingSystem.IsLinux())
            {
                TryExecutable(Path.Combine(staging, "runner"));
                TryExecutable(Path.Combine(staging, "launch.sh"));
            }
            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => new { Path = Path.GetRelativePath(staging, f), Sha256 = RuntimeCatalog.Hash(f), Bytes = new FileInfo(f).Length }).ToArray();
            File.WriteAllText(Path.Combine(staging, "gmmt-package.json"), JsonSerializer.Serialize(new
            {
                FormatVersion = 1, CreatedUtc = DateTimeOffset.UtcNow, Strategy = "NativeRunner", Plan = plan,
                InputHashes = inputHashes, Assets = assets, Libraries = libraries, Skipped = skipped, Files = files,
                GameplayVerified = false
            }, RuntimeCatalog.JsonOptions));
            Directory.Move(staging, output);
            return output;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static bool IsInside(string candidate, string directory) => candidate == directory
        || candidate.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void CopyTree(string source, string destination, bool assets, List<string> skipped)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symlinked input directories are unsupported: " + source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlinked input is unsupported: " + entry);
            var name = Path.GetFileName(entry);
            if (name is ".git" or "bin" or "obj") continue;
            var target = Path.Combine(destination, name);
            if ((attributes & FileAttributes.Directory) != 0) CopyTree(entry, target, assets, skipped);
            else
            {
                var extension = Path.GetExtension(name).ToLowerInvariant();
                bool mainArchive = new[] { "data.win", "game.unx", "game.win" }.Contains(name, StringComparer.OrdinalIgnoreCase);
                if (mainArchive || extension is ".exe" or ".bat" or ".cmd" or ".ps1" or ".sh" or ".xdelta")
                { skipped.Add(entry); continue; }
                File.Copy(entry, target, true);
            }
        }
    }

    private static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";
    private static void TryExecutable(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
    }
}
