using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationEditControllerTests
{
    private static (AnnotationDocument Document, AnnotationSession Session, AnnotationEditController Editor) Create(
        int width = 400,
        int height = 300)
    {
        var session = new AnnotationSession(new PixelRect(0, 0, width, height));
        return (session.Document, session, session.Editing!);
    }

    private static RectangleAnnotation AddRect(AnnotationSession session, PixelRect region)
    {
        var rectangle = new RectangleAnnotation(region);
        session.History.Execute(new AddAnnotationCommand(session.Document, rectangle));
        return rectangle;
    }

    // --- 选中 ---

    [Fact]
    public void BeginDrag_OnAnnotation_SelectsItAndStartsMove()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));

        var started = editor.BeginDrag(new PixelPoint(100, 90));

        Assert.True(started);
        Assert.Same(rectangle, editor.Selected);
        Assert.True(editor.HasSelection);
        Assert.Equal(AnnotationEditMode.Move, editor.Mode);
        Assert.Equal(AnnotationHandle.Move, editor.ActiveHandle);

        editor.CancelDrag();
    }

    [Fact]
    public void BeginDrag_OnEmptySpace_ClearsSelection()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.Select(rectangle);

        var started = editor.BeginDrag(new PixelPoint(300, 250));

        Assert.False(started);
        Assert.Null(editor.Selected);
        Assert.False(editor.HasSelection);
        Assert.Equal(AnnotationEditMode.None, editor.Mode);
    }

    [Fact]
    public void BeginDrag_PicksTopMostAnnotation()
    {
        var (_, session, editor) = Create();
        var bottom = AddRect(session, new PixelRect(40, 40, 200, 160));
        var top = AddRect(session, new PixelRect(60, 60, 80, 60));

        editor.BeginDrag(new PixelPoint(100, 90));

        Assert.Same(top, editor.Selected);
        editor.CancelDrag();
        Assert.NotNull(bottom);
    }

    // --- 移动 ---

    [Fact]
    public void MoveDrag_RecordsExactlyOneHistoryStep()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        var undoBefore = session.History.UndoCount;

        editor.BeginDrag(new PixelPoint(100, 90));
        for (var i = 1; i <= 10; i++)
        {
            editor.DragTo(new PixelPoint(100 + (i * 3), 90 + (i * 2)));
        }

        var description = editor.EndDrag();

        Assert.Equal("移动矩形", description);
        Assert.Equal(undoBefore + 1, session.History.UndoCount);
        Assert.Equal(new PixelRect(80, 70, 100, 80), rectangle.Region);

        // 撤销一次就回到原位（不是逐帧回退）。
        Assert.True(session.Undo());
        Assert.Equal(new PixelRect(50, 50, 100, 80), rectangle.Region);

        Assert.True(session.Redo());
        Assert.Equal(new PixelRect(80, 70, 100, 80), rectangle.Region);
    }

    [Fact]
    public void MoveDrag_WithoutMovement_RecordsNothing()
    {
        var (_, session, editor) = Create();
        AddRect(session, new PixelRect(50, 50, 100, 80));
        var undoBefore = session.History.UndoCount;

        editor.BeginDrag(new PixelPoint(100, 90));
        var description = editor.EndDrag();

        Assert.Null(description);
        Assert.Equal(undoBefore, session.History.UndoCount);
    }

    [Fact]
    public void MoveDrag_IsClampedToImageBounds()
    {
        var (_, session, editor) = Create(400, 300);
        var rectangle = AddRect(session, new PixelRect(300, 200, 90, 90));

        editor.BeginDrag(new PixelPoint(340, 240));
        editor.DragTo(new PixelPoint(380, 280));
        editor.EndDrag();

        // 右/下贴住 400/300。
        Assert.Equal(new PixelRect(310, 210, 90, 90), rectangle.Region);

        // 抓内部（远离手柄，落在包围盒中心）再拖到左上角外。
        editor.BeginDrag(new PixelPoint(355, 255));
        editor.DragTo(new PixelPoint(-500, -500));
        editor.EndDrag();

        Assert.Equal(new PixelRect(0, 0, 90, 90), rectangle.Region);
    }

    [Fact]
    public void MoveDrag_MovesTextAndFreehandToo()
    {
        var (_, session, editor) = Create();
        var text = new TextAnnotation(new PixelPoint(20, 20), "hello", 18);
        session.History.Execute(new AddAnnotationCommand(session.Document, text));
        var freehand = new FreehandAnnotation(new[] { new PixelPoint(100, 200), new PixelPoint(150, 210) });
        session.History.Execute(new AddAnnotationCommand(session.Document, freehand));

        editor.BeginDrag(new PixelPoint(30, 30));
        editor.DragTo(new PixelPoint(50, 45));
        editor.EndDrag();

        Assert.Equal(new PixelPoint(40, 35), text.Position);

        editor.BeginDrag(new PixelPoint(120, 205));
        editor.DragTo(new PixelPoint(130, 215));
        editor.EndDrag();

        Assert.Equal(new PixelPoint(110, 210), freehand.Points[0]);
    }

    // --- 缩放 ---

    [Fact]
    public void ResizeDrag_BottomRight_RecordsOneStep()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        var undoBefore = session.History.UndoCount;

        // 手柄只在选中后才出现：先选中，再按右下角 (150,130)。
        editor.Select(rectangle);
        var handle = editor.HitTestHandle(new PixelPoint(150, 130));
        Assert.Equal(AnnotationHandle.BottomRight, handle);

        editor.BeginDrag(new PixelPoint(150, 130));
        Assert.Equal(AnnotationEditMode.Resize, editor.Mode);

        editor.DragTo(new PixelPoint(200, 170));
        editor.DragTo(new PixelPoint(220, 190));
        var description = editor.EndDrag();

        Assert.Equal("调整矩形大小", description);
        Assert.Equal(undoBefore + 1, session.History.UndoCount);
        Assert.Equal(new PixelRect(50, 50, 170, 140), rectangle.Region);

        Assert.True(session.Undo());
        Assert.Equal(new PixelRect(50, 50, 100, 80), rectangle.Region);

        Assert.True(session.Redo());
        Assert.Equal(new PixelRect(50, 50, 170, 140), rectangle.Region);
    }

    [Fact]
    public void ResizeDrag_TopLeft_MovesCornerAndKeepsOppositeFixed()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(100, 100, 100, 100));
        editor.Select(rectangle);

        editor.BeginDrag(new PixelPoint(100, 100));
        editor.DragTo(new PixelPoint(60, 70));
        editor.EndDrag();

        Assert.Equal(new PixelRect(60, 70, 140, 130), rectangle.Region);
    }

    [Fact]
    public void ResizeDrag_StopsAtMinimumSizeInsteadOfFlipping()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(100, 100, 100, 100));
        editor.Select(rectangle);

        // 把右下手柄拖到左上角之外：不翻转，停在最小尺寸。
        editor.BeginDrag(new PixelPoint(200, 200));
        editor.DragTo(new PixelPoint(10, 10));
        editor.EndDrag();

        Assert.Equal(AnnotationEditController.MinimumSize, rectangle.Region.Width);
        Assert.Equal(AnnotationEditController.MinimumSize, rectangle.Region.Height);
        Assert.Equal(100, rectangle.Region.Left);
        Assert.Equal(100, rectangle.Region.Top);
    }

    [Fact]
    public void ResizeDrag_Mosaic_Works()
    {
        var (_, session, editor) = Create();
        var mosaic = new MosaicAnnotation(new PixelRect(20, 20, 60, 40));
        session.History.Execute(new AddAnnotationCommand(session.Document, mosaic));
        editor.Select(mosaic);

        editor.BeginDrag(new PixelPoint(80, 60));
        editor.DragTo(new PixelPoint(140, 100));
        editor.EndDrag();

        Assert.Equal(new PixelRect(20, 20, 120, 80), mosaic.Region);
    }

    [Fact]
    public void HitTestHandle_OnNonResizableAnnotation_ReturnsNone()
    {
        var (_, session, editor) = Create();
        var freehand = new FreehandAnnotation(new[] { new PixelPoint(50, 50), new PixelPoint(100, 100) });
        session.History.Execute(new AddAnnotationCommand(session.Document, freehand));
        editor.Select(freehand);

        Assert.Empty(editor.GetHandleRects());
        Assert.Equal(AnnotationHandle.None, editor.HitTestHandle(new PixelPoint(50, 50)));
    }

    // --- 线段端点 ---

    [Fact]
    public void SegmentEndpoints_DragStartAndEnd_RecordsOneStep()
    {
        var (_, session, editor) = Create();
        var arrow = new ArrowAnnotation(new PixelPoint(100, 100), new PixelPoint(200, 200));
        session.History.Execute(new AddAnnotationCommand(session.Document, arrow));
        editor.Select(arrow);
        var undoBefore = session.History.UndoCount;

        Assert.Equal(AnnotationHandle.StartPoint, editor.HitTestHandle(new PixelPoint(100, 100)));
        editor.BeginDrag(new PixelPoint(100, 100));
        Assert.Equal(AnnotationEditMode.StartPoint, editor.Mode);
        editor.DragTo(new PixelPoint(140, 60));
        editor.EndDrag();

        Assert.Equal(new PixelPoint(140, 60), arrow.Start);
        Assert.Equal(new PixelPoint(200, 200), arrow.End);
        Assert.Equal(undoBefore + 1, session.History.UndoCount);

        Assert.True(session.Undo());
        Assert.Equal(new PixelPoint(100, 100), arrow.Start);

        // 再拖终点。
        editor.Select(arrow);
        editor.BeginDrag(new PixelPoint(200, 200));
        Assert.Equal(AnnotationEditMode.EndPoint, editor.Mode);
        editor.DragTo(new PixelPoint(250, 240));
        editor.EndDrag();
        Assert.Equal(new PixelPoint(250, 240), arrow.End);
    }

    [Fact]
    public void SegmentEndpoints_AreClampedToImage()
    {
        var (_, session, editor) = Create(400, 300);
        var line = new LineAnnotation(new PixelPoint(100, 100), new PixelPoint(200, 200));
        session.History.Execute(new AddAnnotationCommand(session.Document, line));
        editor.Select(line);

        editor.BeginDrag(new PixelPoint(100, 100));
        editor.DragTo(new PixelPoint(-50, 500));
        editor.EndDrag();

        Assert.Equal(new PixelPoint(0, 300), line.Start);
    }

    // --- 删除 ---

    [Fact]
    public void DeleteSelected_RemovesAndUndoRestoresOrder()
    {
        var (_, session, editor) = Create();
        var first = AddRect(session, new PixelRect(10, 10, 50, 50));
        var second = AddRect(session, new PixelRect(100, 100, 50, 50));

        editor.Select(first);
        var description = editor.DeleteSelected();

        Assert.Equal("删除矩形", description);
        Assert.False(session.Document.Contains(first));
        Assert.True(session.Document.Contains(second));
        Assert.Null(editor.Selected);
        Assert.Equal(1, session.Count);

        Assert.True(session.Undo());
        Assert.Same(first, session.Document.Items[0]);
        Assert.Same(second, session.Document.Items[1]);
    }

    [Fact]
    public void DeleteSelected_WithoutSelection_ReturnsNull() =>
        Assert.Null(Create().Editor.DeleteSelected());

    [Fact]
    public void DeleteSelected_UndoUnlocksSelectionOnlyWhenDocumentEmpty()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(10, 10, 50, 50));
        editor.Select(rectangle);

        Assert.True(session.IsSelectionLocked);
        editor.DeleteSelected();
        Assert.False(session.IsSelectionLocked);

        session.Undo();
        Assert.True(session.IsSelectionLocked);
    }

    // --- 文字编辑 ---

    [Fact]
    public void SetSelectedText_RecordsHistory()
    {
        var (_, session, editor) = Create();
        var text = new TextAnnotation(new PixelPoint(20, 20), "old", 18);
        session.History.Execute(new AddAnnotationCommand(session.Document, text));
        editor.Select(text);

        Assert.Equal("修改文字", editor.SetSelectedText("new value"));
        Assert.Equal("new value", text.Text);

        Assert.True(session.Undo());
        Assert.Equal("old", text.Text);
    }

    [Fact]
    public void SetSelectedText_UnchangedOrWrongTarget_ReturnsNull()
    {
        var (_, session, editor) = Create();
        var text = new TextAnnotation(new PixelPoint(20, 20), "same", 18);
        session.History.Execute(new AddAnnotationCommand(session.Document, text));
        editor.Select(text);
        var undoBefore = session.History.UndoCount;

        Assert.Null(editor.SetSelectedText("same"));
        Assert.Equal(undoBefore, session.History.UndoCount);

        var rectangle = AddRect(session, new PixelRect(80, 80, 40, 40));
        editor.Select(rectangle);
        Assert.Null(editor.SetSelectedText("nope"));
    }

    // --- 取消 / 校验 ---

    [Fact]
    public void CancelDrag_RestoresInitialGeometryWithoutHistory()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        var undoBefore = session.History.UndoCount;

        editor.BeginDrag(new PixelPoint(100, 90));
        editor.DragTo(new PixelPoint(180, 150));
        Assert.Equal(new PixelRect(130, 110, 100, 80), rectangle.Region);

        Assert.True(editor.CancelDrag());
        Assert.Equal(new PixelRect(50, 50, 100, 80), rectangle.Region);
        Assert.Equal(undoBefore, session.History.UndoCount);
        Assert.Equal(AnnotationEditMode.None, editor.Mode);
    }

    [Fact]
    public void ValidateSelection_DropsSelectionNotInDocument()
    {
        var (_, session, editor) = Create();
        var rectangle = new RectangleAnnotation(new PixelRect(50, 50, 100, 80));
        session.History.Execute(new AddAnnotationCommand(session.Document, rectangle));
        editor.Select(rectangle);

        session.Undo(); // 撤销"添加"：对象离开文档。

        Assert.False(editor.HasSelection);
        Assert.Null(editor.Selected);
    }

    [Fact]
    public void Session_UndoDuringDrag_IsIgnored()
    {
        var (_, session, editor) = Create();
        AddRect(session, new PixelRect(50, 50, 100, 80));

        editor.BeginDrag(new PixelPoint(100, 90));
        editor.DragTo(new PixelPoint(120, 110));

        Assert.False(session.Undo());

        editor.EndDrag();
        Assert.True(session.Undo());
    }

    [Fact]
    public void SelectToolWithTool_ClearsSelection()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.Select(rectangle);
        session.SetActiveTool(null);
        editor.Select(rectangle);
        Assert.True(session.HasSelection);

        session.SetActiveTool(AnnotationTool.Ellipse);

        Assert.False(session.HasSelection);
    }

    [Fact]
    public void ClearSelection_ReturnsFalseWhenNothingSelected()
    {
        var (_, session, editor) = Create();
        Assert.False(editor.ClearSelection());
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.Select(rectangle);
        Assert.True(editor.ClearSelection());
        Assert.Null(editor.Selected);
    }

    [Fact]
    public void Session_WithoutImageBounds_HasNoEditor()
    {
        var session = new AnnotationSession();
        Assert.Null(session.Editing);
        Assert.False(session.HasSelection);
        Assert.Null(session.DeleteSelected());
        Assert.False(session.ClearSelection());
    }

    // --- 拖动实时预览与提交语义 ---

    [Fact]
    public void DragTo_BeforeBegin_ReturnsFalse()
    {
        var (_, _, editor) = Create();
        Assert.False(editor.DragTo(new PixelPoint(10, 10)));
        Assert.Equal(AnnotationEditMode.None, editor.Mode);
    }

    [Fact]
    public void DragTo_AfterEnd_ReturnsFalse()
    {
        var (_, session, editor) = Create();
        AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.BeginDrag(new PixelPoint(100, 90));
        editor.EndDrag();

        Assert.False(editor.DragTo(new PixelPoint(200, 150)));
    }

    [Fact]
    public void BeginDrag_WhileDragging_ReturnsFalse()
    {
        var (_, session, editor) = Create();
        AddRect(session, new PixelRect(50, 50, 100, 80));
        Assert.True(editor.BeginDrag(new PixelPoint(100, 90)));
        Assert.False(editor.BeginDrag(new PixelPoint(120, 100)));
        editor.CancelDrag();
    }

    [Fact]
    public void GetHandleRects_ForResizableAnnotation_ReturnsEightHandles()
    {
        var (_, session, editor) = Create();
        var rectangle = AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.Select(rectangle);

        var handles = editor.GetHandleRects(handleSize: 8);

        Assert.Equal(8, handles.Count);
        foreach (var (_, rect) in handles)
        {
            Assert.Equal(8, rect.Width);
            Assert.Equal(8, rect.Height);
        }
    }

    [Fact]
    public void GetHandleRects_ForSegment_ReturnsTwoEndpointHandles()
    {
        var (_, session, editor) = Create();
        var line = new LineAnnotation(new PixelPoint(10, 20), new PixelPoint(90, 120));
        session.History.Execute(new AddAnnotationCommand(session.Document, line));
        editor.Select(line);

        var handles = editor.GetHandleRects();

        Assert.Equal(2, handles.Count);
        Assert.Equal(AnnotationHandle.StartPoint, handles[0].Handle);
        Assert.Equal(AnnotationHandle.EndPoint, handles[1].Handle);
    }

    [Fact]
    public void Select_WhileDragging_IsIgnored()
    {
        var (_, session, editor) = Create();
        var first = AddRect(session, new PixelRect(50, 50, 100, 80));
        var second = AddRect(session, new PixelRect(200, 50, 60, 60));

        editor.BeginDrag(new PixelPoint(100, 90));
        Assert.Same(first, editor.Selected);

        editor.Select(second);
        Assert.Same(first, editor.Selected);

        editor.CancelDrag();
    }

    [Fact]
    public void ClearSelection_WhileDragging_ReturnsFalse()
    {
        var (_, session, editor) = Create();
        AddRect(session, new PixelRect(50, 50, 100, 80));
        editor.BeginDrag(new PixelPoint(100, 90));

        Assert.False(editor.ClearSelection());
        editor.CancelDrag();
    }
}
