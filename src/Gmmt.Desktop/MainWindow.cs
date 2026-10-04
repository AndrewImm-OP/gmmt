using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Gmmt.Runtime;

namespace Gmmt.Desktop;

public sealed class MainWindow : Window
{
    private readonly LocalizationService locale;
    private readonly List<Action> translations = [];
    private readonly ComboBox language = new() { Name = "LanguageSelector", MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right };
    private bool changingLanguage;
    private Func<string>? renderLog;
    private Func<string>? renderStatus;
    private SteamDiscoveryResult? lastDiscovery;
    private readonly TextBox catalog = new() { Name = "RunnerCatalog" };
    private readonly TextBox input = new() { Name = "ModArchive" };
    private readonly TextBox vanilla = new();
    private readonly TextBox assets = new() { AcceptsReturn = true, MinHeight = 64 };
    private readonly TextBox libraries = new() { AcceptsReturn = true };
    private readonly TextBox output = new();
    private readonly TextBox steamDirectory = new();
    private readonly ComboBox discoveredGames = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox extraRunnerRoots = new() { AcceptsReturn = true };
    private readonly ComboBox discoveredRunners = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox separate = new() { Name = "SeparateOutput", IsChecked = true };
    private readonly TextBox runnerId = new();
    private readonly TextBox donorId = new();
    private readonly TextBox donorRunner = new();
    private readonly TextBox reference = new();
    private readonly TextBox runtime = new();
    private readonly CheckBox reviewed = new();
    private readonly TextBox log = new() { Name = "OperationLog", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
    private readonly TabControl tabs = new() { Name = "WorkflowTabs" };
    private readonly TextBlock status = new() { Name = "OperationStatus", FontWeight = FontWeight.SemiBold };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, IsVisible = false, Height = 3 };
    private static readonly IBrush Muted = Brush.Parse("#B4B8C0");
    private static readonly IBrush Accent = Brush.Parse("#9DDFC5");

