using System.Linq;
using System.Windows;
using CodexSeek.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CodexSeek;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument =>
                string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SelfTest.Run());
            return;
        }

        try
        {
            ApplicationThemeManager.ApplySystemTheme(true);
        }
        catch
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
        }

        var window = new MainWindow();
        MainWindow = window;
        SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
        window.Show();
    }
}
