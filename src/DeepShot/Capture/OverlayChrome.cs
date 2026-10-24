using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using DeepShot.Ui;

namespace DeepShot.Capture;

/// <summary>
/// 截图遮罩 / 贴图 / 编辑窗口共用的“深色玻璃”浮层外观件：矢量图标、按钮样式、徽标、外壳。
/// </summary>
/// <remarks>
/// <para>
/// 浮层压在任意截图画面上，深浅主题下都保持深色玻璃（<c>Theme.OverlayBar</c> 两套主题都是深色），
/// 只有<b>强调色</b>跟随主题（<c>Theme.Accent</c> / <c>Theme.OnAccent</c>），这样选中态与主界面同色。
/// </para>
/// <para>
/// 按钮一律是 <see cref="Style"/>（模板 + 前景色 Setter）：前景色来自样式而不是本地值，
/// 模板触发器（Tag=on、悬停）才能改写它；图标 Path 的描边/填充绑定继承来的
/// <see cref="TextElement.ForegroundProperty"/>，所以图标、文字、粗细圆点跟着按钮前景一起变色。
/// </para>
/// <para>
/// 选中态统一用 <c>Tag="on"</c> 驱动（见 <see cref="SetOn"/>）。悬停动画是模板内固定值的 Storyboard（时长取自 <see cref="Tokens.Motion"/>，拼 XAML 时写死），
/// 不绑定任何值，满足模板触发器里 Storyboard 必须可冻结的要求。
/// </para>
/// </remarks>
internal static class OverlayChrome
{
    // ===== 24×24 网格上的图标（线条风格，圆头圆角）=====
    internal const string IconSelect = "M6,3.5 V18.5 L9.8,15 L12.5,20.5 L15,19.3 L12.3,13.8 L17.5,13.6 Z";
    internal const string IconRectangle = "M4.5,6.5 H19.5 V17.5 H4.5 Z";
    internal const string IconEllipse = "M4,12 A8,6.5 0 1 0 20,12 A8,6.5 0 1 0 4,12 Z";
    internal const string IconArrow = "M5,19 L18,6 M9,6 H18 V15";
    internal const string IconLine = "M5,19 L19,5";
    internal const string IconFreehand = "M4,20 L8.5,18.8 L19.2,8.1 A2,2 0 0 0 19.2,5.3 L18.7,4.8 A2,2 0 0 0 15.9,4.8 L5.2,15.5 Z M14,6.8 L17.2,10";
    internal const string IconText = "M5,7 V5 H19 V7 M12,5 V19 M9,19 H15";
    internal const string IconUndo = "M9,5 L4.5,9.5 L9,14 M4.5,9.5 H14 A5,5 0 0 1 14,19.5 H9";
    internal const string IconRedo = "M15,5 L19.5,9.5 L15,14 M19.5,9.5 H10 A5,5 0 0 0 10,19.5 H15";
    // 复制、贴图直接取 AppIcons：截图浮层、贴图工具条与历史/设置/欢迎页同一动作只有一个字形。
    internal const string IconCopy = AppIcons.Copy;
    internal const string IconSave = "M12,4 V14.5 M7.5,10 L12,14.5 L16.5,10 M5,19.5 H19";
    internal const string IconPin = AppIcons.Pin;
    internal const string IconTrash = "M5,7 H19 M10,7 V5 H14 V7 M7,7 L8,19 H16 L17,7 M10.5,11 V16 M13.5,11 V16";
    internal const string IconMinus = "M6,12 H18";
    internal const string IconPlus = "M12,6 V18 M6,12 H18";
    internal const string IconFit = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15";
    internal const string IconCancel = "M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5";

    /// <summary>挂锁：锁定选区 / 锁定贴图的徽标图标。</summary>
    internal const string IconLock = "M8,11 V8.5 A4,4 0 0 1 16,8.5 V11 M6.5,11 H17.5 A1.5,1.5 0 0 1 19,12.5 V18.5 A1.5,1.5 0 0 1 17.5,20 H6.5 A1.5,1.5 0 0 1 5,18.5 V12.5 A1.5,1.5 0 0 1 6.5,11 Z";

