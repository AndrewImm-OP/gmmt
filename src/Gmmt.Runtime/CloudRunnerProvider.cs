using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Gmmt.Runtime;

public sealed record CloudRunnerManifestEntry(string Id, string EngineVersion, byte BytecodeVersion,
    bool IsGms2, int Bits, string RelativeRunnerPath, string Description);

/// <summary>Pinned optional cache. Never certifies a runner using the target mod as a reference.</summary>
public static class CloudRunnerProvider
{
    public const string CloudRunnersUrl = "https://github.com/AndrewImm-OP/gmmt/releases/download/v0.2.4/gmmt-runners.zip";
    public const string BundleSha256 = "0245afb8bc62ff4d9689ef872f3f1416323ef45449315e12cc24ddfd2e4fa519";
    public static string BuiltinDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners", "builtin-" + BundleSha256[..12]);

    public static async Task<List<DiscoveredRunner>> EnsureRunnersAsync(string? steamRuntimeScript = null,
        CancellationToken cancellation = default, string? cacheDirectory = null)
    {
        var target = cacheDirectory ?? BuiltinDirectory;
        Directory.CreateDirectory(target);
        var bundle = Path.Combine(target, "bundle.zip");
        if (!File.Exists(bundle) || RuntimeCatalog.Hash(bundle) != BundleSha256)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.GetAsync(CloudRunnersUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var temporary = bundle + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                await using (var destination = File.Create(temporary))
                {
                    var buffer = new byte[65536]; long total = 0; int read;
                    while ((read = await source.ReadAsync(buffer, cancellation)) != 0)
                    {
                        total += read;
                        if (total > 32 * 1024 * 1024) throw new InvalidDataException("Runner bundle exceeds size limit.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    }
                }
                if (RuntimeCatalog.Hash(temporary) != BundleSha256) throw new InvalidDataException("Runner bundle checksum mismatch.");
                File.Move(temporary, bundle, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return ReadBundle(bundle, target, steamRuntimeScript);
    }

    public static List<DiscoveredRunner> ReadBundle(string bundle, string target, string? runtime = null)
    {
        if (RuntimeCatalog.Hash(bundle) != BundleSha256) throw new InvalidDataException("Runner bundle checksum mismatch.");
        using var zip = ZipFile.OpenRead(bundle);
        var manifest = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing runner manifest.");
        if (manifest.Length > 65536) throw new InvalidDataException("Runner manifest exceeds size limit.");
        using var stream = manifest.Open();
        var entries = JsonSerializer.Deserialize<List<CloudRunnerManifestEntry>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty manifest.");
        Directory.CreateDirectory(target);
        var results = new List<DiscoveredRunner>();
        foreach (var entry in entries.Take(16))
        {
            var runnerEntry = zip.GetEntry(entry.RelativeRunnerPath) ?? throw new InvalidDataException("Missing runner.");
            var path = ZipInput.Extract(runnerEntry, target);
            var elf = RuntimeCatalog.InspectElf(path);
            if (elf.Bits != entry.Bits || RunnerElfInspector.DetectFamily(path) != entry.IsGms2)
                throw new InvalidDataException("Runner manifest does not match binary.");
            results.Add(new(entry.Description, path, null, elf, runtime, entry.BytecodeVersion, entry.IsGms2, entry.EngineVersion));
        }
        return results;
    }
}
