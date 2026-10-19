using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepShot.Core.Annotations;
using DeepShot.Core.Geometry;
using DeepShot.Core.Output;
using DeepShot.Output;

namespace DeepShot.Editor;

/// <summary>
/// T17 导出渲染：把**冻结图 + 全部标注**合成为最终图片，尺寸等于选区物理像素。
/// </summary>
/// <remarks>
/// <para>
/// 只画两样东西：选区内的冻结图像素 + 标注。遮罩压暗、选区白框、手柄、尺寸文字、工具栏
/// 都是遮罩窗口的装饰元素，从不参与本渲染，所以导出图里天然没有它们。
/// </para>
/// <para>
/// <b>DPI</b>：<see cref="RenderTargetBitmap"/> 的像素尺寸直接取选区物理像素，DPI 取目标屏幕
/// DPI，并在绘制上下文压入 <c>ScaleTransform(96/dpi)</c>——这样 1 个绘制单位正好等于
/// 1 个物理像素，输出宽高就是选区物理像素（125% 屏幕下不会变成 80% 的 DIP 尺寸）。
/// 这与预览层 <see cref="AnnotationLayer"/> 的做法完全一致，因此预览与导出像素级一致。
/// </para>
/// <para>
/// <b>马赛克</b>：真像素化，不是半透明方块示意。按文档 Z 序处理——遇到马赛克时先把
/// 之前累积的标注合成为位图，再对该区域的**当前像素**做块平均（<see cref="MosaicPixelator"/>），
/// 所以"马赛克盖在矩形上"的效果与预览一致，处理的是原图像素（以及位于其下的标注）。
/// </para>
/// <para>
/// 七种标注的绘制复用 <see cref="AnnotationRenderer"/>（与预览同一套代码路径），
/// 只有马赛克换成像素处理。
/// </para>
/// </remarks>
public static class AnnotationExportRenderer
{
    /// <summary>
    /// 合成导出图：选区内的冻结图 + 文档中的全部标注（按 Z 序）。
    /// </summary>
    /// <param name="source">冻结的整屏捕获图（物理像素）。</param>
    /// <param name="selection">屏幕绝对物理像素选区（等于输出尺寸）。</param>
    /// <param name="screenBounds">目标屏幕物理矩形（副屏可为负）。</param>
    /// <param name="dpi">目标屏幕 DPI（96/120/144…）。</param>
    /// <param name="document">标注文档。</param>
    /// <exception cref="InvalidOperationException">选区为空、与屏幕无交集或超出冻结图。</exception>
    public static BitmapSource Render(
        BitmapSource source,
        PixelRect selection,
        PixelRect screenBounds,
        int dpi,
        AnnotationDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);

        if (dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI 必须为正数。");
        }

        var imageRect = SelectionCrop.ToImageRect(selection, screenBounds);
        if (imageRect.IsEmpty)
        {
            throw new InvalidOperationException(
                $"选区 {selection} 与屏幕 {screenBounds} 没有交集，无法导出。");
        }

        if (!SelectionCrop.IsInsideImage(imageRect, source.PixelWidth, source.PixelHeight))
        {
            throw new InvalidOperationException(
                $"选区 {selection} 超出冻结图 {source.PixelWidth}x{source.PixelHeight}。");
        }

        var width = imageRect.Width;
        var height = imageRect.Height;

        // 基础像素：选区内的冻结图，转成 BGRA32 便于马赛克直接改像素。
        var baseImage = ToBgra32(CaptureCropper.Crop(source, selection, screenBounds));

        BitmapSource current = baseImage;
        var rasterized = false;
        var pending = new List<Annotation>();

        foreach (var annotation in document)
        {
            if (annotation is MosaicAnnotation mosaic)
            {
                if (pending.Count > 0)
                {
                    current = Composite(current, pending, imageRect, dpi);
                    rasterized = true;
                    pending.Clear();
                }

                current = Pixelate(current, mosaic);
                rasterized = true;
                continue;
            }

            pending.Add(annotation);
        }

        if (pending.Count > 0 || !rasterized)
        {
            // 没有标注、或最后一批标注还没画：统一合成一次（也把结果变成独立位图，
            // 不持有整屏冻结图的引用）。
            current = Composite(current, pending, imageRect, dpi);
        }

        // 兜底：尺寸必须等于选区物理像素（这是 T17 的验收点）。
        if (current.PixelWidth != width || current.PixelHeight != height)
        {
            throw new InvalidOperationException(
                $"导出尺寸 {current.PixelWidth}x{current.PixelHeight} 与选区物理像素 {width}x{height} 不一致。");
        }

        if (current.CanFreeze)
        {
            current.Freeze();
        }

        return current;
    }

    /// <summary>
    /// 把基础图与一批标注合成为新位图；绘制单位 = 物理像素（压入 96/dpi 缩放）。
    /// </summary>
    internal static BitmapSource Composite(
        BitmapSource background,
        IReadOnlyList<Annotation> annotations,
        PixelRect imageRect,
        int dpi)
    {
        var width = imageRect.Width;
        var height = imageRect.Height;
        var dipPerPixel = 96d / dpi;
        var pixelsPerDip = 1d / dipPerPixel;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(dipPerPixel, dipPerPixel));
            try
            {
                // 基础图按 1:1 像素绘制（裁剪图左上角 = 输出图左上角）。
                context.DrawImage(background, new Rect(0, 0, width, height));

                // 标注坐标是"相对选区左上角"的物理像素（T14 的 AnnotationCanvasTransform），
                // 而裁剪图左上角就是选区左上角，所以这里不需要再平移。
                foreach (var annotation in annotations)
                {
                    AnnotationRenderer.Draw(context, annotation, pixelsPerDip);
                }
            }
            finally
            {
                context.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>对当前位图的马赛克区域做真实像素化（块平均 + 强度混合），返回新位图。</summary>
    internal static BitmapSource Pixelate(BitmapSource source, MosaicAnnotation mosaic)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * MosaicPixelator.BytesPerPixel;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        // 标注区域本身就是相对选区左上角的坐标（= 输出图坐标），越界部分由算法裁掉。
        MosaicPixelator.Apply(
            pixels.AsSpan(),
            width,
            height,
            mosaic.Region,
            mosaic.BlockSize,
            mosaic.Strength);

        var result = BitmapSource.Create(
            width,
            height,
            source.DpiX,
            source.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static BitmapSource ToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();
        return converted;
    }
}
