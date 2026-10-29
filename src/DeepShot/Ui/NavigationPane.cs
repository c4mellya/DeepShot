using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DeepShot.Ui;

/// <summary>
/// 主窗口左侧导航栏（Windows 11 设置 / 照片风格）：直接铺在窗口底色上，
/// 选中项 = 悬停底色 + 左缘 3×16 强调色胶囊；窄窗口收成 48px 纯图标轨（悬停出提示）。
/// </summary>
/// <remarks>
/// 只管外观与键盘交互；选中哪一项由调用方 <see cref="Select"/> 同步（代码切页时也保持一致），
/// 用户点击时通过 <see cref="ItemInvoked"/> 通知，<see cref="Select"/> 本身不触发事件。
/// </remarks>
internal sealed class NavigationPane : Grid
{
    /// <summary>展开宽度。</summary>
    internal const double ExpandedWidth = 240;

    /// <summary>收起后的图标轨宽度。</summary>
    internal const double CompactWidth = 48;

    private const double ItemHeight = 36;

    private static readonly Lazy<Style> ItemStyle = new(() =>
        (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="Button">
          <Setter Property="OverridesDefaultStyle" Value="True" />
          <Setter Property="Background" Value="Transparent" />
          <Setter Property="Foreground" Value="{DynamicResource Theme.Foreground}" />
          <Setter Property="BorderThickness" Value="0" />
          <Setter Property="Padding" Value="0" />
          <Setter Property="SnapsToDevicePixels" Value="True" />
          <Setter Property="UseLayoutRounding" Value="True" />
          <Setter Property="HorizontalContentAlignment" Value="Stretch" />
          <Setter Property="FocusVisualStyle" Value="{DynamicResource DeepShot.FocusVisual}" />
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="Button">
                <Border x:Name="Bg" Background="{TemplateBinding Background}" CornerRadius="4" SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center" />
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Bg" Property="Background" Value="{DynamicResource Theme.HoverBackground}" />
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Bg" Property="Background" Value="{DynamicResource Theme.PressedBackground}" />
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """));

    private readonly StackPanel _top = new();
    private readonly StackPanel _footer = new();
    private readonly List<Item> _items = new();
    private string? _selectedKey;
    private bool _compact;

    /// <summary>用户点击/回车/空格激活某一项（参数为该项的键）。</summary>
    internal event Action<string>? ItemInvoked;

    internal NavigationPane()
    {
        Width = ExpandedWidth;
        Background = Brushes.Transparent;
        // 首项与页面标题行（顶部 24、行高 36）同一水平线。
        Margin = new Thickness(0, Tokens.Space.XL, 0, Tokens.Space.S);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SetRow(_top, 0);
        SetRow(_footer, 2);
        Children.Add(_top);
        Children.Add(_footer);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        AutomationProperties.SetAutomationId(this, "main-navigation");
        AutomationProperties.SetName(this, StringsUi.Get("S.Nav.Name"));
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>当前选中项的键。</summary>
    internal string? SelectedKey => _selectedKey;

    /// <summary>是否处于 48px 纯图标模式。</summary>
    internal bool IsCompact => _compact;

    /// <summary>在顶部添加一项。</summary>
    internal void AddItem(string key, string iconData, string labelKey, string automationId) =>
        Add(_top, key, iconData, labelKey, automationId);

    /// <summary>在底部（固定在导航栏最下方）添加一项。</summary>
    internal void AddFooterItem(string key, string iconData, string labelKey, string automationId) =>
        Add(_footer, key, iconData, labelKey, automationId);

    /// <summary>同步选中项（不触发 <see cref="ItemInvoked"/>）。</summary>
    internal void Select(string key)
    {
        if (string.Equals(_selectedKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _selectedKey = key;
        foreach (var item in _items)
        {
            item.SetSelected(string.Equals(item.Key, key, StringComparison.Ordinal), animate: IsLoaded);
        }
    }

    /// <summary>切换展开 / 图标轨。</summary>
    internal void SetCompact(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }

        _compact = compact;
        Width = compact ? CompactWidth : ExpandedWidth;
        foreach (var item in _items)
        {
            item.SetCompact(compact);
        }
    }

    /// <summary>语言切换后刷新文案。</summary>
    internal void RefreshTexts()
    {
        AutomationProperties.SetName(this, StringsUi.Get("S.Nav.Name"));
        foreach (var item in _items)
        {
            item.RefreshText(_compact);
        }
    }

    private void Add(StackPanel host, string key, string iconData, string labelKey, string automationId)
    {
        var item = new Item(key, iconData, labelKey, automationId);
        item.Button.Click += (_, _) => ItemInvoked?.Invoke(key);
        item.SetCompact(_compact);
        item.SetSelected(string.Equals(_selectedKey, key, StringComparison.Ordinal), animate: false);
        _items.Add(item);
        host.Children.Add(item.Button);
    }

    /// <summary>上下方向键在各项之间移动焦点；回车与空格激活（Button 自带空格，这里补回车）。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var index = _items.FindIndex(item => item.Button.IsKeyboardFocused);
        if (index < 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                _items[(index - 1 + _items.Count) % _items.Count].Button.Focus();
                e.Handled = true;
                break;
            case Key.Down:
                _items[(index + 1) % _items.Count].Button.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                ItemInvoked?.Invoke(_items[index].Key);
                e.Handled = true;
                break;
        }
    }

    /// <summary>一个导航项：36 高、4px 圆角；图标 16、文字 14；选中时左缘出现强调色胶囊。</summary>
    private sealed class Item
    {
        private readonly string _labelKey;
        private readonly TextBlock _label;
        private readonly Rectangle _pill;
        private readonly ScaleTransform _pillScale = new(1, 0.5);

        internal Item(string key, string iconData, string labelKey, string automationId)
        {
            Key = key;
            _labelKey = labelKey;

            _pill = new Rectangle
            {
                Width = 3,
                Height = 16,
                RadiusX = 1.5,
                RadiusY = 1.5,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _pillScale,
                IsHitTestVisible = false,
            };
            _pill.SetResourceReference(Shape.FillProperty, UiKit.Keys.Accent);

            // 图标左缘 12：40px 宽的图标轨里正好居中，展开/收起时图标不挪位。
            var icon = AppIcons.Create(iconData, 16, UiKit.Keys.Foreground);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(12, 0, 0, 0);

            _label = UiKit.Text(StringsUi.Get(labelKey), Tokens.Text.BodyLarge);
            _label.HorizontalAlignment = HorizontalAlignment.Left;
            _label.Margin = new Thickness(40, 0, 12, 0);

            var content = new Grid { Height = ItemHeight };
            content.Children.Add(icon);
            content.Children.Add(_label);
            content.Children.Add(_pill);

            Button = new Button
            {
                Style = ItemStyle.Value,
                Height = ItemHeight,
                Margin = new Thickness(Tokens.Space.XS, 1, Tokens.Space.XS, 1),
                Content = content,
            };
            ToolTipService.SetInitialShowDelay(Button, 400);
            ToolTipService.SetPlacement(Button, System.Windows.Controls.Primitives.PlacementMode.Right);
            AutomationProperties.SetAutomationId(Button, automationId);
            AutomationProperties.SetName(Button, _label.Text);
        }

        internal string Key { get; }

        internal Button Button { get; }

        internal void SetSelected(bool selected, bool animate)
        {
            if (selected)
            {
                Button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.HoverBackground);
            }
            else
            {
                Button.Background = Brushes.Transparent;
            }

            AutomationProperties.SetItemStatus(Button, selected ? "selected" : string.Empty);
            var opacity = selected ? 1.0 : 0.0;
            var scale = selected ? 1.0 : 0.5;
            if (animate)
            {
                UiKit.Fade(_pill, UIElement.OpacityProperty, opacity, Tokens.Motion.Normal.TotalMilliseconds);
                _pillScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(scale, Tokens.Motion.Normal) { EasingFunction = Tokens.Motion.Ease });
            }
            else
            {
                _pill.BeginAnimation(UIElement.OpacityProperty, null);
                _pillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _pill.Opacity = opacity;
                _pillScale.ScaleY = scale;
            }
        }

        internal void SetCompact(bool compact)
        {
            _label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            Button.ToolTip = compact ? _label.Text : null;
        }

        internal void RefreshText(bool compact)
        {
            _label.Text = StringsUi.Get(_labelKey);
            AutomationProperties.SetName(Button, _label.Text);
            SetCompact(compact);
        }
    }
}
