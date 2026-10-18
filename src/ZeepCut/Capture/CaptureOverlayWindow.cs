using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using ZeepCut.Core.Capture;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Output;
using ZeepCut.Core.Selection;
using ZeepCut.Output;
using ZeepCut.Pins;
using ZeepCut.Platform;


namespace ZeepCut.Capture;

/// <summary>
/// 冻结遮罩窗口：无边框、置顶、不显示在任务栏，精确覆盖触发时鼠标所在的那一块屏幕，
/// 把捕获到的冻结图按 1:1 铺满窗口，并在其上提供框选交互（T9）与截图工具栏（T10）。
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
/// 鼠标交互是唯一打通 DIP 与物理像素的地方：所有鼠标事件给的是窗口内 DIP，
/// 必须先按目标屏幕 DPI 换算成物理像素（加屏幕原点，副屏为负）再喂给
/// <see cref="SelectionController"/>；Controller 产出的物理像素矩形再用同一套换算
/// （<see cref="WindowCandidateSelector.ToDipBounds"/>）画回窗口 DIP。
/// 尺寸文字与工具栏显示的数字始终是物理像素。
/// </para>
/// <para>
/// 交互职责分工：窗口只做 DIP↔物理换算、绘制与键盘，全部状态与几何由 Core 的
/// <see cref="SelectionController"/> 决定（悬停候选、拖拽、定稿、移动、八向调整、屏幕夹取）。
/// 遮罩窗口的捕获与窗口枚举都在窗口显示之前完成，所以遮罩本身不会被截进画面。
/// </para>
/// </remarks>
public sealed class CaptureOverlayWindow : Window
{
    private const double HighlightStrokeThickness = 3;
    private const double SelectionStrokeThickness = 1;
    private const double HandleSize = 8;
    private const double HandleStrokeThickness = 1;
    private const double LayoutGap = 8;
    private const double BadgeGap = 6;
    private const int SelectionMinimumSize = 4;
    private const int SelectionHandleTolerance = 6;

