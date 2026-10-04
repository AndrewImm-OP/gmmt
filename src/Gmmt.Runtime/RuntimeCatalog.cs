using System.Security.Cryptography;
using System.Text.Json;
using Gmmt.Core.Loading;

namespace Gmmt.Runtime;

public sealed record ElfInfo(int Bits, ushort Machine);
public sealed record ArchiveInspection(ArchiveMetadata Metadata, string Sha256, string[] NativeExtensions);
public sealed record RunnerProfile(string Id, string RunnerPath, string RunnerSha256, ElfInfo Elf,
    string EngineVersion, byte BytecodeVersion, string ReferenceArchiveSha256, string? SteamRuntimeScript);
public sealed record PackagePlan(ArchiveInspection Archive, RunnerProfile? Runner, string Evidence,
    string[] Blockers, string[] Warnings)
{
    public bool CanPackage => Runner != null && Blockers.Length == 0;
}

public static class ArchiveInspector
{
    public static ArchiveInspection Inspect(string path)
    {
        var loaded = ArchiveLoader.Load(path);
        using var data = loaded.Data;
        var extensions = data.Extensions.SelectMany(e => e.Files)
            .Select(f => f.Filename?.Content ?? "")
            .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        return new(loaded.Metadata, RuntimeCatalog.Hash(path), extensions);
    }
}

