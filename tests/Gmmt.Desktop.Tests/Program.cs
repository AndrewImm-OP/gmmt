using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Gmmt.Desktop;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
var root = Path.Combine(Path.GetTempPath(), "gmmt-desktop-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var screenshots = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(root, "screenshots");
Directory.CreateDirectory(screenshots);
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; }
T Find<T>(Window window, string name) where T : Control => window.GetLogicalDescendants().OfType<T>().Distinct().Single(c => c.Name == name);
void Capture(Window window, string filename)
{
    Dispatcher.UIThread.RunJobs();
    using var image = window.CaptureRenderedFrame() ?? throw new Exception("Headless render failed");
    image.Save(Path.Combine(screenshots, filename));
}
try
{
    var settings = Path.Combine(root, "settings.json");
    var locale = new LocalizationService(settings, () => "en-US");
    var window = new MainWindow(locale, discoverOnOpen: false);
    window.Show();
    var language = Find<ComboBox>(window, "LanguageSelector");
    var input = Find<TextBox>(window, "ModArchive");
    var catalog = Find<TextBox>(window, "RunnerCatalog");
    var separate = Find<CheckBox>(window, "SeparateOutput");
    var tabs = Find<TabControl>(window, "WorkflowTabs");
    Check(window.Title == "GMMT · Mods for Linux" && language.SelectedIndex == 0, "System English UI failed");
    Capture(window, "desktop-en.png");
    input.Text = "/tmp/preserved-mod.win";
    catalog.Text = Path.Combine(root, "empty-catalog.json");
    separate.IsChecked = false;
    tabs.SelectedIndex = 2;
    var list = window.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Content?.ToString() == "Show catalog");
    list.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(Find<TextBox>(window, "OperationLog").Text == "The catalog is empty.", "English catalog result failed");
    language.SelectedIndex = 2;
    Check(window.Title == "GMMT · Моды для Linux" && locale.Preference == "ru", "Live Russian selection failed");
    Check(input.Text == "/tmp/preserved-mod.win" && tabs.SelectedIndex == 2 && separate.IsChecked == false, "Language change reset workflow state");
    Check(Find<TextBox>(window, "OperationLog").Text == "Каталог пуст." && Find<TextBlock>(window, "OperationStatus").Text == "Готово", "Result/status were not retranslated");
    Check(window.GetLogicalDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Установить мод в Steam"), "Dynamic installation button was not translated");
    tabs.SelectedIndex = 0;
    // Keep public documentation images free of input paths.
    input.Text = "";
    separate.IsChecked = true;
    Capture(window, "desktop-ru.png");
    window.Width = 700; window.Height = 640;
    Capture(window, "desktop-ru-narrow.png");
    language.SelectedIndex = 1;
    Check(window.Title == "GMMT · Mods for Linux" && input.Text == "", "Live English selection failed");
    Capture(window, "desktop-en-narrow.png");
    var reopened = new MainWindow(new LocalizationService(settings, () => "ru-RU"), discoverOnOpen: false);
    Check(reopened.Title == "GMMT · Mods for Linux" && Find<ComboBox>(reopened, "LanguageSelector").SelectedIndex == 1, "Saved override did not survive window recreation");
    reopened.Close(); window.Close();
    Console.WriteLine($"PASS: {passed} desktop localization interaction checks; screenshots: {screenshots}");
}
finally { Directory.Delete(root, true); }
