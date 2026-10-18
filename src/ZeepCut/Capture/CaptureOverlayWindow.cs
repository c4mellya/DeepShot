using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ZeepCut.Core.Capture;
using ZeepCut.Core.Geometry;
using ZeepCut.Platform;

namespace ZeepCut.Capture;

/// <summary>
/// 冻结遮罩窗口：无边框、置顶、不显示在任务栏，精确覆盖触发时鼠标所在的那一块屏幕，
/// 并把捕获到的冻结图按 1:1 铺满窗口；鼠标悬停时高亮命中的顶层窗口。
/// </summary>
/// <remarks>
/// <para>
/// PerMonitorV2 下 WPF 的 <see cref="Window.Left"/>/<see cref="Window.Top"/>/<see cref="Window.Width"/>/
/// <see cref="Window.Height"/> 是 DIP，必须用目标屏幕的 DPI 从物理像素换算：
/// 主屏 2560×1440 @125% → DIP (0,0) 2048×1152；副屏 1080×1920 @100% → DIP (-1080,0) 1080×1920。
/// 显示后再用 <c>GetWindowRect</c> 复核一次物理矩形，若 WPF 初始 DPI 判断有偏差就
/// 用 <c>SetWindowPos</c> 强制校正（混合 DPI 下这是最容易出错的点）。
/// </para>
/// <para>
/// 遮罩窗口的捕获与窗口枚举都在窗口显示之前完成（调用方负责），所以遮罩本身不会被截进画面。
/// Esc 关闭窗口并取消本次截图，不产生历史记录。
/// </para>
/// </remarks>
public sealed class CaptureOverlayWindow : Window
{
    private const double HighlightStrokeThickness = 3;

    private readonly ScreenCaptureResult _capture;
    private readonly IReadOnlyList<WindowCandidate> _candidates;
    private readonly DpiTransform _transform;
    private readonly Rectangle _highlight;

    private WindowCandidate? _highlighted;
    private int _lastPhysicalX;
    private int _lastPhysicalY;
    private bool _cancelRequested;
    private bool _closed;

