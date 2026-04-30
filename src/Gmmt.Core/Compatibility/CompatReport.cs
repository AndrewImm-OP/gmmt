namespace Gmmt.Core.Compatibility;

public enum CompatSeverity { Info, Warning, Error }

public sealed record CompatIssue(CompatSeverity Severity, string Code, string Message);

public sealed class CompatReport
{
    private readonly List<CompatIssue> _issues = new();

    public IReadOnlyList<CompatIssue> Issues => _issues;
    public bool HasErrors => _issues.Any(i => i.Severity == CompatSeverity.Error);
    public bool HasWarnings => _issues.Any(i => i.Severity == CompatSeverity.Warning);

    public void AddError(string code, string message)
        => _issues.Add(new CompatIssue(CompatSeverity.Error, code, message));

    public void AddWarning(string code, string message)
        => _issues.Add(new CompatIssue(CompatSeverity.Warning, code, message));

    public void AddInfo(string code, string message)
        => _issues.Add(new CompatIssue(CompatSeverity.Info, code, message));
}
