using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Editing;

public class AnnotationCommandsTests
{
    private static RectangleAnnotation Rect(int left = 0, int top = 0) =>
        new(new PixelRect(left, top, 10, 10));

    [Fact]
    public void AddCommand_UndoRemovesRedoReaddsSameObject()
    {
        var document = new AnnotationDocument();
        var annotation = Rect();
        var command = new AddAnnotationCommand(document, annotation);

        command.Execute();
        Assert.Same(annotation, document.Items[0]);

        command.Undo();
        Assert.True(document.IsEmpty);

        command.Execute();
        Assert.Same(annotation, document.Items[0]);
        Assert.Equal(1, annotation.CreationOrder);
    }

    [Fact]
    public void AddCommand_AlreadyInDocument_Throws()
    {
        var document = new AnnotationDocument();
        var annotation = Rect();
        document.Add(annotation);

        Assert.Throws<ArgumentException>(() => new AddAnnotationCommand(document, annotation));
    }

    [Fact]
    public void RemoveCommand_RestoresOriginalIndexAndOrder()
    {
        var document = new AnnotationDocument();
        var a = Rect(0, 0);
        var b = Rect(10, 10);
        var c = Rect(20, 20);
        document.Add(a);
        document.Add(b);
        document.Add(c);

        var command = new RemoveAnnotationCommand(document, b);
        command.Execute();
        Assert.Equal(new Annotation[] { a, c }, document.Items.ToArray());

        command.Undo();
        Assert.Equal(new Annotation[] { a, b, c }, document.Items.ToArray());
        Assert.Equal(2, b.CreationOrder);

        command.Execute();
        Assert.Equal(new Annotation[] { a, c }, document.Items.ToArray());
    }

    [Fact]
    public void RemoveCommand_NotInDocument_Throws()
    {
        var document = new AnnotationDocument();

        Assert.Throws<ArgumentException>(() => new RemoveAnnotationCommand(document, Rect()));
    }

    [Fact]
    public void MoveCommand_RoundTripsOffset()
    {
        var annotation = new EllipseAnnotation(new PixelRect(100, 100, 40, 30));
        var command = new MoveAnnotationCommand(annotation, 15, -25);

        command.Execute();
        Assert.Equal(new PixelRect(115, 75, 40, 30), annotation.Region);

        command.Undo();
        Assert.Equal(new PixelRect(100, 100, 40, 30), annotation.Region);

        command.Execute();
        Assert.Equal(new PixelRect(115, 75, 40, 30), annotation.Region);
    }

    [Fact]
    public void ClearCommand_UndoRestoresAllObjectsAndOrder()
    {
        var document = new AnnotationDocument();
        var a = Rect(0, 0);
        var b = new TextAnnotation(new PixelPoint(10, 10), "B");
        var c = new FreehandAnnotation(new[] { new PixelPoint(0, 0) });
        document.Add(a);
        document.Add(b);
        document.Add(c);

        var command = new ClearAnnotationsCommand(document);
        command.Execute();
        Assert.True(document.IsEmpty);

        command.Undo();
        Assert.Equal(new Annotation[] { a, b, c }, document.Items.ToArray());
        Assert.Equal(1, a.CreationOrder);
        Assert.Equal(2, b.CreationOrder);
        Assert.Equal(3, c.CreationOrder);

        command.Execute();
        Assert.True(document.IsEmpty);
    }

    [Fact]
    public void ClearCommand_OnEmptyDocument_IsHarmless()
    {
        var document = new AnnotationDocument();
        var command = new ClearAnnotationsCommand(document);

        command.Execute();
        command.Undo();

        Assert.True(document.IsEmpty);
    }

    [Fact]
    public void PropertyCommand_RestoresOldThickness()
    {
        var annotation = Rect();
        var command = new AnnotationPropertyCommand<int>(
            annotation,
            "粗细",
            15,
            target => target.Thickness,
            (target, value) => target.Thickness = value);

        command.Execute();
        Assert.Equal(15, annotation.Thickness);

        command.Undo();
        Assert.Equal(AnnotationDefaults.DefaultThickness, annotation.Thickness);

        command.Execute();
        Assert.Equal(15, annotation.Thickness);
    }

    [Fact]
    public void PropertyCommand_ClampedNewValue_UndoRestoresOldValue()
    {
        var annotation = Rect();
        var command = new AnnotationPropertyCommand<int>(
            annotation,
            "粗细",
            999,
            target => target.Thickness,
            (target, value) => target.Thickness = value);

        command.Execute();
        Assert.Equal(AnnotationDefaults.MaxThickness, annotation.Thickness);

        command.Undo();
        Assert.Equal(AnnotationDefaults.DefaultThickness, annotation.Thickness);
    }

