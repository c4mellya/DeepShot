using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepShot.Core.Updates;
using DeepShot.History;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.Updates;

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
/// 分别显示"尚未配置更新渠道"/"检查更新失败"；成功获得发布页地址时显示"打开发布页"按钮。
/// 图标也按结论区分（信息/警告/错误），不会让失败看起来像成功。
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

        // 状态徽标（统一走 UiKit.StatusBadge）。
        var (iconData, toneKey) = ToneFor(result.Status);
        var badge = UiKit.StatusBadge(iconData, toneKey, 44);
        badge.VerticalAlignment = VerticalAlignment.Center;

        _title = UiKit.Text(TitleFor(result), TextRole.Title, wrap: true);
        AutomationProperties.SetAutomationId(_title, "update-check-title");
        AutomationProperties.SetName(_title, _title.Text);

        // 当前版本单独一行（任何结论下都要看得到"我是什么版本"）。
        var version = UiKit.Text(StringsUi.Format("S.Update.CurrentVersion", result.CurrentVersion), TextRole.Caption);
        version.Margin = new Thickness(0, Tokens.Space.XS, 0, 0);
        AutomationProperties.SetAutomationId(version, "update-check-current-version");

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Tokens.Space.L, 0, 0, 0) };
        titleStack.Children.Add(_title);
        titleStack.Children.Add(version);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(titleStack, 1);
        header.Children.Add(badge);
        header.Children.Add(titleStack);

        // 只有“失败原因 / 更新说明”才有正文；其余结论标题已经说清楚，不再重复。
        // 正文放进一块浅底圆角面板里，和标题区明确分层（更新说明可能很长，面板内滚动）。
        var bodyText = BodyFor(result);
        // 更新说明里的 Markdown 列表（"- " / "* "）显示成圆点；自动化名称仍是原文。
        var displayText = result.Status == UpdateStatus.UpdateAvailable ? AsBullets(bodyText) : bodyText;
        _body = UiKit.Text(displayText, TextRole.Body, result.Status == UpdateStatus.Failed ? ThemeManager.BrushKeys.SecondaryForeground : null, wrap: true);
        _body.LineHeight = 21;
        _body.Visibility = string.IsNullOrWhiteSpace(bodyText) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAutomationId(_body, "update-check-body");
        AutomationProperties.SetName(_body, bodyText);

        var bodyScroll = new ScrollViewer
        {
            Content = _body,
            MaxHeight = Math.Min(300, SystemParameters.WorkArea.Height * 0.42),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var bodyPanel = new Border
        {
            Child = bodyScroll,
            Padding = new Thickness(Tokens.Space.L, Tokens.Space.M, Tokens.Space.L, Tokens.Space.M),
            CornerRadius = new CornerRadius(Tokens.Radius.Popup),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, Tokens.Space.L + 2, 0, 0),
            Visibility = _body.Visibility,
        };
        bodyPanel.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
        bodyPanel.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.Divider);

        // 仅在打开发布页失败时才出现。
        _channel = UiKit.Text(string.Empty, TextRole.Caption, ThemeManager.BrushKeys.Error, wrap: true);
        _channel.Margin = new Thickness(0, Tokens.Space.S + 2, 0, 0);
        _channel.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_channel, "update-check-channel");

        var downloadText = StringsUi.Get("S.Update.Download");
        _download = UiKit.Button(downloadText, downloadText, OpenDownloadPage, ButtonKind.Primary, "update-check-download");
        _download.MinWidth = 88;
        _download.Visibility = result.DownloadUrl is null ? Visibility.Collapsed : Visibility.Visible;

        // 有"打开发布页"时它是主按钮，"知道了"退为次按钮；否则"知道了"就是主按钮。
        var okText = StringsUi.Get("S.Update.Ok");
        _ok = UiKit.Button(okText, okText, Close,
            _download.Visibility == Visibility.Visible ? ButtonKind.Secondary : ButtonKind.Primary, "update-check-ok");
        _ok.MinWidth = 88;

        // 内容区 + 底部操作栏（与设置窗口、提示框的底栏同款）。
        var buttons = UiKit.ButtonBar(_ok, _download);

        var body = new StackPanel { Margin = new Thickness(Tokens.DialogPadding.Left, Tokens.DialogPadding.Top + 4, Tokens.DialogPadding.Right, Tokens.DialogPadding.Bottom) };
        body.Children.Add(header);
        body.Children.Add(bodyPanel);
        body.Children.Add(_channel);

        var stack = new StackPanel();
        stack.Children.Add(body);
        stack.Children.Add(UiKit.FooterBar(buttons));
        Content = stack;
        AppChrome.Apply(this, allowMaximize: false, allowMinimize: false);
        AppChrome.SyncDarkTitleBar(this);

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

    private static string AsBullets(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                lines[i] = "•  " + trimmed[2..].TrimStart();
            }
        }

        return string.Join("\n", lines);
    }

    private static string BodyFor(UpdateCheckResult result) => result.Status switch
    {
        UpdateStatus.Failed => result.Detail,
        UpdateStatus.UpdateAvailable => result.Manifest?.Notes ?? string.Empty,
        _ => string.Empty,
    };

    private static (string Icon, string ToneKey) ToneFor(UpdateStatus status) => status switch
    {
        UpdateStatus.NotConfigured => (AppIcons.Settings, ThemeManager.BrushKeys.Warning),
        UpdateStatus.Failed => (AppIcons.Warning, ThemeManager.BrushKeys.Error),
        UpdateStatus.UpToDate => (AppIcons.Check, ThemeManager.BrushKeys.Success),
        UpdateStatus.UpdateAvailable => (AppIcons.SaveAs, ThemeManager.BrushKeys.Accent),
        _ => (AppIcons.Image, ThemeManager.BrushKeys.Accent),
    };
}
