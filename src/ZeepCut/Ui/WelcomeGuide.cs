using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZeepCut.Platform;
using Shapes = System.Windows.Shapes;

namespace ZeepCut.Ui;

/// <summary>首次启动的欢迎页：只列三件事；点“开始使用”后才写入已读标记。</summary>
internal static class WelcomeGuide
{
    public static void ShowIfNeeded(string screenshotShortcut, string pinShortcut)
    {
        var marker = Path.Combine(AppPaths.DataRoot, "welcome-v1.completed");
        if (File.Exists(marker)) return;

        var chinese = StringsUi.IsChinese;
        var window = new Window
        {
            Title = chinese ? "欢迎使用 Zeep Cut" : "Welcome to Zeep Cut",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
        };
        window.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.WindowBackground);
        window.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.Foreground);
        window.SetResourceReference(Control.FontFamilyProperty, ThemeManager.FontFamilyKey);
        window.SourceInitialized += (_, _) => DwmApi.TrySetDarkTitleBar(window, ThemeManager.Palette.IsDark);

        var panel = new StackPanel { Margin = new Thickness(32, 30, 32, 26) };

        // 标题：品牌徽标 + 一句话。
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 24) };
        header.Children.Add(Badge());
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        titles.Children.Add(Text("Zeep Cut", 22, ThemeManager.BrushKeys.Foreground, FontWeights.SemiBold));
        titles.Children.Add(Text(chinese ? "截图、标注、贴在桌面" : "Capture, annotate, pin", 13, ThemeManager.BrushKeys.SecondaryForeground));
        header.Children.Add(titles);
        panel.Children.Add(header);

        // 三件事。
        var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Surface);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.Divider);
        var rows = new StackPanel();
        rows.Children.Add(Row(screenshotShortcut, chinese ? "截图" : "Capture", false));
        rows.Children.Add(Row(pinShortcut, chinese ? "贴图" : "Pin from clipboard", true));
        rows.Children.Add(Row(chinese ? "托盘图标" : "Tray icon", chinese ? "最近截图" : "Recent captures", true));
        card.Child = rows;
        panel.Children.Add(card);

        var button = new Button
        {
            Content = chinese ? "开始使用" : "Get started",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(20, 10, 20, 10),
            FontSize = 14,
            BorderThickness = new Thickness(0),
        };
        button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.Accent);
        button.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.OnAccent);
        button.Click += (_, _) => window.Close();
        panel.Children.Add(button);

        window.Content = panel;
        window.Closed += (_, _) =>
        {
            try { File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O")); }
            catch (Exception ex) { AppLog.Warn("Welcome", $"Unable to save guide state: {ex.Message}"); }
        };
        window.Show();
    }

    private static FrameworkElement Row(string key, string label, bool divider)
    {
        var cap = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 5, 12, 5),
            MinWidth = 84,
            Child = Text(key, 13, ThemeManager.BrushKeys.Foreground, FontWeights.SemiBold, center: true),
        };
        cap.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
        cap.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.Border);

        var grid = new Grid { Margin = new Thickness(18, 14, 18, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = Text(label, 14, ThemeManager.BrushKeys.Foreground);
        text.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(cap);
        grid.Children.Add(text);

        var host = new StackPanel();
        if (divider)
        {
            var line = new Border { Height = 1, Margin = new Thickness(18, 0, 0, 0) };
            line.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Divider);
            host.Children.Add(line);
        }

        host.Children.Add(grid);
        return host;
    }

    private static TextBlock Text(string text, double size, string brushKey, FontWeight? weight = null, bool center = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        if (weight is { } value) block.FontWeight = value;
        return block;
    }

    private static FrameworkElement Badge()
    {
        var icon = new Shapes.Path
        {
            Data = Geometry.Parse("M7,3 V17 H21 M3,7 H17 V21"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        icon.SetResourceReference(Shapes.Shape.StrokeProperty, ThemeManager.BrushKeys.OnAccent);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(icon);
        var gloss = new Border
        {
            CornerRadius = new CornerRadius(14),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
        };
        var grid = new Grid();
        grid.Children.Add(gloss);
        grid.Children.Add(new Viewbox { Width = 24, Height = 24, Child = canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var badge = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(15), Child = grid };
        badge.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Accent);
        return badge;
    }
}