    /// <summary>马赛克图标：4×4 棋盘格（实心）。</summary>
    internal static readonly string IconMosaic = BuildMosaic();

    /// <summary>弱化文字（尺寸、提示）：深色玻璃上 70% 白。</summary>
    internal static readonly SolidColorBrush MutedBrush = UiKit.Frozen(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

    /// <summary>状态行：普通提示。</summary>
    internal static readonly SolidColorBrush StatusInfoBrush = UiKit.Frozen(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));

    /// <summary>状态行：错误提示（深底上可读的珊瑚红）。</summary>
    internal static readonly SolidColorBrush StatusErrorBrush = UiKit.Frozen((Color)ColorConverter.ConvertFromString(Tokens.Danger.GlassText));

    /// <summary>玻璃面板的细描边（深浅主题同一值：深色玻璃上 12% 白）。</summary>
    internal static readonly SolidColorBrush HairlineBrush = UiKit.Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    /// <summary>外壳描边：顶部更亮、底部更暗的渐变，模拟玻璃上沿的高光。</summary>
    private static readonly Brush ShellEdgeBrush = CreateShellEdge();

    // 模板里的固定值直接取自设计令牌（模板触发器里的 Storyboard 不能绑定，只能在拼 XAML 时写死）。
    private static readonly string Ctl = Tokens.Radius.Control.ToString(CultureInfo.InvariantCulture);
    private static readonly string Fast = Tokens.Motion.Fast.ToString("c", CultureInfo.InvariantCulture);
    private static readonly string Normal = Tokens.Motion.Normal.ToString("c", CultureInfo.InvariantCulture);

    /// <summary>浮层按钮的统一边长（工具、撤销/重做、动作按钮、粗细按钮）。</summary>
    private const double ControlSize = 32;

    /// <summary>分组底托的内边距与外沿高度（按钮 + 上下内边距）。</summary>
    private const double TrayPadding = 2;
    private const double TrayHeight = ControlSize + (TrayPadding * 2);

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    /// <summary>悬停层淡入淡出（固定值，模板触发器里的 Storyboard 不能绑定）。</summary>
    private static string HoverFade(string target, string to) => $$"""
            <Trigger.EnterActions>
              <BeginStoryboard>
                <Storyboard>
                  <DoubleAnimation Storyboard.TargetName="{{target}}" Storyboard.TargetProperty="Opacity" To="{{to}}" Duration="{{Fast}}" />
                </Storyboard>
              </BeginStoryboard>
            </Trigger.EnterActions>
            <Trigger.ExitActions>
              <BeginStoryboard>
                <Storyboard>
                  <DoubleAnimation Storyboard.TargetName="{{target}}" Storyboard.TargetProperty="Opacity" To="0" Duration="{{Normal}}" />
                </Storyboard>
              </BeginStoryboard>
            </Trigger.ExitActions>
""";

