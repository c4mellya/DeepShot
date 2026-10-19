using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZeepCut.Platform;

/// <summary>读取剪贴板图片的结果分类（T21）。</summary>
public enum ClipboardImageStatus
{
    /// <summary>读到了一张图片。</summary>
    Success = 0,

    /// <summary>剪贴板是空的（没有任何数据）。</summary>
    Empty = 1,

    /// <summary>剪贴板里有数据，但不是图片（例如纯文本）。</summary>
    NotAnImage = 2,

    /// <summary>剪贴板被其他程序占用（COMException），重试后仍失败。</summary>
    Busy = 3,

    /// <summary>剪贴板里确实是图片，但解码失败。</summary>
    Failed = 4,
}

/// <summary>
/// 一次读取剪贴板图片的结果。<see cref="Status"/> 为 <see cref="ClipboardImageStatus.Success"/> 时
/// <see cref="Image"/> 是一张已冻结、可跨线程使用的独立位图。
/// </summary>
/// <param name="Status">结果分类。</param>
/// <param name="Image">读到的图片（失败时为 null）。</param>
/// <param name="Format">实际命中的剪贴板格式，用于日志（例如 "Bitmap"、"FileDrop:png"）。</param>
/// <param name="Message">面向用户的中文提示（成功时为简短描述）。</param>
public sealed record ClipboardImageResult(
    ClipboardImageStatus Status,
    BitmapSource? Image,
    string Format,
    string Message)
{
    /// <summary>是否成功读到图片。</summary>
    public bool Success => Status == ClipboardImageStatus.Success && Image is not null;
}

/// <summary>
/// 从系统剪贴板读取图片（T21）。支持三种形态：
/// <list type="number">
/// <item><b>PNG</b>：优先读取原始 PNG，保留真实颜色与透明度；</item>
/// <item><b>DIBV5 / DIB</b>：复制原生数据后按 BMP 解码，区分保留字节和显式 alpha 掩码；
/// 仅提供托管 Bitmap 的来源再回退到 <see cref="Clipboard.GetImage"/>；</item>
/// <item><b>FileDrop</b>：在资源管理器里复制 PNG/BMP/JPG 文件，读第一个图片文件。</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>剪贴板被占用</b>是常态（另一个进程正 OpenClipboard），因此每次读取都重试若干次；
/// 全部失败时返回 <see cref="ClipboardImageStatus.Busy"/> 而不是抛异常，调用方据此提示用户。
/// </para>
/// <para>
/// 读到的位图一律显式复制像素缓冲区，创建独立的 <see cref="BitmapSource"/> 并冻结：剪贴板的 DIB 内存
/// 属于它的所有者，直接持有 <see cref="BitmapSource"/> 在对方释放后可能变成黑图。
/// </para>
/// </remarks>
public static class ClipboardImage
{
    /// <summary>默认重试次数与间隔（毫秒）。</summary>
    public const int DefaultAttempts = 5;
    public const int DefaultRetryDelayMilliseconds = 60;

    /// <summary>允许作为文件拖放来源的图片扩展名。</summary>
    private static readonly string[] ImageExtensions = { ".png", ".bmp", ".jpg", ".jpeg", ".gif" };

    /// <summary>读取剪贴板图片；失败时返回带原因的结果，不抛异常。</summary>
    public static ClipboardImageResult TryRead()
    {
        ClipboardImageResult? lastFailure = null;

        for (var attempt = 1; attempt <= DefaultAttempts; attempt++)
        {
            var result = TryReadOnce(attempt);
            if (result.Success)
            {
                return result;
            }

            // 只有“被占用”值得重试；无图/非图片格式重试也不会有图。
            if (result.Status != ClipboardImageStatus.Busy)
            {
                return result;
            }

            lastFailure = result;
            Thread.Sleep(DefaultRetryDelayMilliseconds);
        }

        return lastFailure ?? new ClipboardImageResult(
            ClipboardImageStatus.Busy,
            null,
            "-",
            "剪贴板被其他程序占用，暂时读不到图片，请稍后重试。");
    }

