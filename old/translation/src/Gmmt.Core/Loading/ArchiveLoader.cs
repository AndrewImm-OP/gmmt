using UndertaleModLib;

namespace Gmmt.Core.Loading;

/// <summary>
/// Loads a GameMaker data archive (.win / .unx / .ios / .droid) via UndertaleModLib
/// and extracts metadata. The same code path handles all platform archives.
/// </summary>
public static class ArchiveLoader
{
    public sealed record LoadResult(UndertaleData Data, ArchiveMetadata Metadata);

    /// <summary>
    /// Load and parse a GameMaker archive from disk.
    /// </summary>
    /// <param name="path">Path to the archive file.</param>
    /// <param name="log">Optional log callback for progress/warning messages.</param>
    /// <returns>Parsed data and extracted metadata.</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    /// <exception cref="InvalidDataException">If the file cannot be parsed.</exception>
    public static LoadResult Load(string path, Action<string>? log = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archive not found: {path}", path);

        log?.Invoke($"Loading {path}...");

        UndertaleData data;
        var warnings = new List<string>();

        using (var stream = File.OpenRead(path))
        {
            try
            {
                data = UndertaleIO.Read(
                    stream,
                    warningHandler: (msg, _) => warnings.Add(msg),
                    messageHandler: msg => log?.Invoke($"  [utmt] {msg}"));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Failed to parse archive '{path}': {ex.Message}", ex);
            }
        }

        foreach (var w in warnings)
            log?.Invoke($"  [WARN] {w}");

        var metadata = ExtractMetadata(data, path);

        log?.Invoke($"  Game: {metadata.GameName}");
        log?.Invoke($"  Version: {metadata.VersionString} (bytecode {metadata.BytecodeVersion})");
        log?.Invoke($"  YYC: {metadata.IsYYC}, GMS2: {metadata.IsGMS2}");
        log?.Invoke($"  Code: {metadata.CodeCount}, Objects: {metadata.ObjectCount}, " +
                    $"Rooms: {metadata.RoomCount}, Sprites: {metadata.SpriteCount}, " +
                    $"Strings: {metadata.StringCount}");

        return new LoadResult(data, metadata);
    }

    private static ArchiveMetadata ExtractMetadata(UndertaleData data, string path)
    {
        var gen = data.GeneralInfo;
        if (gen is null)
            throw new InvalidDataException($"Archive '{path}' has no GEN8 chunk (GeneralInfo is null)");

        return new ArchiveMetadata
        {
            FilePath = Path.GetFullPath(path),
            GameName = gen.Name?.Content ?? "<unknown>",
            FileName = gen.FileName?.Content,
            BytecodeVersion = gen.BytecodeVersion,
            Major = gen.Major,
            Minor = gen.Minor,
            Release = gen.Release,
            Build = gen.Build,
            GameId = gen.GameID,
            IsYYC = data.IsYYC(),
            IsGMS2 = data.IsGameMaker2(),

            SpriteCount = data.Sprites?.Count ?? 0,
            SoundCount = data.Sounds?.Count ?? 0,
            CodeCount = data.Code?.Count ?? 0,
            ObjectCount = data.GameObjects?.Count ?? 0,
            RoomCount = data.Rooms?.Count ?? 0,
            ScriptCount = data.Scripts?.Count ?? 0,
            StringCount = data.Strings?.Count ?? 0,
            TextureCount = data.EmbeddedTextures?.Count ?? 0,
            AudioCount = data.EmbeddedAudio?.Count ?? 0,
            FontCount = data.Fonts?.Count ?? 0,
            ShaderCount = data.Shaders?.Count ?? 0,
            ExtensionCount = data.Extensions?.Count ?? 0,
            BackgroundCount = data.Backgrounds?.Count ?? 0,
            PathCount = data.Paths?.Count ?? 0,
            TimelineCount = data.Timelines?.Count ?? 0,
            FunctionCount = data.Functions?.Count ?? 0,
            VariableCount = data.Variables?.Count ?? 0,
            TpagCount = data.TexturePageItems?.Count ?? 0,
        };
    }
}
