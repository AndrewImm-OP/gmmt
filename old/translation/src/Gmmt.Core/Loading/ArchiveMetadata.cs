namespace Gmmt.Core.Loading;

/// <summary>
/// Lightweight summary of a loaded GameMaker archive.
/// Extracted once during load, used for compatibility checks and reporting.
/// </summary>
public sealed record ArchiveMetadata
{
    public required string FilePath { get; init; }
    public required string GameName { get; init; }
    public required string? FileName { get; init; }
    public required byte BytecodeVersion { get; init; }
    public required uint Major { get; init; }
    public required uint Minor { get; init; }
    public required uint Release { get; init; }
    public required uint Build { get; init; }
    public required uint GameId { get; init; }
    public required bool IsYYC { get; init; }
    public required bool IsGMS2 { get; init; }

    public required int SpriteCount { get; init; }
    public required int SoundCount { get; init; }
    public required int CodeCount { get; init; }
    public required int ObjectCount { get; init; }
    public required int RoomCount { get; init; }
    public required int ScriptCount { get; init; }
    public required int StringCount { get; init; }
    public required int TextureCount { get; init; }
    public required int AudioCount { get; init; }
    public required int FontCount { get; init; }
    public required int ShaderCount { get; init; }
    public required int ExtensionCount { get; init; }
    public required int BackgroundCount { get; init; }
    public required int PathCount { get; init; }
    public required int TimelineCount { get; init; }
    public required int FunctionCount { get; init; }
    public required int VariableCount { get; init; }
    public required int TpagCount { get; init; }

    public string VersionString => $"{Major}.{Minor}.{Release}.{Build}";
}