    /// <summary>
    /// 工具 / 图标 / 次要动作按钮：透明底，悬停 12% 白，按下再叠 8%；Tag=on 时是强调色实心 + OnAccent 前景。
    /// </summary>
    private static readonly Lazy<Style> ToolStyle = new(() => ParseStyle($$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource Theme.OverlayForeground}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid SnapsToDevicePixels="True" Background="Transparent">
                  <Border x:Name="Sel" CornerRadius="{{Ctl}}" Background="{DynamicResource Theme.Accent}" Opacity="0" />
                  <Border x:Name="Hov" CornerRadius="{{Ctl}}" Background="#FFFFFFFF" Opacity="0" />
                  <Border x:Name="Prs" CornerRadius="{{Ctl}}" Background="#FFFFFFFF" Opacity="0" />
                  <ContentPresenter x:Name="Cp" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
        {{HoverFade("Hov", "0.12")}}
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Prs" Property="Opacity" Value="0.08" />
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="Sel" Property="Opacity" Value="1" />
                    <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter TargetName="Cp" Property="Opacity" Value="0.38" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>粗细按钮：选中时是 18% 白底 + 20% 白细边（中性色，和强调色的工具区分开）。</summary>
    private static readonly Lazy<Style> ChipStyle = new(() => ParseStyle($$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource Theme.OverlayForeground}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid SnapsToDevicePixels="True" Background="Transparent">
                  <Border x:Name="On" CornerRadius="{{Ctl}}" Background="#2EFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1" Opacity="0" />
                  <Border x:Name="Hov" CornerRadius="{{Ctl}}" Background="#FFFFFFFF" Opacity="0" />
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
        {{HoverFade("Hov", "0.10")}}
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="On" Property="Opacity" Value="1" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>颜色圆点：悬停时圆点轻微放大 + 半透明外环，选中时实心白环。</summary>
    private static readonly Lazy<Style> SwatchStyle = new(() => ParseStyle($$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid Width="24" Height="24" Background="Transparent" SnapsToDevicePixels="True">
                  <Ellipse x:Name="HovRing" Stroke="#73FFFFFF" StrokeThickness="1.5" Opacity="0" />
                  <Ellipse x:Name="OnRing" Stroke="#FFFFFFFF" StrokeThickness="2" Opacity="0" />
                  <Ellipse x:Name="Dot" Margin="4" Fill="{TemplateBinding Background}" Stroke="#40FFFFFF" StrokeThickness="1"
                           RenderTransformOrigin="0.5,0.5">
                    <Ellipse.RenderTransform>
                      <ScaleTransform ScaleX="1" ScaleY="1" />
                    </Ellipse.RenderTransform>
                  </Ellipse>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="HovRing" Property="Opacity" Value="1" />
                    <Trigger.EnterActions>
                      <BeginStoryboard>
                        <Storyboard>
                          <DoubleAnimation Storyboard.TargetName="Dot" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1.12" Duration="{{Fast}}" />
                          <DoubleAnimation Storyboard.TargetName="Dot" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1.12" Duration="{{Fast}}" />
                        </Storyboard>
                      </BeginStoryboard>
                    </Trigger.EnterActions>
                    <Trigger.ExitActions>
                      <BeginStoryboard>
                        <Storyboard>
                          <DoubleAnimation Storyboard.TargetName="Dot" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1" Duration="{{Normal}}" />
                          <DoubleAnimation Storyboard.TargetName="Dot" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1" Duration="{{Normal}}" />
                        </Storyboard>
                      </BeginStoryboard>
                    </Trigger.ExitActions>
                  </Trigger>
                  <Trigger Property="Tag" Value="on">
                    <Setter TargetName="OnRing" Property="Opacity" Value="1" />
                    <Setter TargetName="HovRing" Property="Opacity" Value="0" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>主操作（复制）：强调色实心，悬停提亮、按下压暗。</summary>
    private static readonly Lazy<Style> PrimaryStyle = new(() => ParseStyle($$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource Theme.OnAccent}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid SnapsToDevicePixels="True">
                  <Border CornerRadius="{{Ctl}}" Background="{DynamicResource Theme.Accent}" />
                  <Border x:Name="Hov" CornerRadius="{{Ctl}}" Background="#FFFFFFFF" Opacity="0" />
                  <Border x:Name="Prs" CornerRadius="{{Ctl}}" Background="#FF000000" Opacity="0" />
                  <ContentPresenter x:Name="Cp" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
        {{HoverFade("Hov", "0.14")}}
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Prs" Property="Opacity" Value="0.14" />
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter TargetName="Cp" Property="Opacity" Value="0.5" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>取消：平时是弱化白，悬停泛红、文字转珊瑚红。</summary>
    private static readonly Lazy<Style> DangerStyle = new(() => ParseStyle($$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Foreground" Value="#D9FFFFFF" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid SnapsToDevicePixels="True" Background="Transparent">
                  <Border x:Name="Hov" CornerRadius="{{Ctl}}" Background="{{Tokens.Danger.GlassFill}}" Opacity="0" />
                  <Border x:Name="Prs" CornerRadius="{{Ctl}}" Background="{{Tokens.Danger.GlassFill}}" Opacity="0" />
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{{Tokens.Danger.GlassText}}" />
        {{HoverFade("Hov", "0.20")}}
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Prs" Property="Opacity" Value="0.14" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    /// <summary>圆形玻璃按钮（贴图关闭等）：深色玻璃圆 + 细白边，悬停提亮。</summary>
    private static readonly Lazy<Style> GlassStyle = new(() => ParseStyle(GlassXaml(hover: "#FFFFFFFF", hoverTo: "0.16", hoverForeground: null)));

    /// <summary>危险版圆形玻璃按钮：悬停变成实心红 + 白色图标（关闭贴图）。</summary>
    private static readonly Lazy<Style> GlassDangerStyle = new(() => ParseStyle(GlassXaml(hover: Tokens.Danger.GlassFill, hoverTo: "1", hoverForeground: "#FFFFFFFF")));

    private static string GlassXaml(string hover, string hoverTo, string? hoverForeground) => $$"""
        <Style {{Ns}} TargetType="Button">
          <Setter Property="Foreground" Value="{DynamicResource Theme.OverlayForeground}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Grid SnapsToDevicePixels="True">
                  <Ellipse Fill="{DynamicResource Theme.OverlayBar}" Stroke="#33FFFFFF" StrokeThickness="1" />
                  <Ellipse x:Name="Hov" Fill="{{hover}}" Opacity="0" />
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    {{(hoverForeground is null ? string.Empty : $"<Setter Property=\"Foreground\" Value=\"{hoverForeground}\" />")}}
        {{HoverFade("Hov", hoverTo)}}
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Hov" Property="Opacity" Value="0.8" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """;

    private static Style ParseStyle(string xaml)
    {
        var style = (Style)XamlReader.Parse(xaml);
        style.Seal();
        return style;
    }

    /// <summary>
    /// 创建矢量图标。描边/填充绑定元素自身继承来的前景色（<see cref="TextElement.ForegroundProperty"/>），
    /// 放进按钮或徽标后自动跟随它们的前景色（含选中、悬停的变化）。
    /// </summary>
    internal static FrameworkElement Icon(string data, bool filled = false, double size = 16)
    {
        var path = new Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = filled ? 1 : 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        var foreground = new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self };
        path.SetBinding(Shapes.Shape.StrokeProperty, foreground);
        if (filled)
        {
            path.SetBinding(Shapes.Shape.FillProperty, foreground);
        }

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    private static Button Base(Style style)
    {
        var button = new Button
        {
            Style = style,
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            FontSize = Tokens.Text.Body,
            FontWeight = AppFonts.Semibold,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        button.SetResourceReference(Control.FontFamilyProperty, ThemeManager.FontFamilyKey);
        return button;
    }

    /// <summary>浮层上的一段文字：不设本地前景色，继承按钮 / 徽标的前景。</summary>
    private static TextBlock Label(string text, double fontSize = Tokens.Text.Body)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = AppFonts.Semibold,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);
        return label;
    }

    /// <summary>方形图标按钮（工具、撤销/重做）。</summary>
    internal static Button IconButton(string iconData, string tooltip, bool filled = false, double width = ControlSize, double height = ControlSize)
    {
        var button = Base(ToolStyle.Value);
        button.Width = width;
        button.Height = height;
        button.Content = Icon(iconData, filled, 18);
        button.ToolTip = tooltip;
        return button;
    }

    /// <summary>纯文字的小按钮（如缩放的“1:1”）。</summary>
    internal static Button TextButton(string text, string tooltip, double width = 40)
    {
        var button = Base(ToolStyle.Value);
        button.Width = width;
        button.Height = ControlSize;
        button.ToolTip = tooltip;
        var label = Label(text, Tokens.Text.Caption);
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        button.Content = label;
        return button;
    }

    /// <summary>
    /// 图标 + 文字的动作按钮。高度与分组底托（<see cref="Group"/>）外沿一致，
    /// 和同一行的撤销/重做底托上下对齐。
    /// </summary>
    internal static Button ActionButton(string iconData, string text, bool primary = false, bool danger = false)
    {
        var button = Base(primary ? PrimaryStyle.Value : danger ? DangerStyle.Value : ToolStyle.Value);
        button.Height = TrayHeight;
        button.Padding = new Thickness(10, 0, 12, 0);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Icon(iconData, false, 16));
        var label = Label(text);
        label.Margin = new Thickness(6, 0, 0, 0);
        panel.Children.Add(label);
        button.Content = panel;
        return button;
    }

    /// <summary>颜色圆点。</summary>
    internal static Button Swatch(Color color)
    {
        var button = Base(SwatchStyle.Value);
        button.Width = 24;
        button.Height = 24;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Background = UiKit.Frozen(color);
        return button;
    }

    /// <summary>粗细按钮：中间一个与粗细成比例的圆点（颜色跟随按钮前景）。</summary>
    internal static Button ThicknessChip(int thickness)
    {
        var button = Base(ChipStyle.Value);
        button.Width = ControlSize;
        button.Height = ControlSize;
        var diameter = thickness switch { <= 1 => 4.0, <= 3 => 6.0, <= 6 => 10.0, _ => 14.0 };
        var dot = new Shapes.Ellipse
        {
            Width = diameter,
            Height = diameter,
            IsHitTestVisible = false,
        };
        dot.SetBinding(Shapes.Shape.FillProperty, new Binding { Path = new PropertyPath(TextElement.ForegroundProperty), RelativeSource = RelativeSource.Self });
        button.Content = dot;
        return button;
    }

    /// <summary>
    /// 圆形玻璃图标按钮（贴图右上角的关闭等）：<paramref name="danger"/> 时悬停变成实心红。
    /// </summary>
    internal static Button GlassIconButton(string iconData, string tooltip, double size = 24, bool danger = false)
    {
        var button = Base(danger ? GlassDangerStyle.Value : GlassStyle.Value);
        button.Width = size;
        button.Height = size;
        button.ToolTip = tooltip;
        button.Content = Icon(iconData, false, Math.Round(size * 0.55));
        return button;
    }

    /// <summary>选中态开关（由样式触发器渲染）。</summary>
    internal static void SetOn(Button button, bool on) => button.Tag = on ? "on" : null;

    /// <summary>一组按钮的圆角底托（外圆角 = 按钮圆角 8 + 内边距 2，同心）。</summary>
    internal static Border Group(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Border
        {
            Background = GroupBrush,
            CornerRadius = new CornerRadius(Tokens.Radius.Popup),
            Padding = new Thickness(TrayPadding),
            Child = panel,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static readonly SolidColorBrush GroupBrush = UiKit.Frozen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));

    private static readonly SolidColorBrush DividerBrush = UiKit.Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    /// <summary>竖向分隔线。</summary>
    internal static Border Divider() => new()
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(Tokens.Space.S, 0, Tokens.Space.S, 0),
        Background = DividerBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// 工具栏外壳：深色玻璃 + 顶部高光细边 + 柔和投影。
    /// 投影放在内容<b>后面</b>的兄弟底板上（位图缓存），绝不加在文字的祖先上；返回的外层 Border 不带任何效果。
    /// </summary>
    internal static Border Shell(UIElement child)
    {
        var plate = UiKit.ShadowPlate(Tokens.Radius.Card, blur: 28, depth: 8, opacity: 0.42);
        plate.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.OverlayBar);
        plate.CacheMode = new BitmapCache();

        var body = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Card),
            BorderBrush = ShellEdgeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Tokens.Space.S),
            Child = child,
        };
        body.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.OverlayBar);
        body.SetResourceReference(TextElement.ForegroundProperty, ThemeManager.BrushKeys.OverlayForeground);

        var layers = new Grid();
        layers.Children.Add(plate);
        layers.Children.Add(body);

        return new Border
        {
            Child = layers,
            Visibility = Visibility.Collapsed,
        };
    }

