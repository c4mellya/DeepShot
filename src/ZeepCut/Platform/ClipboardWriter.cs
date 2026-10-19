using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZeepCut.Platform;

/// <summary>
/// Places eagerly rendered PNG and standard CF_DIB data on the clipboard.
/// Windows owns the buffers after SetClipboardData, independently of the capture window.
/// </summary>
internal static class ClipboardWriter
{
    private const uint DibFormat = 8;
    private const uint MoveableMemory = 0x0002;
    private static HwndSource? _owner;

    public static void Write(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        // Allocate and encode before opening/clearing the clipboard.
        var dib = EncodeDib(image);
        using var stream = new MemoryStream();
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        png.Save(stream);
        var pngBytes = stream.ToArray();

        var pngFormat = ClipboardNative.RegisterClipboardFormat("PNG");
        if (pngFormat == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _owner ??= new HwndSource(new HwndSourceParameters("ZeepCut.ClipboardOwner")
        {
            ParentWindow = new IntPtr(-3),
            Width = 0, Height = 0, WindowStyle = 0
        });

        var dibHandle = Allocate(dib);
        var pngHandle = IntPtr.Zero;
        var opened = false;
        try
        {
            pngHandle = Allocate(pngBytes);
            for (var attempt = 0; attempt < ClipboardImage.DefaultAttempts; attempt++)
            {
                if (ClipboardNative.OpenClipboard(_owner.Handle))
                {
                    opened = true;
                    break;
                }
                if (attempt + 1 < ClipboardImage.DefaultAttempts)
                    Thread.Sleep(ClipboardImage.DefaultRetryDelayMilliseconds);
            }
            if (!opened)
                throw new COMException("剪贴板被其他程序占用，请稍后重试。", unchecked((int)0x800401D0));
            if (!ClipboardNative.EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (ClipboardNative.SetClipboardData(DibFormat, dibHandle) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            dibHandle = IntPtr.Zero; // Ownership transferred to Windows; never free this handle.

            if (ClipboardNative.SetClipboardData(pngFormat, pngHandle) == IntPtr.Zero)
            {
                // CF_DIB is already a complete copy; do not report the entire copy as failed.
                AppLog.Warn("Clipboard", $"PNG 格式写入失败，标准 DIB 已保留：{Marshal.GetLastWin32Error()}");
            }
            else
            {
                pngHandle = IntPtr.Zero;
            }
            AppLog.Info("Clipboard", $"已持久写入图片 {image.PixelWidth}x{image.PixelHeight}：DIB={dib.Length}B PNG={pngBytes.Length}B");
        }
        finally
        {
            if (opened) ClipboardNative.CloseClipboard();
            if (dibHandle != IntPtr.Zero) ClipboardNative.GlobalFree(dibHandle);
            if (pngHandle != IntPtr.Zero) ClipboardNative.GlobalFree(pngHandle);
        }
    }

    private static IntPtr Allocate(byte[] bytes)
    {
        var handle = ClipboardNative.GlobalAlloc(MoveableMemory, (nuint)bytes.Length);
        if (handle == IntPtr.Zero) throw new OutOfMemoryException("无法分配剪贴板图片内存。");
        var pointer = ClipboardNative.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            ClipboardNative.GlobalFree(handle);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        catch { ClipboardNative.GlobalUnlock(handle); ClipboardNative.GlobalFree(handle); throw; }
        ClipboardNative.GlobalUnlock(handle);
        return handle;
    }

    private static byte[] EncodeDib(BitmapSource image)
    {
        // 24-bit bottom-up BI_RGB is accepted by legacy editors, browsers, and chat apps.
        // PNG supplies lossless alpha; DIB is an opaque fallback on a white background.
        var source = image.Format == PixelFormats.Bgra32 ? image :
            new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var sourceStride = checked(width * 4);
        var stride = checked((checked(width * 3) + 3) & ~3);
        var size = checked(stride * height);
        var pixels = new byte[checked(sourceStride * height)];
        source.CopyPixels(pixels, sourceStride, 0);
        var result = new byte[checked(40 + size)];
        using (var writer = new BinaryWriter(new MemoryStream(result, writable: true)))
        {
            writer.Write(40); writer.Write(width); writer.Write(height);
            writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(0); writer.Write(size);
            writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
        }
        for (var y = 0; y < height; y++)
        {
            var row = 40 + (height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var src = y * sourceStride + x * 4;
                var dst = row + x * 3;
                var alpha = pixels[src + 3];
                for (var channel = 0; channel < 3; channel++)
                    result[dst + channel] = (byte)((pixels[src + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
            }
        }
        return result;
    }
}
