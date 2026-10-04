using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;

namespace Gmmt.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Color.Parse("#9DDFC5");
        Resources["SystemAccentColorLight1"] = Color.Parse("#B8ECD9");
        Resources["SystemAccentColorDark1"] = Color.Parse("#70BEA2");
        var primary = new Style(x => x.OfType<Button>().Class("primary"));
        primary.Setters.Add(new Setter(Button.BackgroundProperty, Brush.Parse("#9DDFC5")));
        primary.Setters.Add(new Setter(Button.ForegroundProperty, Brush.Parse("#172B23")));
        primary.Setters.Add(new Setter(Button.FontWeightProperty, FontWeight.SemiBold));
        Styles.Add(primary);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