    /// <summary>
    /// 浮在画面上的小徽标（选区尺寸、锁定状态）：深色玻璃胶囊 + 细边。不加投影——它跟着鼠标逐帧移动。
    /// </summary>
    internal static Border Badge(UIElement child)
    {
        var badge = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Small),
            BorderBrush = HairlineBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Tokens.Space.S, 3, Tokens.Space.S, 3),
            Child = child,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        badge.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.OverlayBar);
        badge.SetResourceReference(TextElement.ForegroundProperty, ThemeManager.BrushKeys.OverlayForeground);
        return badge;
    }

    /// <summary>徽标里的文字：说明字号、强调字重、等宽数字（尺寸跳动时不抖）。</summary>
    internal static TextBlock BadgeText(string text = "")
    {
        var label = Label(text, Tokens.Text.Caption);
        label.TextTrimming = TextTrimming.None;
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        return label;
    }

    /// <summary>
    /// 锁定徽标：挂锁图标 + 文字。图标色取 <paramref name="iconBrush"/>，其次主题资源 <paramref name="iconBrushKey"/>，都没有时跟随徽标前景色。
    /// </summary>
    internal static Border LockBadge(TextBlock text, Brush? iconBrush = null, string? iconBrushKey = null)
    {
        var icon = Icon(IconLock, false, 13);
        if (iconBrush is not null)
        {
            TextElement.SetForeground(icon, iconBrush);
        }
        else if (iconBrushKey is not null)
        {
            icon.SetResourceReference(TextElement.ForegroundProperty, iconBrushKey);
        }

        icon.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(5, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(text);
        return Badge(row);
    }

    /// <summary>选区尺寸文字的样式（弱化的小字，等宽数字）。</summary>
    internal static void StyleSizeText(TextBlock text)
    {
        text.Foreground = MutedBrush;
        text.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);
        text.FontSize = Tokens.Text.Caption;
        text.FontWeight = AppFonts.Regular;
        text.MinWidth = 0;
        text.Margin = new Thickness(Tokens.Space.XS, 0, Tokens.Space.M, 0);
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
    }

    internal static void SetNames(Button button, string name, string automationId)
    {
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, automationId);
    }

    /// <summary>标注选中态的虚线框（强调色，跟随主题）。截图浮层与历史编辑窗口共用，保证两处选中态一模一样。</summary>
    internal static Shapes.Path EditorOutline(double thickness = 1.5)
    {
        var outline = new Shapes.Path
        {
            StrokeThickness = thickness,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeDashCap = PenLineCap.Flat,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        outline.SetResourceReference(Shapes.Shape.StrokeProperty, UiKit.Keys.Accent);
        return outline;
    }

    /// <summary>标注手柄：白底 + 强调色描边的小圆角方块（与选区的圆形手柄区分开）。</summary>
    internal static Shapes.Rectangle EditorHandle(double size = 8)
    {
        var handle = new Shapes.Rectangle
        {
            Width = size,
            Height = size,
            RadiusX = 2,
            RadiusY = 2,
            Fill = Brushes.White,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        handle.SetResourceReference(Shapes.Shape.StrokeProperty, UiKit.Keys.Accent);
        return handle;
    }

    private static Brush CreateShellEdge()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1));
        brush.Freeze();
        return brush;
    }

    private static string BuildMosaic()
    {
        var sb = new StringBuilder();
        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                if ((row + col) % 2 != 0)
                {
                    continue;
                }

                var x = 4 + col * 4;
                var y = 4 + row * 4;
                sb.Append(CultureInfo.InvariantCulture, $"M{x},{y} h4 v4 h-4 Z ");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
