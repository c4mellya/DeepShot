using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Platform;

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

    private static readonly Color BackgroundColor = Color.FromArgb(0xF2, 0x20, 0x20, 0x20);
    private static readonly Color BorderColor = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);
    private static readonly Color ForegroundColor = Color.FromRgb(0xFF, 0xFF, 0xFF);

    private readonly DispatcherTimer _timer;

    private ToastWindow(string message)
    {
        Title = $"Zeep Cut 提示 {Environment.ProcessId}";
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

        var text = new TextBlock
        {
            Text = message,
            Foreground = CreateFrozenBrush(ForegroundColor),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        };

        Content = new Border
        {
            Background = CreateFrozenBrush(BackgroundColor),
            BorderBrush = CreateFrozenBrush(BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 9, 14, 9),
            Child = text,
        };

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
            var toast = new ToastWindow(message);
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

            if (ZeepCut.Capture.ScreenCapture.TryGetMonitorDpi(monitor, out var detected))
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

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
