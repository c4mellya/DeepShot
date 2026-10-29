using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeepShot.Ui;

/// <summary>按钮的视觉层级。</summary>
internal enum ButtonKind
{
    /// <summary>主操作：强调色底 + 柔光（一个界面最多一个）。</summary>
    Primary,

    /// <summary>次操作：面板色底 + 细边框。</summary>
    Secondary,

    /// <summary>低调操作：浅灰底、无边框（设置页"打开文件夹"这类）。</summary>
    Subtle,

    /// <summary>工具栏按钮：透明底，悬停才出现底色。</summary>
    Ghost,

    /// <summary>破坏性操作：错误色底。</summary>
    Danger,
}

/// <summary>文字的语义层级（字号 + 字重 + 默认颜色一次给全）。</summary>
internal enum TextRole
{
    /// <summary>12 · 次要色：说明、时间戳。</summary>
    Caption,

    /// <summary>13 · 主色：正文。</summary>
    Body,

    /// <summary>13 · Semibold：按钮、强调正文。</summary>
    BodyStrong,

    /// <summary>14 · 主色：设置项标题、对话框正文。</summary>
    BodyLarge,

    /// <summary>15 · Semibold：卡片/分组标题。</summary>
    Subtitle,

    /// <summary>18 · Semibold：窗口/对话框标题。</summary>
    Title,

    /// <summary>24 · Semibold：欢迎页、关于页大标题。</summary>
    Display,

    /// <summary>12 · Semibold · 次要色：卡片上方的分组小标题。</summary>
    Overline,
}

