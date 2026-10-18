using Xunit;
using ZeepCut.Core.Capture;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Capture;

public class WindowCandidateSelectorTests
{
    // 本机真实布局：主屏 2560x1440 @125%（DPI 120），副屏 1080x1920 @100%（DPI 96），副屏原点为负。
    private static readonly PixelRect PrimaryScreen = new(0, 0, 2560, 1440);
    private static readonly PixelRect SecondaryScreen = new(-1080, 0, 1080, 1920);

    private static WindowCandidate Candidate(int left, int top, int width, int height, string title = "window") =>
        new(new IntPtr(0x1234), new PixelRect(left, top, width, height), title);

    [Fact]
    public void ClipToScreen_KeepsOnlyIntersectionOfCrossScreenWindow()
    {
        // 窗口左边越出主屏左侧 100px。
        var candidates = new[] { Candidate(-100, 100, 500, 400) };

        var clipped = WindowCandidateSelector.ClipToScreen(candidates, PrimaryScreen);

        var only = Assert.Single(clipped);
        Assert.Equal(new PixelRect(0, 100, 400, 400), only.Bounds);
    }

    [Fact]
    public void ClipToScreen_NegativeOriginScreen_ClipsCorrectly()
    {
        // 负坐标副屏：窗口同时越出左、上边界。
        var candidates = new[] { Candidate(-1200, -100, 500, 500) };

        var clipped = WindowCandidateSelector.ClipToScreen(candidates, SecondaryScreen);

        var only = Assert.Single(clipped);
        Assert.Equal(new PixelRect(-1080, 0, 380, 400), only.Bounds);
    }

    [Theory]
    [InlineData(2560, 0, 100, 100)]   // 完全在主屏右侧
    [InlineData(0, 1440, 100, 100)]   // 完全在主屏下方
    [InlineData(-100, 0, 100, 100)]   // 与左边界仅相接，没有共享像素
    [InlineData(2600, 0, 100, 100)]   // 更靠右，仍然完全在外
    public void ClipToScreen_DropsEmptyIntersection(int left, int top, int width, int height)
    {
        var clipped = WindowCandidateSelector.ClipToScreen(
            new[] { Candidate(left, top, width, height) },
            PrimaryScreen);

        Assert.Empty(clipped);
    }

    [Fact]
    public void ClipToScreen_DropsEmptyCandidate()
    {
        var clipped = WindowCandidateSelector.ClipToScreen(new[] { Candidate(10, 10, 0, 0) }, PrimaryScreen);

        Assert.Empty(clipped);
    }

    [Fact]
    public void ClipToScreen_PreservesZOrder()
    {
        var candidates = new[]
        {
            Candidate(10, 10, 100, 100, "top"),
            Candidate(20, 20, 100, 100, "middle"),
            Candidate(30, 30, 100, 100, "bottom"),
        };

        var clipped = WindowCandidateSelector.ClipToScreen(candidates, PrimaryScreen);

        Assert.Equal(new[] { "top", "middle", "bottom" }, clipped.Select(c => c.Title));
    }

    [Fact]
    public void ClipToScreen_DoesNotMutateInputCandidates()
    {
        var original = Candidate(-100, 100, 500, 400);
        var candidates = new[] { original };

        var clipped = WindowCandidateSelector.ClipToScreen(candidates, PrimaryScreen);

        Assert.Equal(new PixelRect(-100, 100, 500, 400), candidates[0].Bounds);
        Assert.NotEqual(candidates[0], clipped[0]);
    }

