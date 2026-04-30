using Avalonia.Controls;

namespace Gmmt.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.LogUpdated += OnLogUpdated;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.LogUpdated -= OnLogUpdated;
        }

        base.OnClosed(e);
    }

    private void OnLogUpdated()
    {
        // Auto-scroll log TextBox to the end
        var logBox = this.FindControl<TextBox>("LogBox");
        if (logBox is null) return;

        // Set caret to end to trigger scroll
        var text = logBox.Text;
        if (text is not null)
        {
            logBox.CaretIndex = text.Length;
        }
    }
}
