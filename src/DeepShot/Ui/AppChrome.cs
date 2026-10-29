using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace DeepShot.Ui;

/// <summary>
/// 自绘标题栏：用 <see cref="WindowChrome"/> 去掉系统标题栏（保留拖动、双击最大化、贴边、缩放、阴影），
/// 在窗口内容顶部画一条与主题一致的标题栏（应用图标 + 标题 + 最小化/最大化/关闭）。
/// </summary>
internal static class AppChrome
{
    internal const double TitleBarHeight = 32;

    private static ImageSource? _icon;

    /// <summary>应用图标（透明底圆角贴纸，256px，已冻结）。</summary>
    internal static ImageSource? AppIcon
    {
        get
        {
            if (_icon is not null)
            {
                return _icon;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri("pack://application:,,,/DeepShot;component/Assets/app-icon.png", UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                _icon = image;
            }
            catch (Exception)
            {
                _icon = null;
            }

            return _icon;
        }
    }

    private static ImageSource? _windowIcon;

    /// <summary>多尺寸 .ico（任务栏、Alt+Tab、窗口左上角按需要的尺寸取帧）。</summary>
    internal static ImageSource? WindowIcon
    {
        get
        {
            if (_windowIcon is not null)
            {
                return _windowIcon;
            }

            try
            {
                var frame = BitmapFrame.Create(
                    new Uri("pack://application:,,,/DeepShot;component/Assets/app.ico", UriKind.Absolute),
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                frame.Freeze();
                _windowIcon = frame;
            }
            catch (Exception)
            {
                _windowIcon = AppIcon;
            }

            return _windowIcon;
        }
    }

    private static bool _iconRegistered;

    /// <summary>让之后创建的每个窗口都显式使用新图标（任务栏、Alt+Tab 不再依赖可执行文件里缓存的旧图标）。</summary>
    internal static void RegisterWindowIcon()
    {
        if (_iconRegistered)
        {
            return;
        }

        _iconRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window { Icon: null } window && WindowIcon is { } icon)
                {
                    window.Icon = icon;
                }
            }));
    }

    /// <summary>
    /// 让系统窗框的深浅色标志（标题栏区域、贴靠/预览缩略图）始终跟随当前主题：
    /// 句柄创建时设置一次，窗口打开期间切换主题再同步一次，关闭时退订。
    /// </summary>
    internal static void SyncDarkTitleBar(Window window)
    {
        void Sync() => DwmApi.TrySetDarkTitleBar(window, ThemeManager.Palette.IsDark);
        void OnThemeChanged(DeepShot.Core.Settings.AppTheme _) => window.Dispatcher.BeginInvoke(Sync);

        window.SourceInitialized += (_, _) => Sync();
        ThemeManager.Changed += OnThemeChanged;
        window.Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    /// <summary>创建一个应用图标 Image（高质量缩放）。</summary>
    internal static Image IconImage(double size)
    {
        var image = new Image
        {
            Source = AppIcon,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>
    /// 给窗口套上自绘标题栏。必须在 <c>window.Content</c> 已经设置之后调用；
    /// 原内容被放到标题栏下方的第二行。
    /// </summary>
    /// <param name="window">要处理的窗口。</param>
    /// <param name="allowMaximize">是否显示最大化按钮（对话框传 false）。</param>
    /// <param name="allowMinimize">是否显示最小化按钮。</param>
    internal static void Apply(Window window, bool allowMaximize = true, bool allowMinimize = true)
    {
        var inner = window.Content as UIElement;
        window.Content = null;

        // 逐像素对齐 + GDI 兼容的文字排版：高 DPI / 非整数缩放下文字与图标边缘才锐利。
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(window, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(window, ClearTypeHint.Enabled);

        var resizable = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = TitleBarHeight,
            ResizeBorderThickness = resizable ? new Thickness(6) : new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += (_, _) => DwmApi.TrySetRoundedCorners(window);

        // --- 标题栏 ---
        var bar = new Grid { Height = TitleBarHeight };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Win11 系统标题栏：16px 图标，左缘 16，与标题间距 12。
        var icon = IconImage(16);
        icon.Margin = new Thickness(16, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        bar.Children.Add(icon);

        var title = new TextBlock
        {
            FontSize = Tokens.Text.Caption,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.SecondaryForeground);
        title.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Button? maximize = null;
        if (allowMinimize && window.ResizeMode != ResizeMode.NoResize)
        {
            buttons.Children.Add(CaptionButton("M0,0.5 H10", StringsUi.Get("S.Common.Minimize"), false, () => window.WindowState = WindowState.Minimized));
        }

        if (allowMaximize && resizable)
        {
            maximize = CaptionButton("M0.5,0.5 H9.5 V9.5 H0.5 Z", StringsUi.Get("S.Common.Maximize"), false, () =>
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            buttons.Children.Add(maximize);
        }

        buttons.Children.Add(CaptionButton("M0,0 L10,10 M10,0 L0,10", StringsUi.Get("S.Common.Close"), true, window.Close));
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);

        // --- 组装 ---
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);
        if (inner is not null)
        {
            Grid.SetRow(inner, 1);
            root.Children.Add(inner);
        }

        window.Content = root;

        // 失去焦点时标题与窗口按钮变淡，和系统标题栏一致。
        window.Activated += (_, _) =>
        {
            title.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.SecondaryForeground);
            buttons.Opacity = 1;
        };
        window.Deactivated += (_, _) =>
        {
            title.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.TertiaryForeground);
            buttons.Opacity = 0.5;
        };

        if (maximize is not null)
        {
            window.SourceInitialized += (_, _) => HookSnapLayouts(window, maximize);
        }

        window.StateChanged += (_, _) =>
        {
            var maximized = window.WindowState == WindowState.Maximized;
            // 最大化时 WindowChrome 的窗口会比工作区大出一圈缩放边框，补回来。
            root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
            if (maximize is not null)
            {
                maximize.Content = GlyphFor(maximized ? "M2.5,0.5 H9.5 V7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
            }
        };
    }

    /// <summary>
    /// 在标题栏与内容"身后"垫一层装饰背景（从窗口最顶端开始，跨过标题栏，不吃命中测试）。
    /// 用于头部晕染这类需要和标题栏连成一片、不能在标题栏下沿留出接缝的背景。
    /// 必须在 <see cref="Apply"/> 之后调用。
    /// </summary>
    internal static void AddBackdrop(Window window, FrameworkElement backdrop)
    {
        if (window.Content is not Grid { RowDefinitions.Count: 2 } root)
        {
            return;
        }

        backdrop.IsHitTestVisible = false;
        Grid.SetRow(backdrop, 0);
        Grid.SetRowSpan(backdrop, 2);
        root.Children.Insert(0, backdrop);
    }

    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;

    /// <summary>
    /// Windows 11 贴靠布局：系统只在 <c>WM_NCHITTEST</c> 返回 <c>HTMAXBUTTON</c> 时才在最大化按钮上弹出布局面板。
    /// 命中后鼠标消息变成非客户区消息，WPF 收不到悬停/点击，所以这里自己维护悬停底色并在抬起时切换最大化。
    /// </summary>
    private static void HookSnapLayouts(Window window, Button maximize)
    {
        if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is not { } source)
        {
            return;
        }

        var hovered = false;
        void SetHover(bool value)
        {
            if (hovered == value)
            {
                return;
            }

            hovered = value;
            if (value)
            {
                maximize.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.HoverBackground);
            }
            else
            {
                maximize.ClearValue(Control.BackgroundProperty);
            }
        }

        bool OverMaximize(IntPtr lParam)
        {
            if (!maximize.IsVisible || maximize.ActualWidth <= 0)
            {
                return false;
            }

            var value = lParam.ToInt64();
            var screen = new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
            var local = maximize.PointFromScreen(screen);
            return local.X >= 0 && local.Y >= 0 && local.X < maximize.ActualWidth && local.Y < maximize.ActualHeight;
        }

        source.AddHook((IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            switch (msg)
            {
                case WmNcHitTest when OverMaximize(lParam):
                    handled = true;
                    return new IntPtr(HtMaxButton);
                case WmNcMouseMove:
                    SetHover(wParam.ToInt32() == HtMaxButton);
                    break;
                case WmNcMouseLeave:
                    SetHover(false);
                    break;
                case WmNcLButtonDown when wParam.ToInt32() == HtMaxButton:
                    handled = true;
                    return IntPtr.Zero;
                case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                    handled = true;
                    SetHover(false);
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        });
        window.MouseMove += (_, _) => SetHover(false);
    }

    private static Button CaptionButton(string glyph, string name, bool close, Action action)
    {
        var button = new Button { Content = GlyphFor(glyph) };
        button.SetResourceReference(
            FrameworkElement.StyleProperty,
            close ? "DeepShot.CaptionCloseButton" : "DeepShot.CaptionButton");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static Path GlyphFor(string data)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
        };
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        return path;
    }
}
