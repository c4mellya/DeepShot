using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace DeepShot.Ui;

/// <summary>泛型类的静态字段按闭合类型各一份，分组号必须放在非泛型类里才全局唯一。</summary>
internal static class SegmentedGroupIds
{
    private static int _counter;

    internal static int Next() => Interlocked.Increment(ref _counter);
}

/// <summary>
/// 分段选择器：几个互斥选项并排，选中项是强调色胶囊。颜色全部走 <c>Theme.*</c> 动态资源，换主题即时生效。
/// </summary>
internal sealed class SegmentedControl<T> : Border where T : struct, Enum
{
    private static readonly Lazy<ControlTemplate> Template = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="RadioButton">
          <Grid SnapsToDevicePixels="True">
            <Border x:Name="Pill" CornerRadius="7" Opacity="0" Background="{DynamicResource Theme.Accent}" />
            <Border x:Name="Ov" CornerRadius="7" Opacity="0" Background="{DynamicResource Theme.Foreground}" />
            <ContentPresenter x:Name="Cp" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}"
                              TextElement.Foreground="{TemplateBinding Foreground}" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.06" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.1" />
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Trigger.EnterActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Pill" Storyboard.TargetProperty="Opacity" From="0" To="1" Duration="0:0:0.14" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.EnterActions>
              <Trigger.ExitActions>
                <BeginStoryboard>
                  <Storyboard>
                    <DoubleAnimation Storyboard.TargetName="Pill" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.1" />
                  </Storyboard>
                </BeginStoryboard>
              </Trigger.ExitActions>
              <Setter TargetName="Ov" Property="Opacity" Value="0" />
              <Setter TargetName="Cp" Property="TextElement.Foreground" Value="{DynamicResource Theme.OnAccent}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    private readonly string _group = "segmented-" + SegmentedGroupIds.Next();
    private readonly StackPanel _stack = new() { Orientation = Orientation.Horizontal };
    private readonly List<(T Value, string Key, RadioButton Button)> _items = new();
    private bool _suppress;

    internal SegmentedControl()
    {
        // 外框圆角 10、内边距 3、选中胶囊圆角 7：三者同心；整体高度对齐 34px 控件高。
        CornerRadius = new CornerRadius(Tokens.Radius.Popup);
        Padding = new Thickness(3);
        MinHeight = Tokens.Height.Control;
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        Child = _stack;
        SetResourceReference(BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
        SetResourceReference(BorderBrushProperty, ThemeManager.BrushKeys.Divider);
    }

    /// <summary>用户点选了某一项（程序调用 <see cref="Select"/> 不触发）。</summary>
    internal event Action<T>? UserSelected;

    internal T? Selected { get; private set; }

    internal void Add(T value, string textKey, string automationId)
    {
        var button = new RadioButton
        {
            GroupName = _group,
            Content = StringsUi.Get(textKey),
            Template = Template.Value,
            Padding = new Thickness(14, 0, 14, 1),
            MinHeight = Tokens.Height.Control - 8,
            MinWidth = 56,
            FontSize = Tokens.Text.Body,
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null,
        };
        button.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.SecondaryForeground);
        button.SetResourceReference(Control.FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, (string)button.Content);
        button.Checked += (_, _) =>
        {
            Selected = value;
            if (!_suppress)
            {
                UserSelected?.Invoke(value);
            }
        };
        _items.Add((value, textKey, button));
        _stack.Children.Add(button);
    }

    /// <summary>程序设置选中项（不触发 <see cref="UserSelected"/>）。</summary>
    internal void Select(T value)
    {
        _suppress = true;
        try
        {
            foreach (var item in _items)
            {
                item.Button.IsChecked = EqualityComparer<T>.Default.Equals(item.Value, value);
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>语言切换后刷新各项文案。</summary>
    internal void RefreshTexts()
    {
        foreach (var item in _items)
        {
            var text = StringsUi.Get(item.Key);
            item.Button.Content = text;
            AutomationProperties.SetName(item.Button, text);
        }
    }
}
