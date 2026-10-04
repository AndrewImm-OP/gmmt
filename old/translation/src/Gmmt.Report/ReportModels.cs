namespace Gmmt.Report;

public enum ReportCategory
{
    Applied,
    Skipped,
    Conflict,
    Risky,
    Unsupported,
    Error,
    Info
}

public enum ReportConfidence
{
    High,
    Medium,
    Low,
    Unknown
}

public record ReportEntry(
    ReportCategory Category,
    string ResourceType,
    string ResourceName,
    string Message,
    string? Detail,
    ReportConfidence Confidence);

public record PatchReport(
    DateTime Timestamp,
    string? VanillaPath,
    string? ModdedPath,
    string? TargetPath,
    string? OutputPath,
    IReadOnlyList<ReportEntry> Entries)
{
    public int AppliedCount => Entries.Count(e => e.Category == ReportCategory.Applied);
    public int SkippedCount => Entries.Count(e => e.Category == ReportCategory.Skipped);
    public int ConflictCount => Entries.Count(e => e.Category == ReportCategory.Conflict);
    public int RiskyCount => Entries.Count(e => e.Category == ReportCategory.Risky);
    public int UnsupportedCount => Entries.Count(e => e.Category == ReportCategory.Unsupported);
    public int ErrorCount => Entries.Count(e => e.Category == ReportCategory.Error);
    public int InfoCount => Entries.Count(e => e.Category == ReportCategory.Info);
}
