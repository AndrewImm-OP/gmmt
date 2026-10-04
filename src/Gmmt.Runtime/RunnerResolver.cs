namespace Gmmt.Runtime;

public static class RunnerResolver
{
    public static async Task<PackagePlan> PlanAsync(RuntimeCatalog catalog, ArchiveInspection archive, ArchiveInput input,
        string? runnerId = null, IEnumerable<DiscoveredRunner>? candidates = null, bool allowDownload = true)
    {
        var local = (candidates ?? Array.Empty<DiscoveredRunner>()).ToList();
        if (input.BundledRunnerPath != null)
        {
            var path = input.BundledRunnerPath;
            local.Insert(0, new("Bundled mod runner", path, null, RuntimeCatalog.InspectElf(path),
                local.Select(c => c.SteamRuntimeScript).FirstOrDefault(s => s != null), null, RunnerElfInspector.DetectFamily(path)));
        }
        var plan = catalog.Plan(archive, runnerId, local);
        if (plan.Runner != null || runnerId != null || archive.Metadata.IsYYC || !allowDownload) return plan;
        try
        {
            var cloud = await CloudRunnerProvider.EnsureRunnersAsync(local.Select(c => c.SteamRuntimeScript).FirstOrDefault(s => s != null));
            return catalog.Plan(archive, null, local.Concat(cloud));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            return plan with { Warnings = plan.Warnings.Append("Optional runner download unavailable: " + ex.Message).ToArray() };
        }
    }
}
