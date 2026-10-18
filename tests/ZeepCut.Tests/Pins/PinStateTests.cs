using Xunit;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Pins;

namespace ZeepCut.Tests.Pins;

public class PinStateTests
{
    private static PinState Create(int width = 400, int height = 300, int x = 100, int y = 200) =>
        PinState.Create(width, height, new PixelPoint(x, y));

    // --- 初始状态 ---

    [Fact]
    public void Create_Defaults_OriginalSizeOpaqueUnlocked()
    {
        var state = Create();

        Assert.Equal(400, state.SourceWidth);
        Assert.Equal(300, state.SourceHeight);
        Assert.Equal(1.0, state.Scale);
        Assert.True(state.IsOriginalSize);
        Assert.Equal(1.0, state.Opacity);
        Assert.False(state.IsLocked);
        Assert.True(state.CanDrag);
        Assert.Equal(new PixelRect(100, 200, 400, 300), state.Bounds);
        Assert.Equal(400, state.DisplayWidth);
        Assert.Equal(300, state.DisplayHeight);
    }

    [Fact]
    public void Create_NonPositiveSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PinState.Create(0, 300, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => PinState.Create(400, 0, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => PinState.Create(-5, 300, default));
    }

    // --- 缩放上下限 ---

    [Fact]
    public void ScaleTo_GrowsAndShrinks_DisplaySizeFollows()
    {
        var state = Create();

        var twice = state.ScaleTo(2.0, new PixelPoint(300, 350));
        Assert.Equal(800, twice.DisplayWidth);
        Assert.Equal(600, twice.DisplayHeight);

        var half = state.ScaleTo(0.5, new PixelPoint(300, 350));
        Assert.Equal(200, half.DisplayWidth);
        Assert.Equal(150, half.DisplayHeight);
    }

    [Fact]
    public void ScaleTo_AboveMaximum_ClampsTo8x()
    {
        var state = Create(100, 100, 0, 0);

        var scaled = state.ScaleTo(50.0, new PixelPoint(0, 0));

        Assert.Equal(PinState.MaximumScale, scaled.Scale);
        Assert.Equal(800, scaled.DisplayWidth);
        Assert.Equal(800, scaled.DisplayHeight);
    }

    [Fact]
    public void ScaleTo_BelowMinimum_ClampsTo40Pixels()
    {
        var state = Create(400, 300, 0, 0);

        var scaled = state.ScaleTo(0.001, new PixelPoint(0, 0));

        // 400×300 原图的最小倍率由高度决定：40/300。
        Assert.Equal(40 / 300.0, scaled.Scale, precision: 12);
        Assert.Equal(PinState.MinimumDisplaySize, scaled.DisplayHeight);
        Assert.True(scaled.DisplayWidth >= PinState.MinimumDisplaySize);
    }

    [Fact]
    public void ScaleTo_TinySource_KeepsMinimumSizeInsteadOfEightTimes()
    {
        // 2×2 原图：8 倍只有 16 像素，会看不见；最小尺寸优先。
        var state = Create(2, 2, 10, 10);

        var scaled = state.ScaleTo(8.0, new PixelPoint(10, 10));

        Assert.Equal(20.0, scaled.MinimumScale);
        Assert.Equal(PinState.MinimumDisplaySize, scaled.DisplayWidth);
        Assert.Equal(PinState.MinimumDisplaySize, scaled.DisplayHeight);
    }

    [Fact]
    public void ScaleTo_InvalidValues_ReturnSameState()
    {
        var state = Create();

        Assert.Equal(state, state.ScaleTo(double.NaN, new PixelPoint(0, 0)));
        Assert.Equal(state, state.ScaleTo(double.PositiveInfinity, new PixelPoint(0, 0)));
        Assert.Equal(state, state.ScaleTo(0, new PixelPoint(0, 0)));
        Assert.Equal(state, state.ScaleTo(-2, new PixelPoint(0, 0)));
    }

    [Fact]
    public void ScaleTo_AtLimit_ReturnsSameState()
    {
        var state = Create(100, 100, 0, 0).ScaleTo(PinState.MaximumScale, new PixelPoint(400, 400));

        Assert.Equal(state, state.ScaleTo(PinState.MaximumScale * 2, new PixelPoint(400, 400)));
    }

    // --- 锚点计算 ---

    [Fact]
    public void ScaleTo_AnchorAtTopLeft_KeepsTopLeftFixed()
    {
        var state = Create(400, 300, 100, 200);

        var scaled = state.ScaleTo(2.0, new PixelPoint(100, 200));

        Assert.Equal(new PixelPoint(100, 200), scaled.TopLeft);
        Assert.Equal(new PixelRect(100, 200, 800, 600), scaled.Bounds);
    }

    [Fact]
    public void ScaleTo_AnchorAtCenter_KeepsCenterFixed()
    {
        var state = Create(400, 300, 100, 200);

        // 中心 (300, 350)。
        var scaled = state.ScaleTo(2.0, new PixelPoint(300, 350));

        Assert.Equal(800, scaled.Bounds.Width);
        Assert.Equal(600, scaled.Bounds.Height);
        Assert.Equal(300, scaled.Bounds.Left + (scaled.Bounds.Width / 2));
        Assert.Equal(350, scaled.Bounds.Top + (scaled.Bounds.Height / 2));
    }

