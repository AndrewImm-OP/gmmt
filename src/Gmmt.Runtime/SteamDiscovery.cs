using System.Text.RegularExpressions;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Gmmt.Runtime;

public sealed record DiscoveredRunner(string Game, string RunnerPath, string? ArchivePath, ElfInfo Elf, string? SteamRuntimeScript, byte? BytecodeVersion = null, bool? IsGMS2 = null, string? EngineVersion = null)
{
    public override string ToString() => $"{Game} · ELF{Elf.Bits} · {(ArchivePath == null ? "runner only" : Path.GetFileName(ArchivePath))}";
}
public sealed record DiscoveredGame(string Name, string Directory, string? AppId = null)
{
    public override string ToString() => Name;
}
public sealed record DiscoveredDepot(string AppId, string DepotId, string ArchivePath)
{
    public override string ToString() => $"App {AppId} · Depot {DepotId} · {(ArchivePath == null ? "runner only" : Path.GetFileName(ArchivePath))}";
}
public sealed record SteamDiscoveryResult(string[] Libraries, DiscoveredGame[] Games, DiscoveredRunner[] Runners, string[] Warnings, DiscoveredDepot[] Depots);

/// <summary>Read-only bounded discovery. Finding an ELF/archive pair does not certify compatibility.</summary>
public static class SteamDiscovery
{
    public static SteamDiscoveryResult Scan(IEnumerable<string>? steamRoots = null, IEnumerable<string>? runnerRoots = null, CancellationToken cancellation = default)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = steamRoots ?? new[] { Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".steam/steam"),
            Path.Combine(home, ".steam/root"), Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam") };
        var libraries = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var root in roots)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(root)) continue;
                var canonical = Canonical(root);
                libraries.Add(canonical);
                foreach (var vdf in new[] { Path.Combine(canonical, "steamapps/libraryfolders.vdf"), Path.Combine(canonical, "config/libraryfolders.vdf") })
                {
                    if (!File.Exists(vdf)) continue;
                    if (new FileInfo(vdf).Length > 4 * 1024 * 1024) { warnings.Add("Steam library file too large: " + vdf); continue; }
                    var text = File.ReadAllText(vdf);
                    foreach (Match match in Regex.Matches(text, "\"(?:path|[0-9]+)\"\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
                    {
                        var value = match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                        if (Path.IsPathFullyQualified(value) && Directory.Exists(Path.Combine(value, "steamapps"))) libraries.Add(Canonical(value));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(root + ": " + ex.Message); }
        }
        var appIdsByInstallDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            try
            {
                foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf").Take(512))
                {
                    try
                    {
                        var text = File.ReadAllText(acf);
                        var appidMatch = Regex.Match(text, "\"appid\"\\s*\"([0-9]+)\"");
                        var installdirMatch = Regex.Match(text, "\"installdir\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
                        if (appidMatch.Success && installdirMatch.Success)
                        {
                            var appId = appidMatch.Groups[1].Value;
                            var installdir = installdirMatch.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                            appIdsByInstallDir[installdir] = appId;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
        var runtime = libraries.SelectMany(l => new[] { Path.Combine(l, "steamapps/common/SteamLinuxRuntime/steam-runtime/run.sh"),
            Path.Combine(l, "steamapps/common/SteamLinuxRuntime_scout/steam-runtime/run.sh") }).FirstOrDefault(File.Exists);
        var games = new List<DiscoveredGame>();
        var runners = new List<DiscoveredRunner>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in libraries.OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var common = Path.Combine(library, "steamapps/common");
            if (!Directory.Exists(common)) continue;
            try
            {
                foreach (var entry in Directory.EnumerateDirectories(common).Take(2048))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var title = Path.GetFileName(entry);
                    if (title.StartsWith("SteamLinuxRuntime", StringComparison.OrdinalIgnoreCase) || title.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) || title == "Steamworks Shared") continue;
                    var game = Canonical(entry);
                    if (!visited.Add(game)) continue;
                    var found = FindPairs(game, Path.GetFileName(entry), runtime, cancellation, warnings);
                    runners.AddRange(found);
                    if (File.Exists(Path.Combine(game, "runner")) && File.Exists(Path.Combine(game, "assets/game.unx"))
                        && File.Exists(Path.Combine(game, "run.sh")) && found.Any(p => p.RunnerPath == Path.Combine(game, "runner")))
                    {
                        appIdsByInstallDir.TryGetValue(title, out var appId);
                        games.Add(new(title, game, appId));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(common + ": " + ex.Message); }
        }
        var extra = (runnerRoots ?? new[] { Path.Combine(home, "Downloads"), Path.Combine(home, "Загрузки") }).ToArray();
        foreach (var directory in extra)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(directory)) continue;
                var canonical = Canonical(directory);
                if (visited.Add(canonical)) runners.AddRange(FindPairs(canonical, Path.GetFileName(canonical), runtime, cancellation, warnings));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(directory + ": " + ex.Message); }
        }
        var zipRunnerCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners", "cache");
        foreach (var directory in extra)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var zipPath in Directory.EnumerateFiles(directory, "*.zip").Take(32))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(zipPath);
                        if (info.Length > 2L * 1024 * 1024 * 1024) continue;
                        using var zip = ZipFile.OpenRead(zipPath);
                        var runnerEntry = zip.Entries.FirstOrDefault(e => e.Name.Equals("runner", StringComparison.OrdinalIgnoreCase) && e.Length > 50_000);
                        var archiveEntry = zip.Entries.FirstOrDefault(e => (e.Name.EndsWith(".unx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".win", StringComparison.OrdinalIgnoreCase)) && e.Length > 50_000);
                        if (runnerEntry != null)
                        {
                            ZipInput.Validate(zip);
                            var cacheDir = Path.Combine(zipRunnerCache, RuntimeCatalog.Hash(zipPath));
                            Directory.CreateDirectory(cacheDir);
                            var cachedRunner = ZipInput.Extract(runnerEntry, cacheDir);
                            var elf = RuntimeCatalog.InspectElf(cachedRunner);
                            var cachedArchive = archiveEntry == null ? null : ZipInput.Extract(archiveEntry, cacheDir);
                            var family = RunnerElfInspector.DetectFamily(cachedRunner);
                            if (cachedArchive != null || family != null)
                                runners.Add(new(Path.GetFileNameWithoutExtension(zipPath), cachedRunner, cachedArchive, elf, runtime, null, family));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(directory + ": " + ex.Message); }
        }
        var depots = new List<DiscoveredDepot>();
        var contentFolders = libraries.SelectMany(l => new[] {
            Path.Combine(l, "steamapps/content"),
            Path.Combine(l, "ubuntu12_32/steamapps/content")
        }).Concat(roots.SelectMany(r => new[] {
            Path.Combine(r, "steamapps/content"),
            Path.Combine(r, "ubuntu12_32/steamapps/content")
        })).Distinct().Where(Directory.Exists);

        foreach (var contentFolder in contentFolders)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                foreach (var appDir in Directory.EnumerateDirectories(contentFolder, "app_*").Take(128))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var appId = Path.GetFileName(appDir).Replace("app_", "");
                    foreach (var depotDir in Directory.EnumerateDirectories(appDir, "depot_*").Take(128))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var depotId = Path.GetFileName(depotDir).Replace("depot_", "");
                        // Check common archive filenames directly in depot root first, then search recursively if needed
                        var candidates = new[] { "data.win", "game.win", "game.unx" };
                        bool foundDirect = false;
                        foreach (var name in candidates)
                        {
                            var direct = Path.Combine(depotDir, name);
                            if (File.Exists(direct))
                            {
                                depots.Add(new(appId, depotId, Path.GetFullPath(direct)));
                                foundDirect = true;
                            }
                        }
                        if (!foundDirect)
                        {
                            foreach (var file in Directory.EnumerateFiles(depotDir, "*.*", SearchOption.AllDirectories).Take(512))
                            {
                                var fname = Path.GetFileName(file).ToLowerInvariant();
                                if (fname is "data.win" or "game.win" or "game.unx")
                                {
                                    depots.Add(new(appId, depotId, Path.GetFullPath(file)));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(contentFolder + ": " + ex.Message); }
        }

        return new(libraries.Order().ToArray(), games.OrderBy(g => g.Name).ToArray(),
            runners.DistinctBy(r => (r.RunnerPath, r.ArchivePath)).OrderBy(r => r.Game).ThenBy(r => r.ArchivePath).ToArray(), warnings.ToArray(),
            depots.DistinctBy(d => d.ArchivePath).OrderBy(d => d.AppId).ThenBy(d => d.DepotId).ToArray());
    }

    private static List<DiscoveredRunner> FindPairs(string game, string name, string? runtime, CancellationToken cancellation, List<string> warnings)
    {
        var found = new List<DiscoveredRunner>();
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((game, 0));
        int scanned = 0;
        while (queue.Count != 0 && scanned++ < 256)
        {
            cancellation.ThrowIfCancellationRequested();
            var (directory, depth) = queue.Dequeue();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                var assetFolder = Path.Combine(directory, "assets");
                var assetFolderAllowed = !Directory.Exists(assetFolder) || (File.GetAttributes(assetFolder) & FileAttributes.ReparsePoint) == 0;
                var archives = new[] { "game.unx", "game.win", "data.win", "game.unx.vanilla", "gameog.unx" }
                    .SelectMany(n => new[] { Path.Combine(directory, n), Path.Combine(directory, "assets", n) })
                    .Where(p => (assetFolderAllowed || Path.GetDirectoryName(p) != assetFolder) && File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0).ToArray();
                if (archives.Length != 0)
                {
                    foreach (var executable in Directory.EnumerateFiles(directory).Take(256))
                    {
                        if ((File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0 || Path.GetExtension(executable) is ".so" or ".unx" or ".win") continue;
                        // Most ordinary resources fail this cheap header check; ELF shared objects are not runners.
                        using var stream = File.OpenRead(executable);
                        var header = new byte[20];
                        if (stream.Read(header) != 20 || header[0] != 0x7f || header[1] != 69 || header[2] != 76 || header[3] != 70) continue;
                        if (header[16] != 2 && header[16] != 3) continue;
                        if (header[16] == 3 && !HasInterpreter(executable)) continue;
                        try
                        {
                            var elf = RuntimeCatalog.InspectElf(executable);
                            if (!LooksLikeGameMaker(executable)) continue;
                            found.AddRange(archives.Select(archive => new DiscoveredRunner(directory == game ? name : name + "/" + Path.GetRelativePath(game, directory), executable, archive, elf, runtime)));
                        }
                        catch (InvalidDataException) { }
                    }
                }
                if (depth < 3)
                    foreach (var child in Directory.EnumerateDirectories(directory).Take(256))
                        if (Path.GetFileName(child) is not (".gmmt-original" or ".git" or "node_modules") && !Path.GetFileName(child).StartsWith(".gmmt-", StringComparison.Ordinal)) queue.Enqueue((child, depth + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(directory + ": " + ex.Message); }
        }
        if (queue.Count != 0) warnings.Add("Directory scan limit reached: " + game);
        return found;
    }

    private static bool LooksLikeGameMaker(string path)
    {
        using var stream = File.OpenRead(path);
        // Bounded read, not execution: avoid mistaking adjacent xdelta/installer helpers for runners.
        var buffer = new byte[65536 + 32];
        int overlap = 0;
        bool engine = false, archive = false;
        long scanned = 0;
        while (scanned < 64 * 1024 * 1024)
        {
            int read = stream.Read(buffer, overlap, 65536);
            if (read == 0) break;
            scanned += read;
            var bytes = buffer.AsSpan(0, overlap + read);
            engine |= bytes.IndexOf("YoYo Games"u8) >= 0 || bytes.IndexOf("GameMaker"u8) >= 0 || bytes.IndexOf("YYRunner"u8) >= 0;
            archive |= bytes.IndexOf("game.unx"u8) >= 0 || bytes.IndexOf("instance_create"u8) >= 0;
            if (engine && archive) return true;
            overlap = Math.Min(32, bytes.Length);
            bytes[^overlap..].CopyTo(buffer);
        }
        return false;
    }

    private static bool HasInterpreter(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[64];
        if (stream.Read(header) != 64 || header[5] != 1) return false;
        bool elf64 = header[4] == 2;
        if (!elf64 && header[4] != 1) return false;
        ulong offset = elf64 ? BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32)) : BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28));
        int size = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(elf64 ? 54 : 42));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(elf64 ? 56 : 44));
        if (size < 4 || count > 1024 || offset > (ulong)stream.Length || (ulong)(size * count) > (ulong)stream.Length - offset) return false;
        var type = new byte[4];
        for (int i = 0; i < count; i++)
        {
            stream.Position = (long)offset + i * size;
            if (stream.Read(type) == 4 && BinaryPrimitives.ReadUInt32LittleEndian(type) == 3) return true;
        }
        return false;
    }

    private static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var segment in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget != null) current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Broken Steam library link: " + current);
        }
        return current;
    }
}
