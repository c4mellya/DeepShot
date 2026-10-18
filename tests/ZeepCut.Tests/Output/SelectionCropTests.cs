using Xunit;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Output;

namespace ZeepCut.Tests.Output;

public class SelectionCropTests
{
    // 本机真实屏幕（物理像素）：主屏 2560x1440 @ (0,0)，副屏 1080x1920 @ (-1080,0)。
    private static readonly PixelRect Primary = new(0, 0, 2560, 1440);
    private static readonly PixelRect Secondary = new(-1080, 0, 1080, 1920);

    [Fact]
    public void ToImageRect_PrimaryScreenAtOrigin_KeepsCoordinates()
    {
        var selection = new PixelRect(300, 300, 640, 480);

        var result = SelectionCrop.ToImageRect(selection, Primary);

        Assert.Equal(new PixelRect(300, 300, 640, 480), result);
    }

    [Fact]
    public void ToImageRect_PrimaryScreen_DoesNotScaleFor125PercentDpi()
    {
        // 125% 只影响 WPF DIP；选区与冻结图都是物理像素，裁剪矩形不能被 1.25 缩放。
        var selection = new PixelRect(100, 50, 200, 100);

        var result = SelectionCrop.ToImageRect(selection, Primary);

        Assert.Equal(new PixelRect(100, 50, 200, 100), result);
    }

    [Fact]
    public void ToImageRect_SecondaryNegativeOrigin_SubtractsScreenOrigin()
    {
        // 副屏物理原点 (-1080,0)：选区内 x=-980 对应冻结图内的 x=100。
        var selection = new PixelRect(-980, 300, 640, 480);

        var result = SelectionCrop.ToImageRect(selection, Secondary);

        Assert.Equal(new PixelRect(100, 300, 640, 480), result);
    }

    [Fact]
    public void ToImageRect_SecondarySelectionAtScreenLeftEdge_StartsAtZero()
    {
        var selection = new PixelRect(-1080, 0, 100, 100);

        var result = SelectionCrop.ToImageRect(selection, Secondary);

        Assert.Equal(new PixelRect(0, 0, 100, 100), result);
    }

    [Fact]
    public void ToImageRect_SecondarySelectionAtRightBottomEdge_FitsImage()
    {
        // 选区块贴着副屏右下角：图像内矩形右/下边界正好是 1080/1920。
        var selection = new PixelRect(-200, 1400, 200, 520);

        var result = SelectionCrop.ToImageRect(selection, Secondary);

        Assert.Equal(new PixelRect(880, 1400, 200, 520), result);
        Assert.True(SelectionCrop.IsInsideImage(result, 1080, 1920));
    }

    [Fact]
    public void ToImageRect_PrimarySelectionAtRightBottomEdge_FitsImage()
    {
        var selection = new PixelRect(2200, 1200, 360, 240);

        var result = SelectionCrop.ToImageRect(selection, Primary);

        Assert.Equal(new PixelRect(2200, 1200, 360, 240), result);
        Assert.Equal(2560, result.Right);
        Assert.Equal(1440, result.Bottom);
        Assert.True(SelectionCrop.IsInsideImage(result, 2560, 1440));
    }

    [Fact]
    public void ToImageRect_FullScreenSelection_EqualsWholeImage()
    {
        var result = SelectionCrop.ToImageRect(Secondary, Secondary);

        Assert.Equal(new PixelRect(0, 0, 1080, 1920), result);
    }

    [Fact]
    public void ToImageRect_SelectionPartiallyOutsideScreen_ClipsToVisiblePart()
    {
        // 左/上越界 100/50，只保留屏幕内 200x150 的部分。
        var selection = new PixelRect(-100, -50, 300, 200);

        var result = SelectionCrop.ToImageRect(selection, Primary);

        Assert.Equal(new PixelRect(0, 0, 200, 150), result);
    }

    [Fact]
    public void ToImageRect_SelectionCompletelyOutsideScreen_ReturnsEmpty()
    {
        var selection = new PixelRect(3000, 2000, 200, 200);

        var result = SelectionCrop.ToImageRect(selection, Primary);

        Assert.True(result.IsEmpty);
        Assert.Equal(PixelRect.Empty, result);
    }

    [Fact]
    public void ToImageRect_SelectionOnlyTouchesScreenBorder_ReturnsEmpty()
    {
        // 半开区间：紧贴右边界但没有共享像素时不算相交。
        var selection = new PixelRect(2560, 0, 100, 100);

        Assert.True(SelectionCrop.ToImageRect(selection, Primary).IsEmpty);
    }

    [Fact]
    public void ToImageRect_EmptySelectionOrScreen_ReturnsEmpty()
    {
        Assert.Equal(PixelRect.Empty, SelectionCrop.ToImageRect(PixelRect.Empty, Primary));
        Assert.Equal(PixelRect.Empty, SelectionCrop.ToImageRect(new PixelRect(10, 10, 20, 20), PixelRect.Empty));
    }

    [Theory]
    [InlineData(640, 480, 0, 0, 640, 480, true)]
    [InlineData(640, 480, 0, 0, 641, 480, false)]
    [InlineData(640, 480, 0, 0, 640, 481, false)]
    [InlineData(0, 0, 0, 0, 640, 480, false)]
    [InlineData(640, 480, -1, 0, 640, 480, false)]
    [InlineData(640, 480, 0, 0, 0, 0, false)]
    public void IsInsideImage_ChecksHalfOpenBounds(
        int imageWidth,
        int imageHeight,
        int left,
        int top,
        int right,
        int bottom,
        bool expected)
    {
        var imageRect = new PixelRect(left, top, right - left, bottom - top);

        Assert.Equal(expected, SelectionCrop.IsInsideImage(imageRect, imageWidth, imageHeight));
    }
}
