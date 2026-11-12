using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CodexSeek.Dialogs;
using CodexSeek.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CodexSeek;

public partial class MainWindow : FluentWindow
{
    // Accent colours taken from the illustrations themselves: the maid's
    // periwinkle hair for DeepSeek, the elf's violet eyes for GPT.
    private static readonly Color DeepSeekAccent = Color.FromRgb(0x5B, 0x72, 0xC8);
    private static readonly Color GptAccent = Color.FromRgb(0x9B, 0x7B, 0xD4);

    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10));
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush DeepSeekAccentBrush = CreateFrozenBrush(DeepSeekAccent);
    private static readonly Brush GptAccentBrush = CreateFrozenBrush(GptAccent);

    private static readonly ImageSource? GptMascot = LoadMascot("Assets/GPT-waifu.png");
    private static readonly ImageSource? DeepSeekMascot = LoadMascot("Assets/DeepSeek-waifu.png");

    private readonly CodexPaths _paths;
    private readonly CodexConfigService _config;
    private CodexMode? _accentMode;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        _paths = new CodexPaths();
        _config = new CodexConfigService(_paths);

        SetupKeyBox.PasswordChanged += (_, _) => UpdatePlaceholders();
        ApiKeyBox.PasswordChanged += (_, _) => UpdatePlaceholders();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// WPF-UI's <see cref="Wpf.Ui.Controls.PasswordBox"/> has no placeholder
    /// property, so the hint is an overlay that is shown only while the field is
    /// empty.
    /// </summary>
    private void UpdatePlaceholders()
    {
        SetupKeyPlaceholder.Visibility = SetupKeyBox.Password.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApiKeyPlaceholder.Visibility = ApiKeyBox.Password.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshUi();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
        }
    }

    private void RefreshUi()
    {
        try
        {
            var environment = _config.DetectEnvironment();
            var model = _config.GetCurrentModel();
            var mode = _config.GetCurrentMode();
            var appRunning = CodexDesktopService.IsRunning();
            var key = _config.GetDeepSeekApiKey() ?? string.Empty;
            var canInteract = !_busy;

            CurrentModeText.Text = mode switch
            {
                CodexMode.Gpt => "GPT",
                CodexMode.DeepSeek => "DeepSeek",
                _ => "未知"
            };
            CurrentModeText.Foreground = mode switch
            {
                CodexMode.Gpt => GptAccentBrush,
                CodexMode.DeepSeek => DeepSeekAccentBrush,
                _ => GrayBrush
            };
            CurrentModelText.Text = string.IsNullOrWhiteSpace(model) ? "（未设置）" : model;
            CurrentModelText.ToolTip = model;

            // First run has no model to show yet, so the banner would only be an empty shell.
            BannerSurface.Visibility = environment.Ready ? Visibility.Visible : Visibility.Collapsed;

            UpdateAccent(mode);
            UpdateMascot(mode);

            EnvStatusText.Text = environment.Ready ? "已就绪" : "未初始化";
            EnvStatusText.Foreground = environment.Ready ? GreenBrush : GrayBrush;
            EnvDot.Fill = environment.Ready ? GreenBrush : GrayBrush;

            ConfigPathText.Text = environment.ConfigPath;
            ConfigPathButton.ToolTip = environment.ConfigPath;
            CatalogPathText.Text = environment.CatalogPath;
            CatalogPathButton.ToolTip = environment.CatalogPath;

            AppStatusText.Text = appRunning ? "运行中" : "未运行";
            AppStatusText.Foreground = appRunning ? GreenBrush : GrayBrush;
            AppDot.Fill = appRunning ? GreenBrush : GrayBrush;

            var target = mode == CodexMode.DeepSeek ? CodexMode.Gpt : CodexMode.DeepSeek;
            SwitchButton.Content = target == CodexMode.Gpt ? "切换到 GPT" : "切换到 DeepSeek";
            SwitchButton.IsEnabled = environment.Ready && canInteract;

            KeyHintText.Text = environment.ApiKeyPresent ? "已设置" : "未设置";
            ApiKeyBox.Password = key;
            UpdatePlaceholders();

            SetupCard.Visibility = environment.Ready ? Visibility.Collapsed : Visibility.Visible;
            MainContent.Visibility = environment.Ready ? Visibility.Visible : Visibility.Collapsed;

            SetupKeyBox.IsEnabled = canInteract;
            SetupButton.IsEnabled = canInteract;
            ApiKeyBox.IsEnabled = environment.Ready && canInteract;
            SaveKeyButton.IsEnabled = environment.Ready && canInteract;
            TestKeyButton.IsEnabled = environment.Ready && canInteract;
            RefreshButton.IsEnabled = canInteract;
        }
        catch
        {
            // Keep the last usable state; operation errors are shown in dialogs.
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshUi();
    }

    /// <summary>
    /// Tints the application accent with the colour of the character that
    /// represents the active model, so primary buttons, links, icons and the
    /// logo header follow the mascot instead of the system accent.
    /// </summary>
    private void UpdateAccent(CodexMode mode)
    {
        if (_accentMode == mode)
        {
            return;
        }

        _accentMode = mode;
        var accent = mode == CodexMode.Gpt ? GptAccent : DeepSeekAccent;

        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        try
        {
            // The single-colour overload derives the dark-theme accent by
            // desaturating it by 45 %, which washes a pastel character colour
            // out to near-white. Supplying the ramp keeps the fill vivid.
            ApplicationAccentColorManager.Apply(
                accent,
                Darken(accent, 0.15),
                accent,
                dark ? Lighten(accent, 0.18) : Darken(accent, 0.28));
        }
        catch
        {
            // Falling back to the system accent is better than failing to start.
        }

        ApplyBannerTint(accent);
        ApplyAccentButtons(accent);
    }

    /// <summary>
    /// WPF-UI derives the primary-button brushes from the theme dictionary, which is
    /// built from the system accent before the per-model colour is known. Pointing
    /// them at the mascot colour keeps primary buttons on-brand.
    /// </summary>
    private static void ApplyAccentButtons(Color accent)
    {
        var resources = Application.Current.Resources;
        resources["AccentButtonBackground"] = CreateFrozenBrush(accent);
        resources["AccentButtonBackgroundPointerOver"] = CreateFrozenBrush(Lighten(accent, 0.12));
        resources["AccentButtonBackgroundPressed"] = CreateFrozenBrush(Darken(accent, 0.08));
        resources["AccentButtonForeground"] = Brushes.White;
        resources["AccentButtonForegroundPointerOver"] = Brushes.White;
        resources["AccentButtonForegroundPressed"] = Brushes.White;
    }

    /// <summary>
    /// Paints the hero banner with a soft accent gradient so the block reads as
    /// a surface of its own instead of empty window background.
    /// </summary>
    private void ApplyBannerTint(Color accent)
    {
        var stops = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0x66, accent.R, accent.G, accent.B), 0.0),
            new GradientStop(Color.FromArgb(0x38, accent.R, accent.G, accent.B), 0.5),
            new GradientStop(Color.FromArgb(0x0A, accent.R, accent.G, accent.B), 1.0)
        };

        var fill = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0.45));
        fill.Freeze();

        var edge = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        edge.Freeze();

        BannerSurface.Background = fill;
        BannerSurface.BorderBrush = edge;
    }

    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount));

    private static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)),
        (byte)(color.G * (1 - amount)),
        (byte)(color.B * (1 - amount)));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Shows the character that belongs to the active model in the bottom-right
    /// corner.  The artwork is embedded as a resource; when it cannot be loaded
    /// the mascot simply stays hidden so the window still opens.
    /// </summary>
    private void UpdateMascot(CodexMode mode)
    {
        var source = mode == CodexMode.Gpt ? GptMascot : DeepSeekMascot;
        if (source is null)
        {
            MascotImage.Visibility = Visibility.Collapsed;
            return;
        }

        MascotImage.Visibility = Visibility.Visible;
        if (ReferenceEquals(MascotImage.Source, source))
        {
            return;
        }

        var hadImage = MascotImage.Source is not null;
        MascotImage.Source = source;

        if (!hadImage)
        {
            return;
        }

        MascotImage.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private static ImageSource? LoadMascot(string relativePath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/" + relativePath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private async void SetupButton_Click(object sender, RoutedEventArgs e)
    {
        var key = (SetupKeyBox.Password ?? string.Empty).Trim();
        if (!await ConfirmKeyAsync(key, "请输入 DeepSeek API Key。"))
        {
            return;
        }

        if (CodexDesktopService.IsRunning() &&
            !await ConfirmAsync(
                "初始化并重启 Codex",
                "Codex 正在运行。初始化会先关闭并重启 Codex，进行中的任务会中断。继续？",
                "继续",
                "取消"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _config.InitializeDeepSeekAsync(key);

            SetupKeyBox.Password = string.Empty;
            await ShowMessageAsync(
                "初始化完成",
                DescribeResult("DeepSeek 初始化完成。", result),
                "知道了");
        });
    }

    private async void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var target = _config.GetCurrentMode() == CodexMode.DeepSeek
            ? CodexMode.Gpt
            : CodexMode.DeepSeek;

        if (target == CodexMode.DeepSeek &&
            string.IsNullOrWhiteSpace(_config.GetDeepSeekApiKey()) &&
            !await ConfirmAsync(
                "未设置 API Key",
                "DeepSeek API Key 尚未设置，切换后 Codex 可能无法连接。仍要切换吗？",
                "仍然切换",
                "取消",
                danger: true))
        {
            return;
        }

        if (CodexDesktopService.IsRunning() &&
            !await ConfirmAsync(
                "切换并重启 Codex",
                "Codex 正在运行。切换会先关闭并重启 Codex，进行中的任务会中断。继续？",
                "继续切换",
                "取消"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _config.SwitchAsync(target);
            var targetName = result.Target == CodexMode.Gpt ? "GPT" : "DeepSeek";

            await ShowMessageAsync(
                "切换成功",
                DescribeResult($"已切换到 {targetName}。", result),
                "知道了");
        });
    }

    private async void SaveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = (ApiKeyBox.Password ?? string.Empty).Trim();
        if (!await ConfirmKeyAsync(key, "请输入 DeepSeek API Key。"))
        {
            return;
        }

        if (IsCurrentApiKey(key))
        {
            await ShowMessageAsync("无需修改", "API Key 没有变化。", "知道了");
            return;
        }

        if (CodexDesktopService.IsRunning() &&
            !await ConfirmAsync(
                "保存并重启 Codex",
                "Codex 正在运行。保存 API Key 会先关闭并重启 Codex，进行中的任务会中断。继续？",
                "继续",
                "取消"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _config.SaveDeepSeekApiKeyAsync(key);

            if (!result.Changed)
            {
                await ShowMessageAsync("无需修改", "API Key 没有变化。", "知道了");
                return;
            }

            var message = "API Key 已保存。";
            if (result.BackupPath is not null)
            {
                message += "\n\n备份：" + result.BackupPath;
            }

            if (result.AppWasRunning)
            {
                message += result.AppRestarted
                    ? "\n\nCodex 已重启。"
                    : "\n\nCodex 未能自动重启，请手动打开。";
            }
            else
            {
                message += "\n\nCodex 未运行，未自动打开。";
            }

            await ShowMessageAsync("保存成功", message, "知道了");
        });
    }

    private async void TestKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = (ApiKeyBox.Password ?? string.Empty).Trim();
        if (!await ConfirmKeyAsync(key, "请先输入要测试的 API Key。"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var environment = _config.DetectEnvironment();
            var result = await DeepSeekApiService.TestAsync(key, environment.BaseUrl);

            await ShowMessageAsync(
                result.Success ? "连接成功" : "连接失败",
                result.Message,
                "知道了",
                danger: !result.Success);
        });
    }

    private async void ConfigPathButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenAndSelectAsync(_paths.ConfigPath);
    }

    private async void CatalogPathButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenAndSelectAsync(_paths.CatalogPath);
    }

    private async Task OpenAndSelectAsync(string path)
    {
        try
        {
            var fileExists = File.Exists(path);
            var folder = Path.GetDirectoryName(path);
            if (!fileExists && (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)))
            {
                throw new FileNotFoundException("文件及其所在目录都不存在。", path);
            }

            var arguments = fileExists
                ? $"/select,\"{path}\""
                : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("打开失败", exception.Message, "知道了", danger: true);
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        BusyRing.Visibility = Visibility.Visible;
        RefreshUi();

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("操作失败", exception.Message, "知道了", danger: true);
        }
        finally
        {
            _busy = false;
            BusyRing.Visibility = Visibility.Collapsed;
            RefreshUi();
        }
    }

    private async Task<bool> ConfirmKeyAsync(string key, string emptyMessage)
    {
        if (_busy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            await ShowMessageAsync("API Key 为空", emptyMessage, "知道了", danger: true);
            return false;
        }

        if (!CodexConfigService.LooksLikeDeepSeekKey(key) &&
            !await ConfirmAsync(
                "API Key 格式",
                "输入的 Key 看起来不像 DeepSeek API Key（通常以 sk- 开头）。仍要继续吗？",
                "仍然继续",
                "取消",
                danger: true))
        {
            return false;
        }

        return true;
    }

    private bool IsCurrentApiKey(string key)
    {
        try
        {
            return string.Equals(
                _config.GetDeepSeekApiKey(),
                CodexConfigService.NormalizeAndValidateKey(key),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeResult(string prefix, SwitchResult result)
    {
        var message = prefix;
        if (result.BackupPath is not null)
        {
            message += "\n\n备份：" + result.BackupPath;
        }

        if (result.AppWasRunning)
        {
            message += result.AppRestarted
                ? "\n\nCodex 已重启。"
                : "\n\nCodex 未能自动重启，请手动打开。";
        }
        else
        {
            message += "\n\nCodex 未运行，未自动打开。";
        }

        if (result.Warnings.Count > 0)
        {
            message += "\n\n" + string.Join("\n", result.Warnings);
        }

        return message;
    }

    private Task<bool> ConfirmAsync(
        string title,
        string message,
        string primary,
        string secondary,
        bool danger = false)
    {
        var result = ShowFluentDialog(title, message, primary, secondary, danger);
        return Task.FromResult(result == true);
    }

    private Task ShowMessageAsync(
        string title,
        string message,
        string primary = "确定",
        bool danger = false)
    {
        ShowFluentDialog(title, message, primary, null, danger);
        return Task.CompletedTask;
    }

    private bool? ShowFluentDialog(
        string title,
        string message,
        string primary,
        string? secondary,
        bool danger)
    {
        var dialog = new FluentDialog(title, message, primary, secondary, danger)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return dialog.ShowDialog();
    }
}
