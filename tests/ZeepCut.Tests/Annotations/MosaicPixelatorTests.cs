using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class MosaicPixelatorTests
{
    /// <summary>造一张 8x8 的 BGRA 图：每个像素的灰度按 (x + y*8) 递增，便于判断块平均。</summary>
    private static byte[] CreateGradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var value = (byte)((x + (y * width)) % 251);
                pixels[offset] = value;
                pixels[offset + 1] = (byte)(255 - value);
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] CreateCheckerboard(int width, int height, int cell)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var white = ((x / cell) + (y / cell)) % 2 == 0;
                var value = white ? (byte)255 : (byte)0;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static (byte B, byte G, byte R, byte A) ReadPixel(byte[] pixels, int width, int x, int y)
    {
        var offset = ((y * width) + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    [Fact]
    public void Apply_MakesEachBlockUniform()
    {
        // 4x4 棋盘 + 块大小 4 = 整个 4x4 区域被平均成一个颜色。
        var pixels = CreateCheckerboard(4, 4, 1);
        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(0, 0, 4, 4), blockSize: 4, strength: 1d);

        var first = ReadPixel(pixels, 4, 0, 0);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(first, ReadPixel(pixels, 4, x, y));
            }
        }

        // 黑白各一半 → 128 或 127（四舍五入）。
        Assert.InRange(first.B, 127, 128);
        Assert.Equal(255, first.A);
    }

    [Fact]
    public void Apply_UsesBlockOriginAtRegionTopLeft()
    {
        // 8x8 棋盘（2px 格）+ 块大小 4、区域左移 2：块网格从区域左上角开始，
        // 所以 (2,0) 与 (5,3) 同一块、(6,0) 是下一块。
        var pixels = CreateCheckerboard(8, 8, 2);
        MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(2, 0, 4, 4), blockSize: 4, strength: 1d);

        // 区域 (2,0)-(5,3) 内棋盘 2px 格：块内有两种颜色各 8 像素 → 平均 127/128。
        var inside = ReadPixel(pixels, 8, 2, 0);
        Assert.Equal(inside, ReadPixel(pixels, 8, 5, 3));
        Assert.InRange(inside.B, 127, 128);

        // x=6 是区域外，保持原棋盘（(6,0) 属于格子 (6/2=3, 0) → (3+0)%2=1 → 黑）。
        Assert.Equal(0, ReadPixel(pixels, 8, 6, 0).B);
    }

    [Fact]
    public void Apply_SinglePixelRegion_KeepsPixelValue()
    {
        var pixels = CreateGradient(4, 4);
        var before = (byte[])pixels.Clone();

        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(2, 2, 1, 1), blockSize: 4, strength: 1d);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void Apply_RegionOutsideImage_IsNoOp()
    {
        var pixels = CreateGradient(4, 4);
        var before = (byte[])pixels.Clone();

        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(100, 100, 10, 10), blockSize: 4, strength: 1d);
        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(-40, -40, 10, 10), blockSize: 4, strength: 1d);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void Apply_PartiallyOutsideRegion_OnlyTouchesIntersection()
    {
        var pixels = CreateGradient(8, 8);
        var before = (byte[])pixels.Clone();

        // 区域从 (-2,-2) 到 (2,2)：只有 (0,0)-(1,1) 在图像内。
        MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(-2, -2, 4, 4), blockSize: 4, strength: 1d);

        var changed = ReadPixel(pixels, 8, 0, 0);
        var unchanged = ReadPixel(pixels, 8, 3, 3);
        Assert.NotEqual(ReadPixel(before, 8, 0, 0), changed);
        Assert.Equal(ReadPixel(before, 8, 3, 3), unchanged);
        // 图像内受影响的 (0,0)-(1,1) 变成同一个平均值。
        Assert.Equal(changed, ReadPixel(pixels, 8, 1, 1));
    }

    [Fact]
    public void Apply_ClampsBlockSizeToMinimum()
    {
        // blockSize 1 会被钳制到 4：4x4 区域整体被平均。
        var pixels = CreateCheckerboard(4, 4, 1);
        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(0, 0, 4, 4), blockSize: 1, strength: 1d);

        var first = ReadPixel(pixels, 4, 0, 0);
        Assert.Equal(first, ReadPixel(pixels, 4, 3, 3));
        Assert.InRange(first.B, 127, 128);
    }

    [Fact]
    public void Apply_ClampsBlockSizeToMaximum()
    {
        // 100x2 渐变 + blockSize 999 → 钳制到 64：x=0..63 一块、x=64..99 另一块，
        // 两块的平均色不同（渐变），证明没有被当成"整图一块"。
        var pixels = CreateGradient(100, 2);
        MosaicPixelator.Apply(pixels, 100, 2, new PixelRect(0, 0, 100, 2), blockSize: 999, strength: 1d);

        var firstBlock = ReadPixel(pixels, 100, 0, 0);
        Assert.Equal(firstBlock, ReadPixel(pixels, 100, 63, 1));
        Assert.NotEqual(firstBlock, ReadPixel(pixels, 100, 64, 0));
        Assert.Equal(ReadPixel(pixels, 100, 64, 0), ReadPixel(pixels, 100, 99, 1));
    }

    [Fact]
    public void Apply_DifferentBlockSizes_ProduceDifferentPixels()
    {
        var source = CreateGradient(16, 16);

        var small = (byte[])source.Clone();
        MosaicPixelator.Apply(small, 16, 16, new PixelRect(0, 0, 16, 16), blockSize: 4, strength: 1d);

        var large = (byte[])source.Clone();
        MosaicPixelator.Apply(large, 16, 16, new PixelRect(0, 0, 16, 16), blockSize: 16, strength: 1d);

        Assert.NotEqual(small, large);

        // 块大小 4 时 (0,0) 与 (5,5) 属于不同块、平均色不同；
        // 块大小 16 时整区一块，(0,0) 与 (5,5) 必须相同。
        Assert.NotEqual(ReadPixel(small, 16, 0, 0), ReadPixel(small, 16, 5, 5));
        Assert.Equal(ReadPixel(large, 16, 0, 0), ReadPixel(large, 16, 5, 5));
    }

    [Fact]
    public void Apply_StrengthZero_LeavesPixelsUnchanged()
    {
        var pixels = CreateGradient(8, 8);
        var before = (byte[])pixels.Clone();

        MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(0, 0, 8, 8), blockSize: 8, strength: 0d);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void Apply_NanStrength_FallsBackToDefault()
    {
        var pixels = CreateGradient(8, 8);
        var before = (byte[])pixels.Clone();

        MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(0, 0, 8, 8), blockSize: 8, strength: double.NaN);

        // NaN 回退到默认强度（1.0 = 完全像素化）→ 结果变化且不等于原图。
        Assert.NotEqual(before, pixels);
    }

    [Fact]
    public void Apply_HalfStrength_BlendsHalfway()
    {
        // 4x2 图：左列全黑、右列全白；块大小 4 → 一块平均 128（约）。
        var pixels = new byte[4 * 2 * 4];
        for (var y = 0; y < 2; y++)
        {
            for (var x = 2; x < 4; x++)
            {
                var offset = ((y * 4) + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 1] = 255;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = 255;
            }
        }

        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        MosaicPixelator.Apply(pixels, 4, 2, new PixelRect(0, 0, 4, 2), blockSize: 4, strength: 0.5d);

        // 原黑像素 0 → 0*0.5 + 127.5*0.5 ≈ 64；原白像素 255 → 255*0.5 + 127.5*0.5 ≈ 191。
        Assert.InRange(ReadPixel(pixels, 4, 0, 0).B, 63, 64);
        Assert.InRange(ReadPixel(pixels, 4, 3, 1).B, 191, 192);
    }

    [Fact]
    public void Apply_DoesNotChangeAlpha()
    {
        var pixels = CreateGradient(8, 8);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 128;
        }

        MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(0, 0, 8, 8), blockSize: 8, strength: 1d);

        for (var i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(128, pixels[i]);
        }
    }

    [Fact]
    public void Apply_ThrowsWhenBufferTooSmall()
    {
        var pixels = new byte[4 * 4 * 4];
        Assert.Throws<ArgumentException>(() =>
            MosaicPixelator.Apply(pixels, 8, 8, new PixelRect(0, 0, 8, 8), 4, 1d));
    }

    [Fact]
    public void Apply_RejectsNonPositiveImageSize()
    {
        var pixels = new byte[16];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MosaicPixelator.Apply(pixels, 0, 4, new PixelRect(0, 0, 1, 1), 4, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MosaicPixelator.Apply(pixels, 4, -1, new PixelRect(0, 0, 1, 1), 4, 1d));
    }

    [Fact]
    public void Apply_EmptyRegion_IsNoOp()
    {
        var pixels = CreateGradient(4, 4);
        var before = (byte[])pixels.Clone();

        MosaicPixelator.Apply(pixels, 4, 4, PixelRect.Empty, 4, 1d);
        MosaicPixelator.Apply(pixels, 4, 4, new PixelRect(1, 1, 0, 3), 4, 1d);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void ApplyCopy_LeavesInputUntouched()
    {
        var source = CreateCheckerboard(8, 8, 1);
        var before = (byte[])source.Clone();

        var result = MosaicPixelator.ApplyCopy(source, 8, 8, new PixelRect(0, 0, 8, 8), 8, 1d);

        Assert.Equal(before, source);
        Assert.NotEqual(source, result);
        Assert.Equal(result[0], result[(8 * 7) * 4]);
    }
}