    public CaptureOverlayWindow(ScreenCaptureResult capture, IReadOnlyList<WindowCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(candidates);

        _capture = capture;
        _candidates = candidates;
        _transform = new DpiTransform(capture.Dpi);

        Title = "Zeep Cut 截图遮罩";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Focusable = true;
        ShowActivated = true;
        SnapsToDevicePixels = true;

        // 物理像素 → DIP：位置与尺寸都必须按目标屏幕 DPI 换算。
        var dip = _transform.ToDip(capture.Bounds);
        Left = dip.Left;
        Top = dip.Top;
        Width = dip.Width;
        Height = dip.Height;

        _highlight = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xFF)),
            StrokeThickness = HighlightStrokeThickness,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        var highlightLayer = new Canvas { IsHitTestVisible = false };
        highlightLayer.Children.Add(_highlight);

        var image = new Image
        {
            Source = capture.Image,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };

        // 冻结图与窗口的宽高比一致（同一块屏幕），Fill 不会变形，且能保证铺满。
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(image);
        root.Children.Add(highlightLayer);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        Closed += OnClosed;
    }

    /// <summary>本次截图是否由 Esc（或关闭窗口）取消。</summary>
    public bool WasCancelled => _cancelRequested;

    /// <summary>目标屏幕的物理矩形。</summary>
    public PixelRect ScreenBounds => _capture.Bounds;

    /// <summary>目标屏幕 DPI。</summary>
    public int ScreenDpi => _capture.Dpi;

    /// <summary>取消本次截图并关闭遮罩；重复调用为空操作。</summary>
    public void Cancel()
    {
        if (_cancelRequested || _closed)
        {
            return;
        }

        _cancelRequested = true;
        AppLog.Info(
            "Overlay",
            $"取消本次截图（Esc）：屏幕={_capture.DeviceName} 物理={_capture.Bounds}，不写入历史");

        try
        {
            Close();
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn("Overlay", $"关闭遮罩窗口失败：{ex.Message}");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 工具窗口样式：不出现在 Alt+Tab / 任务栏，也从窗口枚举中被排除。
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        var updatedStyle = extendedStyle | NativeMethods.WS_EX_TOOLWINDOW;
        if (updatedStyle != extendedStyle)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, updatedStyle);
        }

        AppLog.Info("Overlay", $"遮罩窗口已创建 hwnd=0x{hwnd.ToInt64():X}，已设置 WS_EX_TOOLWINDOW");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsurePhysicalBounds("Loaded");

        var dpi = VisualTreeHelper.GetDpi(this);
        AppLog.Info(
            "Overlay",
            $"遮罩窗口：屏幕={_capture.DeviceName} 主屏={_capture.IsPrimary} 物理={_capture.Bounds} " +
            $"DPI={_capture.Dpi}（WPF PixelsPerDip={dpi.PixelsPerDip:0.##}）；" +
            $"期望 DIP=({Left:0.##},{Top:0.##}) {Width:0.##}x{Height:0.##}，" +
            $"实际 DIP={ActualWidth:0.##}x{ActualHeight:0.##}");
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        Activate();
        Focus();
        Keyboard.Focus(this);

        AppLog.Info(
            "Overlay",
            $"遮罩已激活：IsActive={IsActive} IsKeyboardFocusWithin={IsKeyboardFocusWithin}，" +
            $"窗口候选={_candidates.Count} 个");
    }

    /// <summary>
    /// 用 <c>GetWindowRect</c> 复核物理矩形；WPF 在混合 DPI 下首次摆放可能偏差，
    /// 发现不一致时以物理像素强制校正。
    /// </summary>
    private void EnsurePhysicalBounds(string stage)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var expected = _capture.Bounds;

        if (!NativeMethods.GetWindowRect(hwnd, out var actualRect))
        {
            AppLog.Warn("Overlay", $"{stage}：GetWindowRect 失败（Win32Error={Marshal.GetLastWin32Error()}）");
            return;
        }

        var actual = new PixelRect(actualRect.Left, actualRect.Top, actualRect.Width, actualRect.Height);
        if (actual == expected)
        {
            AppLog.Info("Overlay", $"{stage}：遮罩物理矩形与目标屏幕一致 {actual}");
            return;
        }

        AppLog.Warn("Overlay", $"{stage}：遮罩物理矩形 {actual} 与目标屏幕 {expected} 不一致，用 SetWindowPos 校正");
        if (!NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                expected.Left,
                expected.Top,
                expected.Width,
                expected.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
        {
            AppLog.Error(
                "Overlay",
                $"{stage}：SetWindowPos 校正失败（Win32Error={Marshal.GetLastWin32Error()}）");
            return;
        }

        if (NativeMethods.GetWindowRect(hwnd, out var correctedRect))
        {
            var corrected = new PixelRect(
                correctedRect.Left,
                correctedRect.Top,
                correctedRect.Width,
                correctedRect.Height);
            AppLog.Info("Overlay", $"{stage}：校正后遮罩物理矩形 {corrected}");
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Cancel();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        // WPF 鼠标坐标是 DIP：先按目标屏幕 DPI 换算成物理像素，再加屏幕物理原点（副屏为负）。
        _lastPhysicalX = _capture.Bounds.Left + _transform.DipToPixel(position.X);
        _lastPhysicalY = _capture.Bounds.Top + _transform.DipToPixel(position.Y);

        var hit = WindowCandidateSelector.HitTestDip(
            _candidates,
            position.X,
            position.Y,
            _capture.Bounds,
            _capture.Dpi);

        UpdateHighlight(hit);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        UpdateHighlight(null);
    }

    private void UpdateHighlight(WindowCandidate? hit)
    {
        if (hit == _highlighted)
        {
            return;
        }

        _highlighted = hit;

        if (hit is null)
        {
            _highlight.Visibility = Visibility.Collapsed;
            return;
        }

        var dip = WindowCandidateSelector.ToDipBounds(hit.Bounds, _capture.Bounds, _capture.Dpi);
        var inset = HighlightStrokeThickness / 2;

        _highlight.StrokeThickness = HighlightStrokeThickness;
        _highlight.Width = Math.Max(0, dip.Width - HighlightStrokeThickness);
        _highlight.Height = Math.Max(0, dip.Height - HighlightStrokeThickness);
        Canvas.SetLeft(_highlight, dip.Left + inset);
        Canvas.SetTop(_highlight, dip.Top + inset);
        _highlight.Visibility = Visibility.Visible;

        AppLog.Info(
            "Overlay",
            $"高亮窗口候选：{hit}；指针物理=({_lastPhysicalX},{_lastPhysicalY})，" +
            $"边框 DIP=({dip.Left:0.##},{dip.Top:0.##}) {dip.Width:0.##}x{dip.Height:0.##}");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _highlighted = null;
        AppLog.Info(
            "Overlay",
            $"遮罩窗口已关闭：屏幕={_capture.DeviceName} 物理={_capture.Bounds} 已取消={_cancelRequested}");
    }
}
