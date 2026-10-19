using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZeepCut.Ui;

namespace ZeepCut.History;

/// <summary>
/// 图库/预览/对话框共用的样式与小部件。
/// </summary>
/// <remarks>
/// <para>
/// <b>全部走主题资源</b>：新建的控件一律用 <c>SetResourceReference</c> 指向 <c>Theme.*</c> 键
/// （<see cref="ThemeManager.BrushKeys"/>），切换主题时 WPF 自动重绘，不需要窗口再手动"翻译"颜色。
/// 少数仍需要具体画刷的地方（如 <see cref="Frozen"/>）读的是 <see cref="ThemeManager.Palette"/> 当前值。
/// </para>
/// </remarks>
internal static class HistoryUi
{
    /// <summary>主题画刷键的短别名。</summary>
    internal static class Keys
    {
        internal const string Window = ThemeManager.BrushKeys.WindowBackground;
        internal const string Surface = ThemeManager.BrushKeys.Surface;
        internal const string SurfaceAlt = ThemeManager.BrushKeys.SurfaceAlt;
        internal const string Border = ThemeManager.BrushKeys.Border;
        internal const string Divider = ThemeManager.BrushKeys.Divider;
        internal const string Foreground = ThemeManager.BrushKeys.Foreground;
        internal const string Secondary = ThemeManager.BrushKeys.SecondaryForeground;
        internal const string Accent = ThemeManager.BrushKeys.Accent;
        internal const string OnAccent = ThemeManager.BrushKeys.OnAccent;
        internal const string Error = ThemeManager.BrushKeys.Error;
        internal const string Star = ThemeManager.BrushKeys.Star;
        internal const string StarInactive = ThemeManager.BrushKeys.StarInactive;
    }

    private static ThemePalette Palette => ThemeManager.Palette;

    /// <summary>窗口背景（当前主题的具体画刷；新代码请用 <see cref="Keys"/> + 资源引用）。</summary>
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

