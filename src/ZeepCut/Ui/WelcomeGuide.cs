using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZeepCut.Platform;

namespace ZeepCut.Ui;

/// <summary>First-run instructions; the marker is written only when the user dismisses the guide.</summary>
internal static class WelcomeGuide
{
    public static void ShowIfNeeded(string screenshotShortcut, string pinShortcut)
    {
        var marker = Path.Combine(AppPaths.DataRoot, "welcome-v1.completed");
        if (File.Exists(marker)) return;

        var chinese = StringsUi.IsChinese;
        var palette = ThemeManager.Palette;
        var window = new Window
        {
            Title = chinese ? "欢迎使用 Zeep Cut" : "Welcome to Zeep Cut",
            Width = 520, Height = 470, MinHeight = 370, MinWidth = 440,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = palette.WindowBackgroundBrush,
            Foreground = palette.ForegroundBrush,
            FontFamily = new FontFamily(ThemeManager.FontFamilyName),
            ShowInTaskbar = true
        };
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock
        {
            Text = chinese ? "截图，标注，贴在桌面" : "Capture, annotate, and pin",
            FontSize = 24, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18)
        });
        var instructions = chinese
            ? $"{screenshotShortcut}   冻结当前屏幕并截图\n{pinShortcut}   将剪贴板图片贴在桌面\n\n框选后可添加箭头、文字与马赛克；Enter 复制，Esc 取消。\n\n贴图：拖动移动，滚轮缩放，Ctrl + 滚轮调透明度；右键锁定或关闭。\n\n软件常驻系统托盘。通过托盘打开历史、设置，或退出。\n\n普通历史保留 30 天，收藏不自动清理。图片保存在本机。"
            : $"{screenshotShortcut}   Freeze the current screen and capture\n{pinShortcut}   Pin an image from your clipboard\n\nSelect a region, then add arrows, text, or mosaic. Enter copies; Esc cancels.\n\nPins: drag to move, wheel to zoom, Ctrl + wheel for opacity. Right-click to lock or close.\n\nZeep Cut stays in the system tray. Open History or Settings, or exit from its tray menu.\n\nHistory is kept for 30 days; favorites are retained. Images stay on your computer.";
        panel.Children.Add(new TextBlock { Text = instructions, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22 });
        var button = new Button
        {
            Content = chinese ? "开始使用" : "Get started",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0), Padding = new Thickness(20, 8, 20, 8),
            Background = palette.AccentBrush, Foreground = palette.OnAccentBrush
        };
        button.Click += (_, _) => window.Close();
        panel.Children.Add(button);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.Closed += (_, _) =>
        {
            try { File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O")); }
            catch (Exception ex) { AppLog.Warn("Welcome", $"Unable to save guide state: {ex.Message}"); }
        };
        window.Show();
    }
}