public sealed class RuntimeCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Path { get; }
    public Func<string, ArchiveInspection> Inspector { get; init; } = ArchiveInspector.Inspect;
    public RuntimeCatalog(string path, Func<string, ArchiveInspection>? inspector = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        if (inspector != null) Inspector = inspector;
    }
    public List<RunnerProfile> Read() => File.Exists(Path)
        ? JsonSerializer.Deserialize<List<RunnerProfile>>(File.ReadAllText(Path))
            ?? throw new InvalidDataException("Empty runner catalog") : [];

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static ElfInfo InspectElf(string path)
    {
        using var file = File.OpenRead(path);
        Span<byte> header = stackalloc byte[20];
        file.ReadExactly(header);
        if (!header[..4].SequenceEqual(new byte[] { 0x7f, 69, 76, 70 }) || header[5] != 1
            || header[6] != 1 || header[4] is not (1 or 2))
            throw new InvalidDataException("Runner must be a little-endian Linux ELF executable, not a Windows EXE.");
        ushort machine = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[18..]);
        ushort type = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[16..]);
        if (type is not (2 or 3) || machine is not (3 or 62 or 183 or 40))
            throw new InvalidDataException("Unsupported ELF executable architecture/type.");
        if (file.Length < (header[4] == 1 ? 52 : 64))
            throw new InvalidDataException("Truncated ELF header.");
        return new(header[4] == 1 ? 32 : 64, machine);
    }

    public RunnerProfile Register(string id, string runner, string reference, string? runtimeScript = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Runner id is required");
        runner = System.IO.Path.GetFullPath(runner);
        var elf = InspectElf(runner);
        var archive = Inspector(reference);
        if (archive.Metadata.IsYYC) throw new InvalidDataException("YYC archives are unsupported.");
        if (runtimeScript != null)
        {
            runtimeScript = System.IO.Path.GetFullPath(runtimeScript);
            if (!File.Exists(runtimeScript)) throw new FileNotFoundException("Steam runtime script not found", runtimeScript);
        }
        var profile = new RunnerProfile(id, runner, Hash(runner), elf, archive.Metadata.VersionString,
            archive.Metadata.BytecodeVersion, archive.Sha256, runtimeScript);
        var profiles = Read();
        if (profiles.Any(p => p.Id == id)) throw new InvalidOperationException($"Runner id '{id}' already exists. Use a new id.");
        profiles.Add(profile);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profiles, JsonOptions));
            File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return profile;
    }

    public PackagePlan Plan(ArchiveInspection archive, string? runnerId = null, IEnumerable<DiscoveredRunner>? fallbackCandidates = null)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (archive.Metadata.IsYYC) blockers.Add("YYC game data cannot be packaged with an interchangeable VM runner.");
        var profiles = Read();
        var matches = profiles.Where(p => p.EngineVersion == archive.Metadata.VersionString
            && p.BytecodeVersion == archive.Metadata.BytecodeVersion).ToList();
        if (runnerId != null)
        {
            var requested = profiles.SingleOrDefault(p => p.Id == runnerId);
            if (requested == null) blockers.Add($"Runner '{runnerId}' is not registered.");
            else if (!matches.Contains(requested)) blockers.Add("Selected runner reference has different engine/bytecode metadata.");
            matches = requested != null && matches.Contains(requested) ? [requested] : [];
        }
        else
        {
            var exact = matches.Where(p => p.ReferenceArchiveSha256 == archive.Sha256).ToList();
            if (exact.Count != 0) matches = exact;
        }

        if (matches.Count == 0 && blockers.Count == 0 && fallbackCandidates != null && runnerId == null)
        {
            DiscoveredRunner? bestCandidate = null;
            bool exactVersion = false;
            foreach (var candidate in fallbackCandidates)
            {
                try
                {
                    if (!File.Exists(candidate.RunnerPath) || !File.Exists(candidate.ArchivePath)) continue;
                    var candArchive = Inspector(candidate.ArchivePath);
                    if (candArchive.Metadata.BytecodeVersion == archive.Metadata.BytecodeVersion &&
                        candArchive.Metadata.IsGMS2 == archive.Metadata.IsGMS2)
                    {
                        if (candArchive.Metadata.VersionString == archive.Metadata.VersionString)
                        {
                            bestCandidate = candidate;
                            exactVersion = true;
                            break;
                        }
                        bestCandidate ??= candidate;
                    }
                }
                catch { }
            }

            if (bestCandidate != null)
            {
                var sanitizedName = string.Concat(bestCandidate.Game.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(sanitizedName)) sanitizedName = "candidate";
                var autoId = $"auto-{sanitizedName}";
                var existing = profiles.FirstOrDefault(p => p.Id == autoId);
                if (existing == null)
                {
                    try
                    {
                        var autoProfile = Register(autoId, bestCandidate.RunnerPath, bestCandidate.ArchivePath, bestCandidate.SteamRuntimeScript);
                        profiles.Add(autoProfile);
                        matches.Add(autoProfile);
                        warnings.Add($"Auto-registered matching runner '{autoId}' from {bestCandidate.RunnerPath}.");
                        if (!exactVersion)
                        {
                            warnings.Add($"Runner matched by bytecode version (BC{archive.Metadata.BytecodeVersion}), but engine version differs.");
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Failed to auto-register runner '{autoId}': {ex.Message}");
                    }
                }
                else
                {
                    matches.Add(existing);
                    warnings.Add($"Selected auto-registered runner '{autoId}'.");
                }
            }
        }

        bool inferredRunner = false;
        if (matches.Count == 0 && blockers.Count == 0 && runnerId == null && fallbackCandidates != null)
        {
            foreach (var candidate in fallbackCandidates.Where(c => c.ArchivePath == null))
            {
                // Markers only identify a family. Only attempt the historical BC16 VM family;
                // newer bytecode requires real reference data or an explicit manifest entry.
                bool historicalVm = archive.Metadata.BytecodeVersion == 16 &&
                    (archive.Metadata.Major == 1 || (archive.Metadata.Major == 2 && archive.Metadata.Minor < 3));
                if ((candidate.EngineVersion != null && candidate.EngineVersion != archive.Metadata.VersionString) ||
                    candidate.IsGMS2 != archive.Metadata.IsGMS2 ||
                    (candidate.BytecodeVersion.HasValue ? candidate.BytecodeVersion != archive.Metadata.BytecodeVersion : !historicalVm)) continue;
                try
                {
                    var elf = InspectElf(candidate.RunnerPath);
                    var hash = Hash(candidate.RunnerPath);
                    matches.Add(new RunnerProfile("candidate-" + hash[..12], candidate.RunnerPath, hash, elf,
                        archive.Metadata.VersionString, archive.Metadata.BytecodeVersion, "", candidate.SteamRuntimeScript));
                    inferredRunner = true;
                    warnings.Add("Runner selected as an unverified VM candidate. No known-compatible reference archive is available; launch and gameplay testing are required.");
                    break;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                { warnings.Add("Runner candidate unavailable: " + ex.Message); }
            }
        }

        RunnerProfile? selected = matches.Count == 1 ? matches[0] : null;
        if (matches.Count == 0 && blockers.Count == 0) blockers.Add("No matching local Linux runner. Register a runner together with its known-compatible reference archive.");
        if (matches.Count > 1) blockers.Add("Multiple matching runners. Select one explicitly with --runner-id.");
        if (selected != null)
        {
            try
            {
                if (Hash(selected.RunnerPath) != selected.RunnerSha256 || InspectElf(selected.RunnerPath) != selected.Elf)
                    blockers.Add("Registered runner changed since registration. Register the new binary with a new id.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { blockers.Add($"Runner is unavailable: {ex.Message}"); }
            var host = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            bool architectureMatches = host switch
            {
                System.Runtime.InteropServices.Architecture.X64 => selected.Elf.Machine is 3 or 62,
                System.Runtime.InteropServices.Architecture.X86 => selected.Elf.Machine == 3,
                System.Runtime.InteropServices.Architecture.Arm64 => selected.Elf.Machine == 183,
                System.Runtime.InteropServices.Architecture.Arm => selected.Elf.Machine == 40,
                _ => false
            };
            if (!architectureMatches) blockers.Add("Runner architecture does not match this host.");
            if (selected.Elf.Bits == 32) warnings.Add("32-bit runner requires compatible 32-bit system libraries.");
            if (selected.SteamRuntimeScript != null && !File.Exists(selected.SteamRuntimeScript))
                blockers.Add("Registered Steam runtime script is no longer available.");
        }
        if (archive.NativeExtensions.Length > 0)
            warnings.Add("Native extensions require manual Linux dependency review: " + string.Join(", ", archive.NativeExtensions));
        warnings.Add("Metadata matching is a candidate selection, not a whole-game compatibility guarantee.");
        var evidence = selected == null ? "NoRunner" : inferredRunner ? "UnverifiedRunnerCandidate" : selected.ReferenceArchiveSha256 == archive.Sha256
            ? "SameArchiveAsReference" : "MatchingMetadataOnly";
        return new(archive, selected, evidence, blockers.ToArray(), warnings.ToArray());
    }
}
