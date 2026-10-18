using Xunit;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Selection;

namespace ZeepCut.Tests.Selection;

public class SelectionControllerTests
{
    private static readonly PixelRect Screen = new(0, 0, 1000, 800);

    private static SelectionController NewController(int minimumSize = 4, PixelRect? screen = null) =>
        new(screen ?? Screen, minimumSize);

    /// <summary>用框选手势建立一个 100x100 以上的有效选区，保证处于 Selected。</summary>
    private static SelectionController WithSelection(PixelRect rect)
    {
        var controller = NewController();
        controller.PointerDown(rect.Left, rect.Top);
        controller.PointerDragged(rect.Right, rect.Bottom);
        controller.PointerUp(rect.Right, rect.Bottom);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(rect, controller.Selection);
        return controller;
    }

    [Fact]
    public void NewController_StartsIdle()
    {
        var controller = NewController();

        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.False(controller.HoverCandidate.HasValue);
        Assert.Equal(SelectionHandle.None, controller.ActiveHandle);
        Assert.False(controller.IsCaptureReady);
        Assert.Equal(Screen, controller.ScreenBounds);
        Assert.Equal(4, controller.MinimumSize);
    }

    [Fact]
    public void HoverPressDragRelease_FollowsExpectedStateSequence()
    {
        var controller = NewController();
        controller.SetWindowCandidates(new[] { new PixelRect(100, 100, 300, 200) });

        controller.PointerMoved(150, 150);
        Assert.Equal(SelectionState.HoverWindow, controller.State);
        Assert.Equal(new PixelRect(100, 100, 300, 200), controller.HoverCandidate!.Value);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.False(controller.IsCaptureReady);

        controller.PointerDown(200, 200);
        Assert.Equal(SelectionState.Dragging, controller.State);
        Assert.False(controller.HoverCandidate.HasValue);
        Assert.False(controller.IsCaptureReady);

        controller.PointerDragged(400, 350);
        Assert.Equal(SelectionState.Dragging, controller.State);
        Assert.Equal(new PixelRect(200, 200, 200, 150), controller.Selection);
        Assert.False(controller.IsCaptureReady);

        controller.PointerUp(400, 350);
        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(new PixelRect(200, 200, 200, 150), controller.Selection);
        Assert.True(controller.IsCaptureReady);
    }

    [Theory]
    [InlineData(500, 500, 100, 100)] // 左上方向
    [InlineData(100, 100, 500, 500)] // 右下方向
    [InlineData(500, 100, 100, 500)] // 左下方向
    [InlineData(100, 500, 500, 100)] // 右上方向
    public void Drag_AnyDirection_ProducesPositiveRect(int x1, int y1, int x2, int y2)
    {
        var controller = NewController();

        controller.PointerDown(x1, y1);
        controller.PointerDragged(x2, y2);
        controller.PointerUp(x2, y2);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(new PixelRect(100, 100, 400, 400), controller.Selection);
        Assert.True(controller.IsCaptureReady);
    }

    [Fact]
    public void Drag_BeyondScreen_ClampsToScreen()
    {
        var controller = NewController();

        controller.PointerDown(10, 20);
        controller.PointerDragged(5000, 5000);
        controller.PointerUp(5000, 5000);

        Assert.Equal(new PixelRect(10, 20, 990, 780), controller.Selection);
        Assert.True(controller.ScreenBounds.Contains(controller.Selection));
    }

