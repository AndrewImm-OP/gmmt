using System.Text.Json;
using Gmmt.Runtime;

namespace Gmmt.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args[0] == "help")
        {
            Console.WriteLine("""
                GMMT — native Linux runner packaging
                inspect --archive DATA
                register-runner --id NAME --runner ELF --reference DATA [--steam-runtime RUN.SH]
                runners
                fetch-runners (download/check the pinned optional runner cache)
                discover [--steam-root DIR ...] [--runner-root DIR ...]
                install --package PACKAGE_FOLDER --game-dir NATIVE_LINUX_GAME
                restore --game-dir NATIVE_LINUX_GAME
                plan --archive DATA [--runner-id NAME]
                package --archive DATA_OR_ZIP [--vanilla BASE] --output NEW_FOLDER [--runner-id NAME] [--assets DIR ...] [--libraries DIR ...]
                package --patch XDELTA --vanilla WINDOWS_DATA --output NEW_FOLDER [same options]
                All commands accept --catalog FILE (default: local application data/gmmt/runners.json).
                package: --native-extensions-reviewed acknowledges manually supplied native dependencies.
                Runner reference metadata selects candidates; packaging never claims verified gameplay.
                The previous converter is preserved under old/translation.
                """);
            return 0;
        }
        try
        {
            var values = new Dictionary<string, List<string>>();
            var flags = new HashSet<string> { "--native-extensions-reviewed" };
            var allowed = new HashSet<string> { "--archive", "--id", "--runner", "--reference", "--steam-runtime", "--catalog",
                "--steam-root", "--runner-root", "--package", "--game-dir", "--runner-id", "--output", "--assets", "--libraries", "--patch", "--vanilla", "--native-extensions-reviewed" };
            for (int i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (!allowed.Contains(key)) throw new ArgumentException("Unknown option: " + key);
                var value = flags.Contains(key) ? "true" : ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for " + key);
                if (!values.TryGetValue(key, out var list)) values[key] = list = [];
                list.Add(value);
            }
            string? One(string key) => !values.TryGetValue(key, out var list) ? null : list.Count == 1
                ? list[0] : throw new ArgumentException("Option may only occur once: " + key);
            string Required(string key) => One(key) ?? throw new ArgumentException("Required: " + key);
            string[] Many(string key) => values.TryGetValue(key, out var list) ? list.ToArray() : [];
            var catalog = new RuntimeCatalog(One("--catalog") ?? Environment.GetEnvironmentVariable("GMMT_CATALOG") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners.json"));
            void Print(object result) => Console.WriteLine(JsonSerializer.Serialize(result, RuntimeCatalog.JsonOptions));
            switch (args[0])
            {
                case "fetch-runners": Print(await CloudRunnerProvider.EnsureRunnersAsync()); return 0;
                case "discover": Print(SteamDiscovery.Scan(Many("--steam-root").Length == 0 ? null : Many("--steam-root"), Many("--runner-root").Length == 0 ? null : Many("--runner-root"))); return 0;
                case "install": Print(new { Backup = SteamInstaller.Install(Required("--package"), Required("--game-dir")), GameplayVerified = false }); return 0;
                case "restore": SteamInstaller.Restore(Required("--game-dir")); Print(new { Restored = true }); return 0;
                case "inspect": Print(ArchiveInspector.Inspect(Required("--archive"))); return 0;
                case "runners": Print(catalog.Read()); return 0;
                case "register-runner":
                    Print(catalog.Register(Required("--id"), Required("--runner"), Required("--reference"), One("--steam-runtime"))); return 0;
                case "plan":
                    var plan = catalog.Plan(ArchiveInspector.Inspect(Required("--archive")), One("--runner-id"));
                    Print(plan); return plan.CanPackage ? 0 : 2;
                case "package":
                    var archive = One("--archive");
                    var patch = One("--patch");
                    if ((archive == null) == (patch == null)) throw new ArgumentException("Supply either --archive or --patch with --vanilla.");
                    var output = Required("--output");
                    using (var prepared = await ArchiveInput.PrepareAsync(archive ?? patch!, One("--vanilla"), patch != null))
                    {
                        var packagePlan = await RunnerResolver.PlanAsync(catalog, ArchiveInspector.Inspect(prepared.Path), prepared, One("--runner-id"), SteamDiscovery.Scan().Runners);
                        if (!packagePlan.CanPackage) { Print(packagePlan); return 2; }
                        Print(new { Output = PackageBuilder.Create(packagePlan, output, Many("--assets").Concat(prepared.ExtraAssetPaths).ToArray(), Many("--libraries"),
                            values.ContainsKey("--native-extensions-reviewed"), prepared.InputHashes), Evidence = packagePlan.Evidence, GameplayVerified = false });
                        return 0;
                    }
                default: throw new ArgumentException("Unknown command: " + args[0]);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("GMMT: " + ex.Message); return 1; }
    }

}