    private static ClipboardImageResult TryReadOnce(int attempt)
    {
        try
        {
            // PNG is authoritative when available. DIB must be decoded from its own
            // header, rather than the alpha-bearing bitmap synthesized by WPF/OLE.
            var native = TryReadNativeImage(attempt);
            if (native is not null) return native;

            // Managed/OLE PNG providers may expose a stream instead of raw PNG bytes.
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is Stream pngStream)
            {
                var image = DecodeStream(pngStream);
                if (image is not null) return Success(image, "PNG", attempt);
            }

            // Fallback for clipboard providers that expose a managed bitmap only.
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is not null)
                {
                    return Success(image, "Bitmap", attempt);
                }
            }

            // 3) 文件拖放：资源管理器里复制一个图片文件。
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                foreach (var file in files)
                {
                    if (string.IsNullOrWhiteSpace(file) || !IsImageFile(file))
                    {
                        continue;
                    }

                    var image = LoadFile(file);
                    if (image is not null)
                    {
                        return Success(image, "FileDrop:" + Path.GetExtension(file).TrimStart('.').ToLowerInvariant(), attempt);
                    }
                }

                return Failure(
                    ClipboardImageStatus.NotAnImage,
                    "FileDrop",
                    "剪贴板里的文件不是支持的图片（PNG/BMP/JPG）。");
            }

            // 4) 判断到底是“空”还是“非图片”，好给出准确提示。
            var data = Clipboard.GetDataObject();
            var formats = data?.GetFormats(false) ?? Array.Empty<string>();
            if (formats.Length == 0)
            {
                return Failure(ClipboardImageStatus.Empty, "-", "剪贴板是空的，请先复制一张图片。");
            }

            return Failure(
                ClipboardImageStatus.NotAnImage,
                string.Join(",", formats.Take(4)),
                $"剪贴板里没有图片（当前格式：{string.Join("、", formats.Take(3))}）。");
        }
        catch (COMException ex)
        {
            AppLog.Warn("Clipboard", $"读取剪贴板失败（第 {attempt}/{DefaultAttempts} 次，剪贴板可能被占用）：{ex.Message}");
            return Failure(
                ClipboardImageStatus.Busy,
                "-",
                "剪贴板被其他程序占用，暂时读不到图片，请稍后重试。");
        }
        catch (Exception ex)
        {
            AppLog.Error("Clipboard", "读取剪贴板图片时发生未预期错误", ex);
            return Failure(ClipboardImageStatus.Failed, "-", $"读取剪贴板失败：{ex.Message}");
        }
    }

    private static ClipboardImageResult? TryReadNativeImage(int attempt)
    {
        var pngFormat = ClipboardNative.RegisterClipboardFormat("PNG");
        byte[]? png;
        byte[]? dibV5;
        byte[]? dib;
        if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
            return Failure(ClipboardImageStatus.Busy, "-", "剪贴板被其他程序占用，暂时读不到图片，请稍后重试。");
        try
        {
            png = ReadClipboardBytes(pngFormat);
            // Avoid asking Windows to synthesize DIBs when the original PNG is present.
            dibV5 = png is null ? ReadClipboardBytes(17) : null; // CF_DIBV5
            dib = png is null ? ReadClipboardBytes(8) : null; // CF_DIB
        }
        finally
        {
            ClipboardNative.CloseClipboard();
        }

        if (png is not null)
        {
            using var stream = new MemoryStream(png, writable: false);
            if (DecodeStream(stream) is { } image) return Success(image, "PNG", attempt);
            // A malformed PNG must not prevent a valid DIB from being read.
            if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
                return Failure(ClipboardImageStatus.Busy, "-", "剪贴板被其他程序占用，暂时读不到图片，请稍后重试。");
            try
            {
                dibV5 = ReadClipboardBytes(17);
                dib = ReadClipboardBytes(8);
            }
            finally { ClipboardNative.CloseClipboard(); }
        }
        if (dibV5 is not null && DecodeDib(dibV5) is { } v5Image)
            return Success(v5Image, "DIBV5", attempt);
        if (dib is not null && DecodeDib(dib) is { } dibImage)
            return Success(dibImage, "DIB", attempt);
        return null;
    }

    // The clipboard owns the handle. Copy before unlocking/closing; never free it.
    private static byte[]? ReadClipboardBytes(uint format)
    {
        if (format == 0 || !ClipboardNative.IsClipboardFormatAvailable(format)) return null;
        var handle = ClipboardNative.GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var length = checked((int)ClipboardNative.GlobalSize(handle));
        if (length == 0) return null;
        var pointer = ClipboardNative.GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }
        finally { ClipboardNative.GlobalUnlock(handle); }
    }

    private static BitmapSource? DecodeDib(byte[] dib)
    {
        try
        {
            if (dib.Length < 12) return null;
            var headerSize = checked((int)BitConverter.ToUInt32(dib, 0));
            int pixelOffset;
            if (headerSize == 12) // BITMAPCOREHEADER uses RGBTRIPLE palette entries.
            {
                var bits = BitConverter.ToUInt16(dib, 10);
                pixelOffset = checked(headerSize + (bits is > 0 and <= 8 ? (1 << bits) * 3 : 0));
            }
            else if (headerSize >= 40 && headerSize <= dib.Length)
            {
                var bits = BitConverter.ToUInt16(dib, 14);
                var compression = BitConverter.ToUInt32(dib, 16);
                var colors = BitConverter.ToUInt32(dib, 32);
                if (colors == 0 && bits is > 0 and <= 8) colors = 1u << bits;
                var masks = headerSize == 40 ? compression == 3 ? 12 : compression == 6 ? 16 : 0 : 0;
                pixelOffset = checked(headerSize + masks + checked((int)colors * 4));
            }
            else return null;
            if (pixelOffset >= dib.Length) return null;

            // Decode the packed DIB as a BMP. BI_RGB's reserved byte stays unused;
            // explicit V4/V5 alpha masks retain their real transparency.
            using var stream = new MemoryStream(checked(14 + dib.Length));
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write((ushort)0x4D42);
                writer.Write(checked(14 + dib.Length));
                writer.Write(0);
                writer.Write(checked(14 + pixelOffset));
                writer.Write(dib);
            }
            return DecodeStream(stream);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Clipboard", $"剪贴板 DIB 解码失败：{ex.Message}");
            return null;
        }
    }

    private static ClipboardImageResult Success(BitmapSource image, string format, int attempt)
    {
        var independent = ToIndependentBitmap(image);
        AppLog.Info(
            "Clipboard",
            $"剪贴板图片读取成功（第 {attempt} 次尝试）：格式={format} " +
            $"尺寸={independent.PixelWidth}x{independent.PixelHeight}");
        return new ClipboardImageResult(
            ClipboardImageStatus.Success,
            independent,
            format,
            $"剪贴板图片 {independent.PixelWidth}×{independent.PixelHeight}");
    }

    private static ClipboardImageResult Failure(ClipboardImageStatus status, string format, string message) =>
        new(status, null, format, message);

    /// <summary>把任意 <see cref="BitmapSource"/> 复制成独立、已冻结的 Pbgra32 位图。</summary>
    public static BitmapSource ToIndependentBitmap(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BitmapSource converted = source;
        if (source.Format != PixelFormats.Pbgra32)
        {
            var format = new FormatConvertedBitmap();
            format.BeginInit();
            format.Source = source;
            format.DestinationFormat = PixelFormats.Pbgra32;
            format.EndInit();
            converted = format;
        }

        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var copy = BitmapSource.Create(
            converted.PixelWidth, converted.PixelHeight, converted.DpiX, converted.DpiY,
            PixelFormats.Pbgra32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static BitmapSource? LoadFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // OnLoad：立刻把像素读进内存并释放文件句柄（用户可以随时删掉那个文件）。
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return ToIndependentBitmap(bitmap);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Clipboard", $"剪贴板图片文件无法加载：{path}：{ex.Message}");
            return null;
        }
    }

    private static BitmapSource? DecodeStream(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame.Freeze();
            return ToIndependentBitmap(frame);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Clipboard", $"剪贴板图像数据解码失败：{ex.Message}");
            return null;
        }
    }
}
