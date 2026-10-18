using Xunit;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Geometry;

public class PixelRectTests
{
    [Fact]
    public void RightAndBottom_AreDerivedFromBounds()
    {
        var rect = new PixelRect(10, 20, 100, 50);

        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.False(rect.IsEmpty);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(-10, 10)]
    public void IsEmpty_WhenWidthOrHeightNotPositive(int width, int height)
    {
        Assert.True(new PixelRect(0, 0, width, height).IsEmpty);
    }

    [Theory]
    [InlineData(10, 20, 2, 5, 2, 5, 8, 15)]   // 左上方向
    [InlineData(2, 20, 10, 5, 2, 5, 8, 15)]   // 右上方向
    [InlineData(2, 5, 10, 20, 2, 5, 8, 15)]   // 右下方向
    [InlineData(10, 5, 2, 20, 2, 5, 8, 15)]   // 左下方向
    [InlineData(-5, -5, 10, 10, -5, -5, 15, 15)]
    public void FromTwoPoints_AnyDirection_ProducesPositiveRect(
        int x1, int y1, int x2, int y2,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var rect = PixelRect.FromTwoPoints(x1, y1, x2, y2);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight), rect);
    }

    [Fact]
    public void FromTwoPoints_SamePoint_IsEmpty()
    {
        var rect = PixelRect.FromTwoPoints(7, 7, 7, 7);

        Assert.True(rect.IsEmpty);
        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }

    [Theory]
    [InlineData(0, 0, true)]     // Left/Top 包含
    [InlineData(99, 49, true)]   // Right-1/Bottom-1 包含
    [InlineData(100, 0, false)]  // Right 不包含
    [InlineData(0, 50, false)]   // Bottom 不包含
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    public void Contains_Point_UsesHalfOpenBounds(int x, int y, bool expected)
    {
        var rect = new PixelRect(0, 0, 100, 50);

        Assert.Equal(expected, rect.Contains(x, y));
    }

    [Fact]
    public void Contains_Point_EmptyRectContainsNothing()
    {
        Assert.False(PixelRect.Empty.Contains(0, 0));
    }

    [Fact]
    public void Contains_Rect_OnlyWhenFullyInside()
    {
        var outer = new PixelRect(0, 0, 100, 100);

        Assert.True(outer.Contains(new PixelRect(10, 10, 20, 20)));
        Assert.True(outer.Contains(outer));
        Assert.False(outer.Contains(new PixelRect(90, 90, 20, 20)));
        Assert.False(outer.Contains(PixelRect.Empty));
    }

    [Fact]
    public void Intersect_ReturnsOverlap()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);

        Assert.Equal(new PixelRect(50, 50, 50, 50), a.Intersect(b));
    }

    [Fact]
    public void Intersect_WhenOneContainsOther_ReturnsSmaller()
    {
        var outer = new PixelRect(0, 0, 100, 100);
        var inner = new PixelRect(10, 10, 20, 20);

        Assert.Equal(inner, outer.Intersect(inner));
        Assert.Equal(inner, inner.Intersect(outer));
    }

    [Fact]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        var a = new PixelRect(0, 0, 50, 50);
        var b = new PixelRect(100, 100, 50, 50);

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void Intersect_EdgeTouching_ReturnsEmpty()
    {
        // 半开区间：a 的 Right=50 不属于 a，b 的 Left=50 属于 b，没有共享像素。
        var a = new PixelRect(0, 0, 50, 50);
        var b = new PixelRect(50, 0, 50, 50);

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void Clamp_RectAlreadyInside_IsUnchanged()
    {
        var target = new PixelRect(0, 0, 200, 150);

        Assert.Equal(new PixelRect(10, 10, 50, 50), new PixelRect(10, 10, 50, 50).Clamp(target));
    }

    [Fact]
    public void Clamp_PartiallyOverlapping_SlidesFullyInside()
    {
        // 选区右下越界：宽高保持 100x100，位置被拉回目标内部。
        var target = new PixelRect(0, 0, 200, 150);
        var selection = new PixelRect(150, 100, 100, 100);

        Assert.Equal(new PixelRect(100, 50, 100, 100), selection.Clamp(target));
    }

    [Fact]
    public void Clamp_CompletelyOutside_SlidesToNearestEdge()
    {
        var target = new PixelRect(0, 0, 200, 150);
        var beyondRightBottom = new PixelRect(500, 300, 40, 20);
        var beyondLeftTop = new PixelRect(-500, -500, 40, 20);

        Assert.Equal(new PixelRect(160, 130, 40, 20), beyondRightBottom.Clamp(target));
        Assert.Equal(new PixelRect(0, 0, 40, 20), beyondLeftTop.Clamp(target));
    }

    [Fact]
    public void Clamp_LargerThanTarget_ShrinksToTarget()
    {
        var target = new PixelRect(0, 0, 200, 150);
        var selection = new PixelRect(-10, -10, 300, 200);

        Assert.Equal(target, selection.Clamp(target));
    }

    [Fact]
    public void Clamp_EmptyTargetOrEmptyRect_ReturnsEmpty()
    {
        Assert.True(new PixelRect(0, 0, 10, 10).Clamp(PixelRect.Empty).IsEmpty);
        Assert.True(PixelRect.Empty.Clamp(new PixelRect(0, 0, 200, 150)).IsEmpty);
    }

    [Fact]
    public void Offset_MovesWithoutResizing()
    {
        var rect = new PixelRect(10, 20, 30, 40);

        Assert.Equal(new PixelRect(-90, -80, 30, 40), rect.Offset(-100, -100));
    }
}
