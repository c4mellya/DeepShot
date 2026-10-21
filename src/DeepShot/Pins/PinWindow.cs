using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepShot.Core.Geometry;
using DeepShot.Core.Output;
using DeepShot.Core.Pins;
using DeepShot.Output;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.Pins;

/// <summary>
/// 贴图窗口（T18 基础 + T19 增强）：无边框、置顶、不占任务栏按钮的独立窗口，
/// 1:1 显示一张截图，支持拖动、滚轮缩放、Ctrl+滚轮透明度、锁定位置、
/// 右键菜单（原始大小/复制/另存为/锁定/关闭）与 Esc/× 关闭。
/// </summary>
/// <remarks>
/// <para>
/// 所有几何与限制规则都在 Core 的 <see cref="PinState"/> 里（可单测），本类型只负责：
/// 物理像素 ↔ DIP 换算、Win32 窗口摆放、鼠标/键盘/右键菜单接线、以及把状态画出来。
/// </para>
/// <para>
/// <b>坐标</b>：窗口位置尺寸一律按“贴图当前所在屏幕”的 DPI 从物理像素换算成 DIP；
/// 贴图被拖到另一块 DPI 不同的屏幕后重新取 DPI（<c>MonitorFromWindow</c>），
/// 并再次用 <c>SetWindowPos</c> 复核物理矩形，避免 125%/100% 混合 DPI 下尺寸漂移。
/// </para>
/// <para>
/// <b>锚点</b>：滚轮缩放以**鼠标位置**为锚点（鼠标下的画面点不动），
/// 这是 T18 选定的锚点策略；右键“原始大小”则以窗口中心为锚点，避免贴图跳走。
/// </para>
/// <para>
/// <b>锁定</b>：锁定后禁止拖动，但鼠标仍在窗口内（不穿透），滚轮缩放、Ctrl+滚轮透明度
/// 与右键菜单照常可用；窗口外框变为橙色提示，鼠标指针改为普通箭头。
/// </para>
/// </remarks>
public sealed class PinWindow : Window
{
    /// <summary>边框线宽（DIP）：用来把贴图与桌面区分开。</summary>
    private const double BorderThicknessValue = 1.0;

    /// <summary>锁定描边的线宽（DIP）与颜色（橙色）。</summary>
    private const double LockedBorderThicknessValue = 2.0;

    /// <summary>关闭按钮尺寸（DIP）与右上角边距（DIP）。</summary>
    private const double CloseButtonSize = 22;
    private const double CloseButtonMargin = 4;

    /// <summary>鼠标停止移动多久之后重新取 DPI / 复核物理矩形（毫秒）。</summary>
    private const int DpiRefreshDelayMilliseconds = 220;

