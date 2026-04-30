using System.Text;
using Gmmt.Pipeline;
using Gmmt.Report;

namespace Gmmt.Desktop.Services;

/// <summary>
/// Encapsulates translation pipeline execution, logging, and result formatting.
/// Keeps the ViewModel thin by owning all pipeline-related state and logic.
/// </summary>
public sealed class TranslationExecutor
{
    private readonly Action<string> _appendLog;
    private CancellationTokenSource? _cts;

    public TranslationExecutor(Action<string> appendLog)
    {
        _appendLog = appendLog;
    }

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Validates that required inputs are present. Returns null if valid, or an error message.
    /// </summary>
    public static string? ValidateInputs(string xdeltaPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(xdeltaPath))
            return "XDelta patch file is required.";

        if (string.IsNullOrWhiteSpace(targetPath))
            return "Target archive is required.";

        if (!File.Exists(xdeltaPath))
            return $"XDelta patch file not found: {xdeltaPath}";

        if (!File.Exists(targetPath))
            return $"Target archive not found: {targetPath}";

        return null;
    }

    /// <summary>
    /// Runs the xdelta translation pipeline asynchronously.
    /// Returns a formatted result summary string and the underlying TranslationResult.
    /// </summary>
    public async Task<(TranslationResult Result, string Summary)> RunAsync(
        TranslateXDeltaOptions options)
    {
        if (IsRunning)
            throw new InvalidOperationException("Translation is already running.");

        IsRunning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var logger = new TimestampedLogger(_appendLog);
            var pipeline = new TranslationPipeline(logger);

            var result = await pipeline.RunXDeltaTranslateAsync(options, _cts.Token);
            var summary = FormatSummary(result);
            return (result, summary);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Cancels any running operation.
    /// </summary>
    public void Cancel()
    {
        _cts?.Cancel();
    }

    private static string FormatSummary(TranslationResult result)
    {
        var report = result.Report;
        var sb = new StringBuilder();

        if (result.Success)
        {
            sb.AppendLine("Translation completed successfully.");
            if (result.OutputPath is not null)
                sb.AppendLine($"Output: {result.OutputPath}");
        }
        else
        {
            sb.AppendLine($"Translation failed: {result.ErrorMessage}");
        }

        sb.AppendLine();
        sb.AppendLine($"Applied:     {report.AppliedCount}");
        sb.AppendLine($"Skipped:     {report.SkippedCount}");
        sb.AppendLine($"Conflicts:   {report.ConflictCount}");
        sb.AppendLine($"Risky:       {report.RiskyCount}");
        sb.AppendLine($"Unsupported: {report.UnsupportedCount}");
        sb.AppendLine($"Errors:      {report.ErrorCount}");
        sb.AppendLine($"Info:        {report.InfoCount}");

        return sb.ToString();
    }

    /// <summary>
    /// Logger that prepends HH:mm:ss timestamps and routes through IPipelineLogger.
    /// </summary>
    private sealed class TimestampedLogger : IPipelineLogger
    {
        private readonly Action<string> _append;

        public TimestampedLogger(Action<string> append) => _append = append;

        private static string Ts() => DateTime.Now.ToString("HH:mm:ss");

        public void Log(string message) => _append($"[{Ts()}] {message}");
        public void LogWarning(string message) => _append($"[{Ts()}] WARN  {message}");
        public void LogError(string message) => _append($"[{Ts()}] ERROR {message}");
    }
}
