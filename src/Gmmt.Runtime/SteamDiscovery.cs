using System.Text.RegularExpressions;
using System.Buffers.Binary;

namespace Gmmt.Runtime;

public sealed record DiscoveredRunner(string Game, string RunnerPath, string ArchivePath, ElfInfo Elf, string? SteamRuntimeScript)
{
    public override string ToString() => $"{Game} · ELF{Elf.Bits} · {Path.GetFileName(ArchivePath)}";
}
public sealed record DiscoveredGame(string Name, string Directory)
{
    public override string ToString() => Name;
}
public sealed record SteamDiscoveryResult(string[] Libraries, DiscoveredGame[] Games, DiscoveredRunner[] Runners, string[] Warnings);

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
                        games.Add(new(Path.GetFileName(entry), game));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(common + ": " + ex.Message); }
        }
        var extra = runnerRoots ?? new[] { Path.Combine(home, "Downloads"), Path.Combine(home, "Загрузки") };
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
        return new(libraries.Order().ToArray(), games.OrderBy(g => g.Name).ToArray(),
            runners.DistinctBy(r => (r.RunnerPath, r.ArchivePath)).OrderBy(r => r.Game).ThenBy(r => r.ArchivePath).ToArray(), warnings.ToArray());
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
