using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gmmt.Desktop.Services;
using Gmmt.Pipeline;
using Gmmt.VanillaLibrary;

namespace Gmmt.Desktop;

public partial class MainWindowViewModel : ObservableObject
{
    // ── Translate tab fields ──────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string _xDeltaPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(ValidationError))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string _targetPath = "";

    [ObservableProperty]
    private string _vanillaPath = "";

    [ObservableProperty]
    private string _outputPath = "";

    [ObservableProperty]
    private bool _dryRun;

    [ObservableProperty]
    private bool _backup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(RunButtonText))]
    private bool _isRunning;

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string _errorMessage = "";

    // ── Result detail fields ──────────────────────────────
    [ObservableProperty]
    private string _appliedCount = "0";

    [ObservableProperty]
    private string _skippedCount = "0";

    [ObservableProperty]
    private string _conflictCount = "0";

    [ObservableProperty]
    private string _errorCount = "0";

    [ObservableProperty]
    private IBrush _resultBadgeBackground = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _resultBadgeForeground = Brushes.White;

    [ObservableProperty]
    private string _resultBadgeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputPath))]
    private string _outputPathDisplay = "";

    // ── Vanilla Library tab fields ────────────────────────
    [ObservableProperty]
    private string _libraryPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveVanilla))]
    private VanillaEntryViewModel? _selectedVanillaEntry;

    public ObservableCollection<VanillaEntryViewModel> VanillaEntries { get; } = [];

    // ── Computed properties ───────────────────────────────
    public bool CanRun => !IsRunning
        && !string.IsNullOrWhiteSpace(XDeltaPath)
        && !string.IsNullOrWhiteSpace(TargetPath);

    public bool HasResult => !string.IsNullOrEmpty(ResultSummary);

    public string RunButtonText => IsRunning ? "Running..." : "Run Translation";

    public bool CanRemoveVanilla => SelectedVanillaEntry is not null;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasOutputPath => !string.IsNullOrEmpty(OutputPathDisplay);

    public string ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(XDeltaPath) && string.IsNullOrWhiteSpace(TargetPath))
                return "";
            if (string.IsNullOrWhiteSpace(XDeltaPath))
                return "XDelta patch file is required.";
            if (string.IsNullOrWhiteSpace(TargetPath))
                return "Target archive is required.";
            return "";
        }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    // ── Log buffer + auto-scroll event ────────────────────
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();

    public event Action? LogUpdated;

    // ── Service ───────────────────────────────────────────
    private readonly TranslationExecutor _executor;

    // Brushes for result badge
    private static readonly IBrush SuccessBg = SolidColorBrush.Parse("#3340C057");
    private static readonly IBrush SuccessFg = SolidColorBrush.Parse("#88EE88");
    private static readonly IBrush FailBg = SolidColorBrush.Parse("#33E05252");
    private static readonly IBrush FailFg = SolidColorBrush.Parse("#FF8888");

    public MainWindowViewModel()
    {
        _executor = new TranslationExecutor(AppendLog);
        RefreshLibrary();
    }

    // ══════════════════════════════════════════════════════
    // Browse commands
    // ══════════════════════════════════════════════════════

    [RelayCommand]
    private async Task BrowseXDeltaAsync()
    {
        var path = await PickFileAsync("Select XDelta Patch", [
            new FilePickerFileType("XDelta Patches") { Patterns = ["*.xdelta", "*.vcdiff", "*.xd3"] },
            FilePickerFileTypes.All
        ]);
        if (path is not null) XDeltaPath = path;
    }

    [RelayCommand]
    private async Task BrowseTargetAsync()
    {
        var path = await PickFileAsync("Select Target Archive", [
            new FilePickerFileType("GameMaker Archives") { Patterns = ["*.unx", "*.win", "*.ios", "*.droid"] },
            FilePickerFileTypes.All
        ]);
        if (path is not null) TargetPath = path;
    }

    [RelayCommand]
    private async Task BrowseVanillaAsync()
    {
        var path = await PickFileAsync("Select Vanilla Archive", [
            new FilePickerFileType("GameMaker Archives") { Patterns = ["*.win", "*.unx"] },
            FilePickerFileTypes.All
        ]);
        if (path is not null) VanillaPath = path;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var window = GetMainWindow();
        if (window is null) return;

        var result = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Output Archive",
            DefaultExtension = "unx",
            FileTypeChoices = [
                new FilePickerFileType("GameMaker Archives") { Patterns = ["*.unx", "*.win"] },
                FilePickerFileTypes.All
            ]
        });

        if (result?.TryGetLocalPath() is { } path)
            OutputPath = path;
    }

    // ══════════════════════════════════════════════════════
    // Run Translation
    // ══════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RunTranslateAsync()
    {
        if (IsRunning) return;

        var validationErr = TranslationExecutor.ValidateInputs(XDeltaPath, TargetPath);
        if (validationErr is not null)
        {
            ErrorMessage = validationErr;
            return;
        }

        ErrorMessage = "";
        IsRunning = true;
        ResultSummary = "";
        ResetResultStats();
        ClearLog();

        try
        {
            var options = new TranslateXDeltaOptions
            {
                PatchFilePath = XDeltaPath,
                TargetFilePath = TargetPath,
                OutputPath = string.IsNullOrWhiteSpace(OutputPath) ? null : OutputPath,
                VanillaPath = string.IsNullOrWhiteSpace(VanillaPath) ? null : VanillaPath,
                DryRun = DryRun,
                Backup = Backup,
            };

            var (result, summary) = await _executor.RunAsync(options);
            ResultSummary = summary;
            UpdateResultStats(result);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[Cancelled]");
            ResultSummary = "cancelled";
            ResultBadgeText = "CANCELLED";
            ResultBadgeBackground = SolidColorBrush.Parse("#33E0A830");
            ResultBadgeForeground = SolidColorBrush.Parse("#FFCC66");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ══════════════════════════════════════════════════════
    // Vanilla Library commands
    // ══════════════════════════════════════════════════════

    [RelayCommand]
    private void RefreshLibrary()
    {
        try
        {
            var manager = new VanillaLibraryManager();
            LibraryPath = manager.LibraryPath;
            VanillaEntries.Clear();

            foreach (var entry in manager.GetEntries())
            {
                VanillaEntries.Add(new VanillaEntryViewModel(entry));
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Library Error] {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task AddVanillaAsync()
    {
        var path = await PickFileAsync("Add Vanilla Archive", [
            new FilePickerFileType("GameMaker Archives") { Patterns = ["*.win"] },
            FilePickerFileTypes.All
        ]);

        if (path is null) return;

        try
        {
            AppendLog($"Adding vanilla archive: {path}");
            var manager = new VanillaLibraryManager();

            await Task.Run(() =>
            {
                manager.Add(path, null, msg => AppendLog(msg));
            });

            AppendLog("Archive added successfully.");
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add archive: {ex.Message}";
            AppendLog($"[Error] {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveVanillaAsync()
    {
        if (SelectedVanillaEntry is null) return;

        var hash = SelectedVanillaEntry.Hash;
        try
        {
            var manager = new VanillaLibraryManager();
            await Task.Run(() =>
            {
                manager.Remove(hash, msg => AppendLog(msg));
            });
            AppendLog("Archive removed.");
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to remove archive: {ex.Message}";
            AppendLog($"[Error] {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        lock (_logLock)
        {
            _logBuffer.Clear();
        }
        LogText = "";
    }

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = "";
    }

    // ══════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════

    private void AppendLog(string message)
    {
        lock (_logLock)
        {
            _logBuffer.AppendLine(message);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            lock (_logLock)
            {
                LogText = _logBuffer.ToString();
            }
            LogUpdated?.Invoke();
        });
    }

    private void UpdateResultStats(TranslationResult result)
    {
        var report = result.Report;
        AppliedCount = report.AppliedCount.ToString();
        SkippedCount = report.SkippedCount.ToString();
        ConflictCount = report.ConflictCount.ToString();
        ErrorCount = report.ErrorCount.ToString();

        if (result.Success)
        {
            ResultBadgeText = "SUCCESS";
            ResultBadgeBackground = SuccessBg;
            ResultBadgeForeground = SuccessFg;
        }
        else
        {
            ResultBadgeText = "FAILED";
            ResultBadgeBackground = FailBg;
            ResultBadgeForeground = FailFg;
            if (result.ErrorMessage is not null)
                ErrorMessage = result.ErrorMessage;
        }

        if (result.OutputPath is not null)
            OutputPathDisplay = $"Output written to: {result.OutputPath}";
        else
            OutputPathDisplay = "";
    }

    private void ResetResultStats()
    {
        AppliedCount = "0";
        SkippedCount = "0";
        ConflictCount = "0";
        ErrorCount = "0";
        ResultBadgeText = "";
        ResultBadgeBackground = Brushes.Transparent;
        ResultBadgeForeground = Brushes.White;
        OutputPathDisplay = "";
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private static async Task<string?> PickFileAsync(string title, FilePickerFileType[] fileTypes)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var results = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        });

        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }
}
