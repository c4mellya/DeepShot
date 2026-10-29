using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeepShot.Core.Geometry;
using DeepShot.Ui;

namespace DeepShot.Platform;

/// <summary>
/// 轻量提示窗口（T20/T21）：在光标附近显示一行短消息，约 2 秒后自动消失。
/// </summary>
/// <remarks>
/// <para>
/// 托盘气球（<c>Shell_NotifyIcon</c> 通知）在本机常被系统“专注/通知”设置抑制，
/// 实测桌面截图里看不到（T5 已知问题）。因此“剪贴板没有图片”“没有贴图可隐藏”
/// 这类**必须让用户看见**的反馈改用这个自绘小窗：无边框、置顶、不抢焦点、
/// 不进入任务栏/Alt+Tab（<c>WS_EX_TOOLWINDOW</c>）、不参与鼠标命中。
/// </para>
/// <para>
/// 它只是提示，不承载任何交互：<see cref="Window.IsHitTestVisible"/> 为 false，
/// 鼠标点击穿透到下面的窗口（本窗口不响应任何点击，也不需要）。
/// </para>
/// </remarks>
public sealed class ToastWindow : Window
{
    /// <summary>自动关闭时长（毫秒）。</summary>
    public const int AutoCloseMilliseconds = 2200;

    /// <summary>提示框与光标的间距（DIP）。</summary>
    private const double CursorGap = 18;

    private readonly DispatcherTimer _timer;

    /// <summary>当前显示的提示；新提示出现时关闭旧的，避免叠在一起。</summary>
    private static ToastWindow? _current;

    private ToastWindow(string message)
    {
        Title = StringsUi.Format("S.Toast.WindowTitle", Environment.ProcessId);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;

        // T30：取主题里的提示色（深色底/浅色边框都是半透明，压在冻结画面上仍然清晰）。
        // 一律用资源引用：提示显示期间切换主题也会跟着换色。
        var text = new TextBlock
        {
            Text = message,
            FontSize = Tokens.Text.Body,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.ToastForeground);
        text.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);

        // 行首一枚小应用图标：一眼认出"这是 DeepShot 的提示"，不必读完文字。
        var mark = AppChrome.IconImage(18);
        mark.Margin = new Thickness(0, 0, Tokens.Space.S + 2, 0);
        mark.VerticalAlignment = VerticalAlignment.Center;
        var line = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        line.Children.Add(mark);
        line.Children.Add(text);

        var bubble = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.Radius.Popup),
            Padding = new Thickness(Tokens.Space.M, Tokens.Space.S + 2, Tokens.Space.L, Tokens.Space.S + 2),
            Child = line,
        };
        bubble.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.ToastBackground);
        bubble.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.ToastBorder);

        // 投影做成气泡身后的兄弟元素（不加在文字的祖先上，避免文字被位图化发虚）；
        // 外围留出投影的绘制空间，窗口本身是透明的。
        var shadow = UiKit.ShadowPlate(Tokens.Radius.Popup, blur: 16, depth: 4, opacity: 0.28);
        shadow.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.ToastBackground);

        var host = new Grid { Margin = new Thickness(12, 8, 12, 16) };
        host.Children.Add(shadow);
        host.Children.Add(bubble);
        Content = host;

        // 轻轻浮入（只动透明度与位移，不影响定位与命中）。
        Loaded += (_, _) => UiKit.Entrance(host, 4);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoCloseMilliseconds) };
        _timer.Tick += OnTimerTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// 在光标附近弹一条提示。调用方必须已经在 UI 线程上（托盘/热键回调都在 Dispatcher 上）。
    /// </summary>
    /// <param name="message">提示文字（一行，过长会换行）。</param>
    /// <returns>显示的窗口；失败时返回 null（调用方自行兜底），不抛异常。</returns>
    public static ToastWindow? Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        try
        {
            _current?.Close();
            var toast = new ToastWindow(message);
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, toast))
                {
                    _current = null;
                }
            };
            _current = toast;
            toast.Show();
            AppLog.Info("Toast", $"已显示提示：{message}");
            return toast;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Toast", $"显示轻量提示失败（改为日志 + 通知兜底）：{ex.Message}");
            return null;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowStyle(hwnd, NativeMethods.GWL_EXSTYLE);
        var updated = style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        if (updated != style)
        {
            NativeMethods.SetWindowStyle(hwnd, NativeMethods.GWL_EXSTYLE, updated);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlaceNearCursor();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        Close();
    }

    /// <summary>把提示窗放到光标右下方，并夹进光标所在屏幕的工作区（副屏负坐标同样适用）。</summary>
    private void PlaceNearCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var dpi = 96;
        var workArea = new PixelRect(0, 0, 1920, 1080);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (!monitor.Equals(IntPtr.Zero))
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            };

            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                workArea = new PixelRect(
                    info.rcWork.Left,
                    info.rcWork.Top,
                    info.rcWork.Width,
                    info.rcWork.Height);
            }

            if (DeepShot.Capture.ScreenCapture.TryGetMonitorDpi(monitor, out var detected))
            {
                dpi = detected;
            }
        }

        var transform = new DpiTransform(dpi);
        var width = ActualWidth > 0 ? ActualWidth : transform.PixelToDip(320);
        var height = ActualHeight > 0 ? ActualHeight : transform.PixelToDip(48);

        // 光标物理 → 屏幕 DIP 坐标（相对目标屏幕原点，副屏原点可为负）。
        var cursorDipX = transform.PixelToDip(cursor.X);
        var cursorDipY = transform.PixelToDip(cursor.Y);
        var workDipRight = transform.PixelToDip(workArea.Right);
        var workDipBottom = transform.PixelToDip(workArea.Bottom);
        var workDipLeft = transform.PixelToDip(workArea.Left);
        var workDipTop = transform.PixelToDip(workArea.Top);

        var left = cursorDipX + CursorGap;
        if (left + width > workDipRight)
        {
            left = cursorDipX - CursorGap - width;
        }

        var top = cursorDipY + CursorGap;
        if (top + height > workDipBottom)
        {
            top = cursorDipY - CursorGap - height;
        }

        Left = Math.Clamp(left, workDipLeft, Math.Max(workDipLeft, workDipRight - width));
        Top = Math.Clamp(top, workDipTop, Math.Max(workDipTop, workDipBottom - height));
    }

}
