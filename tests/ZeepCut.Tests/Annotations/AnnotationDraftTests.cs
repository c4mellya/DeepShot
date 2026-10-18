using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationDraftTests
{
    private static readonly AnnotationColor Color = AnnotationColor.FromRgb(0x0A, 0x84, 0xFF);

    [Fact]
    public void Palette_HasSixOpaqueDistinctColors()
    {
        Assert.Equal(6, AnnotationPalette.Colors.Count);
        Assert.Equal(6, AnnotationPalette.Colors.Distinct().Count());
        Assert.All(AnnotationPalette.Colors, color => Assert.Equal(byte.MaxValue, color.Alpha));
        Assert.Contains(AnnotationColor.Default, AnnotationPalette.Colors);
    }

    [Fact]
    public void Palette_HasFourThicknessPresetsWithinModelRange()
    {
        Assert.Equal(new[] { 1, 3, 6, 10 }, AnnotationPalette.Thicknesses);
        Assert.All(
            AnnotationPalette.Thicknesses,
            thickness => Assert.Equal(thickness, AnnotationDefaults.ClampThickness(thickness)));
    }

    [Fact]
    public void ToolSettings_ClampsThicknessAndFontSize()
    {
        var settings = new AnnotationToolSettings { Thickness = 99, FontSize = 1 };

        Assert.Equal(AnnotationDefaults.MaxThickness, settings.Thickness);
        Assert.Equal(AnnotationDefaults.MinFontSize, settings.FontSize);
    }

    [Fact]
    public void ToolSettings_DefaultsToRectangleRedDefaultThickness()
    {
        var settings = new AnnotationToolSettings();

        Assert.Equal(AnnotationTool.Rectangle, settings.Tool);
        Assert.Equal(AnnotationColor.Default, settings.Color);
        Assert.Equal(AnnotationDefaults.DefaultThickness, settings.Thickness);
        Assert.Equal(AnnotationPalette.DefaultFontSize, settings.FontSize);
    }

    [Fact]
    public void ToolSettings_RejectsUndefinedTool()
    {
        var settings = new AnnotationToolSettings();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Tool = (AnnotationTool)99);
    }

    [Theory]
    [InlineData(AnnotationTool.Rectangle, AnnotationKind.Rectangle)]
    [InlineData(AnnotationTool.Ellipse, AnnotationKind.Ellipse)]
    [InlineData(AnnotationTool.Arrow, AnnotationKind.Arrow)]
    [InlineData(AnnotationTool.Line, AnnotationKind.Line)]
    [InlineData(AnnotationTool.Freehand, AnnotationKind.Freehand)]
    [InlineData(AnnotationTool.Mosaic, AnnotationKind.Mosaic)]
    public void Begin_CreatesAnnotationOfMatchingKind(AnnotationTool tool, AnnotationKind expected)
    {
        var draft = AnnotationDraftFactory.Begin(tool, new PixelPoint(10, 20), Color, 6);

        Assert.NotNull(draft);
        Assert.Equal(tool, draft!.Tool);
        Assert.Equal(expected, draft.Annotation.Kind);

        // 马赛克按设计不使用颜色/粗细（块大小与强度取 AnnotationDefaults），其余工具继承当前设置。
        if (tool != AnnotationTool.Mosaic)
        {
            Assert.Equal(Color, draft.Annotation.Color);
            Assert.Equal(6, draft.Annotation.Thickness);
        }
    }

    [Fact]
    public void Begin_TextTool_ReturnsNull()
    {
        Assert.Null(AnnotationDraftFactory.Begin(AnnotationTool.Text, new PixelPoint(0, 0), Color));
    }

    [Fact]
    public void Begin_UnknownTool_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnnotationDraftFactory.Begin((AnnotationTool)42, new PixelPoint(0, 0), Color));
    }

    [Fact]
    public void RectangleDraft_UpdatesRegionAndIsMeaningfulAfterDrag()
    {
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Rectangle, new PixelPoint(100, 100), Color)!;

        Assert.False(draft.IsMeaningful); // 刚按下：零面积

        draft.DragTo(new PixelPoint(300, 250));

        var rectangle = Assert.IsType<RectangleAnnotation>(draft.Annotation);
        Assert.Equal(new PixelRect(100, 100, 200, 150), rectangle.Region);
        Assert.True(draft.IsMeaningful);
        Assert.Equal(new PixelPoint(300, 250), draft.End);
    }

    [Fact]
    public void RectangleDraft_ReverseDrag_ProducesPositiveRegion()
    {
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Rectangle, new PixelPoint(300, 250), Color)!;

        draft.DragTo(new PixelPoint(100, 100));

        Assert.Equal(new PixelRect(100, 100, 200, 150), draft.Annotation.Bounds);
    }

    [Fact]
    public void EllipseDraft_UpdatesRegion()
    {
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Ellipse, new PixelPoint(400, 300), Color)!;
        draft.DragTo(new PixelPoint(500, 380));

        var ellipse = Assert.IsType<EllipseAnnotation>(draft.Annotation);
        Assert.Equal(new PixelRect(400, 300, 100, 80), ellipse.Region);
    }

    [Theory]
    [InlineData(AnnotationTool.Line)]
    [InlineData(AnnotationTool.Arrow)]
    public void SegmentDraft_UpdatesEndpointsAndMeaningfulness(AnnotationTool tool)
    {
        var draft = AnnotationDraftFactory.Begin(tool, new PixelPoint(10, 10), Color)!;

        Assert.False(draft.IsMeaningful); // 零长度

        draft.DragTo(new PixelPoint(110, 60));

        var segment = Assert.IsAssignableFrom<SegmentAnnotation>(draft.Annotation);
        Assert.Equal(new PixelPoint(10, 10), segment.Start);
        Assert.Equal(new PixelPoint(110, 60), segment.End);
        Assert.True(draft.IsMeaningful);
    }

    [Fact]
    public void FreehandDraft_AppendsPointsAndSkipsDuplicates()
    {
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Freehand, new PixelPoint(10, 10), Color)!;
        var freehand = Assert.IsType<FreehandAnnotation>(draft.Annotation);

        Assert.True(freehand.Smooth);
        Assert.Single(freehand.Points);
        Assert.True(draft.IsMeaningful); // 单击画一个点

        draft.DragTo(new PixelPoint(20, 20));
        draft.DragTo(new PixelPoint(20, 20)); // 同一像素重复 MouseMove
        draft.DragTo(new PixelPoint(30, 25));

        Assert.Equal(3, freehand.Points.Count);
        Assert.Equal(3, draft.PointCount);
        Assert.Equal(new PixelRect(10, 10, 20, 15), freehand.Bounds);
    }

    [Fact]
    public void MosaicDraft_UpdatesRegionAndUsesDefaultBlockSize()
    {
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Mosaic, new PixelPoint(50, 40), Color)!;
        draft.DragTo(new PixelPoint(150, 140));

        var mosaic = Assert.IsType<MosaicAnnotation>(draft.Annotation);
        Assert.Equal(new PixelRect(50, 40, 100, 100), mosaic.Region);
        Assert.Equal(AnnotationDefaults.DefaultMosaicBlockSize, mosaic.BlockSize);
        Assert.True(draft.IsMeaningful);
    }

    [Fact]
    public void DegenerateClick_IsNotMeaningfulForRegionAndSegmentTools()
    {
        foreach (var tool in new[]
                 {
                     AnnotationTool.Rectangle,
                     AnnotationTool.Ellipse,
                     AnnotationTool.Arrow,
                     AnnotationTool.Line,
                     AnnotationTool.Mosaic,
                 })
        {
            var draft = AnnotationDraftFactory.Begin(tool, new PixelPoint(42, 42), Color)!;
            draft.DragTo(new PixelPoint(42, 42));
            Assert.False(draft.IsMeaningful);
        }
    }

    [Fact]
    public void Draft_CommitsThroughAddAnnotationCommandAndUndoRestoresEmptyDocument()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var draft = AnnotationDraftFactory.Begin(AnnotationTool.Arrow, new PixelPoint(0, 0), Color)!;
        draft.DragTo(new PixelPoint(120, 60));

        history.Execute(new AddAnnotationCommand(document, draft.Annotation));

        Assert.Equal(1, document.Count);
        Assert.Same(draft.Annotation, document.Items[0]);
        Assert.Equal(1, history.UndoCount);

        Assert.True(history.Undo());
        Assert.Equal(0, document.Count);

        Assert.True(history.Redo());
        Assert.Equal(1, document.Count);
        Assert.Same(draft.Annotation, document.Items[0]);
    }
}
