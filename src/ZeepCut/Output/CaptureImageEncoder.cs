using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Output;

namespace ZeepCut.Output;

/// <summary>
/// 把输出图保存为 PNG 或 JPEG。
/// </summary>
/// <remarks>
/// PNG 保留 alpha（截图本身 alpha 全为 255，但将来标注/圆角可能产生透明像素）。
/// JPEG 不支持透明，编码前显式转换为 <see cref="PixelFormats.Bgr24"/>（去掉 alpha），
/// 并使用可配置的质量等级（默认 90）。
/// </remarks>
public static class CaptureImageEncoder
{
    /// <summary>JPEG 默认编码质量（1–100）。</summary>
    public const int DefaultJpegQuality = 90;

    /// <summary>把图片按指定格式写文件；失败时抛出 IO/编码异常，由调用方提示并留在截图状态。</summary>
    /// <param name="image">已冻结的输出图。</param>
    /// <param name="path">目标文件完整路径。</param>
    /// <param name="format">PNG 或 JPEG。</param>
    /// <param name="jpegQuality">JPEG 质量（1–100），PNG 忽略。</param>
    public static void Save(
        BitmapSource image,
        string path,
        CaptureImageFormat format,
        int jpegQuality = DefaultJpegQuality)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("保存路径不能为空。", nameof(path));
        }

        var encoder = CreateEncoder(image, format, jpegQuality);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    /// <summary>创建对应格式的编码器（供保存与测试复用）。</summary>
    public static BitmapEncoder CreateEncoder(
        BitmapSource image,
        CaptureImageFormat format,
        int jpegQuality = DefaultJpegQuality)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (format == CaptureImageFormat.Jpeg)
        {
            var jpeg = new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(jpegQuality, 1, 100),
            };
            jpeg.Frames.Add(BitmapFrame.Create(RemoveAlpha(image)));
            return jpeg;
        }

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        return png;
    }

    /// <summary>JPEG 不支持透明：转换为 24 位 BGR（丢弃 alpha，截图下视觉不变）。</summary>
    private static BitmapSource RemoveAlpha(BitmapSource image)
    {
        if (image.Format == PixelFormats.Bgr24)
        {
            return image;
        }

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = image;
        converted.DestinationFormat = PixelFormats.Bgr24;
        converted.EndInit();
        converted.Freeze();
        return converted;
    }
}
