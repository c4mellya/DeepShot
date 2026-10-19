using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ZeepCut.Ui;

namespace ZeepCut.History;

/// <summary>
/// 图库/预览/对话框共用的朴素样式（T25/T26）。
/// </summary>
/// <remarks>
/// <para>
/// <b>T30 起全部走主题</b>：这些属性都是 <b>属性</b> 而不是 <c>readonly</c> 字段，读的是
/// <see cref="ThemeManager.Palette"/> 里当前的语义色。切换主题后，已创建的控件由各窗口
/// 监听 <see cref="ThemeManager.Changed"/> 调 <see cref="ApplyThemeTo"/> 就地换色；
/// 新建的控件自动取新颜色。
/// </para>
/// <para>
/// 保留原来的属性名（<see cref="WindowBackground"/> 等），这样 T25/T26 写好的窗口代码
/// 不用改名，只是"颜色是什么"从写死变成跟着主题。
/// </para>
/// </remarks>
internal static class HistoryUi
{
    private static ThemePalette Palette => ThemeManager.Palette;

    /// <summary>窗口背景。</summary>
    internal static Brush WindowBackground => Palette.WindowBackgroundBrush;

    /// <summary>内容卡片背景。</summary>
    internal static Brush CardBackground => Palette.SurfaceBrush;

    /// <summary>卡片/控件边框。</summary>
    internal static Brush Border => Palette.BorderBrush;

    /// <summary>选中卡片边框（强调色）。</summary>
    internal static Brush SelectedBorder => Palette.AccentBrush;

    /// <summary>主要文字。</summary>
    internal static Brush Foreground => Palette.ForegroundBrush;

    /// <summary>次要文字（时间/尺寸/状态）。</summary>
    internal static Brush SecondaryForeground => Palette.SecondaryForegroundBrush;

    /// <summary>缩略图占位底色。</summary>
    internal static Brush ThumbnailBackground => Palette.SurfaceAltBrush;

    /// <summary>收藏星标颜色（金黄色）。</summary>
    internal static Brush StarBrush => Palette.StarBrush;

    /// <summary>未收藏星标颜色。</summary>
    internal static Brush StarInactiveBrush => Palette.StarInactiveBrush;

    /// <summary>错误提示颜色。</summary>
    internal static Brush ErrorForeground => Palette.ErrorBrush;

    /// <summary>字体（与 Toast/贴图一致）。</summary>
    internal const string FontFamilyName = ThemeManager.FontFamilyName;

    /// <summary>字体族实例（每次新建，WPF 会做内部缓存）。</summary>
    internal static FontFamily Font => new(FontFamilyName);

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>创建一段文字。</summary>
    /// <param name="text">文字。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="foreground">颜色（默认主文字色）。</param>
    /// <param name="weight">字重。</param>
    /// <param name="measureNaturalWidth">
    /// 去掉 <see cref="TextTrimming.CharacterEllipsis"/> 让文本按内容测宽。
    /// 默认的省略号样式放进 Grid 自动列/按钮内容里会被量得很窄，把中文裁成一条。
    /// </param>
    internal static TextBlock Text(
        string text,
        double fontSize = 13,
        Brush? foreground = null,
        FontWeight? weight = null,
        bool measureNaturalWidth = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = fontSize,
            Foreground = foreground ?? Foreground,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = measureNaturalWidth ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (weight is { } value)
        {
            block.FontWeight = value;
        }

        return block;
    }

