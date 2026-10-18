using Xunit;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Selection;

namespace ZeepCut.Tests.Selection;

public class SelectionOverlayLayoutTests
{
    private static readonly DipRect Client = new(0, 0, 1000, 800);

    [Fact]
    public void FormatSize_UsesMultiplicationSignAndSpaces()
    {
        Assert.Equal("640 × 480", SelectionOverlayLayout.FormatSize(640, 480));
    }

    [Fact]
    public void FormatSize_SeparatorIsUnicode00D7()
    {
        Assert.Contains('\u00D7', SelectionOverlayLayout.SizeSeparator);
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(640, 0)]
    [InlineData(-640, 480)]
    [InlineData(640, -480)]
    public void FormatSize_NonPositive_ReturnsEmpty(int width, int height)
    {
        Assert.Equal(string.Empty, SelectionOverlayLayout.FormatSize(width, height));
    }

    [Fact]
    public void FormatSize_EmptyPixelRect_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SelectionOverlayLayout.FormatSize(PixelRect.Empty));
    }

    [Fact]
    public void FormatSize_PhysicalRect_UsesWidthAndHeight()
    {
        Assert.Equal("640 × 480", SelectionOverlayLayout.FormatSize(new PixelRect(300, 300, 640, 480)));
    }

    [Fact]
    public void PlaceToolbar_WhenRoomBelow_PlacesUnderSelection()
    {
        var selection = new DipRect(100, 100, 200, 100);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 40, gap: 8);

        Assert.Equal(new DipRect(100, 208, 300, 40), result);
    }

    [Fact]
    public void PlaceToolbar_WhenBottomCrowded_PlacesAboveSelection()
    {
        var selection = new DipRect(100, 700, 200, 90);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 40, gap: 8);

        Assert.Equal(new DipRect(100, 652, 300, 40), result);
    }

    [Fact]
    public void PlaceToolbar_KeepsInsideRightEdge()
    {
        var selection = new DipRect(900, 100, 90, 100);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 40, gap: 8);

        Assert.Equal(700, result.Left);
        Assert.True(result.Right <= Client.Right);
    }

    [Fact]
    public void PlaceToolbar_KeepsInsideLeftEdgeForNegativeSelection()
    {
        var selection = new DipRect(-20, 100, 200, 100);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 40, gap: 8);

        Assert.Equal(0, result.Left);
    }

    [Fact]
    public void PlaceToolbar_SelectionTouchingBottom_FallsBackInsideWindow()
    {
        // 选区从 0 一直到底：上下都放不下，夹到窗口内且完整可见。
        var selection = new DipRect(100, 0, 200, 800);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 40, gap: 8);

        Assert.Equal(760, result.Top);
        Assert.Equal(300, result.Width);
        Assert.Equal(40, result.Height);
    }

    [Fact]
    public void PlaceToolbar_ClampsOversizedToolbarToWindow()
    {
        var selection = new DipRect(100, 100, 200, 100);

        var result = SelectionOverlayLayout.PlaceToolbar(selection, Client, 2000, 2000, gap: 8);

        Assert.Equal(new DipRect(0, 0, 1000, 800), result);
    }

    [Fact]
    public void PlaceToolbar_EmptySelectionOrClient_ReturnsEmpty()
    {
        Assert.Equal(
            DipRect.Empty,
            SelectionOverlayLayout.PlaceToolbar(DipRect.Empty, Client, 300, 40));
        Assert.Equal(
            DipRect.Empty,
            SelectionOverlayLayout.PlaceToolbar(new DipRect(100, 100, 200, 100), DipRect.Empty, 300, 40));
    }

    [Fact]
    public void PlaceToolbar_NonPositiveToolbarSize_ReturnsEmpty()
    {
        var selection = new DipRect(100, 100, 200, 100);

        Assert.Equal(DipRect.Empty, SelectionOverlayLayout.PlaceToolbar(selection, Client, 0, 40));
        Assert.Equal(DipRect.Empty, SelectionOverlayLayout.PlaceToolbar(selection, Client, 300, 0));
    }

    [Fact]
    public void PlaceSizeBadge_WhenRoomAbove_PlacesAboveRight()
    {
        var selection = new DipRect(100, 100, 200, 100);

        var result = SelectionOverlayLayout.PlaceSizeBadge(selection, Client, 120, 24, gap: 6);

        Assert.Equal(new DipRect(180, 70, 120, 24), result);
    }

    [Fact]
    public void PlaceSizeBadge_WhenTopCrowded_PlacesBelow()
    {
        var selection = new DipRect(100, 10, 200, 100);

        var result = SelectionOverlayLayout.PlaceSizeBadge(selection, Client, 120, 24, gap: 6);

        Assert.Equal(new DipRect(180, 116, 120, 24), result);
    }

    [Fact]
    public void PlaceSizeBadge_ClampsRightEdge()
    {
        // 选区右边缘到达窗口右边，右上方对齐会把标签推出窗口，必须向左夹回。
        var selection = new DipRect(950, 100, 50, 100);

        var result = SelectionOverlayLayout.PlaceSizeBadge(selection, Client, 120, 24, gap: 6);

        Assert.Equal(880, result.Left);
        Assert.Equal(Client.Right, result.Right);
    }

    [Fact]
    public void PlaceSizeBadge_EmptyInputs_ReturnEmpty()
    {
        Assert.Equal(
            DipRect.Empty,
            SelectionOverlayLayout.PlaceSizeBadge(DipRect.Empty, Client, 120, 24));
        Assert.Equal(
            DipRect.Empty,
            SelectionOverlayLayout.PlaceSizeBadge(new DipRect(100, 100, 200, 100), DipRect.Empty, 120, 24));
        Assert.Equal(
            DipRect.Empty,
            SelectionOverlayLayout.PlaceSizeBadge(new DipRect(100, 100, 200, 100), Client, 0, 24));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(96)]
    [InlineData(144)]
    public void ToPhysical_MapsDipRelativeToScreenOrigin(int dpi)
    {
        // 窗口内 (100,50) 的 DIP 点 → 屏幕物理像素，副屏原点为负也必须正确。
        var screen = new PixelRect(-1080, 0, 1080, 1920);
        var dip = new DipRect(100, 50, 200, 100);

        var physical = SelectionOverlayLayout.ToPhysical(dip, screen, dpi);

        var transform = new DpiTransform(dpi);
        Assert.Equal(screen.Left + transform.DipToPixel(100), physical.Left);
        Assert.Equal(screen.Top + transform.DipToPixel(50), physical.Top);
        Assert.Equal(transform.DipToPixel(200), physical.Width);
        Assert.Equal(transform.DipToPixel(100), physical.Height);
    }

    [Fact]
    public void ToPhysical_EmptyInputs_ReturnEmpty()
    {
        Assert.Equal(PixelRect.Empty, SelectionOverlayLayout.ToPhysical(DipRect.Empty, new PixelRect(0, 0, 100, 100), 96));
        Assert.Equal(PixelRect.Empty, SelectionOverlayLayout.ToPhysical(new DipRect(1, 1, 10, 10), PixelRect.Empty, 96));
    }
}
