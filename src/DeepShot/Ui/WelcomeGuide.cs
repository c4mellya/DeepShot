using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeepShot.Platform;

namespace DeepShot.Ui;

/// <summary>首次启动的欢迎页：只列三件事；点“开始使用”后才写入已读标记。</summary>
internal static class WelcomeGuide
{
    public static void ShowIfNeeded(string screenshotShortcut, string pinShortcut)
    {
        var marker = Path.Combine(AppPaths.DataRoot, "welcome-v1.completed");
        if (File.Exists(marker)) return;

        var window = Create(screenshotShortcut, pinShortcut);
        window.Closed += (_, _) =>
        {
            try { File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O")); }
            catch (Exception ex) { AppLog.Warn("Welcome", $"Unable to save guide state: {ex.Message}"); }
        };
        window.Show();
    }

    /// <summary>构建欢迎窗口（不显示、不写标记；预览/截图验证也走这里）。</summary>
    internal static Window Create(string screenshotShortcut, string pinShortcut)
    {
        var chinese = StringsUi.IsChinese;
        var window = new Window
        {
            Title = chinese ? "欢迎使用 DeepShot" : "Welcome to DeepShot",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
        };
        window.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.WindowBackground);
        window.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.Foreground);
        window.SetResourceReference(Control.FontFamilyProperty, ThemeManager.FontFamilyKey);
        AppChrome.SyncDarkTitleBar(window);

        var panel = new StackPanel { Margin = new Thickness(Tokens.Space.XXL, Tokens.Space.L, Tokens.Space.XXL, Tokens.Space.XL + 4) };

        // 头部：居中的品牌徽标（带柔光）+ 大标题 + 一句话。
        var mark = UiKit.BrandMark(64);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(mark);

        var title = UiKit.Text("DeepShot", TextRole.Display);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Margin = new Thickness(0, Tokens.Space.M, 0, 0);
        panel.Children.Add(title);

        var tagline = UiKit.Text(chinese ? "截图、标注、贴在桌面" : "Capture, annotate, pin", TextRole.Body, ThemeManager.BrushKeys.SecondaryForeground);
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.Margin = new Thickness(0, Tokens.Space.XS, 0, Tokens.Space.XL);
        panel.Children.Add(tagline);

        // 三件事：行首图标方块 + 说明 + 右侧键帽。
        panel.Children.Add(UiKit.Card(
            Row(AppIcons.Crop, chinese ? "截图" : "Capture", UiKit.KeyCaps(screenshotShortcut)),
            Row(AppIcons.Pin, chinese ? "贴图" : "Pin from clipboard", UiKit.KeyCaps(pinShortcut)),
            Row(AppIcons.Gallery, chinese ? "最近截图" : "Recent captures",
                UiKit.Text(chinese ? "托盘图标" : "Tray icon", TextRole.Caption))));

        var startText = chinese ? "开始使用" : "Get started";
        var button = UiKit.Button(startText, startText, window.Close, ButtonKind.Primary, "welcome-start");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Height = Tokens.Height.Large;
        button.FontSize = Tokens.Text.BodyLarge;
        button.Margin = new Thickness(0, Tokens.Space.XL, 0, 0);
        button.IsDefault = true;
        panel.Children.Add(button);

        window.Content = panel;
        AppChrome.Apply(window, allowMaximize: false, allowMinimize: false);
        AutomationProperties.SetAutomationId(window, "welcome-window");
        window.Loaded += (_, _) => UiKit.Entrance(panel);
        return window;
    }

    /// <summary>一行：行首图标方块 + 说明 + 右侧附件（统一走 UiKit 的设置行版式）。</summary>
    private static FrameworkElement Row(string iconData, string label, FrameworkElement trailing) =>
        UiKit.SettingsRow(UiKit.RowLeading(UiKit.IconTile(iconData), UiKit.Text(label, TextRole.BodyLarge)), trailing);
}