    [Fact]
    public void Drag_FromScreenEdgeToOppositeEdge_CoversWholeScreen()
    {
        var controller = NewController();

        controller.PointerDown(Screen.Left, Screen.Top);
        controller.PointerDragged(Screen.Right, Screen.Bottom);
        controller.PointerUp(Screen.Right, Screen.Bottom);

        Assert.Equal(Screen, controller.Selection);
        Assert.True(controller.IsCaptureReady);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 3)]
    public void Drag_BelowMinimumSize_EndsWithNoSelection(int width, int height)
    {
        var controller = NewController(minimumSize: 4);

        controller.PointerDown(10, 10);
        controller.PointerDragged(10 + width, 10 + height);
        controller.PointerUp(10 + width, 10 + height);

        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.False(controller.IsCaptureReady);
    }

    [Fact]
    public void Drag_ExactlyMinimumSize_IsCaptureReady()
    {
        var controller = NewController(minimumSize: 4);

        controller.PointerDown(10, 10);
        controller.PointerDragged(14, 14);
        controller.PointerUp(14, 14);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(new PixelRect(10, 10, 4, 4), controller.Selection);
        Assert.True(controller.IsCaptureReady);
    }

    [Fact]
    public void CustomMinimumSize_IsUsedForValidity()
    {
        var controller = NewController(minimumSize: 10);

        controller.PointerDown(10, 10);
        controller.PointerDragged(19, 19); // 9x9
        controller.PointerUp(19, 19);
        Assert.Equal(SelectionState.None, controller.State);

        controller.PointerDown(10, 10);
        controller.PointerDragged(20, 20); // 10x10
        controller.PointerUp(20, 20);
        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.True(controller.IsCaptureReady);
        Assert.Equal(10, controller.MinimumSize);
    }

    [Fact]
    public void Hover_CandidateOutsideScreen_IsClipped()
    {
        var controller = NewController();
        controller.SetWindowCandidates(new[] { new PixelRect(-100, 50, 400, 300) });

        controller.PointerMoved(10, 100);

        Assert.Equal(SelectionState.HoverWindow, controller.State);
        Assert.Equal(new PixelRect(0, 50, 300, 300), controller.HoverCandidate!.Value);
    }

    [Fact]
    public void Hover_CandidatesFullyOutsideScreen_AreDropped()
    {
        var controller = NewController();
        controller.SetWindowCandidates(new[]
        {
            new PixelRect(2000, 2000, 100, 100),
            new PixelRect(100, 100, 200, 200),
        });

        Assert.Single(controller.WindowCandidates);

        controller.PointerMoved(150, 150);
        Assert.Equal(new PixelRect(100, 100, 200, 200), controller.HoverCandidate!.Value);
    }

    [Fact]
    public void Hover_MultipleCandidates_UsesFirstTopmost()
    {
        var controller = NewController();
        controller.SetWindowCandidates(new[]
        {
            new PixelRect(100, 100, 200, 200),
            new PixelRect(0, 0, 500, 500),
        });

        controller.PointerMoved(150, 150);

        Assert.Equal(new PixelRect(100, 100, 200, 200), controller.HoverCandidate!.Value);
    }

    [Fact]
    public void Hover_NoCandidate_StaysIdle()
    {
        var controller = NewController();
        controller.SetWindowCandidates(new[] { new PixelRect(100, 100, 200, 200) });

        controller.PointerMoved(700, 700);

        Assert.Equal(SelectionState.None, controller.State);
        Assert.False(controller.HoverCandidate.HasValue);
    }

    [Fact]
    public void SetWindowCandidates_AfterPointerMove_ReevaluatesHover()
    {
        var controller = NewController();
        controller.PointerMoved(150, 150);
        Assert.Equal(SelectionState.None, controller.State);

        controller.SetWindowCandidates(new[] { new PixelRect(100, 100, 200, 200) });

        Assert.Equal(SelectionState.HoverWindow, controller.State);
        Assert.Equal(new PixelRect(100, 100, 200, 200), controller.HoverCandidate!.Value);
    }

    [Fact]
    public void ClickOnHoverCandidate_AdoptsCandidate()
    {
        var controller = NewController();
        var candidate = new PixelRect(100, 100, 300, 200);
        controller.SetWindowCandidates(new[] { candidate });
        controller.PointerMoved(150, 150);

        controller.PointerDown(150, 150);
        Assert.Equal(SelectionState.Dragging, controller.State);

        controller.PointerUp(150, 150);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(candidate, controller.Selection);
        Assert.True(controller.IsCaptureReady);
    }

    [Fact]
    public void TinyDragOnHoverCandidate_StillAdoptsCandidate()
    {
        var controller = NewController();
        var candidate = new PixelRect(100, 100, 300, 200);
        controller.SetWindowCandidates(new[] { candidate });
        controller.PointerMoved(150, 150);

        controller.PointerDown(150, 150);
        controller.PointerDragged(152, 152); // 2x2，低于最小尺寸
        controller.PointerUp(152, 152);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(candidate, controller.Selection);
    }

    [Fact]
    public void TinyDragOnEmptyArea_LeavesNoSelection()
    {
        var controller = NewController();

        controller.PointerDown(600, 600);
        controller.PointerDragged(602, 602);
        controller.PointerUp(602, 602);

        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
    }

    [Fact]
    public void ClickOnCandidateSmallerThanMinimum_LeavesNoSelection()
    {
        var controller = NewController(minimumSize: 4);
        controller.SetWindowCandidates(new[] { new PixelRect(100, 100, 3, 3) });
        controller.PointerMoved(101, 101);

        controller.PointerDown(101, 101);
        controller.PointerUp(101, 101);

        // 候选有效但小于最小尺寸：不产生选区，指针仍停在窗口上，回到悬停。
        Assert.Equal(SelectionState.HoverWindow, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.False(controller.IsCaptureReady);
    }

    [Fact]
    public void Selected_DragCenter_MovesSelectionWithoutResizing()
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(200, 150);
        Assert.Equal(SelectionState.Adjusting, controller.State);
        Assert.Equal(SelectionHandle.Move, controller.ActiveHandle);

        controller.PointerDragged(300, 250);
        Assert.Equal(new PixelRect(200, 200, 200, 100), controller.Selection);

        controller.PointerUp(300, 250);
        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(SelectionHandle.None, controller.ActiveHandle);
        Assert.Equal(new PixelRect(200, 200, 200, 100), controller.Selection);
    }

    [Theory]
    [InlineData(-500, -500, 0, 0)]
    [InlineData(5000, 5000, 800, 700)]
    public void Move_PushedOutside_IsClampedBackInside(
        int targetX, int targetY, int expectedLeft, int expectedTop)
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(200, 150);
        controller.PointerDragged(targetX, targetY);
        controller.PointerUp(targetX, targetY);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, 200, 100), controller.Selection);
        Assert.True(controller.ScreenBounds.Contains(controller.Selection));
    }

    [Theory]
    [InlineData(SelectionHandle.TopLeft, 100, 100, 80, 70, 80, 70, 220, 130)]
    [InlineData(SelectionHandle.Top, 200, 100, 200, 70, 100, 70, 200, 130)]
    [InlineData(SelectionHandle.TopRight, 299, 100, 330, 70, 100, 70, 230, 130)]
    [InlineData(SelectionHandle.Right, 299, 150, 330, 150, 100, 100, 230, 100)]
    [InlineData(SelectionHandle.BottomRight, 299, 199, 330, 230, 100, 100, 230, 130)]
    [InlineData(SelectionHandle.Bottom, 200, 199, 200, 230, 100, 100, 200, 130)]
    [InlineData(SelectionHandle.BottomLeft, 100, 199, 70, 230, 70, 100, 230, 130)]
    [InlineData(SelectionHandle.Left, 100, 150, 70, 150, 70, 100, 230, 100)]
    public void Adjust_EachHandle_UpdatesOnlyItsEdges(
        SelectionHandle handle,
        int downX, int downY, int dragX, int dragY,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));
        var expected = new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight);

        controller.PointerDown(downX, downY);
        Assert.Equal(SelectionState.Adjusting, controller.State);
        Assert.Equal(handle, controller.ActiveHandle);

        controller.PointerDragged(dragX, dragY);
        Assert.Equal(expected, controller.Selection);

        controller.PointerUp(dragX, dragY);
        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(expected, controller.Selection);
    }

    [Theory]
    [InlineData(SelectionHandle.Left, 100, 150, 500, 150, 296, 100, 4, 100)]  // 左边缘拖过右边缘
    [InlineData(SelectionHandle.Right, 299, 150, -500, 150, 100, 100, 4, 100)] // 右边缘拖过左边缘
    [InlineData(SelectionHandle.Top, 200, 100, 200, 500, 100, 196, 200, 4)]    // 上边缘拖过下边缘
    [InlineData(SelectionHandle.Bottom, 200, 199, 200, -500, 100, 100, 200, 4)] // 下边缘拖过上边缘
    public void Adjust_PastOppositeEdge_ClampsAtMinimumSizeWithoutFlipping(
        SelectionHandle handle,
        int downX, int downY, int dragX, int dragY,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(downX, downY);
        Assert.Equal(handle, controller.ActiveHandle);
        controller.PointerDragged(dragX, dragY);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight), controller.Selection);
        Assert.Equal(SelectionState.Adjusting, controller.State);
    }

    [Theory]
    [InlineData(SelectionHandle.Left, 100, 150, -100, 150, 0, 100, 300, 100)]
    [InlineData(SelectionHandle.Right, 299, 150, 5000, 150, 100, 100, 900, 100)]
    [InlineData(SelectionHandle.Top, 200, 100, 200, -100, 100, 0, 200, 200)]
    [InlineData(SelectionHandle.Bottom, 200, 199, 200, 5000, 100, 100, 200, 700)]
    public void Adjust_OutOfScreen_ClampsToScreen(
        SelectionHandle handle,
        int downX, int downY, int dragX, int dragY,
        int expectedLeft, int expectedTop, int expectedWidth, int expectedHeight)
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(downX, downY);
        Assert.Equal(handle, controller.ActiveHandle);
        controller.PointerDragged(dragX, dragY);

        Assert.Equal(new PixelRect(expectedLeft, expectedTop, expectedWidth, expectedHeight), controller.Selection);
        Assert.True(controller.ScreenBounds.Contains(controller.Selection));
    }

    [Theory]
    [InlineData(200, 150, SelectionHandle.Move)]
    [InlineData(100, 100, SelectionHandle.TopLeft)]
    [InlineData(299, 100, SelectionHandle.TopRight)]
    [InlineData(299, 199, SelectionHandle.BottomRight)]
    [InlineData(100, 199, SelectionHandle.BottomLeft)]
    [InlineData(100, 150, SelectionHandle.Left)]
    [InlineData(299, 150, SelectionHandle.Right)]
    [InlineData(200, 100, SelectionHandle.Top)]
    [InlineData(200, 199, SelectionHandle.Bottom)]
    [InlineData(50, 50, SelectionHandle.None)]   // 选区外
    [InlineData(300, 150, SelectionHandle.None)] // Right 开边界
    [InlineData(100, 200, SelectionHandle.None)] // Bottom 开边界
    public void HitTest_MapsPointToHandle(int x, int y, SelectionHandle expected)
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        Assert.Equal(expected, controller.HitTest(x, y));
    }

    [Fact]
    public void HitTest_WithoutSelection_ReturnsNone()
    {
        Assert.Equal(SelectionHandle.None, NewController().HitTest(10, 10));
    }

    [Fact]
    public void PressWithoutSelection_StartsDraggingNotAdjusting()
    {
        var controller = NewController();

        controller.PointerDown(100, 100);

        Assert.Equal(SelectionState.Dragging, controller.State);
        Assert.Equal(SelectionHandle.None, controller.ActiveHandle);
        Assert.Equal(PixelRect.Empty, controller.Selection);
    }

    [Fact]
    public void PressOutsideSelection_StartsNewDrag()
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(500, 500);
        Assert.Equal(SelectionState.Dragging, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);

        controller.PointerDragged(600, 600);
        controller.PointerUp(600, 600);
        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(new PixelRect(500, 500, 100, 100), controller.Selection);
    }

    [Fact]
    public void PointerMoved_AfterSelection_KeepsSelection()
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));
        controller.SetWindowCandidates(new[] { Screen });

        controller.PointerMoved(10, 10);

        Assert.Equal(SelectionState.Selected, controller.State);
        Assert.Equal(new PixelRect(100, 100, 200, 100), controller.Selection);
        Assert.False(controller.HoverCandidate.HasValue);
    }

    [Fact]
    public void PointerMoved_DuringDrag_DoesNotChangeSelection()
    {
        var controller = NewController();

        controller.PointerDown(10, 10);
        controller.PointerDragged(50, 50);
        controller.PointerMoved(900, 700);

        Assert.Equal(SelectionState.Dragging, controller.State);
        Assert.Equal(new PixelRect(10, 10, 40, 40), controller.Selection);
    }

    [Fact]
    public void IllegalTransitions_AreIgnoredWithoutThrowing()
    {
        var controller = NewController();

        // 未按下就拖动/抬起：无效果。
        controller.PointerDragged(100, 100);
        controller.PointerUp(100, 100);
        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);

        // 拖动期间重复按下：保留第一次按下的锚点。
        controller.PointerDown(10, 10);
        controller.PointerDown(500, 500);
        controller.PointerDragged(20, 20);
        Assert.Equal(new PixelRect(10, 10, 10, 10), controller.Selection);

        // 取消后继续拖动/抬起：无效果。
        controller.Cancel();
        Assert.Equal(SelectionState.None, controller.State);
        controller.PointerDragged(300, 300);
        controller.PointerUp(300, 300);
        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.Equal(SelectionHandle.None, controller.ActiveHandle);
    }

    [Fact]
    public void Cancel_DuringAdjust_ReturnsToIdle()
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));

        controller.PointerDown(100, 150);
        controller.PointerDragged(50, 150);
        Assert.Equal(SelectionState.Adjusting, controller.State);

        controller.Cancel();

        Assert.Equal(SelectionState.None, controller.State);
        Assert.Equal(PixelRect.Empty, controller.Selection);
        Assert.Equal(SelectionHandle.None, controller.ActiveHandle);
        Assert.False(controller.IsCaptureReady);
    }

    [Fact]
    public void IsCaptureReady_IsFalseWhileAdjusting()
    {
        var controller = WithSelection(new PixelRect(100, 100, 200, 100));
        Assert.True(controller.IsCaptureReady);

        controller.PointerDown(200, 150);
        Assert.Equal(SelectionState.Adjusting, controller.State);
        Assert.False(controller.IsCaptureReady);

        controller.PointerUp(200, 150);
        Assert.True(controller.IsCaptureReady);
    }

    [Fact]
    public void NegativeCoordinateScreen_DragAndClampWork()
    {
        var screen = new PixelRect(-1080, 0, 1080, 1920);
        var controller = NewController(screen: screen);

        controller.PointerDown(-500, 500);
        controller.PointerDragged(-2000, 2000);
        controller.PointerUp(-2000, 2000);

        Assert.Equal(new PixelRect(-1080, 500, 580, 1420), controller.Selection);
        Assert.True(controller.ScreenBounds.Contains(controller.Selection));
    }

    [Fact]
    public void Constructor_EmptyScreen_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SelectionController(PixelRect.Empty));
    }

    [Fact]
    public void Constructor_NonPositiveMinimumSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SelectionController(Screen, minimumSize: 0));
    }

    [Fact]
    public void Constructor_MinimumSizeLargerThanScreen_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SelectionController(new PixelRect(0, 0, 3, 100), minimumSize: 4));
    }

    [Fact]
    public void Constructor_NonPositiveHandleTolerance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SelectionController(Screen, minimumSize: 4, handleTolerance: 0));
    }

    [Fact]
    public void SetWindowCandidates_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewController().SetWindowCandidates(null!));
    }
}
