using Gmmt.VanillaLibrary;

namespace Gmmt.Desktop;

/// <summary>
/// View model wrapper for VanillaEntry for DataGrid display.
/// </summary>
public sealed class VanillaEntryViewModel
{
    private readonly VanillaEntry _entry;

    public VanillaEntryViewModel(VanillaEntry entry)
    {
        _entry = entry;
    }

    public string Hash => _entry.Hash;
    public string ShortHash => _entry.ShortHash;
    public string? GameName => _entry.GameName;
    public string? VersionString => _entry.VersionString;
    public string? Platform => _entry.Platform;
    public string[]? Tags => _entry.Tags;

    public string FileSizeDisplay => _entry.FileSize switch
    {
        >= 1024 * 1024 => $"{_entry.FileSize / (1024 * 1024):N0} MB",
        >= 1024 => $"{_entry.FileSize / 1024:N0} KB",
        _ => $"{_entry.FileSize:N0} B"
    };

    public string AddedDisplay => _entry.AddedUtc.ToString("yyyy-MM-dd");
}
