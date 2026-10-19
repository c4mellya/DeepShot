using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Updates;
using ZeepCut.History;
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

    private UpdateCheckWindow(UpdateCheckResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));

        Title = StringsUi.Get("S.Update.Title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, ThemeManager.BrushKeys.WindowBackground);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, WindowAutomationId);
        AutomationProperties.SetName(this, Title);

        // 状态徽标：淡色圆底 + 矢量图标。
        var (iconData, toneKey) = ToneFor(result.Status);
        var tint = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(24), Opacity = 0.16 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var glyph = HistoryIcons.Create(iconData, 24, toneKey, 2.0);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Grid { Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Center };
        badge.Children.Add(tint);
        badge.Children.Add(glyph);

        _title = new TextBlock
        {
            Text = TitleFor(result),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        _title.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.Foreground);
        AutomationProperties.SetAutomationId(_title, "update-check-title");
        AutomationProperties.SetName(_title, _title.Text);

        // 当前版本单独一行（任何结论下都要看得到"我是什么版本"）。
        var version = new TextBlock
        {
            Text = StringsUi.Format("S.Update.CurrentVersion", result.CurrentVersion),
            FontSize = 12.5,
            Margin = new Thickness(0, 3, 0, 0),
        };
        version.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.SecondaryForeground);
        AutomationProperties.SetAutomationId(version, "update-check-current-version");

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        titleStack.Children.Add(_title);
        titleStack.Children.Add(version);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(badge);
        header.Children.Add(titleStack);

        // 只有“失败原因 / 更新说明”才有正文；其余结论标题已经说清楚，不再重复。
        _body = new TextBlock
        {
            Text = BodyFor(result),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0),
            Visibility = string.IsNullOrWhiteSpace(BodyFor(result)) ? Visibility.Collapsed : Visibility.Visible,
        };
        _body.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.SecondaryForeground);
        AutomationProperties.SetAutomationId(_body, "update-check-body");
        AutomationProperties.SetName(_body, _body.Text);

        // 仅在打开发布页失败时才出现。
        _channel = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _channel.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.Error);
        AutomationProperties.SetAutomationId(_channel, "update-check-channel");

        _download = MakeButton(
            StringsUi.Get("S.Update.Download"),
            "update-check-download",
            OpenDownloadPage,
            primary: true);
        _download.Visibility = result.DownloadUrl is null ? Visibility.Collapsed : Visibility.Visible;

        _ok = MakeButton(StringsUi.Get("S.Update.Ok"), "update-check-ok", Close, primary: _download.Visibility != Visibility.Visible);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0),
        };
        buttons.Children.Add(_ok);
        buttons.Children.Add(_download);

        var stack = new StackPanel { Margin = new Thickness(28, 12, 28, 24) };
        stack.Children.Add(header);
        stack.Children.Add(_body);
        stack.Children.Add(_channel);
        stack.Children.Add(buttons);
        Content = stack;
        AppChrome.Apply(this, allowMaximize: false, allowMinimize: false);

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
        _channel.Visibility = Visibility.Visible;
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
        UpdateStatus.Failed => result.Detail,
        UpdateStatus.UpdateAvailable => result.Manifest?.Notes ?? string.Empty,
        _ => string.Empty,
    };

    private static (string Icon, string ToneKey) ToneFor(UpdateStatus status) => status switch
    {
        UpdateStatus.NotConfigured => (HistoryIcons.Settings, ThemeManager.BrushKeys.Warning),
        UpdateStatus.Failed => ("M12,3 L22,20 H2 Z M12,10 V14 M12,17 V17.5", ThemeManager.BrushKeys.Error),
        UpdateStatus.UpToDate => ("M5,13 L10,18 L19,7", ThemeManager.BrushKeys.Success),
        UpdateStatus.UpdateAvailable => (HistoryIcons.SaveAs, ThemeManager.BrushKeys.Accent),
        _ => (HistoryIcons.Image, ThemeManager.BrushKeys.Accent),
    };

    private static Button MakeButton(string text, string automationId, Action onClick, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 88,
            FontSize = 13,
        };
        if (primary)
        {
            button.BorderThickness = new Thickness(0);
            button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.Accent);
            button.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.OnAccent);
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
        }

        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }
}
