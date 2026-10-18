using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationMoveResizeTests
{
    [Fact]
    public void Rectangle_Move_ShiftsRegion()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 80, 60));

        annotation.Move(25, -15);

        Assert.Equal(new PixelRect(125, 85, 80, 60), annotation.Region);
        Assert.Equal(new PixelRect(125, 85, 80, 60), annotation.Bounds);
    }

    [Fact]
    public void Rectangle_MoveTwice_ReturnsToOriginalPosition()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 80, 60));

        annotation.Move(25, 10);
        annotation.Move(-25, -10);

        Assert.Equal(new PixelRect(100, 100, 80, 60), annotation.Region);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-30, 5)]
    [InlineData(12, 400)]
    public void Rectangle_Resize_ReplacesRegionAndNormalizesSize(int width, int height)
    {
        var annotation = new RectangleAnnotation(new PixelRect(0, 0, 10, 10));

        annotation.Resize(new PixelRect(40, 50, width, height));

        var expected = PixelRect.FromTwoPoints(40, 50, 40 + width, 50 + height);
        Assert.Equal(expected, annotation.Region);
    }

    [Fact]
    public void Ellipse_MoveAndResize_UpdateBounds()
    {
        var annotation = new EllipseAnnotation(new PixelRect(0, 0, 100, 80));

        annotation.Move(10, 20);
        annotation.Resize(new PixelRect(5, 6, 40, 30));

        Assert.Equal(new PixelRect(5, 6, 40, 30), annotation.Bounds);
        Assert.True(annotation.CanResize);
    }

    [Fact]
    public void Arrow_Move_ShiftsBothEndpoints()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(10, 20), new PixelPoint(110, 120));

        annotation.Move(-5, 15);

        Assert.Equal(new PixelPoint(5, 35), annotation.Start);
        Assert.Equal(new PixelPoint(105, 135), annotation.End);
        Assert.Equal(new PixelRect(5, 35, 100, 100), annotation.Bounds);
    }

    [Fact]
    public void Line_Move_ShiftsBothEndpoints()
    {
        var annotation = new LineAnnotation(new PixelPoint(0, 0), new PixelPoint(50, 50));

        annotation.Move(10, -10);

        Assert.Equal(new PixelPoint(10, -10), annotation.Start);
        Assert.Equal(new PixelPoint(60, 40), annotation.End);
    }

    [Fact]
    public void Segment_SetEndpoints_RecomputesBounds()
    {
        var annotation = new LineAnnotation(new PixelPoint(0, 0), new PixelPoint(10, 10));

        annotation.SetEndpoints(new PixelPoint(100, 50), new PixelPoint(20, 30));

        Assert.Equal(new PixelRect(20, 30, 80, 20), annotation.Bounds);
    }

    [Fact]
    public void Freehand_Move_ShiftsEveryPoint()
    {
        var annotation = new FreehandAnnotation(new[]
        {
            new PixelPoint(10, 20),
            new PixelPoint(30, 40),
            new PixelPoint(50, 60),
        });

        annotation.Move(5, -5);

        Assert.Equal(new[]
        {
            new PixelPoint(15, 15),
            new PixelPoint(35, 35),
            new PixelPoint(55, 55),
        }, annotation.Points.ToArray());
        Assert.Equal(new PixelRect(15, 15, 40, 40), annotation.Bounds);
    }

    [Fact]
    public void Freehand_AddPoint_GrowsBounds()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(10, 10) });

        annotation.AddPoint(new PixelPoint(50, 70));

        Assert.Equal(2, annotation.Points.Count);
        Assert.Equal(new PixelRect(10, 10, 40, 60), annotation.Bounds);
    }

    [Fact]
    public void Text_Move_ShiftsAnchorAndBounds()
    {
        var annotation = new TextAnnotation(new PixelPoint(100, 100), "AB", fontSize: 20);

        annotation.Move(-20, 30);

        Assert.Equal(new PixelPoint(80, 130), annotation.Position);
        Assert.Equal(new PixelRect(80, 130, 24, 25), annotation.Bounds);
    }

    [Fact]
    public void Mosaic_MoveAndResize_UpdateRegion()
    {
        var annotation = new MosaicAnnotation(new PixelRect(0, 0, 100, 100));

        annotation.Move(10, 10);
        Assert.Equal(new PixelRect(10, 10, 100, 100), annotation.Bounds);

        annotation.Resize(new PixelRect(20, 30, 40, 50));
        Assert.Equal(new PixelRect(20, 30, 40, 50), annotation.Bounds);
        Assert.True(annotation.CanResize);
    }

    [Fact]
    public void Text_DoesNotSupportResize()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "A");

        Assert.False(annotation.CanResize);
        Assert.Throws<NotSupportedException>(() => annotation.Resize(new PixelRect(0, 0, 10, 10)));
    }

    [Fact]
    public void SegmentAnnotations_DoNotSupportResize()
    {
        var line = new LineAnnotation(new PixelPoint(0, 0), new PixelPoint(10, 10));
        var arrow = new ArrowAnnotation(new PixelPoint(0, 0), new PixelPoint(10, 10));

        Assert.False(line.CanResize);
        Assert.False(arrow.CanResize);
        Assert.Throws<NotSupportedException>(() => line.Resize(new PixelRect(0, 0, 100, 100)));
        Assert.Throws<NotSupportedException>(() => arrow.Resize(new PixelRect(0, 0, 100, 100)));
    }

    [Fact]
    public void Freehand_DoesNotSupportResize()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(0, 0), new PixelPoint(10, 10) });

        Assert.False(annotation.CanResize);
        Assert.Throws<NotSupportedException>(() => annotation.Resize(new PixelRect(0, 0, 10, 10)));
    }

    [Fact]
    public void MovingAnnotation_DoesNotChangeDocumentOrder()
    {
        var document = new AnnotationDocument();
        var bottom = new RectangleAnnotation(new PixelRect(0, 0, 100, 100));
        var top = new EllipseAnnotation(new PixelRect(20, 20, 100, 100));
        document.Add(bottom);
        document.Add(top);

        top.Move(5, 5);

        Assert.Equal(0, document.IndexOf(bottom));
        Assert.Equal(1, document.IndexOf(top));
    }
}
