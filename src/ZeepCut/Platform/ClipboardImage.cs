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
/// <item><b>Bitmap / DIB</b>：<see cref="Clipboard.GetImage"/>（WPF 内部按 DIB 解析，
/// 截屏工具、画图、浏览器“复制图片”都走这条）；</item>
/// <item><b>FileDrop</b>：在资源管理器里复制 PNG/BMP/JPG 文件，读第一个图片文件；</item>
/// <item><b>PNG</b>：部分应用（浏览器、新版画图）直接放 PNG 流。</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>剪贴板被占用</b>是常态（另一个进程正 OpenClipboard），因此每次读取都重试若干次；
/// 全部失败时返回 <see cref="ClipboardImageStatus.Busy"/> 而不是抛异常，调用方据此提示用户。
/// </para>
/// <para>
/// 读到的位图一律复制成独立的 <see cref="WriteableBitmap"/> 并冻结：剪贴板的 DIB 内存
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
            // 1) Bitmap / DIB：最常见的“复制图片”。
            if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image is not null)
                {
                    return Success(image, "Bitmap/DIB", attempt);
                }
            }

            // 2) PNG 流（浏览器、Windows 11 截图工具的“复制”常走这条）。
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream pngStream)
            {
                var image = DecodeStream(pngStream);
                if (image is not null)
                {
                    return Success(image, "PNG", attempt);
                }

                return Failure(ClipboardImageStatus.Failed, "PNG", "剪贴板里的 PNG 图片无法解码。");
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

        var copy = new WriteableBitmap(converted);
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
            AppLog.Warn("Clipboard", $"剪贴板 PNG 流解码失败：{ex.Message}");
            return null;
        }
    }
}
