using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Editing;

public class EditHistoryTests
{
    private static RectangleAnnotation Rect(int left = 0, int top = 0) =>
        new(new PixelRect(left, top, 10, 10));

    private sealed class ThrowingCommand : IEditCommand
    {
        public string Description => "总是失败";

        public void Execute() => throw new InvalidOperationException("boom");

        public void Undo()
        {
        }
    }

    private sealed class RecordingCommand : IEditCommand
    {
        public int ExecuteCount { get; private set; }

        public int UndoCount { get; private set; }

        public string Description => "记录调用次数";

        public void Execute() => ExecuteCount++;

        public void Undo() => UndoCount++;
    }

    [Fact]
    public void NewHistory_IsEmptyAndCannotUndoOrRedo()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.Undo());
        Assert.False(history.Redo());
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Null(history.NextUndoDescription);
        Assert.Null(history.NextRedoDescription);
    }

    [Fact]
    public void Execute_PushesUndoAndClearsRedo()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var annotation = Rect();
        history.Execute(new AddAnnotationCommand(document, annotation));
        history.Undo();
        Assert.True(history.CanRedo);

        var second = Rect(20, 20);
        history.Execute(new AddAnnotationCommand(document, second));

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(1, document.Count);
        Assert.Same(second, document.Items[0]);
    }

    [Fact]
    public void UndoAndRedo_AddRoundTrip_PreservesIdentityAndPosition()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var first = Rect();
        var second = Rect(50, 50);
        history.Execute(new AddAnnotationCommand(document, first));
        history.Execute(new AddAnnotationCommand(document, second));

        Assert.True(history.Undo());
        Assert.Equal(new Annotation[] { first }, document.Items.ToArray());

        Assert.True(history.Redo());
        Assert.Equal(new Annotation[] { first, second }, document.Items.ToArray());
        Assert.Same(second, document.Items[1]);
        Assert.Equal(2, second.CreationOrder);
    }

    [Fact]
    public void UndoAndRedo_MultipleSteps_KeepOrder()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var a = Rect();
        var b = Rect(20, 20);
        var c = Rect(40, 40);
        history.Execute(new AddAnnotationCommand(document, a));
        history.Execute(new AddAnnotationCommand(document, b));
        history.Execute(new AddAnnotationCommand(document, c));

        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.True(document.IsEmpty);
        Assert.False(history.CanUndo);
        Assert.Equal(3, history.RedoCount);

        Assert.True(history.Redo());
        Assert.True(history.Redo());
        Assert.True(history.Redo());
        Assert.Equal(new Annotation[] { a, b, c }, document.Items.ToArray());
        Assert.Equal(3, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void Execute_AfterUndo_ClearsRedoBranch()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        history.Execute(new AddAnnotationCommand(document, Rect()));
        history.Undo();

        history.Execute(new AddAnnotationCommand(document, Rect(30, 30)));

        Assert.False(history.CanRedo);
        Assert.False(history.Redo());
        Assert.Equal(1, document.Count);
    }

    [Fact]
    public void Capacity_DropsOldestSteps()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory(capacity: 3);
        var annotations = Enumerable.Range(0, 5)
            .Select(i => new RectangleAnnotation(new PixelRect(i * 10, 0, 10, 10)))
            .ToArray();

        foreach (var annotation in annotations)
        {
            history.Execute(new AddAnnotationCommand(document, annotation));
        }

        Assert.Equal(3, history.UndoCount);
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.False(history.CanUndo);

        // 最早的两步已被丢弃，无法撤销，因此文档里还剩前两个标注。
        Assert.Equal(2, document.Count);
        Assert.Same(annotations[0], document.Items[0]);
        Assert.Same(annotations[1], document.Items[1]);
    }

    [Fact]
    public void Capacity_RedoStillWorksAfterTrim()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory(capacity: 2);
        history.Execute(new AddAnnotationCommand(document, Rect()));
        history.Execute(new AddAnnotationCommand(document, Rect(10, 10)));
        history.Execute(new AddAnnotationCommand(document, Rect(20, 20)));

        Assert.True(history.Undo());
        Assert.True(history.Undo());
        Assert.True(history.Redo());
        Assert.True(history.Redo());
        Assert.Equal(2, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal(3, document.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditHistory(capacity));
    }

    [Fact]
    public void Clear_DropsBothStacks()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        history.Execute(new AddAnnotationCommand(document, Rect()));
        history.Undo();

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.True(document.IsEmpty);
    }

    [Fact]
    public void ThrowingCommand_DoesNotChangeHistory()
    {
        var history = new EditHistory();

        Assert.Throws<InvalidOperationException>(() => history.Execute(new ThrowingCommand()));

        Assert.False(history.CanUndo);
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void GenericCommand_CallsExecuteAndUndo()
    {
        var history = new EditHistory();
        var command = new RecordingCommand();

        history.Execute(command);
        Assert.Equal(1, command.ExecuteCount);
        Assert.Equal(0, command.UndoCount);
        Assert.Equal("记录调用次数", history.NextUndoDescription);

        Assert.True(history.Undo());
        Assert.Equal(1, command.UndoCount);
        Assert.Equal("记录调用次数", history.NextRedoDescription);

        Assert.True(history.Redo());
        Assert.Equal(2, command.ExecuteCount);
        Assert.Null(history.NextRedoDescription);
    }

    [Fact]
    public void NextDescriptions_ReflectStacks()
    {
        var document = new AnnotationDocument();
        var history = new EditHistory();
        var annotation = Rect();
        history.Execute(new AddAnnotationCommand(document, annotation));

        Assert.Equal("添加矩形", history.NextUndoDescription);
        Assert.Null(history.NextRedoDescription);

        history.Undo();
        Assert.Null(history.NextUndoDescription);
        Assert.Equal("添加矩形", history.NextRedoDescription);
    }
}