    [Fact]
    public void SetColorFactory_RoundTrips()
    {
        var annotation = Rect();
        var oldColor = annotation.Color;
        var command = AnnotationEdits.SetColor(annotation, AnnotationColor.FromRgb(0, 120, 255));

        command.Execute();
        Assert.Equal(AnnotationColor.FromRgb(0, 120, 255), annotation.Color);

        command.Undo();
        Assert.Equal(oldColor, annotation.Color);
    }

    [Fact]
    public void SetTextFactory_RoundTrips()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "旧文字");
        var command = AnnotationEdits.SetText(annotation, "新文字");

        command.Execute();
        Assert.Equal("新文字", annotation.Text);

        command.Undo();
        Assert.Equal("旧文字", annotation.Text);
    }

    [Fact]
    public void SetFontSizeFactory_RoundTripsAndClamps()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "文字", fontSize: 20);
        var command = AnnotationEdits.SetFontSize(annotation, 1000);

        command.Execute();
        Assert.Equal(AnnotationDefaults.MaxFontSize, annotation.FontSize);

        command.Undo();
        Assert.Equal(20, annotation.FontSize);
    }

    [Fact]
    public void SetTextBackgroundFactory_RoundTripsNullableValue()
    {
        var annotation = new TextAnnotation(new PixelPoint(0, 0), "文字");
        var command = AnnotationEdits.SetTextBackground(annotation, AnnotationColor.White);

        command.Execute();
        Assert.Equal(AnnotationColor.White, annotation.BackgroundColor);

        command.Undo();
        Assert.Null(annotation.BackgroundColor);
    }

    [Fact]
    public void SetMosaicBlockSizeFactory_RoundTrips()
    {
        var annotation = new MosaicAnnotation(new PixelRect(0, 0, 50, 50), blockSize: 8);
        var command = AnnotationEdits.SetMosaicBlockSize(annotation, 32);

        command.Execute();
        Assert.Equal(32, annotation.BlockSize);

        command.Undo();
        Assert.Equal(8, annotation.BlockSize);
    }

    [Fact]
    public void SetMosaicStrengthFactory_RoundTrips()
    {
        var annotation = new MosaicAnnotation(new PixelRect(0, 0, 50, 50), strength: 0.25);
        var command = AnnotationEdits.SetMosaicStrength(annotation, 0.9);

        command.Execute();
        Assert.Equal(0.9, annotation.Strength, precision: 10);

        command.Undo();
        Assert.Equal(0.25, annotation.Strength, precision: 10);
    }

    [Fact]
    public void SetFreehandSmoothFactory_RoundTrips()
    {
        var annotation = new FreehandAnnotation(new[] { new PixelPoint(0, 0) });
        var command = AnnotationEdits.SetFreehandSmooth(annotation, true);

        command.Execute();
        Assert.True(annotation.Smooth);

        command.Undo();
        Assert.False(annotation.Smooth);
    }

    [Fact]
    public void CommandsRecordedInHistory_RoundTripThroughUndoRedo()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var annotation = new RectangleAnnotation(new PixelRect(10, 10, 50, 40));

        history.Execute(new AddAnnotationCommand(document, annotation));
        history.Execute(new MoveAnnotationCommand(annotation, 5, 5));
        history.Execute(AnnotationEdits.SetThickness(annotation, 9));
        history.Execute(new RemoveAnnotationCommand(document, annotation));

        Assert.True(document.IsEmpty);

        // 逆序撤销：恢复删除 → 恢复粗细 → 恢复位置 → 恢复添加。
        Assert.True(history.Undo());
        Assert.Same(annotation, document.Items[0]);
        Assert.Equal(9, annotation.Thickness);
        Assert.Equal(new PixelRect(15, 15, 50, 40), annotation.Region);

        Assert.True(history.Undo());
        Assert.Equal(AnnotationDefaults.DefaultThickness, annotation.Thickness);

        Assert.True(history.Undo());
        Assert.Equal(new PixelRect(10, 10, 50, 40), annotation.Region);

        Assert.True(history.Undo());
        Assert.True(document.IsEmpty);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var document = new AnnotationDocument();
        var annotation = Rect();
        document.Add(annotation);

        Assert.Throws<ArgumentNullException>(() => new AddAnnotationCommand(null!, annotation));
        Assert.Throws<ArgumentNullException>(() => new AddAnnotationCommand(document, null!));
        Assert.Throws<ArgumentNullException>(() => new RemoveAnnotationCommand(null!, annotation));
        Assert.Throws<ArgumentNullException>(() => new MoveAnnotationCommand(null!, 1, 1));
        Assert.Throws<ArgumentNullException>(() => new ClearAnnotationsCommand(null!));
    }
}
