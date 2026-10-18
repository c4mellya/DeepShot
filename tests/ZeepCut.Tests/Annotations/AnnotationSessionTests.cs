using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationSessionTests
{
    private static AnnotationSession CreateSession(bool withTool = true)
    {
        var session = new AnnotationSession();
        if (withTool)
        {
            session.SetActiveTool(AnnotationTool.Rectangle);
        }

        return session;
    }

    [Fact]
    public void NewSession_IsEmptyUnlockedAndWithoutTool()
    {
        var session = new AnnotationSession();

        Assert.Null(session.ActiveTool);
        Assert.Equal(0, session.Count);
        Assert.False(session.IsSelectionLocked);
        Assert.False(session.IsDrafting);
        Assert.False(session.IsTextEditing);
        Assert.Null(session.Preview);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveCapacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationSession(0));

    [Fact]
    public void SetActiveTool_SyncsSettingsTool()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Arrow);

        Assert.Equal(AnnotationTool.Arrow, session.ActiveTool);
        Assert.Equal(AnnotationTool.Arrow, session.Settings.Tool);

        session.SetActiveTool(null);
        Assert.Null(session.ActiveTool);

        // 回到选择模式不改变上次的设置，重新选工具前颜色/粗细沿用。
        Assert.Equal(AnnotationTool.Arrow, session.Settings.Tool);
    }

    [Fact]
    public void BeginDraft_WithoutTool_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new AnnotationSession().BeginDraft(new PixelPoint(1, 2)));

    [Fact]
    public void BeginDraft_UsesColorAndThicknessFromSettings()
    {
        var session = CreateSession();
        var color = AnnotationColor.FromRgb(0x34, 0xC7, 0x59);
        session.Settings.Color = color;
        session.Settings.Thickness = 10;

        var draft = session.BeginDraft(new PixelPoint(10, 20));

        Assert.NotNull(draft);
        Assert.Equal(color, draft!.Annotation.Color);
        Assert.Equal(10, draft.Annotation.Thickness);
        Assert.True(session.IsDrafting);
        Assert.Same(draft.Annotation, session.Preview);
        Assert.True(session.IsSelectionLocked);
    }

    [Fact]
    public void DraftLifecycle_CommitAddsToDocumentAndUndoRemovesIt()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(100, 100));
        session.UpdateDraft(new PixelPoint(300, 250));
        Assert.True(session.CommitDraft());

        Assert.False(session.IsDrafting);
        Assert.Null(session.Preview);
        Assert.Equal(1, session.Count);
        Assert.True(session.IsSelectionLocked);
        Assert.True(session.CanUndo);

        Assert.True(session.Undo());
        Assert.Equal(0, session.Count);
        Assert.False(session.IsSelectionLocked);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo());
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void CommitDraft_DegenerateRectangle_IsDiscarded()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(50, 50));
        // 没有拖动过：零面积，不应产生标注，也不应进入撤销历史。
        Assert.False(session.CommitDraft());

        Assert.Equal(0, session.Count);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDrafting);
    }

    [Fact]
    public void CommitDraft_ZeroLengthLine_IsDiscarded()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Line);

        session.BeginDraft(new PixelPoint(10, 10));
        session.UpdateDraft(new PixelPoint(10, 10));

        Assert.False(session.CommitDraft());
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void CancelDraft_DiscardsWithoutTouchingDocumentOrHistory()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(100, 100));

        Assert.True(session.CancelDraft());

        Assert.Equal(0, session.Count);
        Assert.False(session.CanUndo);
        Assert.Null(session.Preview);
        Assert.False(session.CancelDraft());
    }

    [Fact]
    public void BeginDraft_WhileDrafting_Throws()
    {
        var session = CreateSession();
        session.BeginDraft(new PixelPoint(0, 0));

        Assert.Throws<InvalidOperationException>(() => session.BeginDraft(new PixelPoint(1, 1)));
    }

    [Fact]
    public void SetActiveTool_WhileDrafting_Throws()
    {
        var session = CreateSession();
        session.BeginDraft(new PixelPoint(0, 0));

        Assert.Throws<InvalidOperationException>(() => session.SetActiveTool(AnnotationTool.Ellipse));
    }

    [Fact]
    public void BeginDraft_WithTextTool_ReturnsNullAndEntersNoState()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Text);

        Assert.Null(session.BeginDraft(new PixelPoint(5, 5)));
        Assert.False(session.IsDrafting);
        Assert.Null(session.Preview);
    }

    [Fact]
    public void TextLifecycle_CommitsNonEmptyTextAndIsUndoable()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Text);
        session.Settings.FontSize = 24;

        var pending = session.BeginText(new PixelPoint(40, 60));
        Assert.Equal(24, pending.FontSize);
        Assert.True(session.IsTextEditing);
        Assert.True(session.IsSelectionLocked);
        Assert.Same(pending, session.Preview);

        session.UpdateTextPreview("你好");
        Assert.Equal("你好", pending.Text);

        Assert.True(session.CommitText("你好，世界"));

        Assert.False(session.IsTextEditing);
        Assert.Equal(1, session.Count);
        var text = Assert.IsType<TextAnnotation>(session.Document.Items[0]);
        Assert.Equal("你好，世界", text.Text);
        Assert.Equal(new PixelPoint(40, 60), text.Position);
        Assert.True(session.CanUndo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void CommitText_BlankContent_IsDiscarded(string? text)
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Text);
        session.BeginText(new PixelPoint(0, 0));

        Assert.False(session.CommitText(text));
        Assert.False(session.IsTextEditing);
        Assert.Equal(0, session.Count);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void CancelText_DiscardsPendingAnnotation()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Text);
        session.BeginText(new PixelPoint(0, 0));
        session.UpdateTextPreview("abc");

        Assert.True(session.CancelText());
        Assert.False(session.IsTextEditing);
        Assert.Null(session.Preview);
        Assert.Equal(0, session.Count);
        Assert.False(session.CancelText());
    }

    [Fact]
    public void BeginText_WhileTextEditingOrDrafting_Throws()
    {
        var textSession = new AnnotationSession();
        textSession.SetActiveTool(AnnotationTool.Text);
        textSession.BeginText(new PixelPoint(0, 0));
        Assert.Throws<InvalidOperationException>(() => textSession.BeginText(new PixelPoint(1, 1)));

        var draftSession = CreateSession();
        draftSession.BeginDraft(new PixelPoint(0, 0));
        Assert.Throws<InvalidOperationException>(() => draftSession.BeginText(new PixelPoint(1, 1)));
    }

    [Fact]
    public void UndoRedo_AreBlockedWhileDraftingOrEditingText()
    {
        var session = CreateSession();
        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(10, 10));
        session.CommitDraft();

        session.BeginDraft(new PixelPoint(20, 20));
        Assert.False(session.Undo());
        Assert.Equal(1, session.Count);
        session.CancelDraft();

        Assert.True(session.Undo());
        Assert.Equal(0, session.Count);
        Assert.True(session.Redo());
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void MultipleAnnotations_KeepInsertionOrderAsZOrder()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(10, 10));
        session.CommitDraft();

        session.SetActiveTool(AnnotationTool.Ellipse);
        session.BeginDraft(new PixelPoint(20, 20));
        session.UpdateDraft(new PixelPoint(40, 40));
        session.CommitDraft();

        Assert.Equal(2, session.Count);
        Assert.Equal(AnnotationKind.Rectangle, session.Document.Items[0].Kind);
        Assert.Equal(AnnotationKind.Ellipse, session.Document.Items[1].Kind);
        Assert.Equal(2, session.History.UndoCount);
    }

    [Fact]
    public void IsSelectionLocked_StaysTrueAfterUndoOnlyWhenOtherAnnotationsRemain()
    {
        var session = CreateSession();

        for (var i = 0; i < 2; i++)
        {
            session.BeginDraft(new PixelPoint(i * 10, i * 10));
            session.UpdateDraft(new PixelPoint((i * 10) + 50, (i * 10) + 50));
            session.CommitDraft();
        }

        Assert.True(session.IsSelectionLocked);
        Assert.True(session.Undo());
        Assert.True(session.IsSelectionLocked); // 还剩一个标注
        Assert.True(session.Undo());
        Assert.False(session.IsSelectionLocked); // 全部撤销 → 解锁
    }

    [Fact]
    public void NewEdit_AfterUndo_ClearsRedoStack()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(10, 10));
        session.CommitDraft();
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        session.BeginDraft(new PixelPoint(30, 30));
        session.UpdateDraft(new PixelPoint(60, 60));
        session.CommitDraft();

        Assert.False(session.CanRedo);
        Assert.Equal(1, session.Count);
    }

    [Fact]
    public void FreehandDraft_SingleClick_IsMeaningfulAndCommitted()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Freehand);

        session.BeginDraft(new PixelPoint(7, 8));
        Assert.True(session.CommitDraft());

        var freehand = Assert.IsType<FreehandAnnotation>(session.Document.Items[0]);
        Assert.Single(freehand.Points);
        Assert.Equal(new PixelPoint(7, 8), freehand.Points[0]);
    }

    [Fact]
    public void MosaicDraft_IgnoresColorAndUsesDefaultBlockSize()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Mosaic);
        session.Settings.Color = AnnotationColor.White;

        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(80, 60));
        Assert.True(session.CommitDraft());

        var mosaic = Assert.IsType<MosaicAnnotation>(session.Document.Items[0]);
        Assert.Equal(new PixelRect(0, 0, 80, 60), mosaic.Region);
        Assert.Equal(AnnotationDefaults.DefaultMosaicBlockSize, mosaic.BlockSize);
    }

    [Fact]
    public void SetActiveTool_Null_MeansSelectModeAndBlocksNothing()
    {
        var session = CreateSession();

        session.BeginDraft(new PixelPoint(0, 0));
        session.UpdateDraft(new PixelPoint(80, 60));
        session.CommitDraft();
        session.SetActiveTool(null);

        // 选择模式下不能新建草稿，但选区仍保持锁定（文档里还有标注）。
        Assert.Equal(1, session.Count);
        Assert.True(session.IsSelectionLocked);
        Assert.Throws<InvalidOperationException>(() => session.BeginDraft(new PixelPoint(1, 1)));
    }

    [Fact]
    public void SessionHistory_UsesProvidedCapacity()
    {
        var session = new AnnotationSession(2);
        session.SetActiveTool(AnnotationTool.Rectangle);

        for (var i = 0; i < 3; i++)
        {
            session.BeginDraft(new PixelPoint(i * 10, 0));
            session.UpdateDraft(new PixelPoint((i * 10) + 40, 40));
            session.CommitDraft();
        }

        Assert.Equal(2, session.History.Capacity);
        Assert.Equal(2, session.History.UndoCount);
        Assert.Equal(3, session.Count);
    }

    [Fact]
    public void CommitText_MultiLineContent_IsKeptAndCounted()
    {
        var session = new AnnotationSession();
        session.SetActiveTool(AnnotationTool.Text);
        session.BeginText(new PixelPoint(10, 10));

        Assert.True(session.CommitText("第一行\r\nsecond line"));

        var text = Assert.IsType<TextAnnotation>(session.Document.Items[0]);
        Assert.Equal(2, text.LineCount);
        Assert.True(text.Bounds.Height >= text.LineHeight * 2);
    }
}
