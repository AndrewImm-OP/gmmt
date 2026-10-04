using System.Diagnostics;

namespace Gmmt.Runtime;

public sealed class ArchiveInput : IDisposable
{
    public string Path { get; }
    public Dictionary<string, string> InputHashes { get; }
    private readonly bool temporary;
    private ArchiveInput(string path, bool temporary, Dictionary<string, string> hashes)
    { Path = path; this.temporary = temporary; InputHashes = hashes; }

    public static async Task<ArchiveInput> PrepareAsync(string input, string? vanilla = null, bool patch = false)
    {
        input = System.IO.Path.GetFullPath(input);
        var hashes = new Dictionary<string, string> { [input] = RuntimeCatalog.Hash(input) };
        if (!patch) return new(input, false, hashes);
        if (string.IsNullOrWhiteSpace(vanilla)) throw new ArgumentException("A clean Windows archive is required for an xdelta patch.");
        vanilla = System.IO.Path.GetFullPath(vanilla);
        hashes[vanilla] = RuntimeCatalog.Hash(vanilla);
        var output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gmmt-" + Guid.NewGuid().ToString("N") + ".win");
        try
        {
            var start = new ProcessStartInfo("xdelta3") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-d", "-s", vanilla, input, output }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start xdelta3");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new IOException("xdelta3 timed out"); }
            await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException($"xdelta3 failed ({process.ExitCode}): {error}");
            if (RuntimeCatalog.Hash(input) != hashes[input] || RuntimeCatalog.Hash(vanilla) != hashes[vanilla])
                throw new IOException("Patch or vanilla archive changed while reconstructing data.");
            return new(output, true, hashes);
        }
        catch { if (File.Exists(output)) File.Delete(output); throw; }
    }

    public void Dispose() { if (temporary && File.Exists(Path)) File.Delete(Path); }
}
