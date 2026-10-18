using System.Windows;
using System.Windows.Media.Imaging;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Output;

namespace ZeepCut.Output;

/// <summary>
/// 从冻结捕获图里按选区裁剪出输出图。选区是屏幕绝对物理像素，
/// 裁剪矩形由 Core 的 <see cref="SelectionCrop"/> 换算（减去目标屏幕原点）。
/// </summary>
public static class CaptureCropper
{
    /// <summary>
    /// 裁剪并冻结输出图。选区为空、与屏幕无交集或裁剪矩形超出冻结图时抛
    /// <see cref="InvalidOperationException"/>，由调用方提示并留在截图状态。
    /// </summary>
    /// <param name="source">冻结的捕获图（物理像素）。</param>
    /// <param name="selection">屏幕绝对物理像素选区。</param>
    /// <param name="screenBounds">目标屏幕的物理矩形，副屏可为负坐标。</param>
    public static BitmapSource Crop(BitmapSource source, PixelRect selection, PixelRect screenBounds)
    {
        ArgumentNullException.ThrowIfNull(source);

        var imageRect = SelectionCrop.ToImageRect(selection, screenBounds);
        if (imageRect.IsEmpty)
        {
            throw new InvalidOperationException(
                $"选区 {selection} 与屏幕 {screenBounds} 没有交集，无法裁剪。");
        }

        if (!SelectionCrop.IsInsideImage(imageRect, source.PixelWidth, source.PixelHeight))
        {
            throw new InvalidOperationException(
                $"裁剪区域 {imageRect} 超出冻结图 {source.PixelWidth}x{source.PixelHeight}。");
        }

        // CroppedBitmap 直接引用源图的像素区域，源图已冻结，因此结果也可冻结并跨线程使用。
        var cropped = new CroppedBitmap(
            source,
            new Int32Rect(imageRect.Left, imageRect.Top, imageRect.Width, imageRect.Height));
        cropped.Freeze();
        return cropped;
    }
}