    private static readonly Color BorderColor = Color.FromArgb(0x8C, 0x00, 0x00, 0x00);
    private static readonly Color LockedBorderColor = Color.FromRgb(0xFF, 0xA0, 0x40);
    private static readonly Color CloseButtonBackgroundColor = Color.FromArgb(0xB3, 0x20, 0x20, 0x20);
    private static readonly Color CloseButtonBorderColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
    private static readonly Color LockBadgeBackgroundColor = Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E);
    private static readonly Color LockBadgeForegroundColor = Color.FromRgb(0xFF, 0xC1, 0x6A);

    private readonly BitmapSource _image;
    private readonly PixelRect _screenBounds;
    private readonly Border _border;
    private readonly Image _picture;
    private readonly Button _closeButton;
    private readonly Border _lockBadge;
    private readonly TextBlock _lockText;
    private readonly Brush _normalBorderBrush;
    private readonly Brush _lockedBorderBrush;

    private PinState _state;
    private int _dpi;
    private IntPtr _hwnd;

    /// <summary>拖动状态：鼠标按下时的屏幕物理坐标与窗口左上角。</summary>
    private bool _dragging;
    private PixelPoint _dragStartPointer;
    private PixelPoint _dragStartTopLeft;

    private (DpiTransform Transform, PixelRect Bounds)? _lastApplied;

    /// <summary>拖动中每次移动都记一条日志会刷屏，只在开始/结束时记录。</summary>
    private long _lastDpiRefreshTicks;

    public PinWindow(BitmapSource image, PixelRect selection, PixelRect screenBounds, int dpi)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (selection.IsEmpty)
        {
            throw new ArgumentException("贴图选区不能为空。", nameof(selection));
        }

        _image = image;
        _screenBounds = screenBounds;
        _dpi = dpi > 0 ? dpi : 96;
        _state = PinState.Create(image.PixelWidth, image.PixelHeight, new PixelPoint(selection.Left, selection.Top));
        _lastDpiRefreshTicks = Environment.TickCount64;

        Title = StringsUi.Format("S.Pin.WindowTitle", Environment.ProcessId);
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
        Cursor = Cursors.SizeAll;

        _normalBorderBrush = CreateFrozenBrush(BorderColor);
        _lockedBorderBrush = CreateFrozenBrush(LockedBorderColor);

        _picture = new Image
        {
            Source = image,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };
        // 贴图是像素图：放大时用最近邻，保持像素锐利（与遮罩预览一致）。
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.NearestNeighbor);

        _lockText = new TextBlock
        {
            Text = StringsUi.Get("S.Pin.LockedBadge"),
            Foreground = CreateFrozenBrush(LockBadgeForegroundColor),
            FontSize = 11,
            FontFamily = new FontFamily(ThemeManager.FontFamilyName),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 2, 6, 2),
        };

        _lockBadge = new Border
        {
            Background = CreateFrozenBrush(LockBadgeBackgroundColor),
            BorderBrush = _lockedBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _lockText,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(CloseButtonMargin),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        _closeButton = new Button
        {
            Content = "×",
            Width = CloseButtonSize,
            Height = CloseButtonSize,
            Margin = new Thickness(CloseButtonMargin),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Brushes.White,
            Background = CreateFrozenBrush(CloseButtonBackgroundColor),
            BorderBrush = CreateFrozenBrush(CloseButtonBorderColor),
            BorderThickness = new Thickness(1),
            FontSize = 14,
            Padding = new Thickness(0),
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            ToolTip = StringsUi.Get("S.Pin.Close"),
        };
        _closeButton.Click += (_, _) =>
        {
            AppLog.Info("Pins", $"贴图窗口关闭（右上 ×）：{_state}");
            Close();
        };

        _border = new Border
        {
            BorderBrush = _normalBorderBrush,
            BorderThickness = new Thickness(BorderThicknessValue),
            IsHitTestVisible = false,
        };

        var root = new Grid { Background = Brushes.Black };
        root.Children.Add(_picture);
        // 1px 边框叠在图片之上：区分贴图与桌面，又不占用内容像素。
        root.Children.Add(_border);
        root.Children.Add(_lockBadge);
        root.Children.Add(_closeButton);
        Content = root;

        ApplyStateToWindow(force: true, stage: "创建");
        ApplyVisualState();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseMove += OnMouseMove;
        LostMouseCapture += OnLostMouseCapture;
        MouseWheel += OnMouseWheel;
        PreviewMouseRightButtonUp += OnMouseRightButtonUp;
        Closed += OnClosed;

        AppLog.Info(
            "Pins",
            $"创建贴图窗口：选区={selection} 屏幕={screenBounds} DPI={dpi} " +
            $"图像={image.PixelWidth}x{image.PixelHeight} 期望物理={_state.Bounds}");
    }

    /// <summary>当前贴图状态（缩放/位置/透明度/锁定），主要供日志与验证使用。</summary>
    public PinState State => _state;

    /// <summary>贴图内容（1:1 原图，T22 历史接缝与右键“复制/另存为”共用）。</summary>
    public BitmapSource Image => _image;

    /// <summary>贴图所在屏幕的 DPI（跨屏移动后会更新）。</summary>
    public int ScreenDpi => _dpi;

    /// <summary>贴图当前占用的屏幕物理矩形。</summary>
    public PixelRect Bounds => _state.Bounds;

    /// <summary>
    /// T20：这张贴图当前是否显示。全局隐藏把窗口设为 <see cref="Visibility.Collapsed"/>，
    /// 窗口实例、状态与集合条目都保留。
    /// </summary>
    public bool IsShown => IsVisible && Visibility == Visibility.Visible;

    /// <summary>
    /// T20：隐藏前把窗口状态拍一张快照（位置/尺寸/透明度/锁定 + DPI）。
    /// 全局隐藏只折叠窗口，<see cref="ShowFromBulk"/> 时用这份快照恢复，
    /// 因此恢复后位置、尺寸、透明度与隐藏前完全一致。
    /// </summary>
    internal PinWindowSnapshot CaptureSnapshot() => new(_state, _dpi);

    /// <summary>T20：隐藏这张贴图（不销毁窗口、不关闭、不改状态）。</summary>
    internal void HideForBulk()
    {
        if (_dragging)
        {
            EndDrag("全局隐藏前收尾");
        }

        // 先取消激活，避免隐藏后系统把焦点留在不可见窗口上。
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// T20：重新显示这张贴图。给了 <paramref name="snapshot"/> 时按快照恢复状态
    /// （隐藏期间窗口理论上不会变，但跨屏/DPI 变化后仍需重新换算，所以恢复后照样复核物理矩形）。
    /// </summary>
    /// <returns>物理矩形是否与状态一致。</returns>
    internal bool ShowFromBulk(PinWindowSnapshot? snapshot)
    {
        if (snapshot is { } saved)
        {
            var dpiChanged = saved.Dpi != _dpi;
            _state = saved.State;
            if (dpiChanged)
            {
                _dpi = saved.Dpi;
            }

            _lastApplied = null;
        }

        Visibility = Visibility.Visible;
        ApplyStateToWindow(force: true, stage: "显示全部贴图");
        ApplyVisualState();
        return CorrectPhysicalBounds("显示全部贴图复核", _state.Bounds);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // 与遮罩一致：工具窗口不出现在任务栏/Alt+Tab，也不进入截图候选。
        var extendedStyle = NativeMethods.GetWindowStyle(_hwnd, NativeMethods.GWL_EXSTYLE);
        var updatedStyle = extendedStyle | NativeMethods.WS_EX_TOOLWINDOW;
        if (updatedStyle != extendedStyle && !NativeMethods.SetWindowStyle(_hwnd, NativeMethods.GWL_EXSTYLE, updatedStyle))
        {
            AppLog.Warn("Pins", $"设置 WS_EX_TOOLWINDOW 失败（Win32Error={Marshal.GetLastWin32Error()}）");
        }

        // 无边框窗口默认没有阴影；给窗口类加 CS_DROPSHADOW，让贴图有轻微投影，
        // 与桌面区分开（比自绘阴影简单，也不会因为 AllowsTransparency 影响 1:1 像素）。
        var classStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GCL_STYLE);
        if ((classStyle & NativeMethods.CS_DROPSHADOW) == 0)
        {
            NativeMethods.SetWindowClassStyle(
                _hwnd,
                NativeMethods.GCL_STYLE,
                classStyle | NativeMethods.CS_DROPSHADOW);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 跨屏时 DPI 可能不同：先用窗口所在屏幕重新取一次 DPI，再按它复核物理矩形。
        RefreshDpi(stage: "Loaded");
        CorrectPhysicalBounds("Loaded", _state.Bounds);

        var dpi = VisualTreeHelper.GetDpi(this);
        AppLog.Info(
            "Pins",
            $"贴图窗口：物理={_state.Bounds} DPI={_dpi}（WPF PixelsPerDip={dpi.PixelsPerDip:0.##}）" +
            $"状态={_state}");
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        Activate();
        Focus();
        Keyboard.Focus(this);
        AppLog.Info("Pins", $"贴图已激活：IsActive={IsActive} 状态={_state}");
    }

    // --- 状态 → 窗口 ---

    /// <summary>
    /// 把 <see cref="_state"/> 的物理矩形应用到窗口：按当前 DPI 换成 DIP 设置 Left/Top/Width/Height，
    /// 再用 <c>SetWindowPos</c> 复核/校正物理矩形（混合 DPI 下 WPF 首次摆放可能偏差）。
    /// </summary>
    /// <returns>是否真的改变了窗口位置或尺寸。</returns>
    private bool ApplyStateToWindow(bool force, string stage)
    {
        var bounds = _state.Bounds;
        var transform = new DpiTransform(_dpi);
        var previous = _lastApplied;

        if (!force && previous is { } last && last.Transform.Dpi == transform.Dpi && last.Bounds == bounds)
        {
            return false;
        }

        try
        {
            Left = transform.PixelToDip(bounds.Left);
            Top = transform.PixelToDip(bounds.Top);
            Width = transform.PixelToDip(bounds.Width);
            Height = transform.PixelToDip(bounds.Height);

            if (!_hwnd.Equals(IntPtr.Zero))
            {
                CorrectPhysicalBounds(stage, bounds);
            }
        }
        catch (InvalidOperationException ex)
        {
            AppLog.Warn("Pins", $"应用贴图物理矩形失败（{stage}）：{ex.Message}");
            return false;
        }

        _lastApplied = (transform, bounds);
        return true;
    }

    /// <summary>用 SetWindowPos 把窗口强制摆到物理矩形；失败只记日志（WPF 的 DIP 摆放仍然生效）。</summary>
    private bool CorrectPhysicalBounds(string stage, PixelRect expected)
    {
        if (_hwnd.Equals(IntPtr.Zero))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(_hwnd, out var actualRect))
        {
            AppLog.Warn("Pins", $"{stage}：GetWindowRect 失败（Win32Error={Marshal.GetLastWin32Error()}）");
            return false;
        }

        var actual = new PixelRect(actualRect.Left, actualRect.Top, actualRect.Width, actualRect.Height);
        if (actual == expected)
        {
            return true;
        }

        if (!NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                expected.Left,
                expected.Top,
                expected.Width,
                expected.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE))
        {
            AppLog.Error("Pins", $"{stage}：SetWindowPos 失败（Win32Error={Marshal.GetLastWin32Error()}）");
            return false;
        }

        if (NativeMethods.GetWindowRect(_hwnd, out var corrected))
        {
            var result = new PixelRect(corrected.Left, corrected.Top, corrected.Width, corrected.Height);
            if (result == expected)
            {
                AppLog.Info("Pins", $"{stage}：物理矩形校正为 {result}");
                return true;
            }

            AppLog.Warn("Pins", $"{stage}：校正后仍为 {result}（期望 {expected}）");
            return false;
        }

        return false;
    }

    /// <summary>把视觉表现（边框颜色、关闭按钮、锁定提示、透明度、鼠标指针）同步到状态。</summary>
    private void ApplyVisualState()
    {
        Opacity = _state.Opacity;
        _border.BorderBrush = _state.IsLocked ? _lockedBorderBrush : _normalBorderBrush;
        _border.BorderThickness = new Thickness(
            _state.IsLocked ? LockedBorderThicknessValue : BorderThicknessValue);
        _lockBadge.Visibility = _state.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        Cursor = _state.IsLocked ? Cursors.Arrow : Cursors.SizeAll;

        // 贴图太小（40px）时 × 按钮会盖住几乎整张图：小于 3 倍按钮尺寸时隐藏，Esc/右键菜单仍可关闭。
        var showClose = _state.DisplayWidth > CloseButtonSize * 3 && _state.DisplayHeight > CloseButtonSize * 3;
        _closeButton.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;

        if (_state.IsLocked)
        {
            _lockText.Text = StringsUi.Format("S.Pin.LockedBadgeWithSize", _state.DisplayWidth, _state.DisplayHeight);
        }
    }

    /// <summary>重新取窗口所在屏幕的 DPI（拖动跨屏时用），变化后重算 DIP 尺寸。</summary>
    private void RefreshDpi(string stage)
    {
        _lastDpiRefreshTicks = Environment.TickCount64;
        if (_hwnd.Equals(IntPtr.Zero))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor.Equals(IntPtr.Zero))
        {
            return;
        }

        if (!DeepShot.Capture.ScreenCapture.TryGetMonitorDpi(monitor, out var dpi) || dpi <= 0 || dpi == _dpi)
        {
            return;
        }

        AppLog.Info("Pins", $"{stage}：贴图所在屏幕 DPI {_dpi} → {dpi}，按新 DPI 重新换算 DIP 尺寸");
        _dpi = dpi;
        _lastApplied = null;
        ApplyStateToWindow(force: true, stage: stage + "（DPI 变化）");
    }

    // --- 鼠标：拖动 / 缩放 / 透明度 ---

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 右上 × 由按钮自己处理，不要开始拖动。
        if (e.OriginalSource is DependencyObject source && IsInside(source, _closeButton))
        {
            return;
        }

        if (!_state.CanDrag)
        {
            AppLog.Info("Pins", $"贴图已锁定，忽略拖动：{_state}");
            e.Handled = true;
            return;
        }

        var pointer = GetPointerPhysical();
        _dragging = true;
        _dragStartPointer = pointer;
        _dragStartTopLeft = _state.TopLeft;
        CaptureMouse();
        AppLog.Info("Pins", $"开始拖动贴图：指针={pointer} 起点={_dragStartTopLeft} 状态={_state}");
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag("左键已松开");
            return;
        }

        var pointer = GetPointerPhysical();
        var target = new PixelPoint(
            _dragStartTopLeft.X + (pointer.X - _dragStartPointer.X),
            _dragStartTopLeft.Y + (pointer.Y - _dragStartPointer.Y));

        // DragTo 在锁定时会拒绝移动（理论上这里不会发生，因为按下时就检查过）。
        var moved = _state.DragTo(target);
        if (moved.TopLeft == _state.TopLeft)
        {
            return;
        }

        _state = moved;
        ApplyStateToWindow(force: true, stage: "拖动");
        MaybeRefreshDpiWhileDragging();
    }

    private void MaybeRefreshDpiWhileDragging()
    {
        var now = Environment.TickCount64;
        if (now - _lastDpiRefreshTicks < DpiRefreshDelayMilliseconds)
        {
            return;
        }

        RefreshDpi(stage: "拖动");
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        EndDrag("左键抬起");
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndDrag("丢失鼠标捕获");

    private void EndDrag(string reason)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        AppLog.Info("Pins", $"拖动结束（{reason}）：{_state}");

        // 拖动可能跨了屏幕：结束时一定重新取 DPI 并复核物理矩形。
        RefreshDpi(stage: "拖动结束");
        _lastApplied = null;
        ApplyStateToWindow(force: true, stage: "拖动结束");
        var verified = CorrectPhysicalBounds("拖动结束复核", _state.Bounds);
        AppLog.Info(
            "Pins",
            $"拖动结束物理矩形={_state.Bounds} 与状态一致={verified}");
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta;
        if (delta == 0)
        {
            return;
        }

        var anchor = GetPointerPhysical();
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (ctrl)
        {
            // Ctrl+滚轮：透明度（有可见下限 20%，不允许调到完全看不见）。
            var before = _state.Opacity;
            _state = _state.AdjustOpacity(delta > 0 ? PinState.OpacityStep : -PinState.OpacityStep);
            ApplyVisualState();
            AppLog.Info(
                "Pins",
                $"Ctrl+滚轮调整透明度：{before:0.##} → {_state.Opacity:0.##}" +
                $"（下限={PinState.MinimumOpacity:0.##}）");
        }
        else
        {
            // 滚轮：以鼠标位置为锚点缩放，锚点下的画面点保持不动。
            var before = _state.Scale;
            var factor = delta > 0 ? PinState.ZoomStep : 1 / PinState.ZoomStep;
            _state = _state.ZoomBy(factor, anchor);
            if (Math.Abs(_state.Scale - before) < 1e-9)
            {
                AppLog.Info(
                    "Pins",
                    $"滚轮缩放已到限制：缩放={_state.Scale:0.###}x 尺寸={_state.DisplayWidth}x{_state.DisplayHeight}" +
                    $"（下限={PinState.MinimumDisplaySize}px 上限={PinState.MaximumScale:0.##}x）");
            }
            else
            {
                ApplyStateToWindow(force: true, stage: "滚轮缩放");
                AppLog.Info(
                    "Pins",
                    $"滚轮缩放：{before:0.###}x → {_state.Scale:0.###}x 锚点={anchor} " +
                    $"尺寸={_state.DisplayWidth}x{_state.DisplayHeight} 位置={_state.TopLeft}");
            }
        }

        ApplyVisualState();
        e.Handled = true;
        _ = sender;
    }

    private static bool IsInside(DependencyObject source, DependencyObject container)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }

    private PixelPoint GetPointerPhysical()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return _state.TopLeft;
        }

        return new PixelPoint(point.X, point.Y);
    }

    // --- 键盘：Esc 关闭 ---

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        AppLog.Info("Pins", $"贴图窗口关闭（Esc）：{_state}");
        Close();
    }

    // --- 右键菜单 ---

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowContextMenu();
        e.Handled = true;
    }

    /// <summary>
    /// 右键菜单：原始大小 / 复制 / 另存为 / 锁定·解锁 / 关闭。
    /// 菜单项按当前状态生成，锁定后仍可操作（锁定只禁止拖动）。
    /// </summary>
    private void ShowContextMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        // T31：菜单文案走语言资源（每次弹菜单按当前语言生成）。
        menu.Items.Add(CreateMenuItem(
            StringsUi.Get("S.Pin.Menu.OriginalSize"),
            StringsUi.Get("S.Pin.Menu.OriginalSizeHint"),
            () => ResetToOriginal()));

        var lockItem = CreateMenuItem(
            _state.IsLocked ? StringsUi.Get("S.Pin.Menu.Unlock") : StringsUi.Get("S.Pin.Menu.Lock"),
            _state.IsLocked ? StringsUi.Get("S.Pin.Menu.UnlockHint") : StringsUi.Get("S.Pin.Menu.LockHint"),
            () => ToggleLock());
        lockItem.IsChecked = _state.IsLocked;
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(StringsUi.Get("S.Pin.Menu.Copy"), StringsUi.Get("S.Pin.Menu.CopyHint"), CopyToClipboard));
        menu.Items.Add(CreateMenuItem(StringsUi.Get("S.Pin.Menu.SaveAs"), StringsUi.Get("S.Pin.Menu.SaveAsHint"), SaveAs));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(StringsUi.Get("S.Pin.Menu.Close"), StringsUi.Get("S.Pin.Menu.CloseHint"), () => Close()));

        // 菜单要能被点，必须先激活窗口（Topmost 窗口右键也要成为前台）。
        Activate();
        menu.IsOpen = true;
        AppLog.Info(
            "Pins",
            $"右键菜单已打开：锁定项={( _state.IsLocked ? StringsUi.Get("S.Pin.Menu.Unlock") : StringsUi.Get("S.Pin.Menu.Lock"))} 状态={_state}");
    }

    private static MenuItem CreateMenuItem(string header, string toolTip, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            ToolTip = toolTip,
        };

        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>右键“原始大小”：恢复到 1:1，保持中心点不动。</summary>
    private void ResetToOriginal()
    {
        var before = _state;
        _state = _state.ResetToOriginal();
        ApplyStateToWindow(force: true, stage: "原始大小");
        ApplyVisualState();
        AppLog.Info("Pins", $"右键“原始大小”：{before.Scale:0.###}x → 1x，{before.Bounds} → {_state.Bounds}");
    }

    /// <summary>右键“锁定/解锁”：只改锁定标志，位置与尺寸不变。</summary>
    private void ToggleLock()
    {
        var before = _state.IsLocked;
        _state = _state.ToggleLock();
        ApplyVisualState();
        AppLog.Info(
            "Pins",
            $"右键“{(_state.IsLocked ? "锁定位置" : "解锁位置")}”：锁定 {before} → {_state.IsLocked}，位置={_state.TopLeft}");
    }

    /// <summary>右键“复制”：把原图（不含边框）复制到剪贴板。</summary>
    private void CopyToClipboard()
    {
        try
        {
            ClipboardWriter.Write(_image);
            AppLog.Info(
                "Pins",
                $"右键“复制”：已复制 {_image.PixelWidth}x{_image.PixelHeight} 到剪贴板");
        }
        catch (COMException ex)
        {
            AppLog.Warn("Pins", $"右键“复制”失败（剪贴板被占用）：COMException: {ex.Message}");
            ToastWindow.Show(StringsUi.Get("S.Message.ClipboardBusy"));
        }
        catch (Exception ex)
        {
            AppLog.Error("Pins", "右键“复制”失败", ex);
            ToastWindow.Show(ex.Message);
        }
    }

    /// <summary>
    /// 右键“另存为”：复用 T11 的保存对话框 + 编码器；保存期间临时取消 Topmost
    /// （Topmost 所有者会让 Win32 文件对话框自行返回，T11 已实测）。
    /// </summary>
    private void SaveAs()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
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
        if (!string.IsNullOrWhiteSpace(pictures) && Directory.Exists(pictures))
        {
            dialog.InitialDirectory = pictures;
        }

        var wasTopmost = Topmost;
        bool? result;
        try
        {
            Topmost = false;
            AppLog.Info("Pins", $"贴图另存为对话框已打开：默认名称={dialog.FileName} 目录={dialog.InitialDirectory}");
            result = dialog.ShowDialog(this);
            AppLog.Info("Pins", $"贴图另存为对话框已关闭：DialogResult={result} 选择文件={dialog.FileName}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Pins", "贴图另存为失败（对话框）", ex);
            return;
        }
        finally
        {
            Topmost = wasTopmost;
            Activate();
        }

        if (result != true)
        {
            AppLog.Info("Pins", "贴图另存为已取消，不写入文件");
            return;
        }

        var format = CaptureFileName.TryGetFormatFromExtension(dialog.FileName, out var parsed)
            ? parsed
            : dialog.FilterIndex == 2
                ? CaptureImageFormat.Jpeg
                : CaptureImageFormat.Png;
        var path = Path.HasExtension(dialog.FileName)
            ? dialog.FileName
            : dialog.FileName + CaptureFileName.GetExtension(format);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            CaptureImageEncoder.Save(_image, path, format);
            stopwatch.Stop();
            AppLog.Info(
                "Pins",
                $"贴图已保存：{path} 格式={format} 尺寸={_image.PixelWidth}x{_image.PixelHeight} " +
                $"耗时={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppLog.Error("Pins", $"贴图保存失败：{path}", ex);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        AppLog.Info("Pins", $"贴图窗口已关闭：{_state.Bounds} 状态={_state}");
        PinWindows.NotifyClosed(this);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// T20：全局隐藏前拍下的贴图状态快照（状态 + 当时所在屏幕的 DPI）。
/// 全局显示时用同一份快照恢复，保证"隐藏再显示"位置/尺寸/透明度不变。
/// </summary>
/// <param name="State">隐藏前的缩放/位置/透明度/锁定状态。</param>
/// <param name="Dpi">隐藏前贴图所在屏幕的 DPI（跨屏后重新显示需要正确换算）。</param>
internal readonly record struct PinWindowSnapshot(PinState State, int Dpi);
