using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Updates;
using ZeepCut.Platform;
using ZeepCut.Ui;

namespace ZeepCut.Updates;

/// <summary>
/// “检查更新”结果对话框（T32）：把 <see cref="UpdateCheckResult"/> 如实显示出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 <see cref="MessageBox"/></b>：PLAN 要求"配置后可以打开下载页"，
/// 而 MessageBox 只有固定按钮、不能放"打开发布页"；另外它不跟随主题与语言。
/// 这里是一个普通 WPF 窗口，用 <see cref="ThemeManager"/> 的语义色和 <see cref="StringsUi"/> 的文案。
/// </para>
/// <para>
/// <b>绝不说"已是最新版"</b>：四种结论各自有独立的标题与说明键，未配置渠道与网络失败
/// 分别显示"尚未配置更新渠道"/"检查更新失败"，并且**只有** <see cref="UpdateStatus.UpdateAvailable"/>
/// 才会出现"打开发布页"按钮。图标也按结论区分（信息/警告/错误），不会让失败看起来像成功。
/// </para>
/// </remarks>
internal sealed class UpdateCheckWindow : Window
{
    /// <summary>对话框的 AutomationId（验证脚本按它找窗口）。</summary>
    internal const string WindowAutomationId = "update-check-window";

    private readonly UpdateCheckResult _result;
    private readonly TextBlock _title;
    private readonly TextBlock _body;
    private readonly TextBlock _channel;
    private readonly Button _download;
    private readonly Button _ok;
    private readonly TextBlock _icon;

