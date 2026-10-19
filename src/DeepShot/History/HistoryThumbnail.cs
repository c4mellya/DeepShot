using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepShot.History;

/// <summary>
/// 缩略图生成与 PNG 编码（T23，App 层：WPF 有图像编解码，Core 只收字节）。
/// </summary>
/// <remarks>
/// <para>
/// 缩略图最大边 <see cref="MaxEdge"/> = 640 物理像素（图库卡片 240×144 DIP，
/// 200% 缩放的屏上也不需要放大缩略图）；小图不放大。缩放用 <see cref="TransformedBitmap"/> +
/// <see cref="BitmapScalingMode.HighQuality"/>，比 <c>RenderTargetBitmap</c> 便宜且不丢 DPI。
/// </para>
/// <para>
/// 所有结果都是"已经编码好的字节"，编码器写入 <see cref="MemoryStream"/> 后立刻拿到
/// <c>byte[]</c>：**不返回流、不返回 BitmapImage**，所以图片文件与缩略图文件都不会
/// 被本类持有句柄（PLAN：读图注意释放句柄）。
/// </para>
/// </remarks>
public static class HistoryThumbnail
{
    /// <summary>缩略图最大边（物理像素）。</summary>
    public const int MaxEdge = 640;

    /// <summary>
    /// 把图片编码成 PNG 字节（历史内部一律存 PNG）。
    /// </summary>
    /// <param name="image">已冻结的位图（截图输出图/贴图内容）。</param>
    public static byte[] EncodePng(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// 生成缩略图的 PNG 字节：最长边缩到 <see cref="MaxEdge"/>，小图原样返回。
    /// </summary>
    /// <param name="image">已冻结的位图。</param>
    /// <param name="maxEdge">最大边（1–4096，默认 640）。</param>
    public static byte[] CreatePngBytes(BitmapSource image, int maxEdge = MaxEdge)
    {
        ArgumentNullException.ThrowIfNull(image);

        var edge = Math.Clamp(maxEdge, 1, 4096);
        return EncodePng(Scale(image, edge));
    }

    /// <summary>按最长边缩放（不放大）；需要时返回原图。</summary>
    public static BitmapSource Scale(BitmapSource image, int maxEdge = MaxEdge)
    {
        ArgumentNullException.ThrowIfNull(image);

        var edge = Math.Clamp(maxEdge, 1, 4096);
        var longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest <= edge)
        {
            return image;
        }

        // 等比缩放：宽高都用同一个系数，避免缩略图变形。
        // TransformedBitmap 不理会缩放质量设置（默认双线性，缩小很多时会糊/锯齿）；
        // 这里用 HighQuality(Fant) 绘制到精确像素尺寸的位图。
        var factor = (double)edge / longest;
        var width = Math.Max(1, (int)Math.Round(image.PixelWidth * factor));
        var height = Math.Max(1, (int)Math.Round(image.PixelHeight * factor));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new System.Windows.Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
