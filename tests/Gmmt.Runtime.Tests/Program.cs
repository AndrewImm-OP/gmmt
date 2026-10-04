using System.Text.Json;
using Gmmt.Runtime;
using Gmmt.Core.Loading;

var root = Path.Combine(Path.GetTempPath(), "gmmt-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
void Reject(Action action, string message) { try { action(); } catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException) { passed++; return; } throw new Exception(message); }
try
{
    var runner = Path.Combine(root, "runner");
    var elf = new byte[64]; elf[0] = 0x7f; elf[1] = 69; elf[2] = 76; elf[3] = 70; elf[4] = 1; elf[5] = 1; elf[6] = 1; elf[16] = 2; elf[18] = 3;
    File.WriteAllBytes(runner, elf);
    var pe = Path.Combine(root, "windows.exe"); File.WriteAllBytes(pe, Enumerable.Repeat((byte)77, 64).ToArray());
    Reject(() => RuntimeCatalog.InspectElf(pe), "PE was accepted as Linux runner");
    var data = Path.Combine(root, "input.win"); File.WriteAllText(data, "test archive bytes");
    var metadata = new ArchiveMetadata
    {
        FilePath = data, GameName = "test", FileName = "test", BytecodeVersion = 16,
        Major = 1, Minor = 0, Release = 0, Build = 1539, GameId = 0, IsYYC = false, IsGMS2 = false,
        SpriteCount = 0, SoundCount = 0, CodeCount = 0, ObjectCount = 0, RoomCount = 0, ScriptCount = 0,
        StringCount = 0, TextureCount = 0, AudioCount = 0, FontCount = 0, ShaderCount = 0, ExtensionCount = 0,
        BackgroundCount = 0, PathCount = 0, TimelineCount = 0, FunctionCount = 0, VariableCount = 0, TpagCount = 0
    };
    var inspection = new ArchiveInspection(metadata, RuntimeCatalog.Hash(data), []);
    var profile = new RunnerProfile("test", runner, RuntimeCatalog.Hash(runner), new(32, 3), "1.0.0.1539", 16, inspection.Sha256, null);
    var catalog = new RuntimeCatalog(Path.Combine(root, "catalog.json"), _ => inspection);
    void Set(params RunnerProfile[] profiles) => File.WriteAllText(catalog.Path, JsonSerializer.Serialize(profiles));
    Set(profile);
    var plan = catalog.Plan(inspection);
    Check(plan.CanPackage && plan.Evidence == "SameArchiveAsReference", "Exact reference not selected");
    Check(!catalog.Plan(inspection with { Metadata = metadata with { Major = 2, IsGMS2 = true } }).CanPackage, "GMS2 selected GMS1 runner");
    Check(!catalog.Plan(inspection with { Metadata = metadata with { IsYYC = true } }).CanPackage, "YYC accepted");
    Check(!catalog.Plan(inspection, "absent").CanPackage, "Missing explicit id accepted");
    Set(profile with { ReferenceArchiveSha256 = "other" });
    Check(catalog.Plan(inspection).Evidence == "MatchingMetadataOnly", "Metadata candidate falsely verified");
    Set(profile with { ReferenceArchiveSha256 = "other" }, profile with { Id = "second", ReferenceArchiveSha256 = "other2" });
    Check(!catalog.Plan(inspection).CanPackage, "Ambiguous catalog auto-selected");
    Set(profile, profile with { Id = "second", ReferenceArchiveSha256 = "other" });
    Check(catalog.Plan(inspection).Runner?.Id == "test", "Exact checksum did not take precedence");
    Set(profile); File.AppendAllText(runner, "mutation");
    Check(!catalog.Plan(inspection).CanPackage, "Changed runner accepted"); File.WriteAllBytes(runner, elf);
    Set(profile with { SteamRuntimeScript = Path.Combine(root, "missing") });
    Check(!catalog.Plan(inspection).CanPackage, "Missing runtime script accepted"); Set(profile);
    var fallbackCand = new DiscoveredRunner("Fallback Game", runner, data, new(32, 3), null);
    var emptyCatalog = new RuntimeCatalog(Path.Combine(root, "empty-catalog.json"), _ => inspection);
    var autoPlan = emptyCatalog.Plan(inspection, fallbackCandidates: [fallbackCand]);
    Check(autoPlan.CanPackage && autoPlan.Runner?.Id == "auto-fallback-game", "Fallback runner candidate not auto-registered or selected");
    Check(emptyCatalog.Read().Any(p => p.Id == "auto-fallback-game"), "Auto-registered profile not persisted in catalog");
    var assets = Path.Combine(root, "resources"); Directory.CreateDirectory(assets);
    File.WriteAllText(Path.Combine(assets, "game.unx"), "must not override archive");
    File.WriteAllText(Path.Combine(assets, "music.ogg"), "resource");
    File.WriteAllText(Path.Combine(assets, "installer.exe"), "must not execute or include");
    var output = Path.Combine(root, "package");
    Reject(() => PackageBuilder.Create(plan, assets, [assets]), "Existing directory overwritten");
    Reject(() => PackageBuilder.Create(plan, Path.Combine(assets, "nested"), [assets]), "Recursive output accepted");
    var nativePlan = plan with { Archive = inspection with { NativeExtensions = ["extension.dll"] } };
    Reject(() => PackageBuilder.Create(nativePlan, output, [assets]), "Native dependency review silently bypassed");
    PackageBuilder.Create(plan, output, [assets]);
    Check(RuntimeCatalog.Hash(Path.Combine(output, "assets/game.unx")) == inspection.Sha256, "Archive bytes changed");
    Check(File.ReadAllText(Path.Combine(output, "assets/music.ogg")) == "resource", "Resource was lost");
    Check(!File.Exists(Path.Combine(output, "assets/installer.exe")), "Installer included");
    using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "gmmt-package.json"))))
        Check(!manifest.RootElement.GetProperty("GameplayVerified").GetBoolean(), "Packaging asserted gameplay verified");
    Reject(() => PackageBuilder.Create(plan, output, [assets]), "Second package overwrote output");
    if (OperatingSystem.IsLinux())
    {
    // Install/restore against a synthetic native Steam layout; never touch a real game.
    var game = Path.Combine(root, "steam-game"); Directory.CreateDirectory(Path.Combine(game, "assets"));
    File.WriteAllBytes(Path.Combine(game, "runner"), elf);
    File.WriteAllText(Path.Combine(game, "assets/game.unx"), "original game");
    File.WriteAllText(Path.Combine(game, "assets/original.ogg"), "original media");
    File.WriteAllText(Path.Combine(game, "run.sh"), "original launcher");
    File.WriteAllText(Path.Combine(game, "unrelated.txt"), "preserve me");
    File.SetUnixFileMode(Path.Combine(game, "run.sh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    SteamInstaller.Install(output, game);
    Check(File.ReadAllText(Path.Combine(game, "assets/game.unx")) == "test archive bytes", "Steam archive not installed");
    Check(File.ReadAllText(Path.Combine(game, "run.sh")).Contains("exec sh ./launch.sh"), "Steam launcher not redirected");
    Check(File.Exists(Path.Combine(game, ".gmmt-original/assets/original.ogg")), "Original media not backed up");
    Reject(() => SteamInstaller.Install(output, game), "Second install overwrote original backup");
    File.AppendAllText(Path.Combine(game, "assets/game.unx"), "modified");
    Reject(() => SteamInstaller.Restore(game), "Modified install silently discarded");
    Check(File.Exists(Path.Combine(game, ".gmmt-original/runner")), "Blocked restore lost backup");
    File.Copy(Path.Combine(output, "assets/game.unx"), Path.Combine(game, "assets/game.unx"), true);
    SteamInstaller.Restore(game);
    Check(File.ReadAllText(Path.Combine(game, "assets/game.unx")) == "original game", "Original data not restored");
    Check(File.ReadAllText(Path.Combine(game, "run.sh")) == "original launcher" && (File.GetUnixFileMode(Path.Combine(game, "run.sh")) & UnixFileMode.UserExecute) != 0, "Launcher permissions not restored");
    Check(!File.Exists(Path.Combine(game, "launch.sh")) && !Directory.Exists(Path.Combine(game, ".gmmt-original")), "Restore retained mod files or active backup");
    Check(File.ReadAllText(Path.Combine(game, "unrelated.txt")) == "preserve me", "Unrelated file was changed");
    Reject(() => SteamInstaller.Install(output, output), "Self-install accepted");
    File.WriteAllText(Path.Combine(game, "runner"), "Windows PE");
    Reject(() => SteamInstaller.Install(output, game), "Non-native game accepted");
    File.WriteAllBytes(Path.Combine(game, "runner"), elf);
    File.AppendAllText(Path.Combine(output, "runner"), "changed package");
    Reject(() => SteamInstaller.Install(output, game), "Tampered package accepted");
    Check(!Directory.Exists(Path.Combine(game, ".gmmt-original")), "Rejected package left an active backup");
    File.WriteAllBytes(Path.Combine(output, "runner"), elf);
    if (OperatingSystem.IsLinux())
    {
        File.CreateSymbolicLink(Path.Combine(game, "assets/link"), pe);
        Reject(() => SteamInstaller.Install(output, game), "Linked game assets accepted");
        File.Delete(Path.Combine(game, "assets/link"));
    }
    File.WriteAllText(Path.Combine(output, "assets/extra.ogg"), "not in manifest");
    Reject(() => SteamInstaller.Install(output, game), "Unlisted package file accepted");
    File.Delete(Path.Combine(output, "assets/extra.ogg"));
    SteamInstaller.Install(output, game);
    File.AppendAllText(Path.Combine(game, ".gmmt-original/assets/game.unx"), "bad backup");
    Reject(() => SteamInstaller.Restore(game), "Tampered original backup accepted");
    Check(File.ReadAllText(Path.Combine(game, "assets/game.unx")) == "test archive bytes", "Rejected restore partially changed installation");
    File.WriteAllText(Path.Combine(game, ".gmmt-original/assets/game.unx"), "original game");
    var journalPath = Path.Combine(game, ".gmmt-original/install.json");
    File.WriteAllText(journalPath, File.ReadAllText(journalPath).Replace("Installed", "Restoring"));
    File.Delete(Path.Combine(game, "runner"));
    File.Move(Path.Combine(game, ".gmmt-original/runner"), Path.Combine(game, "runner"));
    SteamInstaller.Restore(game);
    Check(File.ReadAllText(Path.Combine(game, "assets/game.unx")) == "original game" && !Directory.Exists(Path.Combine(game, ".gmmt-original")), "Interrupted restoration did not resume");
    using (var heldLock = new FileStream(Path.Combine(game, ".gmmt-operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Reject(() => SteamInstaller.Install(output, game), "Concurrent game operation accepted");
    File.Delete(Path.Combine(game, ".gmmt-operation.lock"));
    }
    if (OperatingSystem.IsLinux())
    {
        var discoveryElf = elf.Concat(System.Text.Encoding.ASCII.GetBytes("YoYo Games game.unx")).ToArray();
        var steamRoot = Path.Combine(root, "fake-steam");
        var extraLibrary = Path.Combine(root, "external library");
        var nativeGame = Path.Combine(extraLibrary, "steamapps/common/DiscoveredGame");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(nativeGame, "assets"));
        File.WriteAllBytes(Path.Combine(nativeGame, "runner"), discoveryElf);
        File.WriteAllBytes(Path.Combine(nativeGame, "xdelta3"), elf);
        File.WriteAllText(Path.Combine(nativeGame, "assets/game.unx"), "discovery does not parse or run archives");
        File.WriteAllText(Path.Combine(nativeGame, "run.sh"), "launcher");
        var sharedObject = discoveryElf.ToArray(); sharedObject[16] = 3;
        File.WriteAllBytes(Path.Combine(nativeGame, "shared-object"), sharedObject);
        File.WriteAllText(Path.Combine(steamRoot, "steamapps/libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + extraLibrary + "\" } }");
        var discovered = SteamDiscovery.Scan([steamRoot], []);
        Check(discovered.Libraries.Contains(extraLibrary), "Additional Steam library missed");
        Check(discovered.Games.Single().Directory == nativeGame, "Native Steam game not detected");
        Check(discovered.Runners.Single().RunnerPath == Path.Combine(nativeGame, "runner"), "Runner missed or shared object falsely accepted");
        var steamLink = Path.Combine(root, "steam-link"); Directory.CreateSymbolicLink(steamLink, steamRoot);
        Check(SteamDiscovery.Scan([steamRoot, steamLink], []).Runners.Length == 1, "Steam root aliases duplicated candidates");
        var donor = Path.Combine(root, "downloaded-mod"); Directory.CreateDirectory(donor);
        File.WriteAllBytes(Path.Combine(donor, "runner"), discoveryElf); File.WriteAllText(Path.Combine(donor, "game.unx"), "data");
        Check(SteamDiscovery.Scan([], [donor]).Runners.Single().RunnerPath == Path.Combine(donor, "runner"), "Extra runner directory missed");
        File.WriteAllText(Path.Combine(steamRoot, "steamapps/libraryfolders.vdf"), "\"libraryfolders\" { \"1\" \"" + extraLibrary + "\" }");
        Check(SteamDiscovery.Scan([steamRoot], []).Games.Length == 1, "Legacy library layout missed");
        File.Delete(Path.Combine(nativeGame, "runner")); File.WriteAllBytes(Path.Combine(nativeGame, "runner"), [77, 90, 0, 0]);
        Check(SteamDiscovery.Scan([steamRoot], []).Runners.Length == 0, "Windows executable detected as native runner");
        var depotDir = Path.Combine(steamRoot, "ubuntu12_32/steamapps/content/app_391540/depot_391541");
        Directory.CreateDirectory(depotDir);
        File.WriteAllText(Path.Combine(depotDir, "data.win"), "depot data");
        var discoveredDepots = SteamDiscovery.Scan([steamRoot], []).Depots;
        Check(discoveredDepots.Any(d => d.AppId == "391540" && d.DepotId == "391541" && d.ArchivePath == Path.Combine(depotDir, "data.win")), "Steam content depot missed");
    }
    File.WriteAllText(data, "changed");
    var failedOutput = Path.Combine(root, "failed-package");
    Reject(() => PackageBuilder.Create(plan, failedOutput, [assets]), "Changed input accepted");
    Check(!Directory.Exists(failedOutput) && !Directory.EnumerateDirectories(root, ".gmmt-*").Any(), "Failed staging leaked or published");
    if (OperatingSystem.IsLinux())
    {
        File.CreateSymbolicLink(Path.Combine(assets, "outside.ogg"), pe);
        Reject(() => PackageBuilder.Create(plan, Path.Combine(root, "linked-package"), [assets]), "Symlinked assets accepted");
    }
    var settingsPath = Path.Combine(root, "preferences/settings.json");
    var localization = new Gmmt.Desktop.LocalizationService(settingsPath, () => "ru-RU");
    Check(localization.Preference == "system" && localization.Language == "ru", "System Russian was not detected");
    Check(Gmmt.Desktop.LocalizationService.Resolve("system", "de-DE") == "en", "Unsupported system language did not fall back to English");
    Check(Gmmt.Desktop.LocalizationService.Resolve("system", "ru_RU.UTF-8") == "ru", "Linux locale was not recognized");
    Check(Gmmt.Desktop.LocalizationService.Resolve("system", "rubbish") == "en", "Invalid locale incorrectly selected Russian");
    localization.SetPreference("en");
    Check(localization.Language == "en" && localization.Text("tab.runners") == "Runners", "Explicit English override failed");
    Check(new Gmmt.Desktop.LocalizationService(settingsPath, () => "ru").Preference == "en", "Language did not survive reload");
    localization.SetPreference("ru");
    Check(localization.Format("discovery.summary", 2, 1, 3).Contains("Steam: 2"), "Russian formatted string failed");
    localization.SetPreference("system");
    Check(new Gmmt.Desktop.LocalizationService(settingsPath, () => "en-GB").Language == "en", "Saved system mode did not redetect language");
    foreach (var key in Gmmt.Desktop.LocalizationService.Keys)
    {
        localization.SetPreference("en"); var english = localization.Text(key);
        localization.SetPreference("ru"); var russian = localization.Text(key);
        if (string.IsNullOrWhiteSpace(english) || string.IsNullOrWhiteSpace(russian)) throw new Exception("Empty localization entry: " + key);
        var parameters = System.Text.RegularExpressions.Regex.Matches(english, "\\{[0-9]+\\}").Select(m => m.Value).Order();
        if (!parameters.SequenceEqual(System.Text.RegularExpressions.Regex.Matches(russian, "\\{[0-9]+\\}").Select(m => m.Value).Order())) throw new Exception("Format placeholder mismatch: " + key);
    }
    Check(true, "Catalog translations and placeholders");
    File.WriteAllText(settingsPath, "not json");
    Check(new Gmmt.Desktop.LocalizationService(settingsPath, () => "ru").Preference == "system", "Malformed settings prevented system fallback");
    var blockedSettings = Path.Combine(root, "settings-is-a-directory"); Directory.CreateDirectory(blockedSettings);
    var blockedLocalization = new Gmmt.Desktop.LocalizationService(blockedSettings, () => "ru");
    try { blockedLocalization.SetPreference("en"); throw new Exception("Settings overwrite directory unexpectedly succeeded"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(blockedLocalization.Preference == "system", "Failed write changed active preference"); }
    Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Failed settings write leaked a temporary file");
    // ZIP extraction and candidate provenance are exercised with synthetic inputs only.
    void Zip(string path, params (string Name, byte[] Bytes)[] entries)
    {
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries) { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
    }
    var green = Path.Combine(root, "green-runner");
    var greenBytes = new byte[60000]; elf.CopyTo(greenBytes, 0); "GMGreen"u8.CopyTo(greenBytes.AsSpan(65530 % 60000));
    File.WriteAllBytes(green, greenBytes);
    Check(RunnerElfInspector.DetectFamily(green) == true && RunnerElfInspector.DetectFamily(runner) == null, "ELF family hint detection failed");
    var crossChunk = Path.Combine(root, "cross-chunk"); var crossBytes = new byte[70000]; elf.CopyTo(crossBytes, 0); "GMGreen"u8.CopyTo(crossBytes.AsSpan(65533)); File.WriteAllBytes(crossChunk, crossBytes);
    Check(RunnerElfInspector.DetectFamily(crossChunk) == true, "Family marker at chunk boundary was missed");
    var modZip = Path.Combine(root, "mod.zip");
    var archiveBytes = Enumerable.Repeat((byte)65, 60000).ToArray();
    Zip(modZip, ("runner", greenBytes), ("assets/game.unx", archiveBytes), ("assets/music.ogg", "music"u8.ToArray()));
    string extractedPath;
    using (var prepared = await ArchiveInput.PrepareAsync(modZip))
    {
        extractedPath = prepared.Path;
        Check(File.ReadAllBytes(prepared.Path).SequenceEqual(archiveBytes) && prepared.BundledRunnerPath != null, "ZIP mod/runner missing");
        Check(File.ReadAllText(Path.Combine(prepared.ExtraAssetPaths.Single(), "music.ogg")) == "music", "ZIP assets missing");
        var gms2 = inspection with { Metadata = metadata with { Major = 2, Minor = 0, Release = 6, Build = 0, IsGMS2 = true } };
        var candidateCatalog = new RuntimeCatalog(Path.Combine(root, "candidate-catalog.json"), _ => gms2);
        var candidatePlan = await RunnerResolver.PlanAsync(candidateCatalog, gms2, prepared, allowDownload: false);
        Check(candidatePlan.CanPackage && candidatePlan.Evidence == "UnverifiedRunnerCandidate" && candidatePlan.Runner?.ReferenceArchiveSha256 == "", "Unverified bundled candidate falsely certified");
        Check(candidateCatalog.Read().Count == 0, "Target mod falsely persisted as compatible runner reference");
        Check(!(await RunnerResolver.PlanAsync(candidateCatalog, inspection, prepared, allowDownload: false)).CanPackage, "GMS2 hint selected for GMS1");
        Check(!(await RunnerResolver.PlanAsync(candidateCatalog, gms2 with { Metadata = gms2.Metadata with { BytecodeVersion = 17 } }, prepared, allowDownload: false)).CanPackage, "Unknown runner bytecode guessed as BC17");
    }
    Check(!File.Exists(extractedPath), "ZIP temporary extraction leaked");
    var malicious = Path.Combine(root, "malicious.zip"); Zip(malicious, ("assets/../../outside.txt", "escape"u8.ToArray()), ("data.win", archiveBytes));
    try { using var ignored = await ArchiveInput.PrepareAsync(malicious); throw new Exception("ZIP traversal accepted"); }
    catch (InvalidDataException) { Check(!File.Exists(Path.Combine(root, "outside.txt")), "ZIP escaped its extraction directory"); }
    var ambiguous = Path.Combine(root, "ambiguous.zip"); Zip(ambiguous, ("a.win", archiveBytes), ("b.win", archiveBytes));
    try { using var ignored = await ArchiveInput.PrepareAsync(ambiguous); throw new Exception("Ambiguous ZIP accepted"); } catch (InvalidDataException) { Check(true, "Ambiguous ZIP rejected"); }
    var patchOnly = Path.Combine(root, "patch-only.zip"); Zip(patchOnly, ("runner", greenBytes), ("patch.xdelta", "patch"u8.ToArray()));
    var discoveredZip = SteamDiscovery.Scan(Array.Empty<string>(), new[] { root });
    Check(discoveredZip.Runners.Any(r => r.Game == "patch-only" && r.ArchivePath == null && r.IsGMS2 == true), "Patch-only ZIP runner was not discovered");
    var wrongBundle = Path.Combine(root, "wrong-bundle.zip"); File.WriteAllText(wrongBundle, "untrusted");
    Reject(() => CloudRunnerProvider.ReadBundle(wrongBundle, Path.Combine(root, "cloud")), "Runner bundle checksum was not checked");
    // A wrong explicit xdelta base must fall through to a checksum-valid alternate.
    var patchBase = Path.Combine(root, "patch-base"); var patchTarget = Path.Combine(root, "patch-target"); var patchFile = Path.Combine(root, "test.xdelta");
    File.WriteAllText(patchBase, new string('A', 4096)); File.WriteAllText(patchTarget, new string('A', 4000) + new string('B', 96));
    var encode = new System.Diagnostics.ProcessStartInfo("xdelta3") { UseShellExecute = false };
    foreach (var arg in new[] { "-e", "-s", patchBase, patchTarget, patchFile }) encode.ArgumentList.Add(arg);
    using (var proc = System.Diagnostics.Process.Start(encode)!) { await proc.WaitForExitAsync(); if (proc.ExitCode != 0) throw new Exception("Fixture patch encode failed"); }
    using (var prepared = await ArchiveInput.PrepareAsync(patchFile, data, alternateBaselines: new[] { patchBase }))
        Check(RuntimeCatalog.Hash(prepared.Path) == RuntimeCatalog.Hash(patchTarget) && prepared.InputHashes.ContainsKey(patchBase) && !prepared.InputHashes.ContainsKey(data), "xdelta fallback used/recorded wrong base");
    Console.WriteLine($"PASS: {passed} runtime, packaging, discovery and localization checks");
}
finally { Directory.Delete(root, true); }