    /// <summary>把元素的某个画刷属性绑定到主题键（换主题自动更新）。</summary>
    internal static T Ref<T>(this T element, DependencyProperty property, string key)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    /// <summary>用 0.15s 的缓动把 <paramref name="property"/> 动画到目标值（悬停/选中过渡）。</summary>
    internal static void Fade(UIElement element, DependencyProperty property, double to, double milliseconds = 140)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        element.BeginAnimation(property, animation);
    }

    /// <summary>创建一段文字。</summary>
    /// <param name="text">文字。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="foreground">具体画刷（旧用法）；为 null 时用 <paramref name="brushKey"/> 的主题资源引用。</param>
    /// <param name="weight">字重。</param>
    /// <param name="measureNaturalWidth">
    /// 去掉 <see cref="TextTrimming.CharacterEllipsis"/> 让文本按内容测宽。
    /// 默认的省略号样式放进 Grid 自动列/按钮内容里会被量得很窄，把中文裁成一条。
    /// </param>
    /// <param name="brushKey">主题画刷键（默认主文字色）。</param>
    internal static TextBlock Text(
        string text,
        double fontSize = 13,
        Brush? foreground = null,
        FontWeight? weight = null,
        bool measureNaturalWidth = false,
        string? brushKey = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = measureNaturalWidth ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (foreground is not null)
        {
            block.Foreground = foreground;
        }
        else
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey ?? Keys.Foreground);
        }

        if (weight is { } value)
        {
            block.FontWeight = value;
        }

        return block;
    }

    /// <summary>
    /// 创建按钮：统一外观 + UIA 名字（便于真实桌面自动化按名字点击）。
    /// 主按钮 = 强调色底；次按钮 = 面板色底 + 细边框（悬停反馈来自全局按钮样式）。
    /// </summary>
    /// <remarks>
    /// 文案超过 4 个字符（英文 "Save as" / "Annotate"）时 <c>MinWidth</c> 不再压到固定值，
    /// 而是按 <see cref="FrameworkElement.Measure"/> 实测宽度，保证中英文都不被截断。
    /// 内容文字不单独设颜色，继承按钮的 <c>Foreground</c>，所以选中/主按钮换色时文字跟着换。
    /// </remarks>
    internal static Button Button(string text, string automationName, Action onClick, bool primary = false)
    {
        var label = Text(text, 13, measureNaturalWidth: true);
        label.ClearValue(TextBlock.ForegroundProperty);
        var button = new Button
        {
            // 内容用 TextBlock 而不是裸字符串：保证中文字形一定按本类的字体族渲染。
            Content = label,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0),
            FontFamily = Font,
            FontSize = 13,
            MinWidth = 72,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        StyleButton(button, primary);

        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>给按钮套上主/次配色（资源引用，换主题自动更新）。</summary>
    internal static void StyleButton(Button button, bool primary)
    {
        if (primary)
        {
            button.SetResourceReference(Control.BackgroundProperty, Keys.Accent);
            button.SetResourceReference(Control.BorderBrushProperty, Keys.Accent);
            button.SetResourceReference(Control.ForegroundProperty, Keys.OnAccent);
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, Keys.Surface);
            button.SetResourceReference(Control.BorderBrushProperty, Keys.Border);
            button.SetResourceReference(Control.ForegroundProperty, Keys.Foreground);
        }
    }

    /// <summary>
    /// 紧凑图标按钮（无边框透明底，悬停出现浅色覆盖层）。UIA 名字 = <paramref name="automationName"/>，
    /// 鼠标悬停提示 = <paramref name="toolTip"/>。
    /// </summary>
    internal static Button IconButton(
        string iconData,
        string automationName,
        string? toolTip,
        Action onClick,
        double size = 30,
        string iconKey = Keys.Foreground,
        double iconSize = 16)
    {
        var button = new Button
        {
            Content = HistoryIcons.Create(iconData, iconSize, iconKey),
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = toolTip ?? automationName,
        };
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>幽灵按钮：透明底、可选前置图标 + 文字（工具栏用）。</summary>
    internal static Button GhostButton(string text, string? iconData, string automationName, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (iconData is not null)
        {
            var icon = HistoryIcons.Create(iconData, 15, Keys.Secondary);
            icon.Margin = new Thickness(0, 0, 7, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
        }

        var label = Text(text, 12.5, measureNaturalWidth: true);
        label.ClearValue(TextBlock.ForegroundProperty);
        row.Children.Add(label);

        var button = new Button
        {
            Content = row,
            Height = 30,
            Padding = new Thickness(10, 0, 12, 0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = Font,
            FontSize = 12.5,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.SetResourceReference(Control.ForegroundProperty, Keys.Foreground);
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>竖向细分隔线（工具栏分组用）。</summary>
    internal static System.Windows.Controls.Border VerticalSeparator(double height = 18) =>
        new System.Windows.Controls.Border { Width = 1, Height = height, Margin = new Thickness(8, 0, 8, 0) }
            .Ref(System.Windows.Controls.Border.BackgroundProperty, Keys.Divider);

    /// <summary>
    /// 给按钮换文案（语言切换后调用），并按新文字重测最小宽度，避免英文比中文宽时被截断。
    /// </summary>
    internal static void SetButtonText(Button button, string text)
    {
        var label = FindLabel(button.Content);
        if (label is not null)
        {
            label.Text = text;
        }
        else
        {
            button.Content = text;
        }

        FitButtonWidth(button);
    }

    private static TextBlock? FindLabel(object? content) => content switch
    {
        TextBlock block => block,
        Panel panel => panel.Children.OfType<TextBlock>().FirstOrDefault(),
        _ => null,
    };

    /// <summary>按内容实测宽度设置 <c>MinWidth</c>（内边距 + 文字宽度，不缩到比文字还窄）。</summary>
    internal static void FitButtonWidth(Button button)
    {
        if (button.Width > 0)
        {
            return;
        }

        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = button.DesiredSize.Width;
        if (desired > button.MinWidth && !double.IsNaN(desired) && desired > 0)
        {
            button.MinWidth = Math.Ceiling(desired);
        }
    }

    private static readonly Lazy<ControlTemplate> CheckBoxTemplate = new(() =>
        (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="CheckBox">
          <Grid Width="22" Height="22" Background="Transparent" SnapsToDevicePixels="True">
            <Border x:Name="Box" CornerRadius="6" BorderThickness="1.5"
                    Background="{DynamicResource Theme.Surface}"
                    BorderBrush="{DynamicResource Theme.Border}" />
            <Path x:Name="Check" Data="M6,11.4 L9.6,15 L16.2,7.6" Opacity="0" IsHitTestVisible="False"
                  Stroke="{DynamicResource Theme.OnAccent}" StrokeThickness="2.1"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" />
            <Path x:Name="Dash" Data="M6.5,11 H15.5" Opacity="0" IsHitTestVisible="False"
                  Stroke="{DynamicResource Theme.OnAccent}" StrokeThickness="2.1"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Box" Property="Background" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Check" Property="Opacity" Value="1" />
            </Trigger>
            <Trigger Property="IsChecked" Value="{x:Null}">
              <Setter TargetName="Box" Property="Background" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Theme.Accent}" />
              <Setter TargetName="Dash" Property="Opacity" Value="1" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    /// <summary>
    /// 圆角小方框复选框（多选用）：选中 = 强调色底 + 白色对勾；半选 = 强调色底 + 横杠。
    /// 全局 CheckBox 样式是开关，这里显式换模板。
    /// </summary>
    internal static CheckBox CheckBox(string automationId, string automationName)
    {
        var box = new CheckBox
        {
            Template = CheckBoxTemplate.Value,
            Width = 22,
            Height = 22,
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
            ToolTip = automationName,
        };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, automationName);
        return box;
    }

    /// <summary>给元素设置 UIA AutomationId（验证脚本按它精确定位某条记录的控件）。</summary>
    internal static T WithAutomationId<T>(T element, string automationId)
        where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, automationId);
        return element;
    }
}