    private static readonly Color DimColor = Color.FromArgb(0x73, 0x00, 0x00, 0x00);
    private static readonly Color HoverStrokeColor = Color.FromRgb(0x00, 0xA2, 0xFF);
    private static readonly Color ToolbarBackgroundColor = Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E);
    private static readonly Color ToolbarBorderColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    private static readonly Color ButtonBackgroundColor = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
    private static readonly Color ButtonBorderColor = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF);
    private static readonly Color StatusErrorColor = Color.FromRgb(0xFF, 0xA0, 0x80);
    private static readonly Color StatusInfoColor = Color.FromRgb(0xB8, 0xD8, 0xB8);

    private readonly ScreenCaptureResult _capture;
    private readonly IReadOnlyList<WindowCandidate> _candidates;
    private readonly DpiTransform _transform;
    private readonly SelectionController _selection;
    private readonly ICaptureSink _sink;

    private readonly Path _dim;
    private readonly Rectangle _border;
    private readonly Dictionary<SelectionHandle, Rectangle> _handles = new();
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeBadgeText;
    private readonly Border _toolbar;
    private readonly TextBlock _toolbarSizeText;
    private readonly TextBlock _statusText;
    private readonly List<(string Label, Button Element)> _toolbarButtons = new();

    private readonly SolidColorBrush _selectionStroke = CreateFrozenBrush(Colors.White);
    private readonly SolidColorBrush _hoverStroke = CreateFrozenBrush(HoverStrokeColor);
    private readonly SolidColorBrush _statusErrorBrush = CreateFrozenBrush(StatusErrorColor);
    private readonly SolidColorBrush _statusInfoBrush = CreateFrozenBrush(StatusInfoColor);

    private int _lastPhysicalX;
    private int _lastPhysicalY;
    private bool _pointerDown;
    private bool _cancelRequested;
    private bool _closed;

    private PixelRect? _lastHoverLogged;
    private SelectionState _lastLoggedState = SelectionState.None;
    private bool _toolbarLogged;

    public CaptureOverlayWindow(
        ScreenCaptureResult capture,
        IReadOnlyList<WindowCandidate> candidates,
        ICaptureSink sink)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(sink);

        _capture = capture;
        _candidates = candidates;
        _sink = sink;
        _transform = new DpiTransform(capture.Dpi);
        _selection = new SelectionController(
            capture.Bounds,
            SelectionMinimumSize,
            SelectionHandleTolerance);
        _selection.SetWindowCandidates(candidates.Select(candidate => candidate.Bounds));

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

        // --- 视觉层：压暗、边框、手柄、尺寸标签，全部不参与命中测试。 ---
        _dim = new Path
        {
            Fill = CreateFrozenBrush(DimColor),
            IsHitTestVisible = false,
            Stretch = Stretch.None,
        };

        _border = new Rectangle
        {
            Stroke = _selectionStroke,
            StrokeThickness = SelectionStrokeThickness,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        var visualLayer = new Canvas { IsHitTestVisible = false };

        _sizeBadgeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _sizeBadge = new Border
        {
            Background = CreateFrozenBrush(Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E)),
            BorderBrush = CreateFrozenBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Child = _sizeBadgeText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        visualLayer.Children.Add(_dim);
        visualLayer.Children.Add(_border);
        visualLayer.Children.Add(_sizeBadge);
        foreach (var handle in CreateHandles())
        {
            visualLayer.Children.Add(handle.Value);
        }

        // --- 交互层：工具栏按钮必须能被点到。 ---
        _toolbarSizeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 76,
        };

        // 输出动作的内联提示（成功不提示，失败/取消保存时显示），不抢键盘焦点。
        _statusText = new TextBlock
        {
            Foreground = _statusErrorBrush,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        _toolbar = CreateToolbar();
        var chromeLayer = new Canvas();
        chromeLayer.Children.Add(_toolbar);

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
        root.Children.Add(visualLayer);
        root.Children.Add(chromeLayer);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        LostMouseCapture += OnLostMouseCapture;
        Closed += OnClosed;
    }

    /// <summary>本次截图是否由取消（Esc / 工具栏取消 / 退出）结束。</summary>
    public bool WasCancelled => _cancelRequested;

    /// <summary>目标屏幕的物理矩形。</summary>
    public PixelRect ScreenBounds => _capture.Bounds;

    /// <summary>目标屏幕 DPI。</summary>
    public int ScreenDpi => _capture.Dpi;

    /// <summary>当前已定稿的选区（物理像素）；没有有效选区时为空。</summary>
    public PixelRect Selection => _selection.Selection;

    /// <summary>当前是否有可完成截图的选区。</summary>
    public bool IsSelectionReady => _selection.IsCaptureReady;

    /// <summary>
    /// 本次截图的成功动作；取消、失败或尚未完成时为 <c>null</c>。
    /// 只有它为非空时才应该计入历史（T24）。
    /// </summary>
    public CaptureAction? CompletedAction { get; private set; }

    /// <summary>本次截图是否以成功动作结束（复制/另存为/贴图）。</summary>
    public bool WasCompleted => CompletedAction.HasValue;

    /// <summary>取消本次截图并关闭遮罩；重复调用为空操作。</summary>
    /// <param name="reason">取消来源，仅用于日志。</param>
    public void Cancel(string reason = "外部请求")
    {
        if (_cancelRequested || _closed)
        {
            return;
        }

        _cancelRequested = true;
        AppLog.Info(
            "Overlay",
            $"取消本次截图（{reason}）：屏幕={_capture.DeviceName} 物理={_capture.Bounds}，不写入历史");

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
            $"实际 DIP={ActualWidth:0.##}x{ActualHeight:0.##}；" +
            $"窗口候选={_candidates.Count} 个；选区最小尺寸={_selection.MinimumSize}px 手柄容差={_selection.HandleTolerance}px");

        UpdateVisuals();
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

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
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Cancel("Esc");
                break;

            case Key.Enter:
                e.Handled = true;
                // 文字编辑（T12 之后）尚未实现，Enter 直接触发真实的复制动作。
                TriggerCopy("键盘 Enter");
                break;
        }
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);

        // 点在工具栏上时交给按钮/工具栏处理，绝不能让它清掉选区重新框选。
        if (IsInsideToolbar(position))
        {
            return;
        }

        // 点击遮罩时确保窗口有键盘焦点，Enter/Esc 才能一直生效。
        Activate();
        Focus();
        Keyboard.Focus(this);
        ClearStatus();

        var (physicalX, physicalY) = ToPhysical(position);
        var previousState = _selection.State;

        _selection.PointerDown(physicalX, physicalY);
        _pointerDown = _selection.State is SelectionState.Dragging or SelectionState.Adjusting;

        if (_pointerDown)
        {
            CaptureMouse();
        }

        if (previousState == SelectionState.Selected && _selection.State == SelectionState.Adjusting)
        {
            AppLog.Info(
                "Overlay",
                $"开始调整选区：手柄={_selection.ActiveHandle} 指针物理=({physicalX},{physicalY}) " +
                $"当前选区={_selection.Selection}");
        }
        else
        {
            AppLog.Info(
                "Overlay",
                $"鼠标按下：指针物理=({physicalX},{physicalY}) DIP=({position.X:0.##},{position.Y:0.##}) " +
                $"屏幕={_capture.Bounds} DPI={_capture.Dpi} 状态={previousState} → {_selection.State}");
        }

        e.Handled = true;
        UpdateVisuals(position);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown)
        {
            return;
        }

        var position = e.GetPosition(this);
        var (physicalX, physicalY) = ToPhysical(position);
        var previousState = _selection.State;

        _pointerDown = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _selection.PointerUp(physicalX, physicalY);

        if (_selection.IsCaptureReady)
        {
            var label = previousState == SelectionState.Adjusting ? "调整完成" : "框选定稿";
            LogSelectionFinalized(label, physicalX, physicalY);
        }
        else if (previousState == SelectionState.Dragging)
        {
            AppLog.Info(
                "Overlay",
                $"框选未达最小尺寸（{_selection.MinimumSize}px）：指针物理=({physicalX},{physicalY})，" +
                $"结果={_selection.Selection}，状态={_selection.State}");
        }

        e.Handled = true;
        UpdateVisuals(position);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        // WPF 鼠标坐标是 DIP：先按目标屏幕 DPI 换算成物理像素，再加屏幕物理原点（副屏为负）。
        var (physicalX, physicalY) = ToPhysical(position);
        _lastPhysicalX = physicalX;
        _lastPhysicalY = physicalY;

        if (_pointerDown)
        {
            _selection.PointerDragged(physicalX, physicalY);
        }
        else
        {
            _selection.PointerMoved(physicalX, physicalY);
        }

        UpdateVisuals(position);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_pointerDown)
        {
            return;
        }

        // 指针离开遮罩时清掉尚未按下状态的悬停；已定稿的选区保持不变。
        if (_selection.State is SelectionState.None or SelectionState.HoverWindow)
        {
            _selection.Cancel();
            UpdateVisuals();
        }
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // 未收到 MouseUp 就丢失捕获（例如 Alt+Tab）时，用最后位置定稿，避免卡在 Dragging。
        if (!_pointerDown)
        {
            return;
        }

        _pointerDown = false;
        var previousState = _selection.State;
        _selection.PointerUp(_lastPhysicalX, _lastPhysicalY);

        if (_selection.IsCaptureReady &&
            previousState is SelectionState.Dragging or SelectionState.Adjusting)
        {
            LogSelectionFinalized("捕获丢失", _lastPhysicalX, _lastPhysicalY);
        }

        AppLog.Warn("Overlay", "鼠标捕获意外丢失，已用最后位置结束本次框选/调整");
        UpdateVisuals();
    }

    /// <summary>把窗口内 DIP 坐标换算成屏幕物理像素（屏幕原点可能为负）。</summary>
    private (int X, int Y) ToPhysical(Point dip) =>
        (_capture.Bounds.Left + _transform.DipToPixel(dip.X),
         _capture.Bounds.Top + _transform.DipToPixel(dip.Y));

    /// <summary>把物理像素矩形换算成窗口内 DIP（复用 T8 的换算，保证与鼠标换算同一套规则）。</summary>
    private DipRect ToDip(PixelRect physical) =>
        WindowCandidateSelector.ToDipBounds(physical, _capture.Bounds, _capture.Dpi);

    private void UpdateVisuals(Point? pointerDip = null)
    {
        var clientWidth = ActualWidth;
        var clientHeight = ActualHeight;
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            return;
        }

        var client = new DipRect(0, 0, clientWidth, clientHeight);

        // 当前要突出的矩形：悬停候选 / 正在拖的选区 / 已定稿的选区。
        PixelRect? physicalHighlight = _selection.State switch
        {
            SelectionState.Dragging or SelectionState.Selected or SelectionState.Adjusting
                => _selection.Selection.IsEmpty ? null : _selection.Selection,
            SelectionState.HoverWindow => _selection.HoverCandidate,
            _ => null,
        };

        DipRect? highlight = physicalHighlight.HasValue ? ToDip(physicalHighlight.Value) : null;

        UpdateDim(client, highlight);
        UpdateBorder(highlight);
        UpdateHandles(highlight);
        UpdateSizeBadge(client, highlight);
        UpdateToolbar(client, highlight);

        var pointer = pointerDip ?? new Point(
            _transform.PixelToDip(_lastPhysicalX - _capture.Bounds.Left),
            _transform.PixelToDip(_lastPhysicalY - _capture.Bounds.Top));
        UpdateCursor(pointer);
        LogStateChanges();
    }

    private void UpdateDim(DipRect client, DipRect? hole)
    {
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, client.Width, client.Height)));

        if (hole.HasValue && !hole.Value.IsEmpty)
        {
            var rect = hole.Value;
            geometry.Children.Add(new RectangleGeometry(new Rect(rect.Left, rect.Top, rect.Width, rect.Height)));
        }

        // 选区外压暗、选区内显示冻结原图；没有选区时整块屏幕压暗（仍可看清，便于开始框选）。
        _dim.Data = geometry;
    }

    private void UpdateBorder(DipRect? highlight)
    {
        if (!highlight.HasValue || highlight.Value.IsEmpty)
        {
            _border.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = highlight.Value;
        var isHover = _selection.State == SelectionState.HoverWindow;
        var thickness = isHover ? HighlightStrokeThickness : SelectionStrokeThickness;
        var inset = thickness / 2;

        _border.Stroke = isHover ? _hoverStroke : _selectionStroke;
        _border.StrokeThickness = thickness;
        _border.Width = Math.Max(0, rect.Width - thickness);
        _border.Height = Math.Max(0, rect.Height - thickness);
        Canvas.SetLeft(_border, rect.Left + inset);
        Canvas.SetTop(_border, rect.Top + inset);
        _border.Visibility = Visibility.Visible;
    }

    private void UpdateHandles(DipRect? highlight)
    {
        var show = _selection.State is SelectionState.Selected or SelectionState.Adjusting;
        if (!show || !highlight.HasValue || highlight.Value.IsEmpty)
        {
            foreach (var handle in _handles.Values)
            {
                handle.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var rect = highlight.Value;
        var centerX = rect.Left + (rect.Width / 2);
        var centerY = rect.Top + (rect.Height / 2);
        var right = rect.Right;
        var bottom = rect.Bottom;

        PositionHandle(SelectionHandle.TopLeft, rect.Left, rect.Top);
        PositionHandle(SelectionHandle.Top, centerX, rect.Top);
        PositionHandle(SelectionHandle.TopRight, right, rect.Top);
        PositionHandle(SelectionHandle.Right, right, centerY);
        PositionHandle(SelectionHandle.BottomRight, right, bottom);
        PositionHandle(SelectionHandle.Bottom, centerX, bottom);
        PositionHandle(SelectionHandle.BottomLeft, rect.Left, bottom);
        PositionHandle(SelectionHandle.Left, rect.Left, centerY);

        void PositionHandle(SelectionHandle handle, double x, double y)
        {
            if (!_handles.TryGetValue(handle, out var element))
            {
                return;
            }

            Canvas.SetLeft(element, x - (HandleSize / 2));
            Canvas.SetTop(element, y - (HandleSize / 2));
            element.Visibility = Visibility.Visible;
        }
    }

    private void UpdateSizeBadge(DipRect client, DipRect? highlight)
    {
        var show = _selection.State is SelectionState.Dragging or SelectionState.Adjusting
                   && !_selection.Selection.IsEmpty;

        if (!show || !highlight.HasValue || highlight.Value.IsEmpty)
        {
            _sizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = highlight.Value;
        _sizeBadgeText.Text = SelectionOverlayLayout.FormatSize(_selection.Selection);
        _sizeBadge.Visibility = Visibility.Visible;
        _sizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var size = _sizeBadge.DesiredSize;
        var placement = SelectionOverlayLayout.PlaceSizeBadge(rect, client, size.Width, size.Height, BadgeGap);
        if (placement.IsEmpty)
        {
            _sizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(_sizeBadge, placement.Left);
        Canvas.SetTop(_sizeBadge, placement.Top);
    }

    private void UpdateToolbar(DipRect client, DipRect? highlight)
    {
        if (!_selection.IsCaptureReady || !highlight.HasValue || highlight.Value.IsEmpty)
        {
            _toolbar.Visibility = Visibility.Collapsed;
            _toolbarLogged = false;
            return;
        }

        var rect = highlight.Value;
        _toolbarSizeText.Text = SelectionOverlayLayout.FormatSize(_selection.Selection);
        _toolbar.Visibility = Visibility.Visible;
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var size = _toolbar.DesiredSize;
        var placement = SelectionOverlayLayout.PlaceToolbar(rect, client, size.Width, size.Height, LayoutGap);
        if (placement.IsEmpty)
        {
            _toolbar.Visibility = Visibility.Collapsed;
            _toolbarLogged = false;
            return;
        }

        Canvas.SetLeft(_toolbar, placement.Left);
        Canvas.SetTop(_toolbar, placement.Top);

        if (!_toolbarLogged)
        {
            _toolbarLogged = true;
            var physical = SelectionOverlayLayout.ToPhysical(placement, _capture.Bounds, _capture.Dpi);
            var mode = placement.Top >= rect.Bottom ? "选区下方" : "选区上方";
            AppLog.Info(
                "Overlay",
                $"工具栏显示（{mode}）：尺寸={_selection.Selection.Width} × {_selection.Selection.Height} " +
                $"DIP=({placement.Left:0.##},{placement.Top:0.##}) {placement.Width:0.##}x{placement.Height:0.##} " +
                $"物理={physical}；选区 DIP=({rect.Left:0.##},{rect.Top:0.##}) {rect.Width:0.##}x{rect.Height:0.##}");

            _toolbar.UpdateLayout();
            foreach (var (label, element) in _toolbarButtons)
            {
                if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0)
                {
                    continue;
                }

                var origin = element.TransformToAncestor(this).Transform(new Point(0, 0));
                var buttonDip = new DipRect(
                    origin.X,
                    origin.Y,
                    element.ActualWidth,
                    element.ActualHeight);
                var buttonPhysical = SelectionOverlayLayout.ToPhysical(buttonDip, _capture.Bounds, _capture.Dpi);
                AppLog.Info("Overlay", $"工具栏按钮“{label}”物理={buttonPhysical}");
            }
        }
    }

    private void UpdateCursor(Point pointerDip)
    {
        if (IsInsideToolbar(pointerDip))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (_pointerDown || _selection.State == SelectionState.Dragging)
        {
            Cursor = Cursors.Cross;
            return;
        }

        var handle = _selection.State == SelectionState.Adjusting
            ? _selection.ActiveHandle
            : _selection.State == SelectionState.Selected
                ? _selection.HitTest(_lastPhysicalX, _lastPhysicalY)
                : SelectionHandle.None;

        Cursor = handle switch
        {
            SelectionHandle.TopLeft or SelectionHandle.BottomRight => Cursors.SizeNWSE,
            SelectionHandle.TopRight or SelectionHandle.BottomLeft => Cursors.SizeNESW,
            SelectionHandle.Left or SelectionHandle.Right => Cursors.SizeWE,
            SelectionHandle.Top or SelectionHandle.Bottom => Cursors.SizeNS,
            SelectionHandle.Move => Cursors.SizeAll,
            _ => Cursors.Cross,
        };
    }

    private void LogStateChanges()
    {
        var hover = _selection.HoverCandidate;

        if (hover != _lastHoverLogged)
        {
            _lastHoverLogged = hover;
            if (hover is { } candidate)
            {
                var dip = ToDip(candidate);
                AppLog.Info(
                    "Overlay",
                    $"悬停窗口候选：物理={candidate} 指针物理=({_lastPhysicalX},{_lastPhysicalY})，" +
                    $"边框 DIP=({dip.Left:0.##},{dip.Top:0.##}) {dip.Width:0.##}x{dip.Height:0.##}");
            }
            else
            {
                AppLog.Info("Overlay", "悬停窗口候选：无");
            }
        }

        if (_selection.State != _lastLoggedState)
        {
            AppLog.Info(
                "Overlay",
                $"选区状态：{_lastLoggedState} → {_selection.State} 选区={_selection.Selection} " +
                $"已定稿={_selection.IsCaptureReady}");
            _lastLoggedState = _selection.State;
        }
    }

    private void LogSelectionFinalized(string label, int physicalX, int physicalY)
    {
        var selection = _selection.Selection;
        var dip = ToDip(selection);
        AppLog.Info(
            "Overlay",
            $"选区定稿（{label}）：指针物理=({physicalX},{physicalY}) 物理={selection} " +
            $"尺寸={SelectionOverlayLayout.FormatSize(selection)}（物理像素） " +
            $"DIP=({dip.Left:0.##},{dip.Top:0.##}) {dip.Width:0.##}x{dip.Height:0.##}，" +
            $"屏幕={_capture.Bounds} DPI={_capture.Dpi}");
    }

    private bool IsInsideToolbar(Point dip)
    {
        if (_toolbar.Visibility != Visibility.Visible)
        {
            return false;
        }

        var left = Canvas.GetLeft(_toolbar);
        var top = Canvas.GetTop(_toolbar);
        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        var width = _toolbar.ActualWidth > 0 ? _toolbar.ActualWidth : _toolbar.DesiredSize.Width;
        var height = _toolbar.ActualHeight > 0 ? _toolbar.ActualHeight : _toolbar.DesiredSize.Height;

        return dip.X >= left && dip.X <= left + width &&
               dip.Y >= top && dip.Y <= top + height;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _lastHoverLogged = null;
        AppLog.Info(
            "Overlay",
            $"遮罩窗口已关闭：屏幕={_capture.DeviceName} 物理={_capture.Bounds} 已取消={_cancelRequested}");
    }

    // --- T11 输出动作：复制 / 另存为 / 贴图。三者都只在成功后关闭遮罩并写一条完成记录。 ---

    /// <summary>复制到剪贴板。剪贴板被占用（COMException）时提示并留在截图状态。</summary>
    private void TriggerCopy(string source)
    {
        if (!TryBeginOutput(source, CaptureAction.Copy))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var image = CropSelection(CaptureAction.Copy);
            Clipboard.SetImage(image);
            stopwatch.Stop();
            CompleteCapture(CaptureAction.Copy, image, stopwatch.Elapsed, filePath: null);
        }
        catch (COMException ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Copy, "复制失败：剪贴板被占用，请稍后重试", ex, isWarning: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Copy, "复制失败", ex, isWarning: false);
        }
    }

    /// <summary>
    /// 另存为 PNG/JPEG。取消对话框必须留在截图状态（不关闭遮罩、不产生记录）；
    /// 只有文件真正写入成功才结束截图。
    /// </summary>
    private void TriggerSaveAs(string source)
    {
        if (!TryBeginOutput(source, CaptureAction.SaveAs))
        {
            return;
        }

        SaveFileDialog dialog;
        try
        {
            dialog = CreateSaveDialog();
        }
        catch (Exception ex)
        {
            FailOutput(CaptureAction.SaveAs, "无法打开保存对话框", ex, isWarning: false);
            return;
        }

        bool? result;
        var wasTopmost = Topmost;
        try
        {
            // 遮罩是 Topmost 窗口；用 Topmost 的所有者显示 Win32 文件对话框时，
            // 对话框会被所有者的置顶状态干扰（本机实测会自行返回/关闭）。
            // 显示期间临时取消置顶，关闭后恢复并重新激活遮罩。
            Topmost = false;
            AppLog.Info("Output", $"保存对话框已打开：默认名称={dialog.FileName} 目录={dialog.InitialDirectory}");
            result = dialog.ShowDialog(this);
            AppLog.Info("Output", $"保存对话框已关闭：DialogResult={result} 选择文件={dialog.FileName}");
        }
        catch (Exception ex)
        {
            FailOutput(CaptureAction.SaveAs, "无法打开保存对话框", ex, isWarning: false);
            return;
        }
        finally
        {
            Topmost = wasTopmost;
        }

        if (result != true)
        {
            AppLog.Info(
                "Output",
                $"另存为取消（{source}）：选区={_selection.Selection}，留在截图状态，不关闭遮罩、不写入任何记录");
            SetStatus("已取消保存");
            RestoreOverlayFocus();
            return;
        }

        var format = ResolveFormat(dialog.FileName, dialog.FilterIndex);
        var path = EnsureExtension(dialog.FileName, format);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var image = CropSelection(CaptureAction.SaveAs);
            CaptureImageEncoder.Save(image, path, format);
            stopwatch.Stop();
            AppLog.Info(
                "Output",
                $"文件已保存：{path} 格式={format} 尺寸={image.PixelWidth}x{image.PixelHeight} " +
                $"质量={(format == CaptureImageFormat.Jpeg ? CaptureImageEncoder.DefaultJpegQuality : (int?)null)}");
            CompleteCapture(CaptureAction.SaveAs, image, stopwatch.Elapsed, path);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.SaveAs, "保存失败，请重试", ex, isWarning: false);
        }
    }

    /// <summary>贴图：本轮创建最小可用的占位贴图窗口（T18–T22 完善），成功后结束截图。</summary>
    private void TriggerPin(string source)
    {
        if (!TryBeginOutput(source, CaptureAction.Pin))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var image = CropSelection(CaptureAction.Pin);
            var pin = new PinWindow(image, _selection.Selection, _capture.Bounds, _capture.Dpi);
            pin.Show();
            stopwatch.Stop();
            CompleteCapture(CaptureAction.Pin, image, stopwatch.Elapsed, filePath: null);

            // 遮罩关闭后把焦点交回贴图窗口，Esc 才能直接关闭它。
            pin.Activate();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Pin, "贴图失败", ex, isWarning: false);
        }
    }

    /// <summary>输出动作的共同前置检查与日志；没有可输出选区时返回 false。</summary>
    private bool TryBeginOutput(string source, CaptureAction action)
    {
        if (!_selection.IsCaptureReady)
        {
            AppLog.Info(
                "Output",
                $"{source}：当前没有可输出的选区，忽略本次 {LoggingCaptureSink.Describe(action)}（状态={_selection.State}）");
            return false;
        }

        ClearStatus();
        AppLog.Info(
            "Output",
            $"开始输出（{source}）：动作={LoggingCaptureSink.Describe(action)} 选区={_selection.Selection} " +
            $"尺寸={SelectionOverlayLayout.FormatSize(_selection.Selection)}（物理像素） " +
            $"屏幕={_capture.Bounds} DPI={_capture.Dpi}");
        return true;
    }

    /// <summary>按选区裁剪冻结图；失败会抛异常，由各动作的 catch 提示并留在截图状态。</summary>
    private BitmapSource CropSelection(CaptureAction action)
    {
        var image = CaptureCropper.Crop(_capture.Image, _selection.Selection, _capture.Bounds);
        AppLog.Info(
            "Output",
            $"裁剪完成（{LoggingCaptureSink.Describe(action)}）：选区={_selection.Selection} " +
            $"屏幕={_capture.Bounds} → 图像={image.PixelWidth}x{image.PixelHeight} {image.Format}");
        return image;
    }

    /// <summary>成功收尾：写完成记录（T24 历史的接缝）→ 关闭遮罩。</summary>
    private void CompleteCapture(
        CaptureAction action,
        BitmapSource image,
        TimeSpan elapsed,
        string? filePath)
    {
        CompletedAction = action;
        var completion = new CaptureCompletion(
            action,
            image,
            _selection.Selection,
            _capture.Bounds,
            filePath,
            DateTime.Now,
            elapsed);

        AppLog.Info(
            "Overlay",
            $"截图完成（{LoggingCaptureSink.Describe(action)}）：选区={_selection.Selection} " +
            $"输出={image.PixelWidth}x{image.PixelHeight} 耗时={elapsed.TotalMilliseconds:0}ms，关闭遮罩");

        try
        {
            _sink.OnCaptureCompleted(completion);
        }
        catch (Exception ex)
        {
            // 记录失败不能反悔已经成功的复制/保存/贴图，也不能把用户卡在遮罩里。
            AppLog.Error("Output", "输出完成记录失败（不阻断关闭遮罩；T24 历史启用后需关注）", ex);
        }

        Close();
    }

    /// <summary>失败提示：写日志 + 工具栏内联提示，并留在截图状态。</summary>
    private void FailOutput(CaptureAction action, string message, Exception ex, bool isWarning)
    {
        var summary = $"{LoggingCaptureSink.Describe(action)}失败（留在截图状态）：{ex.GetType().Name}: {ex.Message}";
        if (isWarning)
        {
            AppLog.Warn("Output", summary);
        }
        else
        {
            AppLog.Error("Output", summary, ex);
        }

        SetStatus(message, isError: true);
        RestoreOverlayFocus();
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (string.IsNullOrEmpty(message))
        {
            ClearStatus();
        }
        else
        {
            _statusText.Text = message;
            _statusText.Foreground = isError ? _statusErrorBrush : _statusInfoBrush;
            _statusText.Visibility = Visibility.Visible;
        }

        // 提示文字会改变工具栏宽度，需要重新测量与摆放（仍夹在屏幕内）。
        if (ActualWidth > 0)
        {
            UpdateVisuals();
        }
    }

    private void ClearStatus()
    {
        _statusText.Text = string.Empty;
        _statusText.Visibility = Visibility.Collapsed;
    }

    private void RestoreOverlayFocus()
    {
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private static SaveFileDialog CreateSaveDialog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "另存为",
            FileName = CaptureFileName.CreateDefault(DateTime.Now, CaptureImageFormat.Png),
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            FilterIndex = 1,
            RestoreDirectory = true,
        };

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(pictures) && System.IO.Directory.Exists(pictures))
        {
            dialog.InitialDirectory = pictures;
        }

        return dialog;
    }

    /// <summary>输出格式：优先按用户输入/对话框补全的扩展名，否则按过滤器（第 2 项为 JPEG）。</summary>
    private static CaptureImageFormat ResolveFormat(string path, int filterIndex) =>
        CaptureFileName.TryGetFormatFromExtension(path, out var format)
            ? format
            : filterIndex == 2
                ? CaptureImageFormat.Jpeg
                : CaptureImageFormat.Png;

    /// <summary>极端情况下对话框没有补扩展名时，按格式补上，避免生成无扩展名文件。</summary>
    private static string EnsureExtension(string path, CaptureImageFormat format) =>
        System.IO.Path.HasExtension(path) ? path : path + CaptureFileName.GetExtension(format);

    private Border CreateToolbar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(_toolbarSizeText);
        panel.Children.Add(_statusText);
        panel.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(10, 0, 0, 0),
            Background = CreateFrozenBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        panel.Children.Add(CreateToolbarButton("复制", () => TriggerCopy("工具栏按钮“复制”")));
        panel.Children.Add(CreateToolbarButton("另存为", () => TriggerSaveAs("工具栏按钮“另存为”")));
        panel.Children.Add(CreateToolbarButton("贴图", () => TriggerPin("工具栏按钮“贴图”")));
        panel.Children.Add(CreateToolbarButton("取消", () => Cancel("工具栏取消按钮"), emphasize: true));

        return new Border
        {
            Background = CreateFrozenBrush(ToolbarBackgroundColor),
            BorderBrush = CreateFrozenBrush(ToolbarBorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            Child = panel,
            Visibility = Visibility.Collapsed,
        };
    }

    private Button CreateToolbarButton(string text, Action onClick, bool emphasize = false)
    {
        var button = new Button
        {
            Content = text,
            Foreground = Brushes.White,
            Background = CreateFrozenBrush(emphasize
                ? Color.FromArgb(0x33, 0xFF, 0x80, 0x60)
                : ButtonBackgroundColor),
            BorderBrush = CreateFrozenBrush(ButtonBorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
        };

        // 按钮不抢键盘焦点，保证 Enter/Esc 始终作用于遮罩。
        button.Click += (_, _) => onClick();
        _toolbarButtons.Add((text, button));
        return button;
    }

    private IEnumerable<KeyValuePair<SelectionHandle, Rectangle>> CreateHandles()
    {
        foreach (var handle in new[]
                 {
                     SelectionHandle.TopLeft,
                     SelectionHandle.Top,
                     SelectionHandle.TopRight,
                     SelectionHandle.Right,
                     SelectionHandle.BottomRight,
                     SelectionHandle.Bottom,
                     SelectionHandle.BottomLeft,
                     SelectionHandle.Left,
                 })
        {
            var element = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = CreateFrozenBrush(Color.FromArgb(0xCC, 0x20, 0x20, 0x20)),
                StrokeThickness = HandleStrokeThickness,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };

            _handles[handle] = element;
            yield return new KeyValuePair<SelectionHandle, Rectangle>(handle, element);
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
