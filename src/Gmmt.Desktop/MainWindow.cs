using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using Gmmt.Runtime;

namespace Gmmt.Desktop;

public sealed class MainWindow : Window
{
    private readonly TextBox catalog = new();
    private readonly TextBox input = new();
    private readonly TextBox vanilla = new();
    private readonly TextBox assets = new() { AcceptsReturn = true, MinHeight = 64, Watermark = "По одной папке на строку. Сначала оригинальные ресурсы, затем ресурсы мода." };
    private readonly TextBox libraries = new() { Watermark = "Папки Linux-библиотек, по одной на строку", AcceptsReturn = true };
    private readonly TextBox output = new();
    private readonly TextBox runnerId = new() { Watermark = "Пусто — автоматический подбор" };
    private readonly TextBox donorId = new();
    private readonly TextBox donorRunner = new();
    private readonly TextBox reference = new();
    private readonly TextBox runtime = new() { Watermark = "Необязательно: Steam scout steam-runtime/run.sh" };
    private readonly CheckBox reviewed = new() { Content = "Нативные расширения проверены, Linux-зависимости подготовлены" };
    private readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
    private readonly TabControl tabs = new();
    private readonly TextBlock status = new() { Text = "Готов к работе", FontWeight = FontWeight.SemiBold };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, IsVisible = false, Height = 3 };
    private static readonly IBrush Muted = Brush.Parse("#B4B8C0");
    private static readonly IBrush Accent = Brush.Parse("#9DDFC5");

    public MainWindow()
    {
        Title = "GMMT · Моды для Linux";
        Width = 980; Height = 860; MinWidth = 700; MinHeight = 640;
        Background = Brush.Parse("#191B20");
        catalog.Text = Environment.GetEnvironmentVariable("GMMT_CATALOG") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners.json");
        input.Watermark = "Выберите архив мода или .xdelta";
        output.Watermark = "Новая папка для готовой игры";
        donorId.Watermark = "Например, undertale-linux";
        log.Text = "Добавьте локальный runner во вкладке «Раннеры». Затем выберите мод и папки ресурсов.";
        log.Background = Brush.Parse("#202329");
        log.BorderThickness = new Thickness(0);
        log.Padding = new Thickness(14);
        log.MinHeight = 0;
        log.FontSize = 13;

        var build = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 20) };
        build.Children.Add(Heading("Подготовьте мод", "GMMT подбирает Linux-runner и собирает отдельную копию игры."));
        build.Children.Add(Field("Архив мода или xdelta-патч", input));
        build.Children.Add(Field("Папки ресурсов", assets, true));
        build.Children.Add(Field("Папка результата", output, true, true));
        var advanced = new StackPanel { Spacing = 14, Margin = new Thickness(0, 12, 0, 8) };
        advanced.Children.Add(Field("Чистый Windows-архив · только для xdelta", vanilla));
        advanced.Children.Add(Field("Linux-библиотеки · необязательно", libraries, true));
        advanced.Children.Add(Field("ID runner · пусто для автоматического подбора", runnerId, noBrowse: true));
        reviewed.Content = new TextBlock { Text = "Нативные расширения проверены, Linux-зависимости подготовлены", TextWrapping = TextWrapping.Wrap };
        advanced.Children.Add(reviewed);
        build.Children.Add(new Expander { Header = "Дополнительные настройки и xdelta", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var primary = Action("Собрать Linux-пакет", () => Build(true));
        primary.Classes.Add("primary");
        primary.Margin = new Thickness(0, 0, 12, 8);
        actions.Children.Add(primary);
        var inspect = Action("Проверить совместимость", () => Build(false));
        inspect.Margin = new Thickness(0, 0, 0, 8);
        actions.Children.Add(inspect);
        build.Children.Add(actions);
        build.Children.Add(Note("Архив игры сохраняется без изменений. Подбор по версии требует отдельной проверки запуска и прохождения."));

        var donors = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 20) };
        donors.Children.Add(Heading("Каталог раннеров", "Добавьте Linux-runner и эталонный архив, который уже работает с ним."));
        donors.Children.Add(Field("Файл каталога", catalog));
        donors.Children.Add(Field("Уникальный ID", donorId, noBrowse: true));
        donors.Children.Add(Field("Linux-runner · ELF", donorRunner));
        donors.Children.Add(Field("Эталонный игровой архив", reference));
        donors.Children.Add(new Expander { Header = "Steam runtime · необязательно", Content = Field("Steam scout: steam-runtime/run.sh", runtime), HorizontalAlignment = HorizontalAlignment.Stretch });
        var donorActions = new WrapPanel();
        var add = Action("Добавить runner", Register); add.Classes.Add("primary"); add.Margin = new Thickness(0, 0, 12, 8);
        donorActions.Children.Add(add);
        donorActions.Children.Add(Action("Показать каталог", ListRunners));
        donors.Children.Add(donorActions);
        donors.Children.Add(Note("Раннеры берутся из ваших локальных игр. GMMT не скачивает и не распространяет движки."));
        tabs.ItemsSource = new[] {
            new TabItem { Header = "Сборка мода", Content = new ScrollViewer { Content = build } },
            new TabItem { Header = "Раннеры", Content = new ScrollViewer { Content = donors } }
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = "GMMT", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Accent });
        var subtitle = new TextBlock { Text = "Моды GameMaker для Linux", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        Grid.SetColumn(subtitle, 1); header.Children.Add(subtitle);
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
    }

    private static Control Heading(string title, string description)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Note(description));
        return panel;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 820, Foreground = Muted, FontSize = 13, LineHeight = 20 };
    private Control Field(string label, TextBox box, bool folder = false, bool newFolder = false, bool noBrowse = false)
    {
        var field = new StackPanel { Spacing = 7 };
        field.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.Medium });
        AutomationProperties.SetName(box, label);
        box.MinHeight = 38;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(box);
        if (!noBrowse)
        {
            var button = new Button { Content = newFolder ? "Куда сохранить…" : "Обзор…", Margin = new Thickness(8, 0, 0, 0), MinHeight = 38 };
            Grid.SetColumn(button, 1); row.Children.Add(button);
            button.Click += async (_, _) =>
            {
                try
                {
                    string? path;
                    if (folder)
                    {
                        var chosen = await StorageProvider.OpenFolderPickerAsync(new() { Title = label, AllowMultiple = false });
                        path = chosen.Count == 1 ? chosen[0].Path.LocalPath : null;
                    }
                    else
                    {
                        var chosen = await StorageProvider.OpenFilePickerAsync(new() { Title = label, AllowMultiple = false });
                        path = chosen.Count == 1 ? chosen[0].Path.LocalPath : null;
                    }
                    if (path != null)
                        box.Text = newFolder ? Path.Combine(path, "gmmt-linux-package")
                            : box == assets || box == libraries ? string.Join("\n", Lines(box).Append(path)) : path;
                }
                catch (Exception ex) { status.Text = "Не удалось выбрать путь"; status.Foreground = Brush.Parse("#FFB4AB"); log.Text = ex.Message; }
            };
        }
        field.Children.Add(row); return field;
    }

    private Button Action(string title, Func<Task> action)
    {
        var button = new Button { Content = title, MinHeight = 40, Padding = new Thickness(18, 8) };
        button.Click += async (_, _) =>
        {
            tabs.IsEnabled = false; catalog.IsEnabled = false; progress.IsVisible = true;
            status.Text = "Выполняется: " + title; status.Foreground = Muted; log.Text = "Подготавливаю файлы…";
            try
            {
                await action();
                if (status.Text == "Выполняется: " + title) { status.Text = "Готово"; status.Foreground = Accent; }
            }
            catch (Exception ex) { status.Text = "Не удалось выполнить действие"; status.Foreground = Brush.Parse("#FFB4AB"); log.Text = "Ошибка: " + ex.Message; }
            finally { tabs.IsEnabled = true; catalog.IsEnabled = true; progress.IsVisible = false; }
        };
        return button;
    }

    private static string Value(TextBox box) => box.Text?.Trim() ?? "";
    private static string[] Lines(TextBox box) => Value(box).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private Task ListRunners()
    {
        var profiles = new RuntimeCatalog(Value(catalog)).Read();
        log.Text = profiles.Count == 0 ? "Каталог пуст." : string.Join("\n", profiles.Select(p => $"{p.Id}: GMS {p.EngineVersion}, BC{p.BytecodeVersion}, ELF{p.Elf.Bits}\n{p.RunnerPath}"));
        return Task.CompletedTask;
    }
    private async Task Register()
    {
        var database = new RuntimeCatalog(Value(catalog));
        var id = Value(donorId); var runner = Value(donorRunner); var archive = Value(reference); var script = Value(runtime);
        var profile = await Task.Run(() => database.Register(id, runner, archive, script.Length == 0 ? null : script));
        log.Text = $"Добавлен {profile.Id}: GMS {profile.EngineVersion}, BC{profile.BytecodeVersion}, ELF{profile.Elf.Bits}.";
    }
    private async Task Build(bool create)
    {
        var database = new RuntimeCatalog(Value(catalog));
        var mod = Value(input); var baseline = Value(vanilla); var id = Value(runnerId); var destination = Value(output);
        var resourcePaths = Lines(assets); var libraryPaths = Lines(libraries); var acknowledged = reviewed.IsChecked == true;
        var result = await Task.Run(async () =>
        {
            using var prepared = await ArchiveInput.PrepareAsync(mod, baseline, mod.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase));
            var plan = database.Plan(ArchiveInspector.Inspect(prepared.Path), id.Length == 0 ? null : id);
            var text = $"{plan.Archive.Metadata.GameName}: GMS {plan.Archive.Metadata.VersionString}, BC{plan.Archive.Metadata.BytecodeVersion}\n"
                + $"Runner: {plan.Runner?.Id ?? "не найден"}\n"
                + (plan.Evidence == "SameArchiveAsReference" ? "Архив совпадает с эталоном по SHA256.\n" : "Совместимость предполагается по метаданным.\n")
                + string.Join("\n", plan.Blockers.Concat(plan.Warnings));
            if (create)
            {
                if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Выберите новую папку результата.");
                var built = PackageBuilder.Create(plan, destination, resourcePaths, libraryPaths, acknowledged, prepared.InputHashes);
                text += "\n\nСобрано: " + built + "\nЗапуск: bash launch.sh. Прохождение ещё не проверено.";
            }
            return (Text: text, CanPackage: plan.CanPackage);
        });
        log.Text = result.Text;
        if (!result.CanPackage)
        {
            status.Text = "Подбор заблокирован · проверьте результат";
            status.Foreground = Brush.Parse("#FFCE8C");
        }
    }
}
