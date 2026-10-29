using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using DeepShot.Core.Annotations;
using DeepShot.Core.Capture;
using DeepShot.Core.Geometry;
using DeepShot.Core.Output;
using DeepShot.Core.Selection;
using DeepShot.Editor;
using DeepShot.Output;
using DeepShot.Pins;
using DeepShot.Platform;
using DeepShot.Ui;


namespace DeepShot.Capture;

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
    private const double HandleSize = 10;
    private const double HandleStrokeThickness = 1;
    private const double LayoutGap = 8;
    private const double BadgeGap = 8;
    private const int SelectionMinimumSize = 4;
    private const int SelectionHandleTolerance = 6;

    /// <summary>
    /// 选区手柄命中容差（物理像素）：至少盖住画出来的手柄半径（HandleSize/2 DIP 按 DPI 换算）再留 2px 余量，
    /// 高 DPI 下手柄的内侧外圈也能抓到；不低于 <see cref="SelectionHandleTolerance"/>。
    /// </summary>
    private static int SelectionHandleToleranceFor(int dpi) =>
        Math.Max(SelectionHandleTolerance, (int)Math.Ceiling(HandleSize / 2 * dpi / 96.0) + 2);

    /// <summary>选区外压暗（45% 黑）：任何背景上都能看清选区边界，又不至于看不清被压暗的内容。</summary>
    private static readonly Color DimColor = Color.FromArgb(0x73, 0x00, 0x00, 0x00);

    /// <summary>选中态虚线框线宽（DIP）与手柄边长（DIP）。</summary>
    private const double EditorOutlineThickness = 1.5;
    private const double EditorHandleSize = 8;

    /// <summary>手柄命中半径与文档命中容差（物理像素）。</summary>
    private const int EditorHandleRadius = 8;
    private const int EditorHitTolerance = 4;

    /// <summary>双击判定窗口（毫秒），用于文字标注重新进入编辑。</summary>
    private const int DoubleClickMilliseconds = 400;

    /// <summary>文字输入框相对点击点的偏移（DIP），避免框盖住正在输入的基线。</summary>
    private const double TextEditorOffsetY = 4;
    private const double TextEditorMinWidth = 160;

    private readonly ScreenCaptureResult _capture;
    private readonly IReadOnlyList<WindowCandidate> _candidates;
    private readonly DpiTransform _transform;
    private readonly SelectionController _selection;
    private readonly ICaptureSink _sink;
    private readonly bool _quickCopy;

    /// <summary>
    /// 标注会话：文档 + 撤销历史 + 工具设置 + 草稿/文字输入状态（T14/T15）
    /// + 选择/移动/缩放/删除状态机（T16，<see cref="AnnotationSession.Editing"/>）。
    /// </summary>
    private readonly AnnotationSession _session;

    private readonly Path _dim;
    private readonly Rectangle _border;
    private readonly Dictionary<SelectionHandle, Rectangle> _handles = new();
    private readonly Border _sizeBadge;
    private readonly TextBlock _sizeBadgeText;
    private readonly Border _toolbar;
    private readonly TextBlock _toolbarSizeText;
    private readonly TextBlock _statusText;
    private Border? _lockBadge;
    private TextBlock? _lockText;
    private readonly Canvas _annotationHost;
    private readonly List<(string Label, Button Element)> _toolbarButtons = new();
    private readonly List<(AnnotationTool? Tool, Button Element)> _toolButtons = new();
    private readonly List<(AnnotationColor Color, Button Element)> _colorSwatches = new();
    private readonly List<(int Thickness, Button Element)> _thicknessButtons = new();

    /// <summary>遮罩窗口 DIP ↔ 截图图像物理像素（选区定稿后才有值）。</summary>
    private AnnotationCanvasTransform? _canvas;

    /// <summary>选区内绘制层（随选区位置/尺寸摆放）。</summary>
    private AnnotationLayer? _annotationLayer;

    /// <summary>文字输入框宿主（覆盖在选区上方的一层）。</summary>
    private Border? _textEditorHost;
    private TextBox? _textEditor;


    private bool _annotationPointerDown;

    /// <summary>正在拖动选中标注（T16 移动/缩放/端点）。</summary>
    private bool _editorPointerDown;

    /// <summary>双击正在重新编辑的文字标注（T16）；没有时为 null。</summary>
    private TextAnnotation? _editingText;

    /// <summary>重新编辑文字前的原内容（取消/提交对比用）。</summary>
    private string _editingTextOriginal = string.Empty;

    /// <summary>上一次点击的时间/物理坐标（双击判定用）。</summary>
    private long _lastEditorClickAt;
    private int _lastEditorClickX;
    private int _lastEditorClickY;

    /// <summary>选中态可视化：虚线框 + 手柄。</summary>
    private readonly Canvas _editorCanvas;
    private readonly Path _editorOutline;
    private readonly List<(AnnotationHandle Handle, Rectangle Element)> _editorHandles = new();

    private int _lastPhysicalX;
    private int _lastPhysicalY;
    private bool _pointerDown;
    private bool _cancelRequested;
    private bool _closed;
    private CaptureInputGuard? _inputGuard;
    private bool _saveDialogOpen;
    private IntPtr _captureHwnd;

    private PixelRect? _lastHoverLogged;
    private SelectionState _lastLoggedState = SelectionState.None;
    private bool _toolbarLogged;
    private bool _lockLogged;


    public CaptureOverlayWindow(
        ScreenCaptureResult capture,
        IReadOnlyList<WindowCandidate> candidates,
        ICaptureSink sink,
        bool quickCopy = false)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(sink);

        _capture = capture;
        _candidates = candidates;
        _sink = sink;
        _quickCopy = quickCopy;

        // 标注坐标是图像内物理像素，图像范围就是冻结图尺寸（T16 缩放/移动的夹取边界）。
        _session = new AnnotationSession(
            new PixelRect(0, 0, capture.Image.PixelWidth, capture.Image.PixelHeight));
        _transform = new DpiTransform(capture.Dpi);
        _selection = new SelectionController(
            capture.Bounds,
            SelectionMinimumSize,
            SelectionHandleToleranceFor(capture.Dpi));
        _selection.SetWindowCandidates(candidates.Select(candidate => candidate.Bounds));

        Title = StringsUi.Get("S.Overlay.WindowTitle");
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
            Fill = UiKit.Frozen(DimColor),
            IsHitTestVisible = false,
            Stretch = Stretch.None,
        };

        // 选区框与手柄用主题强调色（资源引用只设一次；悬停窗口与选区只换线宽，逐帧路径上不换画刷）。
        _border = new Rectangle
        {
            StrokeThickness = SelectionStrokeThickness,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _border.SetResourceReference(Shape.StrokeProperty, UiKit.Keys.Accent);

        var visualLayer = new Canvas { IsHitTestVisible = false };

        _sizeBadgeText = OverlayChrome.BadgeText();
        _sizeBadge = OverlayChrome.Badge(_sizeBadgeText);
        _sizeBadge.Visibility = Visibility.Collapsed;

        visualLayer.Children.Add(_border);
        visualLayer.Children.Add(_sizeBadge);
        foreach (var handle in CreateHandles())
        {
            visualLayer.Children.Add(handle.Value);
        }

        // --- 标注层宿主：只覆盖选区，随选区摆放；标注画在压暗层之上、边框/手柄之下。 ---
        _annotationHost = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            Visibility = Visibility.Collapsed,
        };

        // --- 交互层：工具栏按钮必须能被点到。 ---
        // 字号/颜色/字重由 OverlayChrome.StyleSizeText 统一（CreateToolbar 里套用）。
        _toolbarSizeText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 输出动作的内联提示（成功不提示，失败/取消保存时显示），不抢键盘焦点。
        _statusText = new TextBlock
        {
            Foreground = OverlayChrome.StatusErrorBrush,
            FontSize = Tokens.Text.Caption,
            FontWeight = AppFonts.Regular,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(Tokens.Space.S, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        _statusText.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);

        // --- T16 选中态层：虚线框 + 手柄，画在标注之上、工具栏之下，不参与命中测试。 ---
        _editorOutline = OverlayChrome.EditorOutline(EditorOutlineThickness);

        _editorCanvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _editorCanvas.Children.Add(_editorOutline);
        foreach (var handle in Enum.GetValues<AnnotationHandle>())
        {
            if (handle is AnnotationHandle.None or AnnotationHandle.Move)
            {
                continue;
            }

            // 标注手柄：白底 + 强调色描边的小圆角方块（与选区的圆形手柄区分开）。
            var element = OverlayChrome.EditorHandle(EditorHandleSize);
            _editorHandles.Add((handle, element));
            _editorCanvas.Children.Add(element);
        }

        _toolbar = CreateToolbar();
        var chromeLayer = new Canvas();
        chromeLayer.Children.Add(_toolbar);

        // --- 文字输入层：最上层，输入框在选区内。 ---
        _textEditorHost = CreateTextEditorHost();
        var textLayer = new Canvas();
        textLayer.Children.Add(_textEditorHost);
        textLayer.Children.Add(CreateLockIndicator());

        var image = new Image
        {
            Source = capture.Image,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };

        // 冻结图与窗口的宽高比一致（同一块屏幕），Fill 不会变形，且能保证铺满。
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var root = new Grid { Background = Brushes.Black };
        // Grid 不支持 Canvas 定位，标注层套一层 Canvas 才能按选区摆放。
        var annotationCanvas = new Canvas { IsHitTestVisible = false };
        annotationCanvas.Children.Add(_annotationHost);

        root.Children.Add(image);
        root.Children.Add(_dim);
        root.Children.Add(annotationCanvas);
        root.Children.Add(visualLayer);
        root.Children.Add(_editorCanvas);
        root.Children.Add(chromeLayer);
        root.Children.Add(textLayer);
        Content = root;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        Activated += (_, _) => RefreshCaptureLayer();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshCaptureLayer));
        SizeChanged += OnSizeChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (Mouse.Captured is null)
            {
                _rightCaptured = CaptureMouse();
            }
        };
        PreviewMouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_rightCaptured)
            {
                _rightCaptured = false;
                ReleaseMouseCapture();
            }

            BackOff("鼠标右键");
        };
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

    /// <summary>
    /// Esc / 右键逐层后退：文字输入 → 选中标注 → 绘制工具 → 选区 → 退出。
    /// 有标注时需在 1.5 秒内再按一次才放弃退出，避免误触丢失标注。
    /// </summary>
    private void BackOff(string reason)
    {
        if (_cancelRequested || _closed)
        {
            return;
        }

        // 拖拽进行中保持原行为：直接退出。
        if (_pointerDown || _annotationPointerDown || _editorPointerDown)
        {
            Cancel(reason);
            return;
        }

        if (_session.IsTextEditing)
        {
            CancelTextInput(reason);
            return;
        }

        if (_editingText is not null)
        {
            CancelExistingTextEdit(reason);
            return;
        }

        if (_session.Editing is { HasSelection: true } editor)
        {
            editor.ClearSelection();
            _annotationLayer?.InvalidateVisual();
            UpdateVisuals();
            return;
        }

        if (_session.ActiveTool is not null && _selection.IsCaptureReady)
        {
            SelectTool(null);
            return;
        }

        if (_session.Count == 0 && _selection.IsCaptureReady)
        {
            AppLog.Info("Overlay", $"{reason}：清除选区，回到框选");
            _selection.Cancel();
            ClearStatus();
            UpdateVisuals();
            return;
        }

        if (_session.Count > 0)
        {
            var now = Environment.TickCount64;
            if (now - _discardArmedTick > DiscardConfirmMilliseconds)
            {
                _discardArmedTick = now;
                SetStatus(StringsUi.Get("S.Overlay.DiscardHint"));
                return;
            }
        }

        Cancel(reason);
    }

    private const long DiscardConfirmMilliseconds = 1500;
    private long _discardArmedTick = long.MinValue / 2;
    private bool _rightCaptured;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _captureHwnd = hwnd;

        // 工具窗口样式：不出现在 Alt+Tab / 任务栏，也从窗口枚举中被排除。
        var extendedStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        var updatedStyle = extendedStyle | NativeMethods.WS_EX_TOOLWINDOW;
        if (updatedStyle != extendedStyle)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, updatedStyle);
        }

        try
        {
            _inputGuard = new CaptureInputGuard(
                () => !_closed && !_saveDialogOpen && IsVisible && IsEnabled,
                reason => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (!_closed && !_saveDialogOpen && IsEnabled) BackOff(reason);
                })));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.Error("Overlay", "截图输入拦截安装失败，保留窗口内按键处理", ex);
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

    private void RefreshCaptureLayer()
    {
        if (!_closed && !_saveDialogOpen && IsVisible && IsEnabled && Topmost)
            CaptureWindowLayer.Raise(_captureHwnd);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        CaptureWindowLayer.MarkFullscreen(_captureHwnd, true);
        RefreshCaptureLayer();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
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
        // Alt+A can leave Alt/IME routing active. Normalize before checking Escape.
        var key = e.Key == Key.System ? e.SystemKey :
            e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.Escape)
        {
            e.Handled = true;
            BackOff("Esc");
            return;
        }

        // Enter confirms text without copying. Escape above backs off one layer.
        if (_session.IsTextEditing)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                // Shift+Enter 插换行，普通 Enter 确认文字（都不触发复制）。
                e.Handled = true;
                CommitTextInput("Enter");
            }

            return;
        }

        // Existing text editing also keeps Enter local to the editor.
        if (_editingText is not null)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                e.Handled = true;
                CommitExistingTextEdit("Enter");
            }

            return;
        }

        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (isCtrl && e.Key is Key.Z or Key.Y)
        {
            e.Handled = true;
            if (e.Key == Key.Z)
            {
                UndoAnnotation("Ctrl+Z");
            }
            else
            {
                RedoAnnotation("Ctrl+Y");
            }

            return;
        }

        switch (key)
        {
            case Key.Delete:
            case Key.Back:
                // T16：只有存在选中标注时才拦截 Delete，否则保持原有行为（不吞键）。
                if (_session.Editing?.HasSelection == true)
                {
                    e.Handled = true;
                    DeleteSelectedAnnotation(e.Key.ToString());
                }

                break;

            case Key.Enter:
                e.Handled = true;
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

        // 点在文字输入框上时交给输入框（移动插入点/选择文字）。
        if (IsInsideTextEditor(position))
        {
            return;
        }

        // 在别处按下：先提交正在输入的文字，再进行本次操作。
        if (_session.IsTextEditing)
        {
            CommitTextInput("点击其它位置");
        }

        // 点击遮罩时确保窗口有键盘焦点，Enter/Esc 才能一直生效。
        Activate();
        Focus();
        Keyboard.Focus(this);
        ClearStatus();

        // T16：选择模式（没有绘制工具）下，点选区内先尝试编辑已有标注。
        if (CanEditAnnotation() && _canvas is { } editCanvas)
        {
            if (BeginAnnotationEditAt(editCanvas, position))
            {
                e.Handled = true;
                UpdateVisuals(position);
                return;
            }
        }

        if (CanDrawAnnotation())
        {
            var canvas = _canvas!.Value;
            if (IsInsideSelectionPhysical(canvas, position))
            {
                BeginAnnotationAt(canvas, position);
                e.Handled = true;
                UpdateVisuals(position);
                return;
            }
        }

        // 已选绘制工具时不允许用拖动改选区（否则会无意中把标注的坐标系挪走）。
        // 要重新框选请先点工具栏“选择”。
        if (_session.ActiveTool.HasValue && _selection.IsCaptureReady)
        {
            if (!_lockLogged)
            {
                _lockLogged = true;
                AppLog.Info("Editor", "已选绘制工具，选区外按下被忽略（先点工具栏“选择”才能重新框选/调整选区）");
            }

            e.Handled = true;
            return;
        }

        // 选区已锁定（已开始标注）时禁止移动/调整选区，点选区外只记日志。
        if (_session.IsSelectionLocked && _selection.IsCaptureReady)
        {
            if (!_lockLogged)
            {
                _lockLogged = true;
                AppLog.Info(
                    "Editor",
                    $"选区已锁定（文档中有 {_session.Count} 个标注），忽略选区外点击，只允许继续画标注");
            }

            e.Handled = true;
            return;
        }

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
        if (_editorPointerDown)
        {
            _editorPointerDown = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            var dip = e.GetPosition(this);
            if (_canvas is { } dragCanvas && _session.Editing is { } editor)
            {
                var imagePoint = dragCanvas.ToImage(dip.X, dip.Y);
                editor.DragTo(imagePoint);
                var mode = editor.Mode;
                var handle = editor.ActiveHandle;
                var bounds = editor.SelectionBounds;
                var description = editor.EndDrag();

                AppLog.Info(
                    "Editor",
                    $"标注编辑抬起：方式={mode} 手柄={handle} 终点图像={imagePoint} 包围盒={bounds} " +
                    $"记录={(description ?? "无变化")} 撤销栈={_session.History.UndoCount} " +
                    $"重做栈={_session.History.RedoCount} 文档标注数={_session.Count}");
                _annotationLayer?.InvalidateVisual();
            }

            e.Handled = true;
            UpdateVisuals(dip);
            return;
        }

        if (_annotationPointerDown)
        {
            _annotationPointerDown = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            var canvas = _canvas!.Value;
            var dip = e.GetPosition(this);
            var imagePoint = canvas.ToImageClamped(dip.X, dip.Y);
            _session.UpdateDraft(imagePoint);

            var draft = _session.Draft;
            var tool = draft?.Tool.ToString() ?? "无";
            var start = draft?.Start ?? default;
            var bounds = draft?.Annotation.Bounds ?? default;
            var points = draft?.PointCount ?? 0;
            var committed = _session.CommitDraft();

            AppLog.Info(
                "Editor",
                $"标注抬起：工具={tool} 起点图像={start} 终点图像={imagePoint} 包围盒={bounds} 画笔点数={points} " +
                $"已提交={committed} 文档标注数={_session.Count} " +
                $"撤销栈={_session.History.UndoCount} 重做栈={_session.History.RedoCount} " +
                $"选区锁定={_session.IsSelectionLocked}");
            e.Handled = true;
            UpdateVisuals(dip);
            return;
        }

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
            if (_quickCopy)
            {
                e.Handled = true;
                TriggerCopy("快速截图框选完成");
                return;
            }
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

        if (_editorPointerDown && _canvas is { } editorCanvas)
        {
            _session.Editing?.DragTo(editorCanvas.ToImage(position.X, position.Y));
            _annotationLayer?.InvalidateVisual();
            UpdateVisuals(position);
            return;
        }

        if (_annotationPointerDown && _canvas is { } canvas)
        {
            _session.UpdateDraft(canvas.ToImageClamped(position.X, position.Y));
            _annotationLayer?.InvalidateVisual();
            UpdateVisuals(position);
            return;
        }

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
        if (_editorPointerDown)
        {
            _editorPointerDown = false;
            var description = _session.Editing?.EndDrag();
            AppLog.Warn(
                "Editor",
                $"标注编辑拖拽被中断（捕获丢失），已用最后位置提交：记录={description ?? "无变化"}");
            _annotationLayer?.InvalidateVisual();
            UpdateVisuals();
            return;
        }

        if (_annotationPointerDown)
        {
            FinishAnnotationDrag("捕获丢失");
            return;
        }

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

    // --- T14/T15 标注：工具、绘制层、文字输入、撤销重做。 ---

    /// <summary>
    /// 是否可以把鼠标按下解释为“画标注”：选区已定稿、没有在调整，且当前有绘制工具。
    /// </summary>
    private bool CanDrawAnnotation() =>
        _canvas.HasValue &&
        _session.ActiveTool.HasValue &&
        _selection.State == SelectionState.Selected &&
        _selection.IsCaptureReady &&
        !_session.IsTextEditing;

    /// <summary>窗口内 DIP 点是否落在选区内部（按下开始绘制的前提）。</summary>
    private bool IsInsideSelectionPhysical(AnnotationCanvasTransform canvas, Point dip)
    {
        var screenX = _capture.Bounds.Left + _transform.DipToPixel(dip.X);
        var screenY = _capture.Bounds.Top + _transform.DipToPixel(dip.Y);
        return canvas.Selection.Contains(screenX, screenY);
    }

    // --- T16 标注编辑：选择 / 移动 / 缩放 / 拖端点 / 删除 / 改文字。 ---

    /// <summary>
    /// 是否可以把鼠标按下解释为"编辑已有标注"：选区已定稿、当前是选择模式（没有绘制工具）、
    /// 文档里有标注，且不在拖拽/输入中。
    /// </summary>
    private bool CanEditAnnotation() =>
        _canvas.HasValue &&
        _session.Editing is { IsDragging: false } &&
        _session.ActiveTool is null &&
        _session.Count > 0 &&
        !_session.IsTextEditing &&
        !_session.IsDrafting &&
        _selection.IsCaptureReady &&
        _selection.State == SelectionState.Selected;

    /// <summary>
    /// 选择模式下的按下：命中手柄 → 缩放/拖端点；命中标注 → 选中并准备整体平移；
    /// 命中已选中的文字标注且是双击 → 重新编辑文本。
    /// </summary>
    private bool BeginAnnotationEditAt(AnnotationCanvasTransform canvas, Point dip)
    {
        var editor = _session.Editing;
        if (editor is null)
        {
            return false;
        }

        var imagePoint = canvas.ToImage(dip.X, dip.Y);

        // 只有落在选区内才处理，避免在选区外误选。
        if (!IsInsideSelectionPhysical(canvas, dip))
        {
            if (editor.HasSelection)
            {
                editor.ClearSelection();
                AppLog.Info("Editor", "点在选区外：取消标注选中态");
                return true;
            }

            return false;
        }

        var previouslySelected = editor.Selected;
        var hitHandle = editor.HitTestHandle(imagePoint, EditorHandleRadius);
        var hitAnnotation = _session.Document.HitTest(imagePoint, EditorHitTolerance);

        // 每次按下都要记录点击时间/位置，否则"第一次点击"不会进入双击判定窗口。
        var isDoubleClick = RegisterClick();

        // 双击文字标注：进入重新编辑（不移动）。第二次点击时它已在选中态，
        // 因此先按"命中的是文字"判定，再补一次选中即可。
        if (isDoubleClick && hitAnnotation is TextAnnotation text && hitHandle == AnnotationHandle.None)
        {
            if (!ReferenceEquals(text, previouslySelected))
            {
                editor.Select(text);
            }

            BeginExistingTextEdit(canvas, text);
            return true;
        }

        var started = editor.BeginDrag(imagePoint, EditorHandleRadius, EditorHitTolerance);
        if (!started)
        {
            AppLog.Info("Editor", $"选择模式按下：图像={imagePoint}，未命中任何标注，取消选中");
            return editor.HasSelection is false && previouslySelected is not null;
        }

        _editorPointerDown = true;
        CaptureMouse();
        var kind = editor.Selected?.Kind.ToString() ?? "无";
        AppLog.Info(
            "Editor",
            $"开始编辑标注：类型={kind} 手柄={(hitHandle == AnnotationHandle.None ? "内部/平移" : hitHandle)} " +
            $"图像={imagePoint} 包围盒={editor.SelectionBounds} 文档标注数={_session.Count}");
        return true;
    }

    /// <summary>删除选中标注（一条可撤销记录）。</summary>
    private void DeleteSelectedAnnotation(string source)
    {
        var editor = _session.Editing;
        if (editor is null)
        {
            return;
        }

        var kind = editor.Selected?.Kind.ToString() ?? "无";
        var description = _session.DeleteSelected();
        AppLog.Info(
            "Editor",
            $"删除标注（{source}）：类型={kind} 记录={description ?? "无"} 文档标注数={_session.Count} " +
            $"撤销栈={_session.History.UndoCount} 选区锁定={_session.IsSelectionLocked}");

        _annotationLayer?.InvalidateVisual();
        UpdateVisuals();
    }

    /// <summary>
    /// 记录本次按下并返回它是否构成双击：两次按下间隔小于
    /// <see cref="DoubleClickMilliseconds"/> 且物理位置相差不超过 3 像素。
    /// 每次按下都必须调用（包括第一次），否则第一次不会进入双击判定窗口。
    /// </summary>
    private bool RegisterClick()
    {
        var now = Environment.TickCount64;
        var isDouble = _lastEditorClickAt != 0 &&
                       now - _lastEditorClickAt <= DoubleClickMilliseconds &&
                       Math.Abs(_lastEditorClickX - _lastPhysicalX) <= 3 &&
                       Math.Abs(_lastEditorClickY - _lastPhysicalY) <= 3;

        if (isDouble)
        {
            // 消费掉这次双击，避免三击又触发一次。
            _lastEditorClickAt = 0;
            return true;
        }

        _lastEditorClickAt = now;
        _lastEditorClickX = _lastPhysicalX;
        _lastEditorClickY = _lastPhysicalY;
        return false;
    }

    /// <summary>双击文字标注：把它的文字放回输入框，提交时用命令改文本（可撤销）。</summary>
    private void BeginExistingTextEdit(AnnotationCanvasTransform canvas, TextAnnotation annotation)
    {
        _editingText = annotation;
        _editingTextOriginal = annotation.Text;
        var editor = EnsureTextEditor();

        // 先清空再赋初值，避免 TextChanged 里把同一段文字重复写回（不影响结果，只是干净）。
        editor.TextChanged -= OnExistingTextChanged;
        editor.Text = annotation.Text;
        editor.TextChanged += OnExistingTextChanged;

        var origin = canvas.ToDip(annotation.Position);
        var available = canvas.SelectionDip.Width - (origin.X - canvas.SelectionDip.Left) - 8;
        var width = Math.Clamp(available, TextEditorMinWidth, 360);
        _textEditorHost!.Width = width;
        Canvas.SetLeft(_textEditorHost, origin.X);
        Canvas.SetTop(_textEditorHost, origin.Y + TextEditorOffsetY);
        _textEditorHost.Visibility = Visibility.Visible;
        editor.FontSize = Math.Max(8, canvas.ToDipLength(annotation.FontSize));
        _textEditorHost.UpdateLayout();

        AppLog.Info(
            "Editor",
            $"重新编辑文字：图像坐标={annotation.Position} 原内容=\"{TextPreview(annotation.Text)}\" " +
            $"输入框DIP=({origin.X:0.##},{origin.Y:0.##}) 宽={width:0.##} 字号={annotation.FontSize}" +
            "（Enter 提交、Esc 取消，均不触发复制/关闭遮罩）");

        _textEditorHost.Focus();
        editor.Focus();
        Keyboard.Focus(editor);
        editor.CaretIndex = editor.Text.Length;
    }

    /// <summary>重新编辑文字时的实时预览：直接改标注文本（提交/取消时再纠正）。</summary>
    private void OnExistingTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_editingText is null)
        {
            return;
        }

        _editingText.Text = _textEditor?.Text ?? string.Empty;
        _annotationLayer?.InvalidateVisual();
    }

    private void CommitExistingTextEdit(string source)
    {
        var annotation = _editingText;
        if (annotation is null)
        {
            return;
        }

        var text = _textEditor?.Text ?? string.Empty;
        _editingText = null;
        _textEditor?.TextChanged -= OnExistingTextChanged;
        HideTextEditor();
        RestoreOverlayFocus();

        // 实时预览期间已经把 Text 改成了新内容；先还原成原值，再由命令记录"原值 → 新值"，
        // 否则 SetSelectedText 会因为"没有变化"而不记录，撤销就失效了。
        annotation.Text = _editingTextOriginal;
        var description = _session.Editing?.SetSelectedText(text);
        AppLog.Info(
            "Editor",
            $"文字重新编辑提交（{source}）：长度={text.Length} 内容=\"{TextPreview(text)}\" " +
            $"记录={description ?? "无变化"} 文档标注数={_session.Count} 撤销栈={_session.History.UndoCount}");

        _annotationLayer?.InvalidateVisual();
        UpdateVisuals();
    }

    private void CancelExistingTextEdit(string source)
    {
        var annotation = _editingText;
        if (annotation is null)
        {
            return;
        }

        annotation.Text = _editingTextOriginal;
        _editingText = null;
        _textEditor?.TextChanged -= OnExistingTextChanged;
        HideTextEditor();
        RestoreOverlayFocus();
        _annotationLayer?.InvalidateVisual();
        AppLog.Info(
            "Editor",
            $"文字重新编辑取消（{source}）：保留原内容=\"{TextPreview(annotation.Text)}\"，不产生记录");
        UpdateVisuals();
    }

    /// <summary>把选中标注的虚线框与手柄画到窗口 DIP 坐标。</summary>
    private void UpdateEditorVisuals()
    {
        var editor = _session.Editing;
        var canvas = _canvas;

        if (editor is null || canvas is null || !editor.HasSelection || !_selection.IsCaptureReady)
        {
            _editorOutline.Visibility = Visibility.Collapsed;
            foreach (var (_, element) in _editorHandles)
            {
                element.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var bounds = editor.SelectionBounds;
        if (bounds.IsEmpty)
        {
            // 退化标注（单点画笔等）：用 2px 的小框示意。
            bounds = new PixelRect(bounds.Left, bounds.Top, Math.Max(2, bounds.Width), Math.Max(2, bounds.Height));
        }

        var dip = canvas.Value.ToDipRect(bounds);
        var geometry = new RectangleGeometry(new Rect(dip.Left, dip.Top, dip.Width, dip.Height));
        _editorOutline.Data = geometry;
        _editorOutline.Visibility = Visibility.Visible;

        var visibleHandles = new HashSet<AnnotationHandle>();
        foreach (var (handle, rect) in editor.GetHandleRects())
        {
            var handleDip = canvas.Value.ToDipRect(rect);
            var element = FindEditorHandle(handle);
            if (element is null)
            {
                continue;
            }

            Canvas.SetLeft(element, handleDip.Left);
            Canvas.SetTop(element, handleDip.Top);
            element.Width = Math.Max(4, handleDip.Width);
            element.Height = Math.Max(4, handleDip.Height);
            element.Visibility = Visibility.Visible;
            visibleHandles.Add(handle);
        }

        foreach (var (handle, element) in _editorHandles)
        {
            if (!visibleHandles.Contains(handle))
            {
                element.Visibility = Visibility.Collapsed;
            }
        }
    }

    private Rectangle? FindEditorHandle(AnnotationHandle handle)
    {
        foreach (var (candidate, element) in _editorHandles)
        {
            if (candidate == handle)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>在选区内的 DIP 位置开始画标注（拖拽草稿或文字输入）。</summary>
    private void BeginAnnotationAt(AnnotationCanvasTransform canvas, Point dip)
    {
        var imagePoint = canvas.ToImageClamped(dip.X, dip.Y);
        ClearStatus();

        if (_session.ActiveTool == AnnotationTool.Text)
        {
            BeginTextInput(canvas, imagePoint);
            return;
        }

        try
        {
            _session.BeginDraft(imagePoint);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn("Editor", $"无法开始标注：{ex.Message}");
            SetStatus(StringsUi.Get("S.Overlay.CannotDraw"));
            return;
        }

        _annotationPointerDown = true;
        CaptureMouse();
        AppLog.Info(
            "Editor",
            $"开始画标注：工具={_session.ActiveTool} 起点图像={imagePoint} 颜色={_session.Settings.Color} " +
            $"粗细={_session.Settings.Thickness} 选区={_selection.Selection} 屏幕DPI={_capture.Dpi}");
    }

    /// <summary>鼠标在画标注时结束草稿（丢失捕获兜底，用最后位置定稿）。</summary>
    private void FinishAnnotationDrag(string reason)
    {
        if (!_annotationPointerDown)
        {
            return;
        }

        _annotationPointerDown = false;
        if (_canvas is { } canvas)
        {
            _session.UpdateDraft(canvas.ToImageClamped(
                _transform.PixelToDip(_lastPhysicalX - _capture.Bounds.Left),
                _transform.PixelToDip(_lastPhysicalY - _capture.Bounds.Top)));
        }

        var committed = _session.CommitDraft();
        AppLog.Warn(
            "Editor",
            $"标注拖拽被中断（{reason}），已用最后位置提交={committed}，文档标注数={_session.Count}");
        UpdateVisuals();
    }

    private void ReleaseAnnotationPointer()
    {
        _annotationPointerDown = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    private void BeginTextInput(AnnotationCanvasTransform canvas, PixelPoint imagePoint)
    {
        TextAnnotation pending;
        try
        {
            pending = _session.BeginText(imagePoint);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn("Editor", $"无法开始文字输入：{ex.Message}");
            SetStatus(StringsUi.Get("S.Overlay.CannotType"));
            return;
        }

        var origin = canvas.ToDip(imagePoint);
        var editor = EnsureTextEditor();
        editor.Text = string.Empty;
        _session.UpdateTextPreview(string.Empty);

        var available = canvas.SelectionDip.Width - (origin.X - canvas.SelectionDip.Left) - 8;
        var width = Math.Clamp(available, TextEditorMinWidth, 360);
        _textEditorHost!.Width = width;
        Canvas.SetLeft(_textEditorHost, origin.X);
        Canvas.SetTop(_textEditorHost, origin.Y + TextEditorOffsetY);
        _textEditorHost.Visibility = Visibility.Visible;
        // 输入框字号与标注字号一致（物理像素 → DIP），所见即所得。
        editor.FontSize = Math.Max(8, canvas.ToDipLength(pending.FontSize));
        _textEditorHost.UpdateLayout();

        AppLog.Info(
            "Editor",
            $"文字输入开始：图像坐标={imagePoint} 锚点DIP=({origin.X:0.##},{origin.Y:0.##}) " +
            $"输入框DIP=({Canvas.GetLeft(_textEditorHost):0.##},{Canvas.GetTop(_textEditorHost):0.##}) " +
            $"宽度={width:0.##} 字号={pending.FontSize} 颜色={pending.Color} " +
            $"（Enter 确认文字、Esc 取消文字，均不触发复制/关闭遮罩）");

        // 输入框自己拿键盘焦点；遮罩的 PreviewKeyDown 仍先收到按键，用于拦截 Enter/Esc。
        _textEditorHost.Focus();
        editor.Focus();
        Keyboard.Focus(editor);
        editor.CaretIndex = 0;
    }

    private void CommitTextInput(string source)
    {
        if (!_session.IsTextEditing)
        {
            return;
        }

        var text = _textEditor?.Text ?? string.Empty;
        var committed = _session.CommitText(text);
        HideTextEditor();
        RestoreOverlayFocus();

        AppLog.Info(
            "Editor",
            $"文字提交（{source}）：长度={text.Length} 内容=\"{TextPreview(text)}\" 已提交={committed} " +
            $"文档标注数={_session.Count} 撤销栈={_session.History.UndoCount}");
        UpdateVisuals();
    }

    private void CancelTextInput(string source)
    {
        if (!_session.IsTextEditing)
        {
            return;
        }

        var text = _textEditor?.Text ?? string.Empty;
        _session.CancelText();
        HideTextEditor();
        RestoreOverlayFocus();
        AppLog.Info("Editor", $"文字输入取消（{source}）：丢弃内容=\"{TextPreview(text)}\"，不产生标注");
        UpdateVisuals();
    }

    private static string TextPreview(string text)
    {
        var single = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return single.Length <= 40 ? single : single[..40] + "…";
    }

    private void HideTextEditor()
    {
        if (_textEditorHost is not null)
        {
            _textEditorHost.Visibility = Visibility.Collapsed;
        }

        if (_textEditor is not null)
        {
            _textEditor.Text = string.Empty;
        }
    }

    private bool IsInsideTextEditor(Point dip)
    {
        if (_textEditorHost is null || _textEditorHost.Visibility != Visibility.Visible)
        {
            return false;
        }

        var left = Canvas.GetLeft(_textEditorHost);
        var top = Canvas.GetTop(_textEditorHost);
        if (double.IsNaN(left))
        {
            left = 0;
        }

        if (double.IsNaN(top))
        {
            top = 0;
        }

        var width = _textEditorHost.ActualWidth > 0 ? _textEditorHost.ActualWidth : _textEditorHost.DesiredSize.Width;
        var height = _textEditorHost.ActualHeight > 0
            ? _textEditorHost.ActualHeight
            : _textEditorHost.DesiredSize.Height;

        return dip.X >= left && dip.X <= left + width && dip.Y >= top && dip.Y <= top + height;
    }

    private TextBox EnsureTextEditor() =>
        _textEditor ?? throw new InvalidOperationException("文字输入框尚未创建。");

    private void UndoAnnotation(string source)
    {
        var description = _session.History.NextUndoDescription ?? "无";
        var done = _session.Undo();
        AppLog.Info(
            "Editor",
            $"撤销（{source}）：{description} 成功={done} 文档标注数={_session.Count} " +
            $"撤销栈={_session.History.UndoCount} 重做栈={_session.History.RedoCount} " +
            $"选区锁定={_session.IsSelectionLocked}");

        if (!done)
        {
            SetStatus(StringsUi.Get("S.Overlay.NothingToUndo"));
            return;
        }

        _annotationLayer?.InvalidateVisual();
        UpdateVisuals();
    }

    private void RedoAnnotation(string source)
    {
        var description = _session.History.NextRedoDescription ?? "无";
        var done = _session.Redo();
        AppLog.Info(
            "Editor",
            $"重做（{source}）：{description} 成功={done} 文档标注数={_session.Count} " +
            $"撤销栈={_session.History.UndoCount} 重做栈={_session.History.RedoCount} " +
            $"选区锁定={_session.IsSelectionLocked}");

        if (!done)
        {
            SetStatus(StringsUi.Get("S.Overlay.NothingToRedo"));
            return;
        }

        _annotationLayer?.InvalidateVisual();
        UpdateVisuals();
    }

    /// <summary>选区变化时更新标注画布：换算、摆放绘制层、刷新工具选中态。</summary>
    private void UpdateAnnotationCanvas(DipRect selectionDip)
    {
        if (!_selection.IsCaptureReady || selectionDip.IsEmpty)
        {
            _canvas = null;
            _annotationHost.Visibility = Visibility.Collapsed;
            _annotationHost.Children.Clear();
            _annotationLayer = null;
            if (_session.IsDrafting)
            {
                _session.CancelDraft();
            }

            // T16：选区失效时选中态也失效。
            _session.Editing?.Select(null);

            return;
        }

        var canvas = new AnnotationCanvasTransform(
            _capture.Bounds,
            _capture.Dpi,
            _selection.Selection);
        _canvas = canvas;

        var layer = _annotationLayer;
        if (layer is null || layer.Dpi != _capture.Dpi)
        {
            // 传入冻结图 + 选区裁剪矩形：马赛克预览用同一套 Core 像素化算法（与导出一致）。
            var cropRect = SelectionCrop.ToImageRect(_selection.Selection, _capture.Bounds);
            layer = new AnnotationLayer(
                _session.Document,
                _capture.Dpi,
                () => _session.Preview,
                _capture.Image,
                cropRect);
            _annotationLayer = layer;
            _annotationHost.Children.Clear();
            _annotationHost.Children.Add(layer);
        }

        layer.Width = selectionDip.Width;
        layer.Height = selectionDip.Height;
        Canvas.SetLeft(_annotationHost, selectionDip.Left);
        Canvas.SetTop(_annotationHost, selectionDip.Top);
        _annotationHost.Width = selectionDip.Width;
        _annotationHost.Height = selectionDip.Height;
        _annotationHost.Visibility = Visibility.Visible;
        layer.InvalidateVisual();
    }

    private void UpdateToolSelectionVisuals()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            OverlayChrome.SetOn(button, tool == _session.ActiveTool);
        }

        foreach (var (color, element) in _colorSwatches)
        {
            OverlayChrome.SetOn(element, color == _session.Settings.Color);
        }

        foreach (var (thickness, element) in _thicknessButtons)
        {
            OverlayChrome.SetOn(element, thickness == _session.Settings.Thickness);
        }

        if (_lockText is not null)
        {
            var locked = _session.IsSelectionLocked;
            _lockText.Text = locked
                ? StringsUi.Format("S.Overlay.SelectionLocked", _session.Count)
                : string.Empty;
            _lockText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        }
    }

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

        UpdateAnnotationCanvas(_selection.IsCaptureReady ? ToDip(_selection.Selection) : DipRect.Empty);
        UpdateDim(client, highlight);
        UpdateBorder(highlight);
        UpdateHandles(highlight);
        UpdateSizeBadge(client, highlight);
        UpdateToolbar(client, highlight);
        UpdateToolSelectionVisuals();
        UpdateEditorVisuals();
        UpdateLockBadge(client, highlight);

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

        _border.StrokeThickness = thickness;
        _border.Width = Math.Max(0, rect.Width - thickness);
        _border.Height = Math.Max(0, rect.Height - thickness);
        Canvas.SetLeft(_border, rect.Left + inset);
        Canvas.SetTop(_border, rect.Top + inset);
        _border.Visibility = Visibility.Visible;
    }

    private void UpdateHandles(DipRect? highlight)
    {
        // 选区锁定（已开始标注）或处于绘制工具模式时隐藏手柄：此时不允许再调整选区尺寸。
        var show = _selection.State is SelectionState.Selected or SelectionState.Adjusting
                   && !_session.IsSelectionLocked
                   && !_session.ActiveTool.HasValue;

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
        if (IsInsideToolbar(pointerDip) || IsInsideTextEditor(pointerDip))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        // 绘制工具 + 已定稿选区：选区内是十字（可画），选区外是箭头（画出界无效）。
        if (_session.ActiveTool.HasValue && _selection.IsCaptureReady && !_session.IsTextEditing)
        {
            Cursor = _canvas is { } canvas && IsInsideSelectionPhysical(canvas, pointerDip)
                ? Cursors.Cross
                : Cursors.Arrow;
            return;
        }

        // T16：选择模式下悬停选中标注的手柄时给出缩放/移动光标。
        if (_session.Editing is { HasSelection: true } editor && _canvas is { } editCanvas)
        {
            var imagePoint = editCanvas.ToImage(pointerDip.X, pointerDip.Y);
            var hoverHandle = editor.HitTestHandle(imagePoint, EditorHandleRadius);
            if (hoverHandle != AnnotationHandle.None)
            {
                Cursor = hoverHandle switch
                {
                    AnnotationHandle.TopLeft or AnnotationHandle.BottomRight => Cursors.SizeNWSE,
                    AnnotationHandle.TopRight or AnnotationHandle.BottomLeft => Cursors.SizeNESW,
                    AnnotationHandle.Left or AnnotationHandle.Right => Cursors.SizeWE,
                    AnnotationHandle.Top or AnnotationHandle.Bottom => Cursors.SizeNS,
                    _ => Cursors.SizeAll,
                };
                return;
            }

            if (_session.Document.HitTest(imagePoint, EditorHitTolerance) is not null)
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }

        if (_pointerDown || _selection.State == SelectionState.Dragging)
        {
            Cursor = Cursors.Cross;
            return;
        }

        // 选区锁定后不再提供移动/八向光标（此时选区不可调整）。
        if (_session.IsSelectionLocked)
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
        CaptureWindowLayer.MarkFullscreen(_captureHwnd, false);
        _captureHwnd = IntPtr.Zero;
        _inputGuard?.Dispose();
        _inputGuard = null;
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
            var image = RenderSelection(CaptureAction.Copy);
            ClipboardWriter.Write(image);
            stopwatch.Stop();
            CompleteCapture(CaptureAction.Copy, image, stopwatch.Elapsed, filePath: null);
        }
        catch (COMException ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Copy, StringsUi.Get("S.Message.ClipboardBusy"), ex, isWarning: true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Copy, StringsUi.Get("S.Overlay.CopyFailed"), ex, isWarning: false);
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
            FailOutput(CaptureAction.SaveAs, StringsUi.Get("S.Overlay.SaveDialogFailed"), ex, isWarning: false);
            return;
        }

        bool? result;
        var wasTopmost = Topmost;
        try
        {
            // 遮罩是 Topmost 窗口；用 Topmost 的所有者显示 Win32 文件对话框时，
            // 对话框会被所有者的置顶状态干扰（本机实测会自行返回/关闭）。
            // 显示期间临时取消置顶，关闭后恢复并重新激活遮罩。
            _saveDialogOpen = true;
            CaptureWindowLayer.MarkFullscreen(_captureHwnd, false);
            Topmost = false;
            AppLog.Info("Output", $"保存对话框已打开：默认名称={dialog.FileName} 目录={dialog.InitialDirectory}");
            result = dialog.ShowDialog(this);
            AppLog.Info("Output", $"保存对话框已关闭：DialogResult={result} 选择文件={dialog.FileName}");
        }
        catch (Exception ex)
        {
            FailOutput(CaptureAction.SaveAs, StringsUi.Get("S.Overlay.SaveDialogFailed"), ex, isWarning: false);
            return;
        }
        finally
        {
            _saveDialogOpen = false;
            Topmost = wasTopmost;
            CaptureWindowLayer.MarkFullscreen(_captureHwnd, wasTopmost);
            RefreshCaptureLayer();
        }

        if (result != true)
        {
            AppLog.Info(
                "Output",
                $"另存为取消（{source}）：选区={_selection.Selection}，留在截图状态，不关闭遮罩、不写入任何记录");
            ClearStatus();
            RestoreOverlayFocus();
            return;
        }

        var format = ResolveFormat(dialog.FileName, dialog.FilterIndex);
        var path = EnsureExtension(dialog.FileName, format);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var image = RenderSelection(CaptureAction.SaveAs);
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
            FailOutput(CaptureAction.SaveAs, StringsUi.Get("S.Overlay.SaveFailed"), ex, isWarning: false);
        }
    }

    /// <summary>
    /// 贴图（T18）：把合成图创建成一张独立的置顶贴图窗口，成功后结束截图。
    /// 多张并存由 <see cref="PinWindows"/> 管理；本轮不接剪贴板贴图（T21）。
    /// </summary>
    private void TriggerPin(string source)
    {
        if (!TryBeginOutput(source, CaptureAction.Pin))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var image = RenderSelection(CaptureAction.Pin);
            // T22：内部截图来源 → 历史已由 ICaptureSink（T11）记录，贴图本身不再记一次。
            var pin = PinWindows.Create(
                image,
                _selection.Selection,
                _capture.Bounds,
                _capture.Dpi,
                DeepShot.Core.Pins.PinSource.Screenshot);
            stopwatch.Stop();
            CompleteCapture(CaptureAction.Pin, image, stopwatch.Elapsed, filePath: null);

            // 遮罩关闭后把焦点交回贴图窗口，Esc 才能直接关闭它。
            pin.Activate();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            FailOutput(CaptureAction.Pin, StringsUi.Get("S.Overlay.PinFailed"), ex, isWarning: false);
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

    /// <summary>
    /// 合成导出图（T17）：选区内的冻结图 + 全部标注，尺寸 = 选区物理像素。
    /// 遮罩压暗、选区边框、手柄、工具栏、尺寸文字都不参与渲染。
    /// 失败会抛异常，由各动作的 catch 提示并留在截图状态。
    /// </summary>
    private BitmapSource RenderSelection(CaptureAction action)
    {
        var image = AnnotationExportRenderer.Render(
            _capture.Image,
            _selection.Selection,
            _capture.Bounds,
            _capture.Dpi,
            _session.Document);

        var expectedWidth = _selection.Selection.Width;
        var expectedHeight = _selection.Selection.Height;
        AppLog.Info(
            "Output",
            $"导出渲染完成（{LoggingCaptureSink.Describe(action)}）：选区={_selection.Selection} " +
            $"屏幕={_capture.Bounds} DPI={_capture.Dpi} 标注={_session.Count} 个" +
            $"（马赛克={CountMosaics()} 个）→ 图像={image.PixelWidth}x{image.PixelHeight} {image.Format} " +
            $"尺寸匹配选区物理像素={image.PixelWidth == expectedWidth && image.PixelHeight == expectedHeight}");

        return image;
    }

    private int CountMosaics()
    {
        var count = 0;
        foreach (var annotation in _session.Document)
        {
            if (annotation is MosaicAnnotation)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>成功收尾：写完成记录（T24：<see cref="HistoryCaptureSink"/> 写一条历史）→ 关闭遮罩。</summary>
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
            AppLog.Error("Output", "输出完成记录失败（不阻断关闭遮罩；历史写入失败不影响已完成的复制/保存/贴图）", ex);
        }

        Close();
        if (action == CaptureAction.Copy)
        {
            ToastWindow.Show(StringsUi.Get(_quickCopy ? "S.Toast.QuickScreenshotCopied" : "S.Overlay.Copied"));
        }
        else if (action == CaptureAction.SaveAs && !string.IsNullOrEmpty(filePath))
        {
            ToastWindow.Show(StringsUi.Format("S.Overlay.Saved", System.IO.Path.GetFileName(filePath)));
        }
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
            _statusText.Foreground = isError ? OverlayChrome.StatusErrorBrush : OverlayChrome.StatusInfoBrush;
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
            Title = StringsUi.Get("S.Dialog.SaveTitle"),
            FileName = CaptureFileName.CreateDefault(DateTime.Now, CaptureImageFormat.Png),
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            Filter = StringsUi.Get("S.Dialog.SaveFilter"),
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
        // 两行：上行 = 工具 | 颜色 | 粗细；下行 = 选区尺寸/状态（左）+ 撤销重做与输出动作（右）。
        var toolGroup = OverlayChrome.Group(
            CreateToolButton("S.Annotation.Tool.Select", null, "select", OverlayChrome.IconSelect, filled: true),
            CreateToolButton("S.Annotation.Tool.Rectangle", AnnotationTool.Rectangle, "rectangle", OverlayChrome.IconRectangle),
            CreateToolButton("S.Annotation.Tool.Ellipse", AnnotationTool.Ellipse, "ellipse", OverlayChrome.IconEllipse),
            CreateToolButton("S.Annotation.Tool.Arrow", AnnotationTool.Arrow, "arrow", OverlayChrome.IconArrow),
            CreateToolButton("S.Annotation.Tool.Line", AnnotationTool.Line, "line", OverlayChrome.IconLine),
            CreateToolButton("S.Annotation.Tool.Freehand", AnnotationTool.Freehand, "freehand", OverlayChrome.IconFreehand),
            CreateToolButton("S.Annotation.Tool.Text", AnnotationTool.Text, "text", OverlayChrome.IconText),
            CreateToolButton("S.Annotation.Tool.Mosaic", AnnotationTool.Mosaic, "mosaic", OverlayChrome.IconMosaic, filled: true));

        // 颜色圆点也放进同一种分组底托，每个圆点占 32×32 的格子，与工具、粗细按钮同一节奏。
        var colorGroup = OverlayChrome.Group(
            AnnotationPalette.Colors.Select(color =>
            {
                var swatch = CreateColorSwatch(color);
                swatch.Margin = new Thickness(Tokens.Space.XS);
                return (UIElement)swatch;
            }).ToArray());

        var thicknessGroup = OverlayChrome.Group(
            AnnotationPalette.Thicknesses.Select(thickness => (UIElement)CreateThicknessButton(thickness)).ToArray());

        // 三个分组之间只留间距：底托本身已经划清边界，不再叠分隔线。
        colorGroup.Margin = new Thickness(Tokens.Space.S, 0, Tokens.Space.S, 0);
        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        topRow.Children.Add(toolGroup);
        topRow.Children.Add(colorGroup);
        topRow.Children.Add(thicknessGroup);

        OverlayChrome.StyleSizeText(_toolbarSizeText);
        var info = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(_toolbarSizeText);
        info.Children.Add(_statusText);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(OverlayChrome.Group(
            CreateIconAction("S.Overlay.Undo", "undo", OverlayChrome.IconUndo, () => UndoAnnotation("工具栏按钮“撤销”")),
            CreateIconAction("S.Overlay.Redo", "redo", OverlayChrome.IconRedo, () => RedoAnnotation("工具栏按钮“重做”"))));
        actions.Children.Add(OverlayChrome.Divider());
        actions.Children.Add(CreateToolbarButton("S.Overlay.SaveAs", "save-as", OverlayChrome.IconSave, () => TriggerSaveAs("工具栏按钮“另存为”")));
        actions.Children.Add(CreateToolbarButton("S.Overlay.Pin", "pin", OverlayChrome.IconPin, () => TriggerPin("工具栏按钮“贴图”")));
        actions.Children.Add(CreateToolbarButton("S.Overlay.Cancel", "cancel", OverlayChrome.IconCancel, () => Cancel("工具栏取消按钮"), danger: true));
        var copy = CreateToolbarButton("S.Overlay.Copy", "copy", OverlayChrome.IconCopy, () => TriggerCopy("工具栏按钮“复制”"), primary: true);
        copy.Margin = new Thickness(Tokens.Space.XS, 0, 0, 0);
        actions.Children.Add(copy);

        var bottomRow = new Grid { Margin = new Thickness(0, Tokens.Space.S, 0, 0) };
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = Tokens.Space.M });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(info, 0);
        Grid.SetColumn(actions, 2);
        bottomRow.Children.Add(info);
        bottomRow.Children.Add(actions);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        rows.Children.Add(topRow);
        rows.Children.Add(bottomRow);

        return OverlayChrome.Shell(rows);
    }

    /// <summary>上行标注工具按钮：“选择”传 <c>null</c> 表示回到不绘制模式。</summary>
    /// <param name="textKey">语言资源键（T31）。</param>
    /// <param name="tool">工具（null = 选择模式）。</param>
    /// <param name="slug">AutomationId 后缀（语言无关，验证脚本按它定位）。</param>
    private Button CreateToolButton(string textKey, AnnotationTool? tool, string slug, string iconData, bool filled = false)
    {
        var label = StringsUi.Get(textKey);
        var button = OverlayChrome.IconButton(iconData, label, filled);
        button.Click += (_, _) => SelectTool(tool);
        OverlayChrome.SetNames(button, label, $"overlay-tool-{slug}");
        _toolButtons.Add((tool, button));
        _toolbarButtons.Add((label, button));
        return button;
    }

    /// <summary>调色板颜色的显示名；不在调色板里的颜色回退为 #RRGGBB。</summary>
    private static string ColorName(AnnotationColor color) => (color.Argb & 0x00FFFFFF) switch
    {
        0xFF3B30 => StringsUi.Get("S.Overlay.Color.Red"),
        0xFFCC00 => StringsUi.Get("S.Overlay.Color.Yellow"),
        0x34C759 => StringsUi.Get("S.Overlay.Color.Green"),
        0x0A84FF => StringsUi.Get("S.Overlay.Color.Blue"),
        0xFFFFFF => StringsUi.Get("S.Overlay.Color.White"),
        0x000000 => StringsUi.Get("S.Overlay.Color.Black"),
        _ => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}",
    };

    /// <summary>颜色圆点：选中的带白色外环。</summary>
    private Button CreateColorSwatch(AnnotationColor color)
    {
        var button = OverlayChrome.Swatch(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        var name = ColorName(color);
        button.ToolTip = name;
        button.Click += (_, _) => SelectColor(color);
        var label = StringsUi.Format("S.Annotation.Color", name);
        AutomationProperties.SetName(button, label);
        _colorSwatches.Add((color, button));
        _toolbarButtons.Add((label, button));
        return button;
    }

    /// <summary>粗细按钮：圆点大小表示粗细，选中高亮。</summary>
    private Button CreateThicknessButton(int thickness)
    {
        var button = OverlayChrome.ThicknessChip(thickness);
        button.ToolTip = StringsUi.Format("S.Annotation.ThicknessTooltip", thickness);
        button.Click += (_, _) => SelectThickness(thickness);
        var label = StringsUi.Format("S.Annotation.Thickness", thickness);
        OverlayChrome.SetNames(button, label, $"overlay-thickness-{thickness}");
        _thicknessButtons.Add((thickness, button));
        _toolbarButtons.Add((label, button));
        return button;
    }

    /// <summary>切换绘制工具（“选择”= 不绘制）。绘制中不允许切换。</summary>
    private void SelectTool(AnnotationTool? tool)
    {
        if (_session.IsDrafting)
        {
            AppLog.Info("Editor", $"正在拖拽标注，忽略切换到工具 {tool?.ToString() ?? "选择"}");
            return;
        }

        if (_session.IsTextEditing)
        {
            CommitTextInput("切换工具");
        }

        try
        {
            _session.SetActiveTool(tool);
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn("Editor", $"切换工具失败：{ex.Message}");
            return;
        }

        AppLog.Info(
            "Editor",
            $"标注工具切换：{(_session.ActiveTool?.ToString() ?? "选择")}；" +
            $"颜色={_session.Settings.Color} 粗细={_session.Settings.Thickness} 字号={_session.Settings.FontSize}；" +
            $"选区锁定={_session.IsSelectionLocked} 文档标注数={_session.Count}");
        UpdateToolSelectionVisuals();
        UpdateVisuals();
    }

    /// <summary>选颜色：只影响之后创建的标注。</summary>
    private void SelectColor(AnnotationColor color)
    {
        _session.Settings.Color = color;
        AppLog.Info("Editor", $"标注颜色切换：{color}（对之后创建的标注生效）");
        UpdateToolSelectionVisuals();
    }

    /// <summary>选粗细：只影响之后创建的标注。</summary>
    private void SelectThickness(int thickness)
    {
        _session.Settings.Thickness = thickness;
        AppLog.Info("Editor", $"标注粗细切换：{_session.Settings.Thickness} 像素（对之后创建的标注生效）");
        UpdateToolSelectionVisuals();
    }

    /// <summary>文字输入框：随点击位置出现在选区内，Enter 确认 / Esc 取消。</summary>
    private Border CreateTextEditorHost()
    {
        _textEditor = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = Tokens.Text.BodyLarge,
            FontFamily = AppFonts.Family,
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        // 不套全局主题输入框样式：圆角框与聚焦光晕会盖住选区里的画面。
        UiKit.UsePlainTextBox(_textEditor);

        _textEditor.TextChanged += (_, _) =>
        {
            if (_session.IsTextEditing)
            {
                _session.UpdateTextPreview(_textEditor.Text);
                _annotationLayer?.InvalidateVisual();
            }
        };

        // 输入框内也拦截 Enter（确认文字），避免它被当成复制；Shift+Enter 换行。
        _textEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                e.Handled = true;
                CommitTextInput("文字输入框 Enter");
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelTextInput("文字输入框 Esc");
            }
        };

        // 前景 / 光标走浮层前景色资源；选中文字用强调色高亮。
        _textEditor.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.OverlayForeground);
        _textEditor.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeManager.BrushKeys.OverlayForeground);
        _textEditor.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, UiKit.Keys.Accent);

        // 深色玻璃 + 强调色细边：与工具栏同一材质，一眼能看出是“正在输入”。
        var host = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.Radius.Small),
            Padding = new Thickness(6, 3, 6, 3),
            Child = _textEditor,
            Visibility = Visibility.Collapsed,
        };
        host.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.OverlayBar);
        host.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Accent);

        AutomationProperties.SetName(host, StringsUi.Get("S.Overlay.TextInput"));
        return host;
    }

    /// <summary>选区锁定的可视化提示（选区右上方小标签），一开始标注就显示。</summary>
    private Border CreateLockIndicator()
    {
        // 与尺寸徽标同一材质：深色玻璃胶囊 + 挂锁图标。图标跟随徽标前景（白）：
        // 浅色主题的强调色是深蓝，压在深色玻璃上几乎看不见。
        _lockText = OverlayChrome.BadgeText();
        _lockBadge = OverlayChrome.LockBadge(_lockText);
        _lockBadge.Visibility = Visibility.Collapsed;
        return _lockBadge;
    }

    /// <summary>把锁定标签摆到选区右上方（放不下就放右下方），并夹在窗口内。</summary>
    private void UpdateLockBadge(DipRect client, DipRect? selection)
    {
        if (_lockBadge is null)
        {
            return;
        }

        if (!_session.IsSelectionLocked || !selection.HasValue || selection.Value.IsEmpty)
        {
            _lockBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _lockBadge.Visibility = Visibility.Visible;
        _lockBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _lockBadge.DesiredSize;
        var rect = selection.Value;

        var left = Math.Clamp(rect.Right - size.Width, client.Left, Math.Max(client.Left, client.Right - size.Width));
        var above = rect.Top - size.Height - BadgeGap;
        var below = rect.Bottom + BadgeGap;
        var top = above >= client.Top ? above : below;
        top = Math.Clamp(top, client.Top, Math.Max(client.Top, client.Bottom - size.Height));

        // 工具栏也可能摆在选区上方（下方放不下时）：徽标别压在工具栏上，
        // 先换到选区另一侧，两侧都不行就收进选区右上角内侧。
        if (OverlapsToolbar(left, top, size))
        {
            var fitsBelow = below + size.Height <= client.Bottom;
            if (fitsBelow && !OverlapsToolbar(left, below, size))
            {
                top = below;
            }
            else
            {
                left = Math.Max(rect.Left, rect.Right - size.Width - BadgeGap);
                top = rect.Top + BadgeGap;
            }
        }

        Canvas.SetLeft(_lockBadge, left);
        Canvas.SetTop(_lockBadge, top);
    }

    /// <summary>某个浮件矩形（DIP）是否与当前显示的工具栏重叠。</summary>
    private bool OverlapsToolbar(double left, double top, Size size)
    {
        if (_toolbar.Visibility != Visibility.Visible)
        {
            return false;
        }

        var toolbarLeft = Canvas.GetLeft(_toolbar);
        var toolbarTop = Canvas.GetTop(_toolbar);
        if (double.IsNaN(toolbarLeft) || double.IsNaN(toolbarTop))
        {
            return false;
        }

        var toolbar = new Rect(toolbarLeft, toolbarTop, _toolbar.DesiredSize.Width, _toolbar.DesiredSize.Height);
        return toolbar.IntersectsWith(new Rect(left, top, size.Width, size.Height));
    }

    /// <summary>下行方形图标按钮（撤销/重做）。</summary>
    private Button CreateIconAction(string textKey, string slug, string iconData, Action onClick)
    {
        var text = StringsUi.Get(textKey);
        var button = OverlayChrome.IconButton(iconData, text);
        button.Click += (_, _) => onClick();
        OverlayChrome.SetNames(button, text, $"overlay-action-{slug}");
        _toolbarButtons.Add((text, button));
        return button;
    }

    /// <summary>下行图标 + 文字动作按钮（按钮不抢键盘焦点，保证 Enter/Esc 始终作用于遮罩）。</summary>
    private Button CreateToolbarButton(string textKey, string slug, string iconData, Action onClick, bool primary = false, bool danger = false)
    {
        var text = StringsUi.Get(textKey);
        var button = OverlayChrome.ActionButton(iconData, text, primary, danger);
        button.Click += (_, _) => onClick();
        OverlayChrome.SetNames(button, text, $"overlay-action-{slug}");
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
                RadiusX = HandleSize / 2,
                RadiusY = HandleSize / 2,
                Fill = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            element.SetResourceReference(Shape.StrokeProperty, UiKit.Keys.Accent);

            _handles[handle] = element;
            yield return new KeyValuePair<SelectionHandle, Rectangle>(handle, element);
        }
    }
}