    private UpdateCheckWindow(UpdateCheckResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));

        var palette = ThemeManager.Palette;
        Title = StringsUi.Get("S.Update.Title");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 640;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily(ThemeManager.FontFamilyName);
        AutomationProperties.SetAutomationId(this, WindowAutomationId);
        AutomationProperties.SetName(this, Title);

        _icon = new TextBlock
        {
            Text = IconFor(result.Status),
            FontSize = 26,
            Foreground = BrushFor(result.Status, palette),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0),
        };

        _title = new TextBlock
        {
            Text = TitleFor(result),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.ForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_title, "update-check-title");
        AutomationProperties.SetName(_title, _title.Text);

        // 当前版本单独一行（任何结论下都要看得到"我是什么版本"）。
        var version = new TextBlock
        {
            Text = StringsUi.Format("S.Update.CurrentVersion", result.CurrentVersion),
            FontSize = 12.5,
            Foreground = palette.SecondaryForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AutomationProperties.SetAutomationId(version, "update-check-current-version");

        _body = new TextBlock
        {
            Text = BodyFor(result),
            FontSize = 12.5,
            Foreground = palette.ForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Margin = new Thickness(0, 10, 0, 0),
        };
        AutomationProperties.SetAutomationId(_body, "update-check-body");
        AutomationProperties.SetName(_body, _body.Text);

        _channel = new TextBlock
        {
            Text = StringsUi.Format(
                "S.Update.Channel",
                result.ManifestUrl ?? StringsUi.Get("S.Update.NotConfiguredTitle")),
            FontSize = 11,
            Foreground = palette.SecondaryForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(_channel, "update-check-channel");

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(_title, 1);

        var titleStack = new StackPanel();
        titleStack.Children.Add(_title);
        titleStack.Children.Add(version);
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(_icon);
        header.Children.Add(titleStack);

        var close = new Button
        {
            Content = "✕",
            FontSize = 13,
            Padding = new Thickness(6, 0, 6, 0),
            Background = Brushes.Transparent,
            Foreground = palette.SecondaryForegroundBrush,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);

        _download = MakeButton(
            StringsUi.Get("S.Update.Download"),
            "update-check-download",
            OpenDownloadPage,
            primary: true);
        _download.Visibility = result.DownloadUrl is null ? Visibility.Collapsed : Visibility.Visible;

        _ok = MakeButton(StringsUi.Get("S.Update.Ok"), "update-check-ok", Close, primary: false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(_download);
        buttons.Children.Add(_ok);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_body);
        stack.Children.Add(_channel);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = palette.SurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(palette.RadiusLarge),
            Padding = new Thickness(20, 16, 20, 16),
            Child = stack,
        };

        // 结果不依赖语言/主题重建：对话框是一次性的，切换语言后下次检查就是新语言。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    /// <summary>当前显示的结果（验证脚本/WARN 日志核对用）。</summary>
    internal UpdateCheckResult Result => _result;

    /// <summary>显示结果对话框。失败时返回 null 并写日志（不抛异常）。</summary>
    internal static UpdateCheckWindow? ShowResult(UpdateCheckResult result)
    {
        try
        {
            var window = new UpdateCheckWindow(result);
            window.Show();
            window.Activate();
            return window;
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", "显示检查更新结果窗口失败", ex);
            return null;
        }
    }

    private void OpenDownloadPage()
    {
        var url = _result.DownloadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            AppLog.Warn("Update", "打开发布页失败：清单里没有 url");
            return;
        }

        if (ExternalLinks.TryOpenUrl(url, out var error))
        {
            AppLog.Info("Update", $"已请求打开下载页：{url}");
            return;
        }

        AppLog.Warn("Update", $"打开发布页失败：{url}（{error}）");
        _channel.Text = StringsUi.Get("S.Update.NoUrl");
    }

    private static string TitleFor(UpdateCheckResult result) => result.Status switch
    {
        UpdateStatus.NotConfigured => StringsUi.Get("S.Update.NotConfiguredTitle"),
        UpdateStatus.Failed => StringsUi.Get("S.Update.FailedTitle"),
        UpdateStatus.UpToDate => StringsUi.Get("S.Update.UpToDateTitle"),
        UpdateStatus.UpdateAvailable => StringsUi.Format("S.Update.AvailableTitle", result.AvailableVersion ?? "?"),
        _ => StringsUi.Get("S.Update.Title"),
    };

    private static string BodyFor(UpdateCheckResult result) => result.Status switch
    {
        UpdateStatus.NotConfigured => StringsUi.Format("S.Update.NotConfigured", result.CurrentVersion),
        UpdateStatus.Failed => StringsUi.Format("S.Update.Failed", result.CurrentVersion, result.Detail),
        UpdateStatus.UpToDate => StringsUi.Format(
            "S.Update.UpToDate",
            result.CurrentVersion,
            result.Manifest?.VersionText ?? "?"),
        UpdateStatus.UpdateAvailable => string.IsNullOrWhiteSpace(result.Manifest?.Notes)
            ? StringsUi.Format("S.Update.Available", result.AvailableVersion ?? "?", result.CurrentVersion)
            : StringsUi.Format(
                "S.Update.AvailableWithNotes",
                result.AvailableVersion ?? "?",
                result.CurrentVersion,
                result.Manifest!.Notes),
        _ => result.Detail,
    };

    private static string IconFor(UpdateStatus status) => status switch
    {
        UpdateStatus.NotConfigured => "⚙",
        UpdateStatus.Failed => "⚠",
        UpdateStatus.UpToDate => "✓",
        UpdateStatus.UpdateAvailable => "↓",
        _ => "i",
    };

    private static Brush BrushFor(UpdateStatus status, ThemePalette palette) => status switch
    {
        UpdateStatus.NotConfigured => palette.WarningBrush,
        UpdateStatus.Failed => palette.ErrorBrush,
        _ => palette.AccentBrush,
    };

    private static Button MakeButton(string text, string automationId, Action onClick, bool primary)
    {
        var palette = ThemeManager.Palette;
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = primary ? palette.OnAccentBrush : palette.ForegroundBrush,
            },
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 84,
            Background = primary ? palette.AccentBrush : palette.SurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }
}
