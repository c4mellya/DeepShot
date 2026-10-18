using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationHitTestTests
{
    [Fact]
    public void Rectangle_Inside_IsHit()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 100, 80));

        Assert.True(annotation.HitTest(150, 140, 0));
    }

    [Fact]
    public void Rectangle_OnBoundary_IsHit()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 100, 80));

        Assert.True(annotation.HitTest(100, 140, 0));
        Assert.True(annotation.HitTest(200, 140, 0));
        Assert.True(annotation.HitTest(150, 100, 0));
        Assert.True(annotation.HitTest(150, 180, 0));
    }

    [Fact]
    public void Rectangle_OutsideBeyondTolerance_Misses()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 100, 80));

        Assert.False(annotation.HitTest(95, 140, 0));
        Assert.False(annotation.HitTest(205, 140, 0));
    }

    [Fact]
    public void Rectangle_OutsideWithinTolerance_IsHit()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 100, 80));

        Assert.True(annotation.HitTest(96, 140, 4));
        Assert.True(annotation.HitTest(204, 140, 4));
    }

    [Fact]
    public void Rectangle_NegativeTolerance_IsTreatedAsZero()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 100, 80));

        Assert.False(annotation.HitTest(99, 140, -10));
    }

    [Fact]
    public void Rectangle_DegenerateWithTolerance_IsHit()
    {
        var annotation = new RectangleAnnotation(new PixelRect(50, 50, 0, 0));

        Assert.True(annotation.HitTest(53, 50, 5));
        Assert.False(annotation.HitTest(60, 50, 5));
    }

    [Fact]
    public void Rectangle_NegativeCoordinates_IsHit()
    {
        var annotation = new RectangleAnnotation(new PixelRect(-200, -100, 80, 60));

        Assert.True(annotation.HitTest(-160, -70, 0));
        Assert.False(annotation.HitTest(0, 0, 0));
    }

    [Fact]
    public void Ellipse_CenterAndBoundary_AreHit()
    {
        var annotation = new EllipseAnnotation(new PixelRect(0, 0, 100, 100));

        Assert.True(annotation.HitTest(50, 50, 0));
        Assert.True(annotation.HitTest(100, 50, 0));
        Assert.True(annotation.HitTest(50, 0, 0));
    }

    [Fact]
    public void Ellipse_BoundingBoxCorner_Misses()
    {
        var annotation = new EllipseAnnotation(new PixelRect(0, 0, 100, 100));

        Assert.False(annotation.HitTest(0, 0, 0));
    }

    [Fact]
    public void Ellipse_OutsideWithinTolerance_IsHit()
    {
        var annotation = new EllipseAnnotation(new PixelRect(0, 0, 100, 100), thickness: 2);

        Assert.False(annotation.HitTest(106, 50, 0));
        Assert.True(annotation.HitTest(106, 50, 6));
    }

    [Fact]
    public void Ellipse_DegenerateRegion_FallsBackToBounds()
    {
        var annotation = new EllipseAnnotation(new PixelRect(10, 10, 0, 100));

        Assert.True(annotation.HitTest(10, 50, 0));
        Assert.False(annotation.HitTest(20, 50, 0));
    }

    [Fact]
    public void Arrow_OnSegment_IsHit()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(0, 0), new PixelPoint(100, 100));

        Assert.True(annotation.HitTest(50, 50, 0));
    }

    [Fact]
    public void Arrow_NearSegment_RespectsToleranceAndStroke()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(0, 0), new PixelPoint(100, 0), thickness: 2);

        Assert.True(annotation.HitTest(50, 1, 0));
        Assert.False(annotation.HitTest(50, 5, 0));
        Assert.True(annotation.HitTest(50, 5, 4));
    }

    [Fact]
    public void Arrow_BeyondEndpoint_MissesUnlessToleranceReaches()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(0, 0), new PixelPoint(100, 0));

        Assert.False(annotation.HitTest(110, 0, 0));
        Assert.True(annotation.HitTest(110, 0, 11));
    }

    [Fact]
    public void Arrow_ZeroLength_IsHitAtThePoint()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(10, 10), new PixelPoint(10, 10));

        Assert.True(annotation.HitTest(10, 10, 0));
        Assert.False(annotation.HitTest(10, 13, 0));
        Assert.True(annotation.HitTest(10, 12, 1));
    }

    [Fact]
    public void Line_Horizontal_IsHitAlongItsLength()
    {
        var annotation = new LineAnnotation(new PixelPoint(0, 40), new PixelPoint(200, 40));

        Assert.True(annotation.HitTest(100, 40, 0));
        Assert.False(annotation.HitTest(100, 60, 0));
        Assert.True(annotation.HitTest(100, 60, 19));
    }

    [Fact]
    public void Line_NegativeCoordinates_IsHit()
    {
        var annotation = new LineAnnotation(new PixelPoint(-100, -100), new PixelPoint(-50, -50));

        Assert.True(annotation.HitTest(-75, -75, 0));
        Assert.False(annotation.HitTest(-100, -50, 0));
    }

    [Fact]
    public void Freehand_OnPolyline_IsHit()
    {
        var annotation = new FreehandAnnotation(new[]
        {
            new PixelPoint(0, 0),
            new PixelPoint(50, 0),
            new PixelPoint(50, 50),
        });

        Assert.True(annotation.HitTest(25, 0, 0));
        Assert.True(annotation.HitTest(50, 25, 0));
        Assert.False(annotation.HitTest(25, 25, 0));
    }

    [Fact]
    public void Freehand_SinglePoint_RespectsTolerance()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(10, 10) });

        Assert.True(annotation.HitTest(10, 10, 0));
        Assert.False(annotation.HitTest(20, 10, 0));
        Assert.True(annotation.HitTest(20, 10, 9));
    }

    [Fact]
    public void Freehand_Empty_IsNeverHit()
    {
        var annotation = new FreehandAnnotation(Array.Empty<PixelPoint>());

        Assert.False(annotation.HitTest(0, 0, 100));
    }

    [Fact]
    public void Text_IsHitByBoundingBox_NotByGlyphs()
    {
        var annotation = new TextAnnotation(new PixelPoint(100, 100), "AB", fontSize: 20);

        // 文字块内的空白区域也算命中（不做字形级命中测试）。
        Assert.True(annotation.HitTest(101, 101, 0));
        Assert.True(annotation.HitTest(120, 110, 0));
        Assert.False(annotation.HitTest(130, 110, 0));
    }

    [Fact]
    public void Text_ToleranceExpandsBoundingBox()
    {
        var annotation = new TextAnnotation(new PixelPoint(100, 100), "AB", fontSize: 20);

        // Bounds 右边界为 124；127 在容差 4 内。
        Assert.False(annotation.HitTest(127, 110, 0));
        Assert.True(annotation.HitTest(127, 110, 4));
    }

    [Fact]
    public void Text_EmptyWithTolerance_IsHitNearAnchor()
    {
        var annotation = new TextAnnotation(new PixelPoint(10, 10), string.Empty);

        Assert.False(annotation.HitTest(12, 12, 0));
        Assert.True(annotation.HitTest(12, 12, 3));
    }

    [Fact]
    public void Mosaic_HitInsideAndByTolerance()
    {
        var annotation = new MosaicAnnotation(new PixelRect(0, 0, 100, 100));

        Assert.True(annotation.HitTest(50, 50, 0));
        Assert.False(annotation.HitTest(105, 50, 0));
        Assert.True(annotation.HitTest(105, 50, 5));
    }

    [Fact]
    public void HitTest_IntegerOverload_MatchesPointOverload()
    {
        var annotation = new RectangleAnnotation(new PixelRect(0, 0, 10, 10));

        Assert.True(annotation.HitTest(5, 5));
        Assert.True(annotation.HitTest(new PixelPoint(5, 5), 0));
    }
}
