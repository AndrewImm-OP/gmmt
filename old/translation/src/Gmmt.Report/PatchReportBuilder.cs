namespace Gmmt.Report;

public sealed class PatchReportBuilder
{
    private readonly List<ReportEntry> _entries = [];
    
    public string? VanillaPath { get; set; }
    public string? ModdedPath { get; set; }
    public string? TargetPath { get; set; }
    public string? OutputPath { get; set; }

    public PatchReportBuilder AddEntry(ReportEntry entry)
    {
        _entries.Add(entry);
        return this;
    }

    public PatchReportBuilder AddApplied(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Applied, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddSkipped(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Skipped, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddConflict(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Conflict, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddRisky(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Risky, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddUnsupported(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Unsupported, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddError(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Error, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReportBuilder AddInfo(
        string resourceType,
        string resourceName,
        string message,
        string? detail = null,
        ReportConfidence confidence = ReportConfidence.Unknown)
    {
        _entries.Add(new ReportEntry(ReportCategory.Info, resourceType, resourceName, message, detail, confidence));
        return this;
    }

    public PatchReport Build()
    {
        return new PatchReport(
            Timestamp: DateTime.UtcNow,
            VanillaPath: VanillaPath,
            ModdedPath: ModdedPath,
            TargetPath: TargetPath,
            OutputPath: OutputPath,
            Entries: _entries.AsReadOnly());
    }
}