    public MainWindow() : this(new LocalizationService()) { }
    public MainWindow(LocalizationService locale, bool discoverOnOpen = true)
    {
        this.locale = locale;
        Bind(() => assets.Watermark = T("assets.placeholder"));
        Bind(() => libraries.Watermark = T("libraries.placeholder"));
        Bind(() => discoveredGames.PlaceholderText = T("games.placeholder"));
        Bind(() => extraRunnerRoots.Watermark = T("search.placeholder"));
        Bind(() => discoveredRunners.PlaceholderText = T("runners.placeholder"));
        Bind(() => separate.Content = T("output.separate"));
        Bind(() => runnerId.Watermark = T("runner.placeholder"));
        Bind(() => runtime.Watermark = T("runtime.placeholder"));
        SetStatus("status.ready");
        Bind(() => Title = T("window.title"));
        Width = 980; Height = 860; MinWidth = 700; MinHeight = 640;
        Background = Brush.Parse("#191B20");
        catalog.Text = Environment.GetEnvironmentVariable("GMMT_CATALOG") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners.json");
        Bind(() => input.Watermark = T("input.placeholder"));
        Bind(() => output.Watermark = T("output.placeholder"));
        Bind(() => donorId.Watermark = T("id.placeholder"));
        SetLog(() => T("welcome"));
        log.Background = Brush.Parse("#202329");
        log.BorderThickness = new Thickness(0);
        log.Padding = new Thickness(14);
        log.MinHeight = 0;
        log.FontSize = 13;

        var build = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 20) };
        build.Children.Add(Heading("build.heading", "build.description"));
        build.Children.Add(Field("input.label", input));
        build.Children.Add(Field("assets.label", assets, true));
        build.Children.Add(separate);
        var packageOutput = Field("output.label", output, true, true);
        build.Children.Add(packageOutput);
        var installation = new StackPanel { Spacing = 12, IsVisible = false };
        installation.Children.Add(discoveredGames);
        installation.Children.Add(Field("steam.label", steamDirectory, true));
        installation.Children.Add(Action("steam.search", Discover));
        discoveredGames.SelectionChanged += (_, _) => { if (discoveredGames.SelectedItem is DiscoveredGame found) steamDirectory.Text = found.Directory; };
        installation.Children.Add(Note("steam.notice"));
        installation.Children.Add(Action("steam.restore", RestoreSteam));
        build.Children.Add(installation);
        separate.IsCheckedChanged += (_, _) => { packageOutput.IsVisible = separate.IsChecked == true; installation.IsVisible = separate.IsChecked != true; };
        var advanced = new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 8) };
        advanced.Children.Add(Field("vanilla.label", vanilla));
        var depotBtn = Action("steam.downloadDepot", DownloadDepotPrompt);
        depotBtn.Margin = new Thickness(0, 0, 0, 4);
        advanced.Children.Add(depotBtn);
        advanced.Children.Add(Field("libraries.label", libraries, true));
        advanced.Children.Add(Field("runner.label", runnerId, noBrowse: true));
        reviewed.Content = Label("extensions.review");
        advanced.Children.Add(reviewed);
        build.Children.Add(Disclosure("advanced", advanced));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var primary = Action("build.action", () => Build(true));
        primary.Classes.Add("primary");
        Bind(() => primary.Content = T(separate.IsChecked == true ? "build.action" : "steam.install"));
        separate.IsCheckedChanged += (_, _) => primary.Content = T(separate.IsChecked == true ? "build.action" : "steam.install");
        primary.Margin = new Thickness(0, 0, 12, 8);
        actions.Children.Add(primary);
        var inspect = Action("build.inspect", () => Build(false));
        inspect.Margin = new Thickness(0, 0, 0, 8);
        actions.Children.Add(inspect);
        build.Children.Add(actions);
        build.Children.Add(Note("build.notice"));

        var donors = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 20) };
        donors.Children.Add(Heading("runners.heading", "runners.description"));
        donors.Children.Add(discoveredRunners);
        donors.Children.Add(Disclosure("search.extra", Field("search.label", extraRunnerRoots, true)));
        var runnerSearchActions = new WrapPanel { Orientation = Orientation.Horizontal };
        var searchRunnersBtn = Action("runners.search", Discover);
        searchRunnersBtn.Margin = new Thickness(0, 0, 12, 8);
        runnerSearchActions.Children.Add(searchRunnersBtn);
        var deltaruneBtn = Action("steam.installDeltarune", InstallDeltarune);
        deltaruneBtn.Margin = new Thickness(0, 0, 0, 8);
        runnerSearchActions.Children.Add(deltaruneBtn);
        donors.Children.Add(runnerSearchActions);
        donors.Children.Add(Note("runners.notice"));
        discoveredRunners.SelectionChanged += (_, _) =>
        {
            if (discoveredRunners.SelectedItem is not DiscoveredRunner found) return;
            donorRunner.Text = found.RunnerPath; reference.Text = found.ArchivePath; runtime.Text = found.SteamRuntimeScript ?? "";
            var slug = System.Text.RegularExpressions.Regex.Replace(found.Game.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            donorId.Text = (slug.Length == 0 ? "runner" : slug) + "-linux";
        };
        donors.Children.Add(Field("catalog.label", catalog));
        donors.Children.Add(Field("id.label", donorId, noBrowse: true));
        donors.Children.Add(Field("elf.label", donorRunner));
        donors.Children.Add(Field("reference.label", reference));
        donors.Children.Add(Disclosure("runtime.optional", Field("runtime.label", runtime)));
        var donorActions = new WrapPanel();
        var add = Action("runners.add", Register); add.Classes.Add("primary"); add.Margin = new Thickness(0, 0, 12, 8);
        donorActions.Children.Add(add);
        donorActions.Children.Add(Action("runners.list", ListRunners));
        donors.Children.Add(donorActions);
        donors.Children.Add(Note("runners.local"));
        tabs.ItemsSource = new[] {
            LocalizedTab("tab.build", new ScrollViewer { Content = build }),
            LocalizedTab("tab.runners", new ScrollViewer { Content = donors })
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = "GMMT", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Accent });
        var subtitle = Label("app.subtitle"); subtitle.Foreground = Muted; subtitle.VerticalAlignment = VerticalAlignment.Center; subtitle.FontSize = 13; subtitle.Margin = new Thickness(16, 0);
        Grid.SetColumn(subtitle, 1); header.Children.Add(subtitle);
        var system = new ComboBoxItem { Tag = "system" };
        Bind(() => system.Content = T("language.system"));
        language.ItemsSource = new[] { system, new ComboBoxItem { Content = "English", Tag = "en" }, new ComboBoxItem { Content = "Русский", Tag = "ru" } };
        language.SelectedIndex = locale.Preference switch { "en" => 1, "ru" => 2, _ => 0 };
        Bind(() => AutomationProperties.SetName(language, T("language.label")));
        language.SelectionChanged += (_, _) => ChangeLanguage();
        Grid.SetColumn(language, 2); header.Children.Add(language);
        var result = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 16, 0, 0) };
        status.Margin = new Thickness(0, 0, 0, 8);
        result.Children.Add(status);
        Grid.SetRow(progress, 1); result.Children.Add(progress);
        Grid.SetRow(log, 2); result.Children.Add(log);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*,190"), Margin = new Thickness(28, 22, 28, 24) };
        page.Children.Add(header);
        Grid.SetRow(tabs, 1); page.Children.Add(tabs);
        Grid.SetRow(result, 2); page.Children.Add(result);
        Content = page;
        if (discoverOnOpen) Opened += async (_, _) =>
        {
            try { await Discover(); }
            catch (Exception ex) { SetLog(() => F("error.discovery", ex.Message)); }
        };
    }

    private string T(string key) => locale.Text(key);
    private string F(string key, params object[] values) => locale.Format(key, values);
    private void Bind(Action update) { translations.Add(update); update(); }
    private void SetLog(Func<string> render) { renderLog = render; log.Text = render(); }
    private void SetStatus(string key, params object[] values) { renderStatus = () => F(key, values); status.Text = renderStatus(); }
    private TextBlock Label(string key, double size = 13, FontWeight? weight = null)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        Bind(() => block.Text = T(key)); return block;
    }
    private Expander Disclosure(string key, Control content)
    {
        var expander = new Expander { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        Bind(() => expander.Header = T(key)); return expander;
    }
    private TabItem LocalizedTab(string key, Control content)
    {
        var tab = new TabItem { Content = content }; Bind(() => tab.Header = T(key)); return tab;
    }
    private void ChangeLanguage()
    {
        if (changingLanguage || language.SelectedItem is not ComboBoxItem item || item.Tag is not string preference) return;
        var previous = locale.Preference;
        try
        {
            locale.SetPreference(preference);
            foreach (var update in translations) update();
            if (renderLog != null) log.Text = renderLog();
            if (renderStatus != null) status.Text = renderStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            changingLanguage = true;
            language.SelectedIndex = previous switch { "en" => 1, "ru" => 2, _ => 0 };
            changingLanguage = false;
            SetStatus("status.failed"); SetLog(() => F("error.settings", ex.Message));
        }
    }

    private Control Heading(string title, string description)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Label(title, 21, FontWeight.SemiBold));
        panel.Children.Add(Note(description));
        return panel;
    }

    private TextBlock Note(string key) { var block = Label(key); block.MaxWidth = 820; block.Foreground = Muted; block.FontSize = 13; block.LineHeight = 20; return block; }
    private Control Field(string label, TextBox box, bool folder = false, bool newFolder = false, bool noBrowse = false)
    {
        var field = new StackPanel { Spacing = 7 };
        field.Children.Add(Label(label, 13, FontWeight.Medium));
        Bind(() => AutomationProperties.SetName(box, T(label)));
        box.MinHeight = 38;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(box);
        if (!noBrowse)
        {
            var button = new Button { Margin = new Thickness(8, 0, 0, 0), MinHeight = 38 };
            Bind(() => button.Content = T(newFolder ? "browse.output" : "browse"));
            Grid.SetColumn(button, 1); row.Children.Add(button);
            button.Click += async (_, _) =>
            {
                try
                {
                    string? path;
                    if (folder)
                    {
                        var chosen = await StorageProvider.OpenFolderPickerAsync(new() { Title = T(label), AllowMultiple = false });
                        path = chosen.Count == 1 ? chosen[0].Path.LocalPath : null;
                    }
                    else
                    {
                        var chosen = await StorageProvider.OpenFilePickerAsync(new() { Title = T(label), AllowMultiple = false });
                        path = chosen.Count == 1 ? chosen[0].Path.LocalPath : null;
                    }
                    if (path != null)
                        box.Text = newFolder ? Path.Combine(path, "gmmt-linux-package")
                            : box.AcceptsReturn ? string.Join("\n", Lines(box).Append(path)) : path;
                }
                catch (Exception ex) { SetStatus("status.pathError"); status.Foreground = Brush.Parse("#FFB4AB"); SetLog(() => ex.Message); }
            };
        }
        field.Children.Add(row); return field;
    }

    private Button Action(string title, Func<Task> action)
    {
        var button = new Button { MinHeight = 40, Padding = new Thickness(18, 8) };
        Bind(() => button.Content = T(title));
        button.Click += async (_, _) =>
        {
            language.IsEnabled = false;
            tabs.IsEnabled = false; catalog.IsEnabled = false; progress.IsVisible = true;
            var currentTitle = button.Content?.ToString() ?? T(title);
            SetStatus("status.busy", currentTitle); status.Foreground = Muted; SetLog(() => T("busy.files"));
            try
            {
                await action();
                if (status.Text == F("status.busy", currentTitle)) { SetStatus("status.done"); status.Foreground = Accent; }
            }
            catch (Exception ex) { SetStatus("status.failed"); status.Foreground = Brush.Parse("#FFB4AB"); SetLog(() => F("error.detail", ex.Data["GmmtLocalizationKey"] is string key ? T(key) : ex.Message)); }
            finally { tabs.IsEnabled = true; catalog.IsEnabled = true; progress.IsVisible = false; language.IsEnabled = true; }
        };
        return button;
    }

    private static string Value(TextBox box) => box.Text?.Trim() ?? "";
    private static string[] Lines(TextBox box) => Value(box).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private Task ListRunners()
    {
        var profiles = new RuntimeCatalog(Value(catalog)).Read();
        SetLog(() => profiles.Count == 0 ? T("catalog.empty") : string.Join("\n", profiles.Select(p => $"{p.Id}: GMS {p.EngineVersion}, BC{p.BytecodeVersion}, ELF{p.Elf.Bits}\n{p.RunnerPath}")));
        return Task.CompletedTask;
    }
    private async Task Register()
    {
        var database = new RuntimeCatalog(Value(catalog));
        var id = Value(donorId); var runner = Value(donorRunner); var archive = Value(reference); var script = Value(runtime);
        var profile = await Task.Run(() => database.Register(id, runner, archive, script.Length == 0 ? null : script));
        SetLog(() => F("runner.registered", profile.Id, profile.EngineVersion, profile.BytecodeVersion, profile.Elf.Bits));
    }
    private async Task Discover()
    {
        var extra = Lines(extraRunnerRoots);
        var found = await Task.Run(() => SteamDiscovery.Scan(runnerRoots: extra.Length == 0 ? null : extra));
        lastDiscovery = found;
        discoveredGames.ItemsSource = found.Games;
        discoveredRunners.ItemsSource = found.Runners;
        var game = found.Games.FirstOrDefault(g => g.Directory == Value(steamDirectory))
            ?? found.Games.FirstOrDefault(g => g.Name.Equals("Undertale", StringComparison.OrdinalIgnoreCase)) ?? found.Games.FirstOrDefault();
        if (game != null && (Value(steamDirectory).Length == 0 || found.Games.Any(g => g.Directory == Value(steamDirectory)))) discoveredGames.SelectedItem = game;
        if (Value(donorRunner).Length == 0 && found.Runners.Length != 0) discoveredRunners.SelectedIndex = 0;
        if (string.IsNullOrWhiteSpace(Value(vanilla)) && found.Depots.Length != 0)
        {
            var matchingDepot = (discoveredGames.SelectedItem is DiscoveredGame dg && dg.AppId != null
                ? found.Depots.FirstOrDefault(d => d.AppId == dg.AppId)
                : null) ?? found.Depots.FirstOrDefault();
            if (matchingDepot != null) vanilla.Text = matchingDepot.ArchivePath;
        }
        if (!tabs.IsEnabled) return;
        SetLog(() => F("discovery.summary", found.Libraries.Length, found.Games.Length, found.Runners.Length)
            + T("discovery.instructions")
            + (found.Warnings.Length == 0 ? "" : "\n" + string.Join("\n", found.Warnings)));
    }
    private static void OpenSteamUri(string uri)
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = uri,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "steam",
                    Arguments = uri,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }
    }
    private async Task DownloadDepotPrompt()
    {
        var appId = (discoveredGames.SelectedItem is DiscoveredGame dg && dg.AppId != null) ? dg.AppId : "391540";
        var depotId = appId == "391540" ? "391541" : (appId + "1");
        OpenSteamUri("steam://open/console");
        if (Clipboard != null)
        {
            try { await Clipboard.SetTextAsync($"download_depot {appId} {depotId}"); } catch { }
        }
        SetLog(() => F("steam.depotInstructions", appId, depotId));
    }
    private Task InstallDeltarune()
    {
        OpenSteamUri("steam://install/1671210");
        SetLog(() => T("steam.deltaruneStarted"));
        return Task.CompletedTask;
    }
    private async Task RestoreSteam()
    {
        var game = Value(steamDirectory);
        await Task.Run(() => SteamInstaller.Restore(game));
        SetLog(() => F("steam.restored", game));
    }
    private async Task Build(bool create)
    {
        var database = new RuntimeCatalog(Value(catalog));
        var mod = Value(input); var baseline = Value(vanilla); var id = Value(runnerId); var destination = Value(output);
        var resourcePaths = Lines(assets); var libraryPaths = Lines(libraries); var acknowledged = reviewed.IsChecked == true;
        var standalone = separate.IsChecked == true; var game = Value(steamDirectory);
        if (string.IsNullOrWhiteSpace(baseline) && mod.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase) && lastDiscovery?.Depots.Length > 0)
        {
            var matchingDepot = (discoveredGames.SelectedItem is DiscoveredGame dg && dg.AppId != null
                ? lastDiscovery.Depots.FirstOrDefault(d => d.AppId == dg.AppId)
                : null) ?? lastDiscovery.Depots.FirstOrDefault();
            if (matchingDepot != null)
            {
                baseline = matchingDepot.ArchivePath;
                vanilla.Text = baseline;
            }
        }
        var fallbackRunners = lastDiscovery?.Runners;
        var result = await Task.Run(async () =>
        {
            using var prepared = await ArchiveInput.PrepareAsync(mod, baseline, mod.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase));
            var plan = database.Plan(ArchiveInspector.Inspect(prepared.Path), id.Length == 0 ? null : id, fallbackRunners);
            string? built = null, backup = null;
            if (create)
            {
                if (standalone)
                {
                    if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException(T("error.output")) { Data = { ["GmmtLocalizationKey"] = "error.output" } };
                    built = PackageBuilder.Create(plan, destination, resourcePaths, libraryPaths, acknowledged, prepared.InputHashes);

                }
                else
                {
                    if (string.IsNullOrWhiteSpace(game)) throw new ArgumentException(T("error.steam")) { Data = { ["GmmtLocalizationKey"] = "error.steam" } };
                    var temporaryPackage = Path.Combine(Path.GetTempPath(), "gmmt-steam-package-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        var originalAssets = Path.Combine(game, "assets");
                        var originalLibraries = Path.Combine(game, "lib");
                        var resources = Directory.Exists(originalAssets) ? new[] { originalAssets }.Concat(resourcePaths).ToArray() : resourcePaths;
                        var dependencies = Directory.Exists(originalLibraries) ? new[] { originalLibraries }.Concat(libraryPaths).ToArray() : libraryPaths;
                        PackageBuilder.Create(plan, temporaryPackage, resources, dependencies, acknowledged, prepared.InputHashes);
                        backup = SteamInstaller.Install(temporaryPackage, game);

                    }
                    finally { if (Directory.Exists(temporaryPackage)) Directory.Delete(temporaryPackage, true); }
                }
            }
            return (Plan: plan, Built: built, Backup: backup);
        });
        SetLog(() => F("build.summary", result.Plan.Archive.Metadata.GameName, result.Plan.Archive.Metadata.VersionString,
                result.Plan.Archive.Metadata.BytecodeVersion, result.Plan.Runner?.Id ?? T("runner.missing"))
            + T(result.Plan.Evidence == "SameArchiveAsReference" ? "evidence.exact" : "evidence.metadata")
            + string.Join("\n", result.Plan.Blockers.Concat(result.Plan.Warnings))
            + (result.Built == null ? "" : F("build.complete", result.Built))
            + (result.Backup == null ? "" : F("steam.complete", game, result.Backup)));
        if (!result.Plan.CanPackage)
        {
            SetStatus("status.blocked");
            status.Foreground = Brush.Parse("#FFCE8C");
        }
    }
}
