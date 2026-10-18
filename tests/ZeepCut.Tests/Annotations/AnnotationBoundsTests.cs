using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationBoundsTests
{
    [Theory]
    [InlineData(100, 100, 200, 180, 100, 100, 100, 80)]
    [InlineData(200, 180, 100, 100, 100, 100, 100, 80)]
    [InlineData(200, 100, 100, 180, 100, 100, 100, 80)]
    [InlineData(100, 180, 200, 100, 100, 100, 100, 80)]
    public void Rectangle_Bounds_NormalizesAnyDragDirection(
        int x1, int y1, int x2, int y2, int left, int top, int width, int height)
    {
        var annotation = new RectangleAnnotation(new PixelPoint(x1, y1), new PixelPoint(x2, y2));

        Assert.Equal(new PixelRect(left, top, width, height), annotation.Bounds);
        Assert.Equal(new PixelRect(left, top, width, height), annotation.Region);
    }

    [Fact]
    public void Rectangle_Bounds_NormalizesNegativeWidthAndHeight()
    {
        var annotation = new RectangleAnnotation(new PixelRect(300, 300, -100, -80));

        Assert.Equal(new PixelRect(200, 220, 100, 80), annotation.Bounds);
    }

    [Fact]
    public void Rectangle_Bounds_DegenerateSelection_IsEmpty()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 0, 0));

        Assert.Equal(new PixelRect(100, 100, 0, 0), annotation.Bounds);
        Assert.True(annotation.Bounds.IsEmpty);
    }

    [Fact]
    public void Rectangle_Bounds_SupportsNegativeCoordinates()
    {
        var annotation = new RectangleAnnotation(new PixelRect(-500, -300, 120, 90));

        Assert.Equal(new PixelRect(-500, -300, 120, 90), annotation.Bounds);
    }

    [Fact]
    public void Ellipse_Bounds_NormalizesReverseDrag()
    {
        var annotation = new EllipseAnnotation(new PixelPoint(300, 250), new PixelPoint(100, 50));

        Assert.Equal(new PixelRect(100, 50, 200, 200), annotation.Bounds);
    }

    [Fact]
    public void Ellipse_Bounds_NormalizesNegativeSize()
    {
        var annotation = new EllipseAnnotation(new PixelRect(50, 60, -20, -30));

        Assert.Equal(new PixelRect(30, 30, 20, 30), annotation.Bounds);
    }

    [Fact]
    public void Arrow_Bounds_IsEndpointBoundingBox()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(100, 100), new PixelPoint(300, 220));

        Assert.Equal(new PixelRect(100, 100, 200, 120), annotation.Bounds);
    }

    [Fact]
    public void Arrow_Bounds_ReverseDrag_IsNormalized()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(300, 220), new PixelPoint(100, 100));

        Assert.Equal(new PixelRect(100, 100, 200, 120), annotation.Bounds);
    }

    [Fact]
    public void Arrow_Bounds_ZeroLength_IsEmptyButKeepsPosition()
    {
        var annotation = new ArrowAnnotation(new PixelPoint(400, 300), new PixelPoint(400, 300));

        Assert.Equal(new PixelRect(400, 300, 0, 0), annotation.Bounds);
        Assert.True(annotation.Bounds.IsEmpty);
    }

    [Fact]
    public void Line_Bounds_HorizontalLine_HasZeroHeight()
    {
        var annotation = new LineAnnotation(new PixelPoint(10, 40), new PixelPoint(210, 40));

        Assert.Equal(new PixelRect(10, 40, 200, 0), annotation.Bounds);
        Assert.True(annotation.Bounds.IsEmpty);
    }

    [Fact]
    public void Line_Bounds_VerticalLine_HasZeroWidth()
    {
        var annotation = new LineAnnotation(new PixelPoint(40, 10), new PixelPoint(40, 210));

        Assert.Equal(new PixelRect(40, 10, 0, 200), annotation.Bounds);
    }

    [Fact]
    public void Line_Bounds_NegativeCoordinates()
    {
        var annotation = new LineAnnotation(new PixelPoint(-100, -80), new PixelPoint(-20, 40));

        Assert.Equal(new PixelRect(-100, -80, 80, 120), annotation.Bounds);
    }

    [Fact]
    public void Freehand_Bounds_IsPointBoundingBox()
    {
        var annotation = new FreehandAnnotation(new[]
        {
            new PixelPoint(10, 20),
            new PixelPoint(50, 20),
            new PixelPoint(30, 80),
        });

        Assert.Equal(new PixelRect(10, 20, 40, 60), annotation.Bounds);
    }

    [Fact]
    public void Freehand_Bounds_SinglePoint_IsEmptyButKeepsPosition()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(7, 9) });

        Assert.Equal(new PixelRect(7, 9, 0, 0), annotation.Bounds);
        Assert.True(annotation.Bounds.IsEmpty);
    }

    [Fact]
    public void Freehand_Bounds_EmptyPointList_IsEmpty()
    {
        var annotation = new FreehandAnnotation(Array.Empty<PixelPoint>());

        Assert.Equal(PixelRect.Empty, annotation.Bounds);
    }

    [Fact]
    public void Freehand_Bounds_SupportsNegativeCoordinates()
    {
        var annotation = new FreehandAnnotation(new[]
        {
            new PixelPoint(-50, -60),
            new PixelPoint(-10, -20),
        });

        Assert.Equal(new PixelRect(-50, -60, 40, 40), annotation.Bounds);
    }

    [Fact]
    public void Text_Bounds_StartsAtPositionAndUsesEstimatedSize()
    {
        var annotation = new TextAnnotation(new PixelPoint(10, 20), "AB", fontSize: 20);

        Assert.Equal(new PixelRect(10, 20, 24, 25), annotation.Bounds);
        Assert.Equal(25, annotation.LineHeight);
    }

    [Fact]
    public void Text_Bounds_MultilineUsesWidestLineAndLineCount()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "A\nBB", fontSize: 20);

        Assert.Equal(2, annotation.LineCount);
        Assert.Equal(24, annotation.Bounds.Width);
        Assert.Equal(50, annotation.Bounds.Height);
    }

    [Fact]
    public void Text_Bounds_NormalizesCrLfAndCr()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "A\r\nB\rC", fontSize: 20);

        Assert.Equal(3, annotation.LineCount);
        Assert.Equal(75, annotation.Bounds.Height);
    }

    [Fact]
    public void Text_Bounds_WideCharactersUseFullFontSize()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "截图", fontSize: 20);

        Assert.Equal(40, annotation.Bounds.Width);
    }

    [Fact]
    public void Text_Bounds_EmptyText_HasZeroWidthAndOneLineHeight()
    {
        var annotation = new TextAnnotation(new PixelPoint(30, 40), string.Empty, fontSize: 20);

        Assert.Equal(new PixelRect(30, 40, 0, 25), annotation.Bounds);
        Assert.Equal(1, annotation.LineCount);
    }

    [Fact]
    public void Text_Bounds_ClampedFontSizeAffectsLineHeight()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "A", fontSize: 1);

        Assert.Equal(8, annotation.FontSize);
        Assert.Equal(10, annotation.LineHeight);
    }

    [Fact]
    public void Mosaic_Bounds_IsNormalizedRegion()
    {
        var annotation = new MosaicAnnotation(new PixelRect(200, 200, -100, -50));

        Assert.Equal(new PixelRect(100, 150, 100, 50), annotation.Bounds);
    }

    [Fact]
    public void RenderBounds_IncludesStrokeThicknessAroundGeometry()
    {
        var annotation = new RectangleAnnotation(new PixelRect(100, 100, 50, 40), thickness: 4);

        Assert.Equal(new PixelRect(98, 98, 54, 44), annotation.GetRenderBounds());
    }

    [Fact]
    public void RenderBounds_DegenerateFreehandSinglePoint_StillHasArea()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(10, 10) }, thickness: 3);

        var renderBounds = annotation.GetRenderBounds();

        Assert.Equal(new PixelRect(8, 8, 4, 4), renderBounds);
    }
}