    [Fact]
    public void ScaleTo_AnchorInside_KeepsAnchorPixelAtSameScreenPoint()
    {
        var state = Create(400, 300, 100, 200);
        // 锚点在旧窗口内的相对比例 (0.25, 0.75) → 屏幕 (200, 425)。
        var anchor = new PixelPoint(100 + 100, 200 + 225);

        var scaled = state.ScaleTo(1.5, anchor);

        var expectedLeft = (int)Math.Round(anchor.X - 0.25 * scaled.Bounds.Width, MidpointRounding.AwayFromZero);
        var expectedTop = (int)Math.Round(anchor.Y - 0.75 * scaled.Bounds.Height, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedLeft, scaled.Bounds.Left);
        Assert.Equal(expectedTop, scaled.Bounds.Top);
    }

    [Fact]
    public void ScaleTo_AnchorOutside_ClampsToNearestEdge()
    {
        var state = Create(400, 300, 100, 200);

        // 锚点远在窗口左上方之外：按左上角锚定，不会甩飞。
        var scaled = state.ScaleTo(2.0, new PixelPoint(-5000, -5000));

        Assert.Equal(new PixelPoint(100, 200), scaled.TopLeft);
    }

    [Fact]
    public void ScaleTo_AnchorRightOfWindow_ClampsToRightEdge()
    {
        var state = Create(400, 300, 100, 200);

        var scaled = state.ScaleTo(2.0, new PixelPoint(9999, 9999));

        // 右下角锚定：右边界仍为 500 / 下边界仍为 500。
        Assert.Equal(500, scaled.Bounds.Right);
        Assert.Equal(500, scaled.Bounds.Bottom);
    }

    [Fact]
    public void ZoomBy_Factor_KeepsAnchor()
    {
        var state = Create(400, 300, 0, 0);
        var anchor = new PixelPoint(0, 0);

        var zoomedIn = state.ZoomBy(PinState.ZoomStep, anchor);
        Assert.True(zoomedIn.Scale > 1.0);
        Assert.Equal(new PixelPoint(0, 0), zoomedIn.TopLeft);

        var zoomedOut = state.ZoomBy(1 / PinState.ZoomStep, anchor);
        Assert.True(zoomedOut.Scale < 1.0);
    }

    [Fact]
    public void ZoomBy_RepeatedSteps_StopAtLimits()
    {
        var state = Create(100, 100, 0, 0);
        for (var i = 0; i < 100; i++)
        {
            state = state.ZoomBy(PinState.ZoomStep, new PixelPoint(50, 50));
        }

        Assert.Equal(PinState.MaximumScale, state.Scale);

        for (var i = 0; i < 200; i++)
        {
            state = state.ZoomBy(1 / PinState.ZoomStep, new PixelPoint(50, 50));
        }

        Assert.True(state.DisplayWidth >= PinState.MinimumDisplaySize);
        Assert.True(state.DisplayHeight >= PinState.MinimumDisplaySize);
    }

    // --- 原始大小 ---

    [Fact]
    public void ResetToOriginal_RestoresScaleAndKeepsCenter()
    {
        var state = Create(400, 300, 100, 200).ScaleTo(2.0, new PixelPoint(300, 350));
        var centerBefore = new PixelPoint(
            state.Bounds.Left + (state.Bounds.Width / 2),
            state.Bounds.Top + (state.Bounds.Height / 2));

        var reset = state.ResetToOriginal();

        Assert.True(reset.IsOriginalSize);
        Assert.Equal(400, reset.DisplayWidth);
        Assert.Equal(300, reset.DisplayHeight);
        var centerAfter = new PixelPoint(
            reset.Bounds.Left + (reset.Bounds.Width / 2),
            reset.Bounds.Top + (reset.Bounds.Height / 2));
        Assert.Equal(centerBefore, centerAfter);
    }

    [Fact]
    public void ResetToOriginal_WhenAlreadyOriginal_ReturnsSameState()
    {
        var state = Create();

        Assert.Equal(state, state.ResetToOriginal());
    }

    // --- 透明度下限 ---

    [Fact]
    public void WithOpacity_BelowMinimum_ClampsToVisibleFloor()
    {
        var state = Create();

        var faded = state.WithOpacity(0.0);

        Assert.Equal(PinState.MinimumOpacity, faded.Opacity);
        Assert.Equal(0.2, faded.Opacity);
    }

    [Fact]
    public void WithOpacity_AboveMaximum_ClampsToOne()
    {
        var state = Create().WithOpacity(0.5);

        Assert.Equal(1.0, state.WithOpacity(5.0).Opacity);
    }

    [Fact]
    public void WithOpacity_NonFinite_KeepsCurrentOpacity()
    {
        var state = Create().WithOpacity(0.6);

        Assert.Equal(0.6, state.WithOpacity(double.NaN).Opacity);
        Assert.Equal(0.6, state.WithOpacity(double.PositiveInfinity).Opacity);
    }