    /// <summary>
    /// 创建按钮：统一外观 + UIA 名字（便于真实桌面自动化按名字点击）。
    /// </summary>
    /// <remarks>
    /// T31：文案超过 4 个字符（英文 "Save as" / "Annotate"）时 <c>MinWidth</c> 不再压到 66，
    /// 而是按 <see cref="FrameworkElement.Measure"/> 实测宽度，保证中英文都不被截断。
    /// </remarks>
    internal static Button Button(string text, string automationName, Action onClick, bool primary = false)
    {
        var palette = Palette;
        var label = Text(text, 13, primary ? palette.OnAccentBrush : palette.ForegroundBrush, measureNaturalWidth: true);
        var button = new Button
        {
            // 内容用 TextBlock 而不是裸字符串：保证中文字形一定按本类的字体族渲染
            // （裸 Content 会走按钮模板的默认字体，缺中文字形时会画出方块）。
            Content = label,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontFamily = Font,
            FontSize = 13,
            MinWidth = 66,
            Background = primary ? palette.AccentBrush : palette.SurfaceBrush,
            Foreground = primary ? palette.OnAccentBrush : palette.ForegroundBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// 给按钮换文案（语言切换后调用），并按新文字重测最小宽度，避免英文比中文宽时被截断。
    /// </summary>
    internal static void SetButtonText(Button button, string text)
    {
        var primary = IsPrimaryLike(button.Background as SolidColorBrush);
        var palette = Palette;
        if (button.Content is TextBlock label)
        {
            label.Text = text;
            label.Foreground = primary ? palette.OnAccentBrush : palette.ForegroundBrush;
        }
        else
        {
            button.Content = text;
        }

        FitButtonWidth(button);
    }

    /// <summary>按内容实测宽度设置 <c>MinWidth</c>（内边距 + 文字宽度，不缩到比文字还窄）。</summary>
    internal static void FitButtonWidth(Button button)
    {
        // 已经放大过的不回缩（卡片操作条的两行按钮共用同一列宽）。
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = button.DesiredSize.Width;
        if (desired > button.MinWidth && !double.IsNaN(desired) && desired > 0)
        {
            button.MinWidth = Math.Ceiling(desired);
        }
    }

    /// <summary>这个按钮是不是"主按钮"（强调色底）。主题切换时要保持它的主按钮外观。</summary>
    internal static bool IsPrimaryLike(SolidColorBrush? background) =>
        background is not null &&
        (Equals(background.Color, ThemePalette.Light.Accent) || Equals(background.Color, ThemePalette.Dark.Accent));

    /// <summary>
    /// 把当前主题套到一棵已建好的控件树上（切换主题后调用）。
    /// </summary>
    /// <remarks>
    /// 只处理本模块自己创建的"朴素控件"（<see cref="TextBlock"/> / <see cref="Button"/> /
    /// <see cref="Border"/> / <see cref="TextBox"/>）；判定依据是"颜色等于旧主题的某个语义色"，
    /// 所以必须**在 <see cref="ThemeManager.Apply"/> 之后**调用，参数 <paramref name="previous"/> 是切换前的那套。
    /// </remarks>
    internal static void ApplyThemeTo(DependencyObject? root, ThemePalette previous)
    {
        if (root is null)
        {
            return;
        }

        var palette = Palette;

        switch (root)
        {
            case TextBlock text:
                text.Foreground = Translate(text.Foreground, previous, palette);
                break;

            case Button button:
                var wasPrimary = IsPrimaryLike(button.Background as SolidColorBrush) ||
                                 Equals((button.Background as SolidColorBrush)?.Color, previous.Accent);
                button.Background = wasPrimary
                    ? palette.AccentBrush
                    : Equals((button.Background as SolidColorBrush)?.Color, previous.Surface)
                        ? palette.SurfaceBrush
                        : Translate(button.Background, previous, palette);
                button.BorderBrush = Translate(button.BorderBrush, previous, palette);
                button.Foreground = Translate(button.Foreground, previous, palette);
                if (button.Content is TextBlock label)
                {
                    label.Foreground = Translate(label.Foreground, previous, palette);
                }

                break;

            case TextBox box:
                box.Background = Translate(box.Background, previous, palette);
                box.Foreground = Translate(box.Foreground, previous, palette);
                box.BorderBrush = Translate(box.BorderBrush, previous, palette);
                box.CaretBrush = Translate(box.CaretBrush, previous, palette);
                break;

            case Border border:
                border.Background = Translate(border.Background, previous, palette);
                border.BorderBrush = Translate(border.BorderBrush, previous, palette);
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyThemeTo(VisualTreeHelper.GetChild(root, i), previous);
        }
    }

    /// <summary>
    /// 把"旧主题里的某个语义色"翻译成新主题里的同一个语义色。
    /// 认不出的颜色（用户自定义的颜色块、透明底、图片）原样保留。
    /// </summary>
    internal static Brush Translate(Brush? brush, ThemePalette previous, ThemePalette next)
    {
        if (brush is not SolidColorBrush solid || solid.Color.A == 0)
        {
            return brush ?? Brushes.Transparent;
        }

        var color = solid.Color;
        if (Equals(color, previous.WindowBackground)) return next.WindowBackgroundBrush;
        if (Equals(color, previous.Surface)) return next.SurfaceBrush;
        if (Equals(color, previous.SurfaceAlt)) return next.SurfaceAltBrush;
        if (Equals(color, previous.Border)) return next.BorderBrush;
        if (Equals(color, previous.Divider)) return next.DividerBrush;
        if (Equals(color, previous.Foreground)) return next.ForegroundBrush;
        if (Equals(color, previous.SecondaryForeground)) return next.SecondaryForegroundBrush;
        if (Equals(color, previous.Accent)) return next.AccentBrush;
        if (Equals(color, previous.OnAccent)) return next.OnAccentBrush;
        if (Equals(color, previous.HoverBackground)) return next.HoverBackgroundBrush;
        if (Equals(color, previous.Error)) return next.ErrorBrush;
        if (Equals(color, previous.Success)) return next.SuccessBrush;
        if (Equals(color, previous.Warning)) return next.WarningBrush;
        if (Equals(color, previous.Star)) return next.StarBrush;
        if (Equals(color, previous.StarInactive)) return next.StarInactiveBrush;
        return brush;
    }

    /// <summary>给元素设置 UIA AutomationId（验证脚本按它精确定位某条记录的控件）。</summary>
    internal static T WithAutomationId<T>(T element, string automationId)
        where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, automationId);
        return element;
    }
}
