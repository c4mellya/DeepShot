using Xunit;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.Pins;

namespace ZeepCut.Tests.Pins;

public class PinPlacementTests
{
    private static readonly PixelRect PrimaryScreen = new(0, 0, 2560, 1440);
    private static readonly PixelRect SecondaryScreen = new(-1080, 0, 1080, 1920);

    // --- 光标附近 ---

    [Fact]
    public void NearCursor_NormalCase_PlacesRightAndBelow()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(800, 600), 320, 240, PrimaryScreen);

        Assert.Equal(800 + PinPlacement.CursorOffset, bounds.Left);
        Assert.Equal(600 + PinPlacement.CursorOffset, bounds.Top);
        Assert.Equal(320, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void NearCursor_KeepsOriginalImageSize()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(100, 100), 640, 420, PrimaryScreen);

        Assert.Equal(640, bounds.Width);
        Assert.Equal(420, bounds.Height);
    }

    // --- 屏幕边缘：翻到光标另一侧 ---

    [Fact]
    public void NearCursor_NearRightEdge_FlipsToLeftOfCursor()
    {
        // 光标离右边缘只有 100px，320 宽的贴图放右边会越界。
        var bounds = PinPlacement.NearCursor(new PixelPoint(2500, 600), 320, 240, PrimaryScreen);

        Assert.True(bounds.Right <= PrimaryScreen.Right);
        Assert.True(bounds.Left < 2500);
        Assert.Equal(320, bounds.Width);
    }

    [Fact]
    public void NearCursor_NearBottomEdge_FlipsAboveCursor()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(800, 1400), 320, 240, PrimaryScreen);

        Assert.True(bounds.Bottom <= PrimaryScreen.Bottom);
        Assert.True(bounds.Top < 1400);
        Assert.Equal(240, bounds.Height);
    }

    [Fact]
    public void NearCursor_BottomRightCorner_FlipsBothAxes()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(2540, 1430), 320, 240, PrimaryScreen);

        Assert.True(bounds.Left < 2540);
        Assert.True(bounds.Top < 1430);
        Assert.True(PrimaryScreen.Contains(bounds));
    }

    [Fact]
    public void NearCursor_CornerWithoutRoomEitherSide_StaysInsideScreen()
    {
        // 光标在右下角、贴图比剩余空间大：翻到左上也放不下，最后夹进屏幕。
        var bounds = PinPlacement.NearCursor(new PixelPoint(2555, 1435), 1000, 800, PrimaryScreen);

        Assert.True(bounds.Right <= PrimaryScreen.Right);
        Assert.True(bounds.Bottom <= PrimaryScreen.Bottom);
        Assert.Equal(1000, bounds.Width);
        Assert.Equal(800, bounds.Height);
    }

    [Fact]
    public void NearCursor_PinLargerThanScreen_ShrinksToScreen()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(10, 10), 4000, 3000, PrimaryScreen);

        Assert.Equal(PrimaryScreen.Width, bounds.Width);
        Assert.Equal(PrimaryScreen.Height, bounds.Height);
        Assert.Equal(PrimaryScreen.Left, bounds.Left);
        Assert.Equal(PrimaryScreen.Top, bounds.Top);
    }

    // --- 副屏负坐标 ---

    [Fact]
    public void NearCursor_SecondaryScreen_UsesNegativeOrigin()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(-900, 400), 200, 150, SecondaryScreen);

        Assert.Equal(-900 + PinPlacement.CursorOffset, bounds.Left);
        Assert.Equal(400 + PinPlacement.CursorOffset, bounds.Top);
        Assert.True(SecondaryScreen.Contains(bounds));
    }

    [Fact]
    public void NearCursor_SecondaryNearLeftEdge_StaysInsideNegativeScreen()
    {
        // 副屏右边缘是 0，左侧 -1080：靠右边缘时往左翻，不能翻到主屏去。
        var bounds = PinPlacement.NearCursor(new PixelPoint(-40, 400), 300, 200, SecondaryScreen);

        Assert.True(bounds.Right <= SecondaryScreen.Right);
        Assert.True(bounds.Left >= SecondaryScreen.Left);
    }

    [Fact]
    public void NearCursor_SecondaryNearTopEdge_StaysInside()
    {
        var bounds = PinPlacement.NearCursor(new PixelPoint(-500, 20), 300, 200, SecondaryScreen);

        Assert.True(bounds.Top >= SecondaryScreen.Top);
        Assert.True(bounds.Bottom <= SecondaryScreen.Bottom);
    }

    // --- DisplaySpace 重载 ---

    [Fact]
    public void NearCursor_DisplaySpace_FindsPrimaryForPositiveCursor()
    {
        var display = new DisplaySpace(new[]
        {
            new MonitorInfo("\\\\.\\DISPLAY1", PrimaryScreen, PrimaryScreen, 120, isPrimary: true),
            new MonitorInfo("\\\\.\\DISPLAY2", SecondaryScreen, SecondaryScreen, 96, isPrimary: false),
        });

        var bounds = PinPlacement.NearCursor(display, new PixelPoint(1000, 700), 320, 240);

        Assert.True(PrimaryScreen.Contains(bounds));
        Assert.Equal(1000 + PinPlacement.CursorOffset, bounds.Left);
    }

    [Fact]
    public void NearCursor_DisplaySpace_FindsSecondaryForNegativeCursor()
    {
        var display = new DisplaySpace(new[]
        {
            new MonitorInfo("\\\\.\\DISPLAY1", PrimaryScreen, PrimaryScreen, 120, isPrimary: true),
            new MonitorInfo("\\\\.\\DISPLAY2", SecondaryScreen, SecondaryScreen, 96, isPrimary: false),
        });

        var bounds = PinPlacement.NearCursor(display, new PixelPoint(-900, 900), 200, 150);

        Assert.True(SecondaryScreen.Contains(bounds));
        Assert.True(bounds.Left < 0);
    }

    [Fact]
    public void NearCursor_DisplaySpace_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PinPlacement.NearCursor(null!, new PixelPoint(0, 0), 100, 100));
    }

    // --- 非法入参 ---

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void NearCursor_NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PinPlacement.NearCursor(new PixelPoint(0, 0), width, height, PrimaryScreen));
    }

    [Fact]
    public void NearCursor_EmptyScreen_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PinPlacement.NearCursor(new PixelPoint(0, 0), 100, 100, PixelRect.Empty));
    }

    [Fact]
    public void CursorOffset_IsPositiveAndSmall()
    {
        Assert.InRange(PinPlacement.CursorOffset, 1, 64);
    }
}
