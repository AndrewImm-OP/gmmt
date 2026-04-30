using System.Text;

namespace Gmmt.Report;

public static class ConsoleReportFormatter
{
    public static string Format(PatchReport report)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("================================================================================");
        sb.AppendLine("                            GMMT Patch Report");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        if (report.VanillaPath is not null)
            sb.AppendLine($"  Vanilla:  {report.VanillaPath}");
        if (report.ModdedPath is not null)
            sb.AppendLine($"  Modded:   {report.ModdedPath}");
        if (report.TargetPath is not null)
            sb.AppendLine($"  Target:   {report.TargetPath}");
        if (report.OutputPath is not null)
            sb.AppendLine($"  Output:   {report.OutputPath}");

        sb.AppendLine();
        sb.AppendLine($"  Timestamp: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Summary
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("Summary");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();
        sb.AppendLine($"  Applied:     {report.AppliedCount}");
        sb.AppendLine($"  Skipped:     {report.SkippedCount}");
        sb.AppendLine($"  Conflicts:   {report.ConflictCount}");
        sb.AppendLine($"  Risky:       {report.RiskyCount}");
        sb.AppendLine($"  Unsupported: {report.UnsupportedCount}");
        sb.AppendLine($"  Errors:      {report.ErrorCount}");
        sb.AppendLine($"  Info:        {report.InfoCount}");
        sb.AppendLine();

        // Sections
        AppendSection(sb, "APPLIED", ReportCategory.Applied, report.Entries);
        AppendSection(sb, "SKIPPED", ReportCategory.Skipped, report.Entries);
        AppendSection(sb, "CONFLICTS", ReportCategory.Conflict, report.Entries);
        AppendSection(sb, "RISKY", ReportCategory.Risky, report.Entries);
        AppendSection(sb, "UNSUPPORTED", ReportCategory.Unsupported, report.Entries);
        AppendSection(sb, "ERRORS", ReportCategory.Error, report.Entries);
        AppendSection(sb, "INFO", ReportCategory.Info, report.Entries);

        sb.AppendLine("================================================================================");

        return sb.ToString();
    }

    private static void AppendSection(
        StringBuilder sb,
        string title,
        ReportCategory category,
        IReadOnlyList<ReportEntry> entries)
    {
        var filtered = entries.Where(e => e.Category == category).ToList();
        if (filtered.Count == 0)
            return;

        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine($"{title} ({filtered.Count})");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine();

        foreach (var entry in filtered)
        {
            sb.AppendLine($"  [{entry.ResourceType}] {entry.ResourceName}");
            sb.AppendLine($"    {entry.Message}");
            if (entry.Detail is not null)
            {
                sb.AppendLine($"    Detail: {entry.Detail}");
            }
            sb.AppendLine();
        }
    }
}
