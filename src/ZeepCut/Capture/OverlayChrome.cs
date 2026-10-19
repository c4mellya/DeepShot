using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace ZeepCut.Capture;

/// <summary>
/// 截图遮罩工具栏的外观件：矢量图标 + 深色玻璃质感的按钮模板。
/// </summary>
/// <remarks>
/// 工具栏是压在冻结画面上的浮层，深浅主题下都保持同一套深色，所以这里的颜色是固定值，不走主题资源。
/// 选中态统一用 <c>Tag="on"</c> 驱动模板触发器（见 <see cref="SetOn"/>），不再逐个按钮换画刷。
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
    internal const string IconCopy = "M9,9 H19 V19 H9 Z M15,9 V5 H5 V15 H9";
    internal const string IconSave = "M12,4 V14.5 M7.5,10 L12,14.5 L16.5,10 M5,19.5 H19";
    internal const string IconPin = "M9,4.5 H15 L14.2,10 L17.5,13 H6.5 L9.8,10 Z M12,13 V20";
    internal const string IconCancel = "M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5";

    /// <summary>马赛克图标：4×4 棋盘格（实心）。</summary>
    internal static readonly string IconMosaic = BuildMosaic();

    private static readonly SolidColorBrush IconBrush = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TextBrush = Frozen(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush MutedBrush = Frozen(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    /// <summary>图标按钮 / 工具按钮：透明底，悬停提亮，Tag=on 时是强调色实心。</summary>
    private static readonly Lazy<ControlTemplate> ToolTemplate = new(() => Parse($$"""
        <ControlTemplate {{Ns}} TargetType="Button">
          <Border x:Name="Bd" CornerRadius="8" Background="Transparent" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#26FFFFFF" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#40FFFFFF" />
            </Trigger>
            <Trigger Property="Tag" Value="on">
              <Setter TargetName="Bd" Property="Background" Value="#FF2F7DF6" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    /// <summary>粗细按钮：选中时是浅色描边，不用强调色（和工具区分开）。</summary>
    private static readonly Lazy<ControlTemplate> ChipTemplate = new(() => Parse($$"""
        <ControlTemplate {{Ns}} TargetType="Button">
          <Border x:Name="Bd" CornerRadius="8" Background="Transparent" BorderThickness="1" BorderBrush="Transparent" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#26FFFFFF" />
            </Trigger>
            <Trigger Property="Tag" Value="on">
              <Setter TargetName="Bd" Property="Background" Value="#33FFFFFF" />
              <Setter TargetName="Bd" Property="BorderBrush" Value="#99FFFFFF" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    /// <summary>颜色圆点：选中时外圈白环。</summary>
    private static readonly Lazy<ControlTemplate> SwatchTemplate = new(() => Parse($$"""
        <ControlTemplate {{Ns}} TargetType="Button">
          <Border x:Name="Ring" Width="24" Height="24" CornerRadius="12" BorderThickness="2"
                  BorderBrush="Transparent" Background="Transparent" SnapsToDevicePixels="True">
            <Ellipse Margin="2" Fill="{TemplateBinding Background}" Stroke="#40FFFFFF" StrokeThickness="1" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ring" Property="BorderBrush" Value="#80FFFFFF" />
            </Trigger>
            <Trigger Property="Tag" Value="on">
              <Setter TargetName="Ring" Property="BorderBrush" Value="#FFFFFFFF" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    /// <summary>主操作（复制）：强调色胶囊。</summary>
    private static readonly Lazy<ControlTemplate> PrimaryTemplate = new(() => Parse($$"""
        <ControlTemplate {{Ns}} TargetType="Button">
          <Border x:Name="Bd" CornerRadius="9" Background="#FF2F7DF6" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#FF4A90F8" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#FF1F68D8" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    /// <summary>取消：悬停时泛红。</summary>
    private static readonly Lazy<ControlTemplate> DangerTemplate = new(() => Parse($$"""
        <ControlTemplate {{Ns}} TargetType="Button">
          <Border x:Name="Bd" CornerRadius="9" Background="Transparent" SnapsToDevicePixels="True">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#55FF5A4D" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#80FF5A4D" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    private static ControlTemplate Parse(string xaml) => (ControlTemplate)XamlReader.Parse(xaml);

    /// <summary>创建一个 16px 的矢量图标。</summary>
    internal static FrameworkElement Icon(string data, bool filled = false, double size = 16)
    {
        var path = new Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = IconBrush,
            StrokeThickness = filled ? 1 : 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = filled ? IconBrush : null,
        };

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    private static Button Base(ControlTemplate template)
    {
        var button = new Button
        {
            Template = template,
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = TextBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        return button;
    }

    /// <summary>方形图标按钮（工具、撤销/重做）。</summary>
    internal static Button IconButton(string iconData, string tooltip, bool filled = false, double width = 34, double height = 32)
    {
        var button = Base(ToolTemplate.Value);
        button.Width = width;
        button.Height = height;
        button.Content = Icon(iconData, filled, 17);
        button.ToolTip = tooltip;
        return button;
    }

    /// <summary>图标 + 文字的动作按钮。</summary>
    internal static Button ActionButton(string iconData, string text, bool primary = false, bool danger = false)
    {
        var button = Base(primary ? PrimaryTemplate.Value : danger ? DangerTemplate.Value : ToolTemplate.Value);
        button.Height = 32;
        button.Padding = new Thickness(11, 0, 13, 0);
        button.Content = Labeled(iconData, text, primary ? TextBrush : danger ? MutedBrush : TextBrush);
        return button;
    }

    private static StackPanel Labeled(string iconData, string text, Brush foreground)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Icon(iconData, false, 15));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    /// <summary>颜色圆点。</summary>
    internal static Button Swatch(Color color)
    {
        var button = Base(SwatchTemplate.Value);
        button.Width = 24;
        button.Height = 24;
        button.Margin = new Thickness(1, 0, 1, 0);
        button.Background = Frozen(color);
        return button;
    }

    /// <summary>粗细按钮：中间一个与粗细成比例的圆点。</summary>
    internal static Button ThicknessChip(int thickness)
    {
        var button = Base(ChipTemplate.Value);
        button.Width = 30;
        button.Height = 30;
        var diameter = thickness switch { <= 1 => 3.0, <= 3 => 6.0, <= 6 => 9.0, _ => 13.0 };
        button.Content = new System.Windows.Shapes.Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = IconBrush,
            IsHitTestVisible = false,
        };
        return button;
    }

    /// <summary>选中态开关（由模板触发器渲染）。</summary>
    internal static void SetOn(Button button, bool on) => button.Tag = on ? "on" : null;

    /// <summary>一组按钮的圆角底托。</summary>
    internal static Border Group(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Border
        {
            Background = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            Child = panel,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>竖向分隔线。</summary>
    internal static Border Divider() => new()
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(10, 0, 10, 0),
        Background = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>工具栏外壳：深色玻璃 + 细描边 + 柔和阴影。</summary>
    internal static Border Shell(UIElement child) => new()
    {
        Background = Frozen(Color.FromArgb(0xF2, 0x1B, 0x1C, 0x21)),
        BorderBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(9),
        Child = child,
        Visibility = Visibility.Collapsed,
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 22,
            ShadowDepth = 5,
            Direction = 270,
            Opacity = 0.45,
            Color = Colors.Black,
        },
    };

    /// <summary>选区尺寸文字的样式（弱化的小字）。</summary>
    internal static void StyleSizeText(TextBlock text)
    {
        text.Foreground = MutedBrush;
        text.FontSize = 12;
        text.FontWeight = FontWeights.Medium;
        text.MinWidth = 0;
        text.Margin = new Thickness(6, 0, 12, 0);
    }

    internal static void SetNames(Button button, string name, string automationId)
    {
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, automationId);
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

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
