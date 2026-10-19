using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>
/// 马赛克的**真实像素化**算法：把某个区域按块大小做块平均（采样），可再按强度与原像素混合。
/// </summary>
/// <remarks>
/// <para>
/// 放在 Core 是为了能单测（块大小边界、区域越界、单像素区域、不同块大小的结果差异），
/// 不引用 WPF/System.Drawing：输入输出都是 BGRA 字节数组（每像素 4 字节，行紧密排列，
/// 即 stride = width × 4），与 <c>PixelFormats.Bgra32</c> / <c>Pbgra32</c> 的内存布局一致。
/// </para>
/// <para>
/// 块网格以**区域左上角**为原点（不是图像原点），这样同一块马赛克移动后视觉效果稳定，
/// 也不会因为选区位置不同而产生半个块。区域边缘可能是不完整的块，此时只对落在图像内、
/// 落在区域内的像素求平均，避免边缘块被图像外的黑色污染。
/// </para>
/// <para>
/// 强度 0 表示不处理（原图），1 表示完全用块平均色替换；中间值按
/// <c>result = original × (1 - strength) + average × strength</c> 混合（逐通道四舍五入）。
/// 块大小按 <see cref="AnnotationDefaults.ClampMosaicBlockSize"/> 钳制到 4–64，
/// 强度按 <see cref="AnnotationDefaults.ClampMosaicStrength"/> 钳制（NaN 回退默认值）。
/// alpha 通道保持不变（截图 alpha 恒为 255，但保留透明度语义）。
/// </para>
/// </remarks>
public static class MosaicPixelator
{
    /// <summary>每像素字节数（BGRA）。</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// 对 <paramref name="pixels"/> 中落在 <paramref name="region"/> 内的像素做块平均马赛克，原地修改。
    /// </summary>
    /// <param name="pixels">BGRA 像素（行紧密排列，长度至少为 width × height × 4）。</param>
    /// <param name="pixelWidth">图像宽度（像素）。</param>
    /// <param name="pixelHeight">图像高度（像素）。</param>
    /// <param name="region">马赛克区域（图像内像素坐标，可越界，只处理与图像相交的部分）。</param>
    /// <param name="blockSize">块大小（像素），钳制到 4–64。</param>
    /// <param name="strength">强度 0–1，钳制；NaN 回退默认值；0 表示不处理。</param>
    /// <exception cref="ArgumentException">像素数组长度不足或图像尺寸非法。</exception>
    public static void Apply(
        Span<byte> pixels,
        int pixelWidth,
        int pixelHeight,
        PixelRect region,
        int blockSize,
        double strength)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), pixelWidth, "图像宽度必须为正数。");
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), pixelHeight, "图像高度必须为正数。");
        }

        var required = (long)pixelWidth * pixelHeight * BytesPerPixel;
        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"像素数组长度 {pixels.Length} 小于 {pixelWidth}x{pixelHeight}x{BytesPerPixel} = {required}。",
                nameof(pixels));
        }

        var amount = AnnotationDefaults.ClampMosaicStrength(strength);
        if (amount <= 0d)
        {
            return;
        }

        var visible = region.Intersect(new PixelRect(0, 0, pixelWidth, pixelHeight));
        if (visible.IsEmpty)
        {
            return;
        }

        var block = AnnotationDefaults.ClampMosaicBlockSize(blockSize);

        // 块索引以区域左上角为原点；区域可能部分在图像外（负坐标），此时从裁剪后的第一块开始。
        var firstColumn = FloorDiv(visible.Left - region.Left, block);
        var firstRow = FloorDiv(visible.Top - region.Top, block);

        for (var row = firstRow; ; row++)
        {
            var blockTop = region.Top + (row * block);
            if (blockTop >= visible.Bottom)
            {
                break;
            }

            var top = Math.Max(blockTop, visible.Top);
            var bottom = Math.Min(blockTop + block, visible.Bottom);

            for (var column = firstColumn; ; column++)
            {
                var blockLeft = region.Left + (column * block);
                if (blockLeft >= visible.Right)
                {
                    break;
                }

                var left = Math.Max(blockLeft, visible.Left);
                var right = Math.Min(blockLeft + block, visible.Right);
                BlendBlock(pixels, pixelWidth, left, top, right, bottom, amount);
            }
        }
    }

    /// <summary>对 <paramref name="pixels"/> 做块平均马赛克（返回新的数组，便于测试与函数式调用）。</summary>
    public static byte[] ApplyCopy(
        byte[] pixels,
        int pixelWidth,
        int pixelHeight,
        PixelRect region,
        int blockSize,
        double strength)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var copy = (byte[])pixels.Clone();
        Apply(copy.AsSpan(), pixelWidth, pixelHeight, region, blockSize, strength);
        return copy;
    }

    private static void BlendBlock(
        Span<byte> pixels,
        int pixelWidth,
        int left,
        int top,
        int right,
        int bottom,
        double amount)
    {
        var count = (long)(right - left) * (bottom - top);
        if (count <= 0)
        {
            return;
        }

        long sumB = 0;
        long sumG = 0;
        long sumR = 0;

        for (var y = top; y < bottom; y++)
        {
            var rowStart = ((y * pixelWidth) + left) * BytesPerPixel;
            for (var x = 0; x < right - left; x++)
            {
                var offset = rowStart + (x * BytesPerPixel);
                sumB += pixels[offset];
                sumG += pixels[offset + 1];
                sumR += pixels[offset + 2];
            }
        }

        var averageB = (double)sumB / count;
        var averageG = (double)sumG / count;
        var averageR = (double)sumR / count;

        for (var y = top; y < bottom; y++)
        {
            var rowStart = ((y * pixelWidth) + left) * BytesPerPixel;
            for (var x = 0; x < right - left; x++)
            {
                var offset = rowStart + (x * BytesPerPixel);
                pixels[offset] = Blend(pixels[offset], averageB, amount);
                pixels[offset + 1] = Blend(pixels[offset + 1], averageG, amount);
                pixels[offset + 2] = Blend(pixels[offset + 2], averageR, amount);
            }
        }
    }

    private static byte Blend(byte original, double average, double amount)
    {
        if (amount >= 1d)
        {
            return ToByte(average);
        }

        return ToByte((original * (1d - amount)) + (average * amount));
    }

    private static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>向下取整的整数除法（用于把越界区域映射到大于等于 0 的块索引）。</summary>
    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        if (value < 0 && (value % divisor) != 0)
        {
            quotient--;
        }

        return quotient;
    }
}