/// <summary>
/// 全应用共用的界面部件：文字、按钮、卡片、设置行、分隔线、状态徽标、键帽……
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么集中</b>：之前设置页、图库、更新窗口、欢迎页、提示框各写了一套 Text/Button/Card，
/// 同样是"次按钮"，内边距有 14/18/20 三种、最小宽度有 72/84/88 三种。这里是唯一来源，
/// 尺寸从 <see cref="Tokens"/> 取，颜色全部是 <c>Theme.*</c> 资源引用（换主题即时生效）。
/// </para>
/// <para>
/// 按钮外观由 <see cref="UiStyles"/> 里的键控样式决定（<c>DeepShot.Button.*</c>），
/// 这里只负责选样式、填内容、挂 UIA 名字与点击事件。
/// </para>
/// </remarks>
internal static class UiKit
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
        internal const string AccentSoft = ThemeManager.BrushKeys.AccentSoft;
        internal const string OnAccent = ThemeManager.BrushKeys.OnAccent;
        internal const string Error = ThemeManager.BrushKeys.Error;
        internal const string Success = ThemeManager.BrushKeys.Success;
        internal const string Warning = ThemeManager.BrushKeys.Warning;
        internal const string Star = ThemeManager.BrushKeys.Star;
        internal const string StarInactive = ThemeManager.BrushKeys.StarInactive;
    }

    /// <summary>键控按钮样式的资源键。</summary>
    internal static class StyleKeys
    {
        internal const string Primary = "DeepShot.Button.Primary";
        internal const string Secondary = "DeepShot.Button.Secondary";
        internal const string Subtle = "DeepShot.Button.Subtle";
        internal const string Ghost = "DeepShot.Button.Ghost";
        internal const string Danger = "DeepShot.Button.Danger";
        internal const string Card = "DeepShot.Card";
    }

    // --- 基础工具 ---

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

    /// <summary>用统一缓动把 <paramref name="property"/> 动画到目标值（悬停/选中过渡）。</summary>
    internal static void Fade(UIElement element, DependencyProperty property, double to, double milliseconds = 140)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = Tokens.Motion.Ease,
        };
        element.BeginAnimation(property, animation);
    }

    /// <summary>
    /// 窗口内容进入动画：从下方 8px、透明度 0 滑入（<see cref="Tokens.Motion.Slow"/>）。
    /// 对话框/设置页加载时调用一次，让窗口"浮"出来而不是硬切。
    /// </summary>
    internal static void Entrance(FrameworkElement element, double offset = 8)
    {
        var shift = new TranslateTransform(0, offset);
        element.RenderTransform = shift;
        element.Opacity = 0;
        var duration = new Duration(Tokens.Motion.Slow);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = Tokens.Motion.Ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = Tokens.Motion.Ease });
    }

    // --- 文字 ---

    /// <summary>创建一段文字（单行、居中对齐，适合按钮/工具栏/卡片内的短文字）。</summary>
    /// <param name="text">文字。</param>
    /// <param name="fontSize">字号。</param>
    /// <param name="foreground">具体画刷；为 null 时用 <paramref name="brushKey"/> 的主题资源引用。</param>
    /// <param name="weight">字重（只用 <see cref="AppFonts.Regular"/> / <see cref="AppFonts.Semibold"/>）。</param>
    /// <param name="measureNaturalWidth">
    /// 去掉 <see cref="TextTrimming.CharacterEllipsis"/> 让文本按内容测宽。
    /// 默认的省略号样式放进 Grid 自动列/按钮内容里会被量得很窄，把中文裁成一条。
    /// </param>
    /// <param name="brushKey">主题画刷键（默认主文字色）。</param>
    internal static TextBlock Text(
        string text,
        double fontSize = Tokens.Text.Body,
        Brush? foreground = null,
        FontWeight? weight = null,
        bool measureNaturalWidth = false,
        string? brushKey = null)
    {
        var block = new TextBlock
        {
            Text = text,
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

        // 字体统一走 Theme.FontFamily 资源（与窗口根同一来源）：还没进可视树就测宽的文字（字样方块、自适应行）也按 Inter 量。
        block.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);

        if (weight is { } value)
        {
            block.FontWeight = value;
        }

        return block;
    }

    /// <summary>
    /// 按语义层级创建文字（左对齐；<paramref name="wrap"/> 时自动换行）。
    /// 颜色默认取该层级的颜色（Caption/Overline 为次要色），可用 <paramref name="brushKey"/> 覆盖。
    /// </summary>
    internal static TextBlock Text(string text, TextRole role, string? brushKey = null, bool wrap = false)
    {
        var (size, weight, defaultKey) = Describe(role);
        var block = Text(text, size, weight: weight, measureNaturalWidth: wrap, brushKey: brushKey ?? defaultKey);
        block.HorizontalAlignment = HorizontalAlignment.Left;
        if (wrap)
        {
            block.TextWrapping = TextWrapping.Wrap;
        }

        if (role is TextRole.Title or TextRole.Display)
        {
            // 大字号收紧行高，标题更紧凑。
            block.LineHeight = Math.Round(size * 1.3);
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        return block;
    }

    private static (double Size, FontWeight Weight, string BrushKey) Describe(TextRole role) => role switch
    {
        TextRole.Caption => (Tokens.Text.Caption, AppFonts.Regular, Keys.Secondary),
        TextRole.BodyStrong => (Tokens.Text.Body, AppFonts.Semibold, Keys.Foreground),
        TextRole.BodyLarge => (Tokens.Text.BodyLarge, AppFonts.Regular, Keys.Foreground),
        TextRole.Subtitle => (Tokens.Text.Subtitle, AppFonts.Semibold, Keys.Foreground),
        TextRole.Title => (Tokens.Text.Title, AppFonts.Semibold, Keys.Foreground),
        TextRole.Display => (Tokens.Text.Display, AppFonts.Semibold, Keys.Foreground),
        TextRole.Overline => (Tokens.Text.Caption, AppFonts.Semibold, Keys.Secondary),
        _ => (Tokens.Text.Body, AppFonts.Regular, Keys.Foreground),
    };

    // --- 按钮 ---

    /// <summary>
    /// 文字按钮：统一外观 + UIA 名字（便于真实桌面自动化按名字点击）。不带外边距，间距由容器决定
    /// （对话框底部用 <see cref="ButtonBar"/>）。
    /// </summary>
    /// <remarks>
    /// 内容是 <see cref="TextBlock"/> 而不是裸字符串，保证中文一定按当前界面字体渲染；
    /// 文字不单独设颜色，继承按钮的 <c>Foreground</c>，换样式时跟着变。
    /// </remarks>
    internal static Button Button(
        string text,
        string automationName,
        Action onClick,
        ButtonKind kind,
        string? automationId = null,
        string? iconData = null)
    {
        var label = Text(text, Tokens.Text.Body, measureNaturalWidth: true);
        label.ClearValue(TextBlock.ForegroundProperty);

        object content = label;
        if (iconData is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = AppIcons.Create(iconData, 15, kind == ButtonKind.Primary || kind == ButtonKind.Danger ? Keys.OnAccent : Keys.Secondary);
            icon.Margin = new Thickness(0, 0, 7, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            row.Children.Add(label);
            content = row;
        }

        var button = new Button { Content = content, MinWidth = kind == ButtonKind.Ghost ? 0 : 80 };
        StyleButton(button, kind);
        AutomationProperties.SetName(button, automationName);
        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(button, automationId);
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>给按钮套上某个层级的样式（资源引用，换主题自动更新）。</summary>
    internal static void StyleButton(Button button, ButtonKind kind)
    {
        button.SetResourceReference(FrameworkElement.StyleProperty, kind switch
        {
            ButtonKind.Primary => StyleKeys.Primary,
            ButtonKind.Subtle => StyleKeys.Subtle,
            ButtonKind.Ghost => StyleKeys.Ghost,
            ButtonKind.Danger => StyleKeys.Danger,
            _ => StyleKeys.Secondary,
        });
    }

    /// <summary>
    /// 紧凑图标按钮（透明底，悬停出现浅色底）。UIA 名字 = <paramref name="automationName"/>，
    /// 鼠标悬停提示 = <paramref name="toolTip"/>。
    /// </summary>
    internal static Button IconButton(
        string iconData,
        string automationName,
        string? toolTip,
        Action onClick,
        double size = 32,
        string iconKey = Keys.Foreground,
        double iconSize = 16)
    {
        var button = new Button
        {
            Content = AppIcons.Create(iconData, iconSize, iconKey),
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = toolTip ?? automationName,
        };
        StyleButton(button, ButtonKind.Ghost);
        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>幽灵按钮：透明底、可选前置图标 + 文字（工具栏用）。</summary>
    internal static Button GhostButton(string text, string? iconData, string automationName, Action onClick)
    {
        var button = Button(text, automationName, onClick, ButtonKind.Ghost, iconData: iconData);
        button.Height = Tokens.Height.Compact + 2;
        button.Padding = new Thickness(10, 0, 12, 0);
        button.FontSize = 12.5;
        if (FindLabel(button.Content) is { } label)
        {
            label.FontSize = 12.5;
        }

        return button;
    }

    /// <summary>对话框/卡片底部的按钮条：右对齐、按钮间 8px。</summary>
    internal static StackPanel ButtonBar(params Button[] buttons)
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Margin = new Thickness(i == 0 ? 0 : Tokens.Space.S, 0, 0, 0);
            bar.Children.Add(buttons[i]);
        }

        return bar;
    }

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

    // --- 画布上的就地文字输入框 ---

    private static readonly Lazy<Style> PlainTextBoxStyle = new(() =>
        (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="TextBox">
          <Setter Property="FocusVisualStyle" Value="{x:Null}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="TextBox">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}">
                  <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                                HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden" />
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>
    /// 让 TextBox 退出全局主题样式（圆角框、强调色描边与聚焦光晕、强调色选区），
    /// 只剩一层朴素外框 + 内容宿主。用于压在截图上的标注文字输入框：
    /// 它要看起来就是最终渲染出的文字，光晕也不能盖住周围的图片像素。
    /// </summary>
    internal static void UsePlainTextBox(TextBox box) => box.Style = PlainTextBoxStyle.Value;

    // --- 复选框 ---

    private static readonly Lazy<ControlTemplate> CheckBoxTemplate = new(() =>
        (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="CheckBox">
          <Grid Width="20" Height="20" Background="Transparent" SnapsToDevicePixels="True">
            <Border x:Name="Box" CornerRadius="4" BorderThickness="1"
                    Background="{DynamicResource Theme.Surface}"
                    BorderBrush="{DynamicResource Theme.SecondaryForeground}" />
            <Border x:Name="Ov" CornerRadius="4" Opacity="0" IsHitTestVisible="False"
                    Background="{DynamicResource Theme.Foreground}" />
            <Border x:Name="FocusRing" CornerRadius="6" Margin="-3" BorderThickness="2" Visibility="Collapsed"
                    IsHitTestVisible="False" BorderBrush="{DynamicResource Theme.FocusRing}" />
            <Path x:Name="Check" Data="M5.5,10.2 L8.6,13.2 L14.5,7" Opacity="0" IsHitTestVisible="False"
                  Stroke="{DynamicResource Theme.OnAccent}" StrokeThickness="1.5"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" />
            <Path x:Name="Dash" Data="M6,10 H14" Opacity="0" IsHitTestVisible="False"
                  Stroke="{DynamicResource Theme.OnAccent}" StrokeThickness="1.5"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.05" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="FocusRing" Property="Visibility" Value="Visible" />
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
    /// Fluent 方框复选框（多选用，20×20，圆角 4）：选中 = 强调色底 + 对勾；半选 = 强调色底 + 横杠。
    /// 全局 CheckBox 样式是开关，这里显式换模板。
    /// </summary>
    internal static CheckBox CheckBox(string automationId, string automationName)
    {
        var box = new CheckBox
        {
            Template = CheckBoxTemplate.Value,
            Width = 20,
            Height = 20,
            FocusVisualStyle = null,
            ToolTip = automationName,
        };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, automationName);
        return box;
    }

    // --- 分隔线 ---

    /// <summary>横向细分隔线；<paramref name="inset"/> 是左侧缩进（卡片行之间用，与行内容对齐）。</summary>
    internal static Border Divider(double inset = 0) =>
        new Border { Height = 1, Margin = new Thickness(inset, 0, 0, 0), SnapsToDevicePixels = true }
            .Ref(Border.BackgroundProperty, Keys.Divider);

    /// <summary>竖向细分隔线（工具栏分组用）。</summary>
    internal static Border VerticalSeparator(double height = 18) =>
        new Border { Width = 1, Height = height, Margin = new Thickness(8, 0, 8, 0), SnapsToDevicePixels = true }
            .Ref(Border.BackgroundProperty, Keys.Divider);

    // --- 卡片与设置行 ---

    /// <summary>
    /// 圆角卡片：行与行之间用缩进细线分隔（与行内文字左对齐，iOS/Win11 设置页的做法）。
    /// </summary>
    internal static Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                stack.Children.Add(Divider(Tokens.CardRowPadding.Left));
            }

            stack.Children.Add(rows[i]);
        }

        return CardShell(stack);
    }

    /// <summary>卡片外壳（面板色底 + 细边 + 大圆角 + 极淡投影），内容自定。</summary>
    internal static Border CardShell(UIElement child, Thickness? padding = null)
    {
        var card = new Border
        {
            Child = child,
            Padding = padding ?? new Thickness(0),
        };
        card.SetResourceReference(FrameworkElement.StyleProperty, StyleKeys.Card);
        return card;
    }

    /// <summary>卡片上方的分组小标题。</summary>
    internal static TextBlock SectionHeader(string text, string? automationId = null)
    {
        var block = Text(text, TextRole.Overline);
        block.Margin = new Thickness(4, Tokens.Space.XL, 0, Tokens.Space.S);
        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(block, automationId);
        }

        return block;
    }

    /// <summary>占满整行的卡片内容（标准内边距）。</summary>
    internal static Border CardRow(FrameworkElement content) =>
        new() { Padding = Tokens.CardRowPadding, Child = content };

    /// <summary>设置行：左边标题（+ 说明），右边控件。</summary>
    internal static FrameworkElement SettingsRow(string title, string? description, FrameworkElement control) =>
        SettingsRow(title, description, control, out _, out _);

    /// <summary>设置行（返回标题/说明文字块，语言切换时就地改字）。</summary>
    internal static FrameworkElement SettingsRow(
        string title,
        string? description,
        FrameworkElement control,
        out TextBlock titleBlock,
        out TextBlock descriptionBlock) =>
        SettingsRow(RowLabel(null, title, description, out titleBlock, out descriptionBlock), control);

    /// <summary>设置行（左侧内容自定）。</summary>
    internal static FrameworkElement SettingsRow(FrameworkElement left, FrameworkElement control)
    {
        var grid = new Grid { Margin = Tokens.CardRowPadding, MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.Margin = new Thickness(0, 0, Tokens.Space.L + 2, 0);
        left.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(left);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>
    /// 图标方块：圆角方底（色调 12%）+ 同色线条图标。设置行、欢迎页列表的行首图标共用。
    /// </summary>
    internal static Grid IconTile(string iconData, string toneKey = Keys.Accent, double size = 32)
    {
        var tint = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Control), Opacity = 0.12 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var glyph = AppIcons.Create(iconData, Math.Round(size / 2), toneKey, 2.0);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        tile.Children.Add(tint);
        tile.Children.Add(glyph);
        return tile;
    }

    /// <summary>行首图标方块（32）+ 与文字的间距：换行后的控件/说明按它缩进，与标题左缘对齐。</summary>
    internal const double RowLeadingIndent = 32 + Tokens.Space.M + 2;

    /// <summary>
    /// 字样方块：与 <see cref="IconTile"/> 同尺寸同色调，但方块里是一小段文字（如字体致谢里的"Aa汉"）。
    /// </summary>
    internal static Grid TextTile(string text, string toneKey = Keys.Accent, double size = 32)
    {
        var tint = new Border { CornerRadius = new CornerRadius(Tokens.Radius.Control), Opacity = 0.12 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var sample = Text(text, Math.Round(size * 0.36 * 2) / 2, weight: AppFonts.Semibold, measureNaturalWidth: true, brushKey: toneKey);
        sample.HorizontalAlignment = HorizontalAlignment.Center;
        sample.VerticalAlignment = VerticalAlignment.Center;

        // 多字字样（"Aa汉"）按方块内宽收小字号，四周至少留 5px，不顶着方块边。
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var room = size - 10;
        if (sample.DesiredSize.Width > room)
        {
            sample.FontSize = Math.Floor(sample.FontSize * room / sample.DesiredSize.Width * 2) / 2;
        }

        var tile = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        tile.Children.Add(tint);
        tile.Children.Add(sample);
        return tile;
    }

    /// <summary>
    /// 设置行左侧：可选行首图标方块 + 标题 + 说明（说明为 null 时折叠）。
    /// 配合 <see cref="SettingsRow(FrameworkElement, FrameworkElement)"/> / <see cref="AdaptiveSettingsRow(FrameworkElement, FrameworkElement, double)"/> 使用。
    /// </summary>
    internal static FrameworkElement RowLabel(
        string? iconData,
        string title,
        string? description,
        out TextBlock titleBlock,
        out TextBlock descriptionBlock)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleBlock = Text(title, TextRole.BodyLarge, wrap: true);
        texts.Children.Add(titleBlock);

        descriptionBlock = Text(description ?? string.Empty, TextRole.Caption, wrap: true);
        descriptionBlock.Margin = new Thickness(0, 3, 0, 0);
        descriptionBlock.Visibility = description is null ? Visibility.Collapsed : Visibility.Visible;
        texts.Children.Add(descriptionBlock);

        return iconData is null ? texts : RowLeading(IconTile(iconData), texts);
    }

    /// <summary>
    /// 行首方块（<see cref="IconTile"/> / <see cref="TextTile"/>）+ 右侧内容，间距 = <see cref="RowLeadingIndent"/>。
    /// 设置行左侧需要自定义文字（多行说明、动态文字）时用它保持同款行首。
    /// </summary>
    internal static Grid RowLeading(FrameworkElement leading, FrameworkElement content)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        leading.Margin = new Thickness(0, 0, Tokens.Space.M + 2, 0);
        leading.VerticalAlignment = VerticalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(content, 1);
        grid.Children.Add(leading);
        grid.Children.Add(content);
        return grid;
    }

    /// <summary>
    /// 自适应设置行：宽度够时左文右控件；左侧文字列被挤到 <paramref name="minLabelWidth"/> 以下时，
    /// 控件自动换到文字下方（左对齐），避免中文标题被压成一字一行。
    /// </summary>
    internal static FrameworkElement AdaptiveSettingsRow(
        string title,
        string? description,
        FrameworkElement control,
        out TextBlock titleBlock,
        out TextBlock descriptionBlock,
        double minLabelWidth = 220) =>
        AdaptiveSettingsRow(RowLabel(null, title, description, out titleBlock, out descriptionBlock), control, minLabelWidth);

    /// <summary>自适应设置行（左侧内容自定），规则同上。</summary>
    internal static FrameworkElement AdaptiveSettingsRow(FrameworkElement left, FrameworkElement control, double minLabelWidth = 220) =>
        AdaptiveSettingsRow(left, control, minLabelWidth, 0);

    /// <summary>
    /// 自适应设置行；换行后控件左缩进 <paramref name="stackIndent"/>
    /// （左侧带行首图标时传 <see cref="RowLeadingIndent"/>，控件与标题文字左缘对齐）。
    /// </summary>
    internal static FrameworkElement AdaptiveSettingsRow(FrameworkElement left, FrameworkElement control, double minLabelWidth, double stackIndent)
    {
        var grid = new Grid { Margin = Tokens.CardRowPadding, MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        grid.Children.Add(control);

        var originalAlignment = control.HorizontalAlignment;
        bool? stacked = null;
        void Arrange(bool stack)
        {
            if (stacked == stack)
            {
                return;
            }

            stacked = stack;
            Grid.SetRow(control, stack ? 1 : 0);
            Grid.SetColumn(control, stack ? 0 : 1);
            Grid.SetColumnSpan(left, stack ? 2 : 1);
            control.HorizontalAlignment = stack ? HorizontalAlignment.Left : originalAlignment;
            control.Margin = stack ? new Thickness(stackIndent, Tokens.Space.M, 0, 0) : new Thickness(0);
            left.Margin = stack ? new Thickness(0) : new Thickness(0, 0, Tokens.Space.L + 2, 0);
        }

        Arrange(false);

        // 控件与左侧文字的自然（单行）宽度都与所在列无关，用它们判断不会来回抖动。
        // 量一次就缓存：拖动窗口改宽度时只做一次比较，不再每个像素都无限宽重量 + 多一轮布局。
        // 只有内容可能变了才重新量：换语言（改字）、或行高变而宽度没变。
        double controlWidth = 0, labelWidth = 0;
        var measured = false;
        void Evaluate(double width)
        {
            if (!measured)
            {
                control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                left.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                controlWidth = control.DesiredSize.Width;
                labelWidth = Math.Ceiling(left.DesiredSize.Width - left.Margin.Left - left.Margin.Right);
                control.InvalidateMeasure();
                left.InvalidateMeasure();
                measured = true;
            }

            // 文字本来就短（"主题"）时，只要单行放得下就不换行，不必等到 minLabelWidth。
            var labelRoom = width - controlWidth - (Tokens.Space.L + 2);
            Arrange(labelRoom < Math.Min(minLabelWidth, labelWidth));
        }

        grid.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged && !e.HeightChanged)
            {
                return;
            }

            if (!e.WidthChanged)
            {
                measured = false;
            }

            Evaluate(e.NewSize.Width);
        };

        // 换语言时文字就地改了但行宽未必变：等这一轮改字结束后重新量一次。
        void OnLanguageChanged(DeepShot.Core.Settings.AppLanguage _)
        {
            measured = false;
            grid.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (grid.IsLoaded && grid.ActualWidth > 0)
                {
                    Evaluate(grid.ActualWidth);
                }
            });
        }

        grid.Loaded += (_, _) => StringsUi.Changed += OnLanguageChanged;
        grid.Unloaded += (_, _) => StringsUi.Changed -= OnLanguageChanged;
        return grid;
    }

    // --- 徽标与键帽 ---

    /// <summary>
    /// 状态徽标：淡色圆底（色调 14%）+ 同色矢量图标。提示框、更新窗口、欢迎页共用。
    /// </summary>
    internal static Grid StatusBadge(string iconData, string toneKey, double size = 44)
    {
        var tint = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Opacity = 0.14 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var ring = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(1),
            Opacity = 0.22,
        };
        ring.SetResourceReference(Border.BorderBrushProperty, toneKey);
        var glyph = AppIcons.Create(iconData, Math.Round(size / 2), toneKey, 2.0);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(tint);
        badge.Children.Add(ring);
        badge.Children.Add(glyph);
        return badge;
    }

    /// <summary>键帽（快捷键展示）：浅底 + 细边 + 底部加厚一像素，像一颗真实的按键。</summary>
    internal static Border KeyCap(string text)
    {
        var label = Text(text, Tokens.Text.Caption, weight: AppFonts.Semibold, measureNaturalWidth: true);
        var cap = new Border
        {
            Child = label,
            MinWidth = 26,
            Padding = new Thickness(8, 3, 8, 3),
            CornerRadius = new CornerRadius(Tokens.Radius.Small),
            BorderThickness = new Thickness(1, 1, 1, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cap.SetResourceReference(Border.BackgroundProperty, Keys.SurfaceAlt);
        cap.SetResourceReference(Border.BorderBrushProperty, Keys.Border);
        return cap;
    }

    /// <summary>小号胶囊标签（"测试版"、"新"、计数）。<paramref name="toneKey"/> 决定颜色。</summary>
    internal static Border Pill(string text, string toneKey = Keys.Accent)
    {
        var label = Text(text, 11.5, weight: AppFonts.Semibold, measureNaturalWidth: true, brushKey: toneKey);
        var tint = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.14 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var grid = new Grid();
        grid.Children.Add(tint);
        grid.Children.Add(new Border { Padding = new Thickness(8, 2, 8, 3), Child = label });
        return new Border { Child = grid, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>
    /// 品牌徽标：应用图标 + 身后一团柔和的强调色光晕（光晕是兄弟元素，模糊不会糊到图标）。
    /// 设置页头部、欢迎页共用。
    /// </summary>
    internal static Grid BrandMark(double iconSize = 52)
    {
        var glowSize = Math.Round(iconSize * 0.78);
        var glow = new Border
        {
            Width = glowSize,
            Height = glowSize,
            CornerRadius = new CornerRadius(glowSize / 2),
            Opacity = 0.45,
            Margin = new Thickness(0, Math.Round(iconSize * 0.2), 0, 0),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = Math.Round(iconSize * 0.46) },
        };
        glow.SetResourceReference(Border.BackgroundProperty, Keys.Accent);

        var mark = AppChrome.IconImage(iconSize);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;

        var host = new Grid { Width = iconSize + 8, Height = iconSize + 8, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(glow);
        host.Children.Add(mark);
        return host;
    }

    /// <summary>
    /// 头部底光：一层强调色的极淡径向晕染（中心 <paramref name="center"/>，相对坐标），向外渐隐到透明。
    /// 只是背景装饰、不吃命中测试；垫在品牌徽标身后（设置页头部、欢迎页共用），
    /// 通常交给 <see cref="AppChrome.AddBackdrop"/> 铺到自绘标题栏身后，标题栏与头部之间不留接缝。
    /// </summary>
    internal static Border HeaderWash(Point center, double radiusX = 0.75, double radiusY = 0.62, byte peakAlpha = 0x3A)
    {
        var mask = new RadialGradientBrush
        {
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
        };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(peakAlpha * 0.38), 0, 0, 0), 0.45));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1));
        mask.Freeze();

        var wash = new Border { OpacityMask = mask, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
        wash.SetResourceReference(Border.BackgroundProperty, Keys.Accent);
        return wash;
    }

    /// <summary>
    /// 窗口底部操作栏：面板色底 + 顶部 1px 分隔线，里面放状态文字/按钮。
    /// 设置窗口、提示框、检查更新窗口共用（Win11 对话框的"按钮区"做法：内容与操作明确分层）。
    /// </summary>
    internal static Border FooterBar(UIElement content, Thickness? padding = null)
    {
        var bar = new Border
        {
            Child = content,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = padding ?? new Thickness(Tokens.DialogPadding.Left, Tokens.Space.M + 2, Tokens.DialogPadding.Right, Tokens.Space.M + 2),
            SnapsToDevicePixels = true,
        };
        bar.SetResourceReference(Border.BackgroundProperty, Keys.Surface);
        bar.SetResourceReference(Border.BorderBrushProperty, Keys.Divider);
        return bar;
    }

    /// <summary>"Ctrl+Shift+A" → 一排键帽（每个键一颗，间隔 4px）；自动化名称是原始组合文字。</summary>
    internal static StackPanel KeyCaps(string shortcut)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            parts = new[] { shortcut };
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var cap = KeyCap(parts[i]);
            if (i > 0)
            {
                cap.Margin = new Thickness(Tokens.Space.XS, 0, 0, 0);
            }

            row.Children.Add(cap);
        }

        AutomationProperties.SetName(row, shortcut);
        return row;
    }

    // --- 投影、悬浮工具条、缩放、空状态（预览 / 编辑 / 快览等看图窗口共用） ---

    /// <summary>
    /// 投影底板：面板色、不参与命中测试，放在内容<b>后面</b>做兄弟元素。
    /// 投影永远不加在内容的祖先上（会把文字/图片位图化发虚）。
    /// </summary>
    internal static Border ShadowPlate(double cornerRadius = Tokens.Radius.Card, double blur = 24, double depth = 6, double opacity = 0.16)
    {
        var plate = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = blur,
                ShadowDepth = depth,
                Direction = 270,
                Opacity = opacity,
                Color = Colors.Black,
            },
        };
        plate.SetResourceReference(Border.BackgroundProperty, Keys.Surface);
        return plate;
    }

    /// <summary>
    /// 给内容垫上 <see cref="ShadowPlate"/>（可选再盖一圈 1px 细描边，深色图在深色底上也有边界）。
    /// 返回的 Grid 尺寸跟随内容；内容的 LayoutTransform 缩放时底板与描边一起跟随。
    /// </summary>
    internal static Grid Elevated(
        FrameworkElement content,
        double cornerRadius = Tokens.Radius.Card,
        bool outline = false,
        double blur = 24,
        double depth = 6,
        double opacity = 0.16)
    {
        var host = new Grid
        {
            HorizontalAlignment = content.HorizontalAlignment,
            VerticalAlignment = content.VerticalAlignment,
        };
        host.Children.Add(ShadowPlate(cornerRadius, blur, depth, opacity));
        host.Children.Add(content);
        if (outline)
        {
            host.Children.Add(Hairline(cornerRadius));
        }

        return host;
    }

    /// <summary>
    /// 1px 细描边圈（分隔线色、不参与命中测试）：盖在图片/缩略图上面，
    /// 浅色截图在白底上、深色截图在深色底上都能看出边界。尺寸跟随所在容器。
    /// </summary>
    internal static Border Hairline(double cornerRadius = Tokens.Radius.Small)
    {
        var ring = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        ring.SetResourceReference(Border.BorderBrushProperty, Keys.Divider);
        return ring;
    }

    /// <summary>
    /// 悬浮工具条：面板色底 + 细边 + 12px 圆角（= 按钮 8px + 内边距 4px，内外圆角同心）+ 柔和投影。
    /// 浮在图片/画布上方，默认底部居中；项之间用 <see cref="ToolbarSeparator"/> 分组。
    /// </summary>
    internal static Grid FloatingBar(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in items)
        {
            row.Children.Add(item);
        }

        var surface = new Border
        {
            Child = row,
            Padding = new Thickness(Tokens.Space.XS),
            CornerRadius = new CornerRadius(Tokens.Radius.Control + Tokens.Space.XS),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
        };
        surface.SetResourceReference(Border.BackgroundProperty, Keys.Surface);
        surface.SetResourceReference(Border.BorderBrushProperty, Keys.Border);

        var bar = Elevated(surface, Tokens.Radius.Control + Tokens.Space.XS, blur: 20, depth: 4, opacity: 0.14);
        bar.HorizontalAlignment = HorizontalAlignment.Center;
        bar.VerticalAlignment = VerticalAlignment.Bottom;
        return bar;
    }

    /// <summary>悬浮工具条里的分组竖线（比 <see cref="VerticalSeparator"/> 更紧凑）。</summary>
    internal static Border ToolbarSeparator()
    {
        var line = VerticalSeparator(20);
        line.Margin = new Thickness(Tokens.Space.XS + 2, 0, Tokens.Space.XS + 2, 0);
        return line;
    }

    /// <summary>缩放比例的统一写法："100%"。</summary>
    internal static string FormatZoom(double zoom) =>
        Math.Round(zoom * 100, 0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 缩放读数按钮：显示当前百分比（等宽数字、固定宽度，数字变化时工具条不跳），点击回到 1:1。
    /// 换数字用 <see cref="SetButtonText"/>。
    /// </summary>
    internal static Button ZoomButton(string automationName, string toolTip, Action onClick, double zoom = 1.0)
    {
        var button = Button(FormatZoom(zoom), automationName, onClick, ButtonKind.Ghost);
        button.Width = 56;
        button.Height = 32;
        button.Padding = new Thickness(0);
        button.ToolTip = toolTip;
        if (FindLabel(button.Content) is { } label)
        {
            label.FontSize = Tokens.Text.Caption;
            label.FontWeight = AppFonts.Semibold;
            System.Windows.Documents.Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        }

        return button;
    }

    /// <summary>
    /// 空状态：淡色圆形徽标 + 标题 +（可选）一行说明，居中摆放。
    /// 返回的面板里，标题与说明是前两个 <see cref="TextBlock"/>，调用方可按需改字。
    /// </summary>
    internal static StackPanel EmptyState(string iconData, TextBlock title, TextBlock? hint = null, double badgeSize = 56)
    {
        // 强调色徽标：灰底灰图标压在浅灰面板上发闷；所有空状态（图库 / 快览）统一用强调色。
        var badge = StatusBadge(iconData, Keys.Accent, badgeSize);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.TextAlignment = TextAlignment.Center;
        title.Margin = new Thickness(0, Tokens.Space.M + 2, 0, 0);
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space.XL),
        };
        panel.Children.Add(badge);
        panel.Children.Add(title);
        if (hint is not null)
        {
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(0, Tokens.Space.XS, 0, 0);
            panel.Children.Add(hint);
        }

        return panel;
    }

    /// <summary>
    /// 去掉 ScrollViewer 右下角横竖滚动条交汇处的系统色方块（默认模板取 SystemColors.Control，深色主题下是一块白斑）。
    /// </summary>
    internal static void QuietScrollCorner(ScrollViewer scroll) =>
        scroll.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;

    /// <summary>
    /// 看图窗口的顶栏（预览 / 标注编辑共用）：面板色底 + 底部分隔线；
    /// 左侧 32px 色调徽标 + 标题（强调，单行省略）+ 可选说明行（次要小字），右侧可放状态与按钮。
    /// 标题/说明的文字、自动化属性由调用方设置；这里只统一排版、间距与配色。
    /// </summary>
    internal static Border ViewerHeader(string iconData, TextBlock title, TextBlock? subtitle = null, FrameworkElement? trailing = null, string toneKey = Keys.Accent)
    {
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(title);
        if (subtitle is not null)
        {
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            subtitle.TextWrapping = TextWrapping.NoWrap;
            subtitle.Margin = new Thickness(0, 2, 0, 0);
            System.Windows.Documents.Typography.SetNumeralAlignment(subtitle, FontNumeralAlignment.Tabular);
            heading.Children.Add(subtitle);
        }

        var badge = StatusBadge(iconData, toneKey, 32);
        badge.VerticalAlignment = VerticalAlignment.Center;
        badge.Margin = new Thickness(0, 0, Tokens.Space.M, 0);

        var row = new DockPanel { LastChildFill = true, MinHeight = Tokens.Height.Large };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(Tokens.Space.L, 0, 0, 0);
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }

        row.Children.Add(heading);

        var bar = new Border
        {
            // 左右同宽（20）：左侧徽标与右侧按钮离窗口边缘一样远。
            Padding = new Thickness(Tokens.Space.L + Tokens.Space.XS, Tokens.Space.S, Tokens.Space.L + Tokens.Space.XS, Tokens.Space.S),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row,
        };
        bar.SetResourceReference(Border.BackgroundProperty, Keys.Surface);
        bar.SetResourceReference(Border.BorderBrushProperty, Keys.Divider);
        return bar;
    }

    // --- 图片上的浮层标签、输入校验（图库卡片、重命名对话框共用） ---

    /// <summary>
    /// 浮在图片上的小标签：实心面板底 + 细边 + 小圆角，任何截图背景上都读得清
    /// （<see cref="Pill"/> 是半透明色调底，只适合放在纯色面板上）。
    /// </summary>
    internal static Border OverlayChip(string text, string? iconData = null)
    {
        var label = Text(text, 11.5, weight: AppFonts.Semibold, measureNaturalWidth: true, brushKey: Keys.Secondary);
        UIElement child = label;
        if (iconData is not null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = AppIcons.Create(iconData, 12, Keys.Secondary, 2.0);
            icon.Margin = new Thickness(0, 0, 5, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            row.Children.Add(label);
            child = row;
        }

        var chip = new Border
        {
            Child = child,
            Height = 22,
            Padding = new Thickness(8, 0, 9, 1),
            CornerRadius = new CornerRadius(Tokens.Radius.Small + 1),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        chip.SetResourceReference(Border.BackgroundProperty, Keys.Surface);
        chip.SetResourceReference(Border.BorderBrushProperty, Keys.Border);
        return chip;
    }

    /// <summary>
    /// 行内状态提示：小图标 + 一行可换行的说明（输入校验错误、警示）。
    /// <paramref name="text"/> 返回文字块，调用方改字 / 设 AutomationId；显隐控制返回的 Grid。
    /// </summary>
    internal static Grid InlineStatus(string iconData, string toneKey, out TextBlock text)
    {
        var icon = AppIcons.Create(iconData, 14, toneKey, 2.0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 6, 0);
        text = Text(string.Empty, TextRole.Caption, toneKey, wrap: true);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>
    /// 给输入框包一层"校验失败"外观：错误色 1px 描边 + 3px 淡错误色光晕，
    /// 叠在全局 TextBox 模板的强调色描边之上（聚焦时也保持红色）。用 <see cref="SetFieldInvalid"/> 切换。
    /// </summary>
    internal static Grid ValidatedField(TextBox box)
    {
        var halo = new Border
        {
            Margin = new Thickness(-3),
            CornerRadius = new CornerRadius(Tokens.Radius.Control + 3),
            BorderThickness = new Thickness(3),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        halo.SetResourceReference(Border.BorderBrushProperty, Keys.Error);
        var edge = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        edge.SetResourceReference(Border.BorderBrushProperty, Keys.Error);
        var host = new Grid { Margin = box.Margin };
        box.Margin = new Thickness(0);
        host.Children.Add(box);
        host.Children.Add(halo);
        host.Children.Add(edge);
        return host;
    }

    /// <summary>切换 <see cref="ValidatedField"/> 的校验失败外观（带淡入淡出）。</summary>
    internal static void SetFieldInvalid(Grid field, bool invalid)
    {
        if (field.Children.Count < 3)
        {
            return;
        }

        Fade(field.Children[1], UIElement.OpacityProperty, invalid ? 0.16 : 0, Tokens.Motion.Normal.TotalMilliseconds);
        Fade(field.Children[2], UIElement.OpacityProperty, invalid ? 1 : 0, Tokens.Motion.Normal.TotalMilliseconds);
    }
}