    [Fact]
    public void AdjustOpacity_RepeatedDown_StopsAtMinimum()
    {
        var state = Create();
        for (var i = 0; i < 20; i++)
        {
            state = state.AdjustOpacity(-PinState.OpacityStep);
        }

        Assert.Equal(PinState.MinimumOpacity, state.Opacity, precision: 12);
    }

    [Fact]
    public void AdjustOpacity_RepeatedUp_StopsAtMaximum()
    {
        var state = Create();
        for (var i = 0; i < 20; i++)
        {
            state = state.AdjustOpacity(PinState.OpacityStep);
        }

        Assert.Equal(PinState.MaximumOpacity, state.Opacity, precision: 12);
    }

    [Fact]
    public void Opacity_DoesNotAffectBounds()
    {
        var state = Create();

        var faded = state.WithOpacity(0.3);

        Assert.Equal(state.Bounds, faded.Bounds);
        Assert.Equal(state.Scale, faded.Scale);
    }

    // --- 锁定 ---

    [Fact]
    public void DragTo_MovesWhenUnlocked()
    {
        var state = Create();

        var moved = state.DragTo(new PixelPoint(500, 600));

        Assert.Equal(new PixelPoint(500, 600), moved.TopLeft);
        Assert.Equal(new PixelRect(500, 600, 400, 300), moved.Bounds);
    }

    [Fact]
    public void DragTo_WhenLocked_IsRejected()
    {
        var state = Create().WithLock(true);

        var attempted = state.DragTo(new PixelPoint(999, 999));

        Assert.Equal(state.TopLeft, attempted.TopLeft);
        Assert.Equal(state, attempted);
        Assert.False(state.CanDrag);
    }

    [Fact]
    public void TryDragTo_ReportsWhetherItMoved()
    {
        var unlocked = Create();
        Assert.True(unlocked.TryDragTo(new PixelPoint(1, 2), out var moved));
        Assert.Equal(new PixelPoint(1, 2), moved.TopLeft);

        var locked = unlocked.WithLock(true);
        Assert.False(locked.TryDragTo(new PixelPoint(3, 4), out var blocked));
        Assert.Equal(locked.TopLeft, blocked.TopLeft);

        // 位置相同也算“没有移动”。
        Assert.False(unlocked.TryDragTo(unlocked.TopLeft, out _));
    }

    [Fact]
    public void DragBy_WhenLocked_IsRejected()
    {
        var state = Create().WithLock(true);

        Assert.Equal(state.TopLeft, state.DragBy(50, 50).TopLeft);
    }

    [Fact]
    public void LockedPin_StillAllowsZoomOpacityAndPlaceAt()
    {
        var state = Create().WithLock(true);

        var zoomed = state.ScaleTo(2.0, new PixelPoint(100, 200));
        Assert.Equal(2.0, zoomed.Scale);
        Assert.True(zoomed.IsLocked);

        var faded = state.WithOpacity(0.5);
        Assert.Equal(0.5, faded.Opacity);
        Assert.True(faded.IsLocked);

        // PlaceAt 是程序化摆放（不受锁定限制）。
        Assert.Equal(new PixelPoint(0, 0), state.PlaceAt(new PixelPoint(0, 0)).TopLeft);
    }

    [Fact]
    public void WithLock_TogglesAndIsIdempotent()
    {
        var state = Create();

        var locked = state.WithLock(true);
        Assert.True(locked.IsLocked);
        Assert.Equal(locked, locked.WithLock(true));
        Assert.False(locked.ToggleLock().IsLocked);
        Assert.True(state.ToggleLock().IsLocked);
    }

    // --- 静态钳制 ---

    [Fact]
    public void ClampScaleFor_UsesLargerOfBothDirections()
    {
        // 宽 1000、高 100：最小倍率由高度 40/100 = 0.4 决定。
        Assert.Equal(0.4, PinState.ClampScaleFor(1000, 100, 0.01));

        // 宽 100、高 1000：最小倍率由宽度 40/100 = 0.4 决定。
        Assert.Equal(0.4, PinState.ClampScaleFor(100, 1000, 0.01));

        Assert.Equal(PinState.MaximumScale, PinState.ClampScaleFor(100, 100, 100));
        Assert.Equal(2.0, PinState.ClampScaleFor(100, 100, 2.0));
    }

    [Fact]
    public void ClampScaleFor_InvalidSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PinState.ClampScaleFor(0, 10, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PinState.ClampScaleFor(10, 0, 1));
    }

    [Fact]
    public void NegativeCoordinates_AreSupportedForSecondaryScreen()
    {
        var state = PinState.Create(640, 480, new PixelPoint(-1080, 0));

        Assert.Equal(new PixelRect(-1080, 0, 640, 480), state.Bounds);

        var scaled = state.ScaleTo(2.0, new PixelPoint(-1080, 0));
        Assert.Equal(-1080, scaled.Bounds.Left);
        Assert.Equal(1280, scaled.Bounds.Width);
    }
}
