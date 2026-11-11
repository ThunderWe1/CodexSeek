using System.Runtime.InteropServices;

namespace CodexSeek;

/// <summary>
/// Custom entry point so DPI awareness is set before WPF creates any window.
/// The apphost also carries a PerMonitorV2 manifest, but this keeps the same
/// behavior when the assembly is launched by a diagnostics host.
/// </summary>
public static class Program
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            try
            {
                SetProcessDpiAwareness(2);
            }
            catch
            {
                // Keep the default awareness on older Windows versions.
            }
        }

        var application = new App();
        application.InitializeComponent();
        return application.Run();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int dpiAwareness);
}
