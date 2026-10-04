using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

    public MainWindow()
    {
        Title = "GMMT · Linux runner packages";
        Width = 920; Height = 820; MinWidth = 650; MinHeight = 600;
        catalog.Text = Environment.GetEnvironmentVariable("GMMT_CATALOG") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "gmmt", "runners.json");
        var build = new StackPanel { Spacing = 10 };
        build.Children.Add(Note("Соберите отдельную Linux-копию мода. Игровой архив сохраняется без изменений."));
        build.Children.Add(Field("Мод: data.win / game.unx или xdelta-патч", input));
        build.Children.Add(Field("Чистый Windows-архив (нужен только для xdelta)", vanilla));
        build.Children.Add(Field("Папки ресурсов", assets, true));
        build.Children.Add(Field("Папки Linux-библиотек (необязательно)", libraries, true));
        build.Children.Add(Field("Новая папка результата", output, true, true));
        build.Children.Add(Field("ID runner из каталога", runnerId, noBrowse: true));
        build.Children.Add(reviewed);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(Action("Проверить подбор", () => Build(false)));
        actions.Children.Add(Action("Собрать Linux-пакет", () => Build(true)));
        build.Children.Add(actions);
        build.Children.Add(Note("Подбор по версии — предварительная совместимость. Запуск и прохождение проверяются отдельно."));
        var donors = new StackPanel { Spacing = 10 };
        donors.Children.Add(Note("Добавьте локальный Linux-runner и эталонный архив, который работает с ним. GMMT не скачивает и не распространяет движки."));
        donors.Children.Add(Field("Уникальный ID", donorId, noBrowse: true));
        donors.Children.Add(Field("Linux-runner (ELF)", donorRunner));
        donors.Children.Add(Field("Работающий с ним эталонный архив", reference));
        donors.Children.Add(Field("Steam runtime run.sh (необязательно)", runtime));
        donors.Children.Add(Action("Добавить runner", Register));
        donors.Children.Add(Action("Показать каталог", ListRunners));
        tabs.ItemsSource = new[] { new TabItem { Header = "Сборка", Content = build }, new TabItem { Header = "Раннеры", Content = donors } };
        var page = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        page.Children.Add(new TextBlock { Text = "GMMT", FontSize = 28, FontWeight = FontWeight.SemiBold });
        page.Children.Add(Field("Файл каталога runner", catalog));
        page.Children.Add(tabs);
        page.Children.Add(new TextBlock { Text = "Результат", FontWeight = FontWeight.SemiBold });
        page.Children.Add(log);
        Content = new ScrollViewer { Content = page };
        log.Text = "Добавьте runner во вкладке «Раннеры», затем выберите мод и папки ресурсов. Старая программа сохранена в old/translation.";
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 820 };
    private Control Field(string label, TextBox box, bool folder = false, bool newFolder = false, bool noBrowse = false)
    {
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(box);
        if (!noBrowse)
        {
            var button = new Button { Content = newFolder ? "Родительская папка…" : "Выбрать…", Margin = new Thickness(8, 0, 0, 0) };
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
                catch (Exception ex) { log.Text = ex.Message; }
            };
        }
        field.Children.Add(row); return field;
    }

    private Button Action(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) =>
        {
            tabs.IsEnabled = false; catalog.IsEnabled = false; log.Text = "Работаю…";
            try { await action(); }
            catch (Exception ex) { log.Text = "Ошибка: " + ex.Message; }
            finally { tabs.IsEnabled = true; catalog.IsEnabled = true; }
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
            return text;
        });
        log.Text = result;
    }
}
