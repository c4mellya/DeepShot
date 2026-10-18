using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Geometry;
using ZeepCut.Platform;

namespace ZeepCut.Pins;

/// <summary>
/// T11 的最小可用贴图窗口**占位**：无边框、置顶、1:1 显示裁剪图，
/// 按 Esc 或点右上角关闭按钮关闭。
/// </summary>
/// <remarks>
/// <para>
/// 完整的贴图能力（拖动、滚轮缩放、Ctrl+滚轮透明度、锁定、右键菜单、多张管理、
/// 全局显示/隐藏）在 T18–T22 实现；本轮只保证"贴图成功后有一张独立置顶窗口显示
/// 裁剪结果"，让工具栏的贴图按钮有真实输出而不是日志占位。
/// </para>
/// <para>
/// 定位与冻结遮罩相同：WPF 的 Left/Top/Width/Height 是 DIP，按目标屏幕 DPI 从物理
/// 像素换算；混合 DPI 下 WPF 首次摆放可能按主屏比例算错，因此 <c>Loaded</c> 时用
/// <c>GetWindowRect</c> 复核物理矩形，不一致时用 <c>SetWindowPos</c> 校正。
/// </para>
/// </remarks>
public sealed class PinWindow : Window
{
    private readonly PixelRect _expectedBounds;

    public PinWindow(BitmapSource image, PixelRect selection, PixelRect screenBounds, int dpi)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (selection.IsEmpty)
        {
            throw new ArgumentException("贴图选区不能为空。", nameof(selection));
        }

        _expectedBounds = selection;

        Title = "Zeep Cut 贴图";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        Focusable = true;
        ShowActivated = true;

        // 物理像素 → DIP：位置使用屏幕绝对坐标（副屏为负），尺寸按目标屏幕 DPI 换算。
        var transform = new DpiTransform(dpi);
        Left = transform.PixelToDip(selection.Left);
        Top = transform.PixelToDip(selection.Top);
        Width = transform.PixelToDip(selection.Width);
        Height = transform.PixelToDip(selection.Height);

        var picture = new Image
        {
            Source = image,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);

        var closeButton = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            FontSize = 14,
            Padding = new Thickness(0),
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            ToolTip = "关闭贴图（Esc）",
        };
        closeButton.Click += (_, _) => Close();

        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(picture);
        root.Children.Add(closeButton);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += OnClosed;

        AppLog.Info(
            "Pins",
            $"创建贴图窗口：选区={selection} 屏幕={screenBounds} DPI={dpi} " +
            $"图像={image.PixelWidth}x{image.PixelHeight} 期望物理={_expectedBounds}");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 与遮罩一致：工具窗口不出现在任务栏/Alt+Tab，也不进入截图候选。
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        var updatedStyle = extendedStyle | NativeMethods.WS_EX_TOOLWINDOW;
        if (updatedStyle != extendedStyle)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, updatedStyle);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            var actual = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
            if (actual == _expectedBounds)
            {
                AppLog.Info("Pins", $"贴图窗口物理矩形与选区一致 {actual}");
            }
            else
            {
                AppLog.Warn("Pins", $"贴图窗口物理矩形 {actual} 与选区 {_expectedBounds} 不一致，用 SetWindowPos 校正");
                if (NativeMethods.SetWindowPos(
                        hwnd,
                        IntPtr.Zero,
                        _expectedBounds.Left,
                        _expectedBounds.Top,
                        _expectedBounds.Width,
                        _expectedBounds.Height,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE) &&
                    NativeMethods.GetWindowRect(hwnd, out var corrected))
                {
                    AppLog.Info(
                        "Pins",
                        $"校正后贴图物理矩形 ({corrected.Left},{corrected.Top}) {corrected.Width}x{corrected.Height}");
                }
                else
                {
                    AppLog.Error("Pins", $"贴图窗口物理矩形校正失败（Win32Error={Marshal.GetLastWin32Error()}）");
                }
            }
        }
        else
        {
            AppLog.Warn("Pins", $"贴图窗口 GetWindowRect 失败（Win32Error={Marshal.GetLastWin32Error()}）");
        }

        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        AppLog.Info("Pins", $"贴图窗口关闭（Esc）：{_expectedBounds}");
        Close();
    }

    private void OnClosed(object? sender, EventArgs e) =>
        AppLog.Info("Pins", $"贴图窗口已关闭：{_expectedBounds}");
}
