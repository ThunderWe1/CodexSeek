using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexSeek.Services;

/// <summary>
/// Starts, stops and detects the Codex desktop app (WindowsApps package).
/// </summary>
public static class CodexDesktopService
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const string FallbackAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    private static readonly Regex AppPathPattern = new(
        @"\\WindowsApps\\OpenAI\.Codex_[^\\]+\\app\\ChatGPT\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsRunning()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                var path = GetProcessPath(process.Id);
                if (!string.IsNullOrEmpty(path) && AppPathPattern.IsMatch(path))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static async Task StopAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var processIds = new List<int>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                var path = GetProcessPath(process.Id);
                if (!string.IsNullOrEmpty(path) && AppPathPattern.IsMatch(path))
                {
                    processIds.Add(process.Id);
                }
            }
        }

        if (processIds.Count == 0)
        {
            return;
        }

        foreach (var processId in processIds)
        {
            KillProcessTree(processId);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRunning())
            {
                return;
            }

            progress?.Report("正在等待 Codex 桌面 App 退出...");
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
    }

    public static void Start()
    {
        var appId = FindAppId() ?? FallbackAppId;
        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            $"\"shell:AppsFolder\\{appId}\"")
        {
            UseShellExecute = true
        });
    }

    public static async Task<bool> WaitForStartAsync(
        TimeSpan timeout,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsRunning())
            {
                return true;
            }

            progress?.Report("正在等待 Codex 桌面 App 启动...");
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return IsRunning();
    }

    private static void KillProcessTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return;
        }
        catch
        {
            // Fall back to taskkill below.
        }

        try
        {
            using var taskkill = Process.Start(new ProcessStartInfo(
                "taskkill.exe",
                $"/PID {processId} /T /F")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            taskkill?.WaitForExit(4000);
        }
        catch
        {
            // The caller verifies whether the process is still running.
        }
    }

    private static string? FindAppId()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            dynamic folder = shell.NameSpace("shell:AppsFolder");
            if (folder is null)
            {
                return null;
            }

            dynamic items = folder.Items();
            var count = (int)items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                string name = item.Name ?? string.Empty;
                string path = item.Path ?? string.Empty;

                if (path.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith("!App", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                if (name.Contains("Codex", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                {
                    if (path.EndsWith("!App", StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }
        }
        catch
        {
            // Fall back to the known package AppID.
        }

        return null;
    }

    private static string? GetProcessPath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString(0, size)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        StringBuilder exeName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
