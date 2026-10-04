using System.Text.Json;

namespace Gmmt.Runtime;

/// <summary>Reversible installation into the native Linux runner/assets/run.sh layout.</summary>
public static class SteamInstaller
{
    private const string BackupName = ".gmmt-original";
    private static readonly string[] Entries = ["runner", "assets", "lib", "run.sh", "launch.sh", "gmmt-package.json"];
    private sealed record Journal(string Phase, string[] Originals, Dictionary<string, string> OriginalHashes,
        Dictionary<string, string> InstalledHashes);

    public static string Install(string package, string gameDirectory)
    {
        if (!OperatingSystem.IsLinux()) throw new IOException("Steam replacement currently supports native Linux games only.");
        package = Path.GetFullPath(package);
        var game = ValidateDirectory(gameDirectory);
        if (Inside(package, game) || Inside(game, package)) throw new IOException("Package and game directory must be separate.");
        using var operation = Lock(game);
        var backup = Path.Combine(game, BackupName);
        if (Directory.Exists(backup) || File.Exists(backup))
            throw new IOException("An original backup already exists. Restore it before installing another mod.");
        foreach (var entry in Entries) CheckTree(Path.Combine(game, entry));
        RuntimeCatalog.InspectElf(Path.Combine(game, "runner"));
        if (!File.Exists(Path.Combine(game, "assets/game.unx")) || !File.Exists(Path.Combine(game, "run.sh")))
            throw new IOException("Select a native Linux game folder containing runner, assets/game.unx and run.sh. Proton layouts are unsupported.");
        CheckTree(package);
        RuntimeCatalog.InspectElf(Path.Combine(package, "runner"));
        if (!File.Exists(Path.Combine(package, "assets/game.unx"))) throw new IOException("Package archive is missing.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "gmmt-package.json")));
        if (manifest.RootElement.GetProperty("Strategy").GetString() != "NativeRunner")
            throw new IOException("Only native-runner packages can be installed.");
        var expected = new Dictionary<string, string>();
        foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(package, file.GetProperty("Path").GetString()!));
            if (!Inside(path, package) || RuntimeCatalog.Hash(path) != file.GetProperty("Sha256").GetString())
                throw new IOException("Package files changed after assembly.");
            expected.Add(Path.GetRelativePath(package, path), file.GetProperty("Sha256").GetString()!);
        }
        var actual = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories)
            .Where(p => p != Path.Combine(package, "gmmt-package.json"))
            .ToDictionary(p => Path.GetRelativePath(package, p), RuntimeCatalog.Hash);
        if (!Equal(expected, actual) || !new[] { "runner", "assets/game.unx", "launch.sh" }.All(expected.ContainsKey))
            throw new IOException("Package contents do not match its manifest.");
        var staging = Path.Combine(game, ".gmmt-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool journalCreated = false;
        try
        {
            foreach (var name in Entries.Where(n => n != "run.sh"))
            {
                var source = Path.Combine(package, name);
                if (Exists(source)) Copy(source, Path.Combine(staging, name));
            }
            foreach (var entry in expected)
                if (!File.Exists(Path.Combine(staging, entry.Key)) || RuntimeCatalog.Hash(Path.Combine(staging, entry.Key)) != entry.Value)
                    throw new IOException("Package changed while preparing installation.");
            File.WriteAllText(Path.Combine(staging, "run.sh"), "#!/bin/sh\nset -eu\ncd -- \"$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\"\nexec sh ./launch.sh \"$@\"\n");
            File.SetUnixFileMode(Path.Combine(staging, "run.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            var originals = Entries.Where(n => Exists(Path.Combine(game, n))).ToArray();
            var journal = new Journal("Installing", originals, Hashes(game, originals), Hashes(staging, Entries));
            Directory.CreateDirectory(backup);
            Save(backup, journal); journalCreated = true;
            // All content is prepared and verified before moving the first installed file.
            foreach (var name in originals) Move(Path.Combine(game, name), Path.Combine(backup, name));
            foreach (var name in Entries)
                if (Exists(Path.Combine(staging, name))) Move(Path.Combine(staging, name), Path.Combine(game, name));
            Save(backup, journal with { Phase = "Installed" });
            return backup;
        }
        catch
        {
            if (journalCreated) Recover(game, Load(backup), false);
            else if (Directory.Exists(backup)) Directory.Delete(backup, true);
            throw;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static void Restore(string gameDirectory)
    {
        var game = ValidateDirectory(gameDirectory);
        using var operation = Lock(game);
        var backup = Path.Combine(game, BackupName);
        CheckTree(backup);
        var journal = Load(backup);
        ValidateJournal(journal);
        if (journal.Phase == "Installed")
        {
            foreach (var name in Entries) CheckTree(Path.Combine(game, name));
            if (!Equal(Hashes(game, Entries), journal.InstalledHashes))
                throw new IOException("Installed files changed after GMMT installation. Backup was preserved; resolve the changes before restoring.");
            if (!Equal(Hashes(backup, journal.Originals), journal.OriginalHashes))
                throw new IOException("Original backup was changed. Restoration stopped without modifying the game.");
        }
        Recover(game, journal, true);
    }

    private static void Recover(string game, Journal journal, bool validate)
    {
        ValidateJournal(journal);
        var backup = Path.Combine(game, BackupName);
        foreach (var name in Entries) CheckTree(Path.Combine(game, name));
        // This is also resumable after interruption while Installing/Restoring.
        // Missing backup entries are originals not moved yet or already restored.
        foreach (var name in journal.Originals)
        {
            var saved = Path.Combine(backup, name);
            var current = Path.Combine(game, name);
            if (!Exists(saved))
            {
                if (!Equal(Hashes(game, [name]), journal.OriginalHashes.Where(p => p.Key == name || p.Key.StartsWith(name + "/", StringComparison.Ordinal)).ToDictionary()))
                    throw new IOException("Recovery is missing an intact original: " + name);
            }
            else
            {
                CheckTree(saved); CheckTree(current);
                if (validate && !Equal(Hashes(backup, [name]), journal.OriginalHashes.Where(p => p.Key == name || p.Key.StartsWith(name + "/", StringComparison.Ordinal)).ToDictionary()))
                    throw new IOException("Backup changed: " + name);
            }
        }
        Save(backup, journal with { Phase = "Restoring" });
        foreach (var name in Entries)
        {
            var saved = Path.Combine(backup, name);
            var current = Path.Combine(game, name);
            if (journal.Originals.Contains(name) && !Exists(saved)) continue;
            if (Exists(current)) Delete(current);
            if (Exists(saved)) Move(saved, current);
        }
        Directory.Delete(backup, true);
    }

    private static void ValidateJournal(Journal journal)
    {
        if (journal.Phase is not ("Installed" or "Installing" or "Restoring") || journal.Originals.Distinct().Count() != journal.Originals.Length
            || journal.Originals.Any(n => !Entries.Contains(n))) throw new InvalidDataException("Invalid installation journal.");
    }
    private static string ValidateDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux()) throw new IOException("Steam replacement currently supports native Linux games only.");
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Choose the installed Linux game directory.");
        var game = Path.GetFullPath(directory);
        if (!Directory.Exists(game)) throw new DirectoryNotFoundException(game);
        for (var parent = new DirectoryInfo(game); parent != null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlinked installation paths are unsupported.");
        return game;
    }
    private static FileStream Lock(string game) => new(Path.Combine(game, ".gmmt-operation.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static bool Inside(string path, string directory) => path == directory || path.StartsWith(directory.TrimEnd('/') + "/", StringComparison.Ordinal);
    private static void Move(string source, string destination) { if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination); }
    private static void Delete(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
    private static void CheckTree(string path)
    {
        if (!Exists(path))
        {
            if (new FileInfo(path).LinkTarget != null) throw new IOException("Symlinked files are unsupported: " + path);
            return;
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlinked files are unsupported: " + path);
        if (Directory.Exists(path)) foreach (var child in Directory.EnumerateFileSystemEntries(path)) CheckTree(child);
    }
    private static void Copy(string source, string target)
    {
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(target);
            foreach (var child in Directory.EnumerateFileSystemEntries(source)) Copy(child, Path.Combine(target, Path.GetFileName(child)));
        }
        else { File.Copy(source, target); if (OperatingSystem.IsLinux()) File.SetUnixFileMode(target, File.GetUnixFileMode(source)); }
    }
    private static Dictionary<string, string> Hashes(string root, IEnumerable<string> names)
    {
        var hashes = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path)) hashes[name] = RuntimeCatalog.Hash(path);
            else if (Directory.Exists(path))
            {
                hashes[name + "/"] = "directory";
                foreach (var folder in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)) hashes[Path.GetRelativePath(root, folder) + "/"] = "directory";
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) hashes[Path.GetRelativePath(root, file)] = RuntimeCatalog.Hash(file);
            }
        }
        return hashes;
    }
    private static bool Equal(Dictionary<string, string> left, Dictionary<string, string> right) => left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var hash) && hash == p.Value);
    private static Journal Load(string backup) => JsonSerializer.Deserialize<Journal>(File.ReadAllText(Path.Combine(backup, "install.json"))) ?? throw new InvalidDataException("Installation journal is missing.");
    private static void Save(string backup, Journal journal)
    {
        var path = Path.Combine(backup, "install.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(journal, RuntimeCatalog.JsonOptions));
        File.Move(path + ".tmp", path, true);
    }
}
