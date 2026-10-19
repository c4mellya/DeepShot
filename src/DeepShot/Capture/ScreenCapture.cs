using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepShot.Core.Geometry;
using DeepShot.Platform;

namespace DeepShot.Capture;

/// <summary>
/// 一次屏幕捕获的结果：图像为物理像素，<see cref="Bounds"/> 是该屏幕在虚拟桌面中的物理矩形
/// （副屏可为负坐标）。<see cref="Image"/> 已冻结，可跨线程使用。
/// </summary>
public sealed record ScreenCaptureResult(
    BitmapSource Image,
    PixelRect Bounds,
    string DeviceName,
    bool IsPrimary,
    int Dpi)
{
    /// <summary>图像宽度（物理像素）。</summary>
    public int PixelWidth => Image.PixelWidth;

    /// <summary>图像高度（物理像素）。</summary>
    public int PixelHeight => Image.PixelHeight;
}

/// <summary>
/// GDI 屏幕捕获：用 <c>GetCursorPos</c> + <c>MonitorFromPoint</c> 找到鼠标所在显示器，
/// 再对它的物理像素矩形做 <c>BitBlt</c>。
/// </summary>
/// <remarks>
/// <para>
/// 进程在 <c>app.manifest</c> 中声明 PerMonitorV2，因此 <c>GetMonitorInfo</c> 的
/// <c>rcMonitor</c> 是真实物理像素；副屏 <c>Left=-1080</c> 这类负坐标直接传给 <c>BitBlt</c> 的
/// 源坐标，不需要平移或额外换算。
/// </para>
/// <para>
/// 输出统一转换为 <see cref="PixelFormats.Pbgra32"/>（BGR32 无 alpha，转换后 alpha 全为 255），
/// 并复制到独立的 <see cref="WriteableBitmap"/>，随后即可安全释放 GDI 位图。
/// </para>
/// </remarks>
public static class ScreenCapture
{
    /// <summary>捕获鼠标当前所在显示器（MONITOR_DEFAULTTONEAREST）。</summary>
    public static ScreenCaptureResult CaptureMonitorUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos 失败，无法定位鼠标所在屏幕");
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "MonitorFromPoint 失败，无法定位鼠标所在屏幕");
        }

        var info = new NativeMethods.MonitorInfoEx
        {
            cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMonitorInfo 失败，无法读取屏幕信息");
        }

        var bounds = new PixelRect(
            info.rcMonitor.Left,
            info.rcMonitor.Top,
            info.rcMonitor.Width,
            info.rcMonitor.Height);

        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException($"屏幕 {info.szDevice} 的物理矩形为空。");
        }

        var isPrimary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
        var dpi = GetEffectiveDpi(monitor);
        AppLog.Info(
            "Capture",
            $"鼠标 ({cursor.X},{cursor.Y}) → 屏幕 {info.szDevice}（主屏={isPrimary}），" +
            $"物理矩形=({bounds.Left},{bounds.Top}) {bounds.Width}x{bounds.Height} DPI={dpi}");

        var image = CaptureRectangle(bounds);
        return new ScreenCaptureResult(image, bounds, info.szDevice, isPrimary, dpi);
    }

    /// <summary>
    /// 目标屏幕的每屏 DPI（PerMonitorV2 下由 <c>GetDpiForMonitor</c> 返回）。
    /// 失败时回退 96，保证遮罩仍可显示，同时留下 WARN 日志。
    /// </summary>
    private static int GetEffectiveDpi(IntPtr monitor)
    {
        if (TryGetMonitorDpi(monitor, out var dpi))
        {
            return dpi;
        }

        return 96;
    }

    /// <summary>
    /// 通过 <c>GetDpiForMonitor</c> 读取指定屏幕的 DPI；失败时写 WARN 日志并返回 false。
    /// 遮罩与贴图都走这里，保证同一块屏幕的换算基准一致（贴图跨屏拖动时重新取 DPI）。
    /// </summary>
    public static bool TryGetMonitorDpi(IntPtr monitor, out int dpi)
    {
        var result = NativeMethods.GetDpiForMonitor(
            monitor,
            NativeMethods.MDT_EFFECTIVE_DPI,
            out var dpiX,
            out _);

        if (result == 0 && dpiX > 0)
        {
            dpi = (int)dpiX;
            return true;
        }

        AppLog.Warn("Capture", $"GetDpiForMonitor 失败（HRESULT=0x{result:X8}），按 96 DPI 处理");
        dpi = 96;
        return false;
    }

    /// <summary>把虚拟桌面坐标中的物理像素矩形复制为冻结的 <see cref="BitmapSource"/>。</summary>
    public static BitmapSource CaptureRectangle(PixelRect bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("捕获矩形不能为空。", nameof(bounds));
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetDC(桌面) 失败");
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousObject = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleDC 失败");
            }

            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateCompatibleBitmap 失败");
            }

            previousObject = NativeMethods.SelectObject(memoryDc, bitmap);
            if (previousObject == IntPtr.Zero || previousObject == new IntPtr(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SelectObject 失败");
            }

            // CAPTUREBLT 让分层窗口（含部分带透明效果的窗口）也进入结果；
            // 源坐标可为负，BitBlt 直接按虚拟桌面坐标取值。
            if (!NativeMethods.BitBlt(
                    memoryDc,
                    0,
                    0,
                    bounds.Width,
                    bounds.Height,
                    screenDc,
                    bounds.Left,
                    bounds.Top,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt 失败");
            }

            return ToBitmapSource(bitmap);
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero && previousObject != new IntPtr(-1))
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// 把捕获结果保存为 PNG 到 <c>%LOCALAPPDATA%\DeepShot\logs\capture-debug\</c>（T6 临时调试）。
    /// 文件名包含时间、屏幕设备名、屏幕原点和像素尺寸，便于区分主副屏。
    /// </summary>
    public static string SaveDebugPng(ScreenCaptureResult capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Directory.CreateDirectory(AppPaths.CaptureDebugDirectory);

        var device = SanitizeDeviceName(capture.DeviceName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"capture-{timestamp}-{device}-{capture.Bounds.Left}x{capture.Bounds.Top}-{capture.PixelWidth}x{capture.PixelHeight}.png");
        var path = Path.Combine(AppPaths.CaptureDebugDirectory, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(capture.Image));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            encoder.Save(stream);
        }

        AppLog.Info("Capture", $"捕获结果已保存：{path}");
        return path;
    }

    /// <summary>
    /// HBITMAP → 冻结的 Pbgra32 <see cref="BitmapSource"/>。
    /// 先转格式再复制到 <see cref="WriteableBitmap"/>，确保像素完全脱离 GDI 位图。
    /// </summary>
    private static BitmapSource ToBitmapSource(IntPtr hBitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Pbgra32;
        converted.EndInit();
        converted.Freeze();

        var independent = new WriteableBitmap(converted);
        independent.Freeze();
        return independent;
    }

    private static string SanitizeDeviceName(string deviceName)
    {
        var sanitized = new string(deviceName.Where(char.IsLetterOrDigit).ToArray());
        return sanitized.Length == 0 ? "monitor" : sanitized;
    }
}
