namespace Gmmt.Runtime;

/// <summary>Family hints only. Binary names do not establish engine/bytecode versions.</summary>
public static class RunnerElfInspector
{
    public static bool? DetectFamily(string path)
    {
        _ = RuntimeCatalog.InspectElf(path);
        using var stream = File.OpenRead(path);
        var buffer = new byte[65536 + 32];
        int overlap = 0;
        bool green = false, red = false;
        long scanned = 0;
        while (scanned < 64L * 1024 * 1024)
        {
            int read = stream.Read(buffer, overlap, 65536);
            if (read == 0) break;
            scanned += read;
            var bytes = buffer.AsSpan(0, overlap + read);
            green |= bytes.IndexOf("GMGreen"u8) >= 0 || bytes.IndexOf("GameMaker Studio 2"u8) >= 0;
            red |= bytes.IndexOf("GMRed"u8) >= 0;
            overlap = Math.Min(32, bytes.Length);
            bytes[^overlap..].CopyTo(buffer);
        }
        return green == red ? null : green;
    }
}