    [Fact]
    public void ClipToScreen_EmptyScreen_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowCandidateSelector.ClipToScreen(new[] { Candidate(0, 0, 10, 10) }, PixelRect.Empty));
    }

    [Fact]
    public void HitTest_TakesFirstCandidateInZOrder()
    {
        var candidates = new[]
        {
            Candidate(100, 100, 200, 200, "top"),
            Candidate(150, 150, 200, 200, "bottom"),
        };

        var hit = WindowCandidateSelector.HitTest(candidates, 180, 180);

        Assert.NotNull(hit);
        Assert.Equal("top", hit.Title);
    }

    [Fact]
    public void HitTest_ReturnsNullWhenNoCandidateContainsPoint()
    {
        var candidates = new[] { Candidate(100, 100, 200, 200) };

        Assert.Null(WindowCandidateSelector.HitTest(candidates, 99, 150));
        Assert.Null(WindowCandidateSelector.HitTest(candidates, 300, 150)); // 右边界不包含
        Assert.Null(WindowCandidateSelector.HitTest(Array.Empty<WindowCandidate>(), 150, 150));
    }

    [Fact]
    public void HitTest_LeftTopInclusiveRightBottomExclusive()
    {
        var candidates = new[] { Candidate(-1080, 0, 100, 100) };

        Assert.NotNull(WindowCandidateSelector.HitTest(candidates, -1080, 0));
        Assert.NotNull(WindowCandidateSelector.HitTest(candidates, -981, 99));
        Assert.Null(WindowCandidateSelector.HitTest(candidates, -980, 99)); // x == Right
        Assert.Null(WindowCandidateSelector.HitTest(candidates, -981, 100)); // y == Bottom
    }

    [Fact]
    public void HitTestDip_ConvertsDipsToPhysicalPixelsOn125Percent()
    {
        // 125%：1 DIP = 1.25 px。物理 (1250,250) 的 DIP 位置是 (1000,200)。
        var candidates = new[] { Candidate(1250, 250, 250, 250) };

        var hit = WindowCandidateSelector.HitTestDip(candidates, 1000, 200, PrimaryScreen, 120);

        Assert.NotNull(hit);
        Assert.Equal(new PixelRect(1250, 250, 250, 250), hit.Bounds);
    }

    [Fact]
    public void HitTestDip_DipJustOutsidePhysicalLeftEdge_DoesNotHit()
    {
        // 999 DIP * 1.25 = 1248.75 → 1249 px，落在窗口左边 1250 之外。
        var candidates = new[] { Candidate(1250, 250, 250, 250) };

        Assert.Null(WindowCandidateSelector.HitTestDip(candidates, 999, 200, PrimaryScreen, 120));
    }

    [Fact]
    public void HitTestDip_NegativeOriginScreen_AddsScreenOrigin()
    {
        // 副屏物理原点 (-1080,0)：DIP (80,100) → 物理 (-1000,100)。
        var candidates = new[] { Candidate(-1000, 100, 200, 200) };

        Assert.NotNull(WindowCandidateSelector.HitTestDip(candidates, 80, 100, SecondaryScreen, 96));
        Assert.Null(WindowCandidateSelector.HitTestDip(candidates, 79, 100, SecondaryScreen, 96));
    }

    [Fact]
    public void HitTestDip_EmptyScreen_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WindowCandidateSelector.HitTestDip(Array.Empty<WindowCandidate>(), 0, 0, PixelRect.Empty, 96));
    }

    [Fact]
    public void HitTestDip_InvalidDpi_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowCandidateSelector.HitTestDip(Array.Empty<WindowCandidate>(), 0, 0, PrimaryScreen, 0));
    }

    [Fact]
    public void ToDipBounds_ConvertsPhysicalRectRelativeToScreenOrigin()
    {
        var dip = WindowCandidateSelector.ToDipBounds(new PixelRect(100, 200, 300, 400), PrimaryScreen, 120);

        Assert.Equal(80, dip.Left, 6);
        Assert.Equal(160, dip.Top, 6);
        Assert.Equal(240, dip.Width, 6);
        Assert.Equal(320, dip.Height, 6);
    }

    [Fact]
    public void ToDipBounds_NegativeOriginScreen_ProducesRelativeCoordinates()
    {
        var dip = WindowCandidateSelector.ToDipBounds(new PixelRect(-1000, 100, 200, 200), SecondaryScreen, 96);

        Assert.Equal(80, dip.Left, 6);
        Assert.Equal(100, dip.Top, 6);
        Assert.Equal(200, dip.Width, 6);
        Assert.Equal(200, dip.Height, 6);
    }
}
