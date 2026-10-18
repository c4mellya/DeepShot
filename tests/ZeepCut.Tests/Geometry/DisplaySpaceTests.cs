using Xunit;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Geometry;

public class DisplaySpaceTests
{
    private static MonitorInfo Primary(int dpi = 96) => new(
        @"\\.\DISPLAY1",
        new PixelRect(0, 0, 2048, 1152),
        new PixelRect(0, 0, 2048, 1100),
        dpi,
        isPrimary: true);

    private static MonitorInfo Secondary(int dpi = 96) => new(
        @"\\.\DISPLAY2",
        new PixelRect(-1080, 0, 1080, 1920),
        new PixelRect(-1080, 0, 1080, 1920),
        dpi,
        isPrimary: false);

    private static DisplaySpace RealDesktop() => new(new[] { Primary(), Secondary() });

    [Fact]
    public void FindByPoint_OnNegativeCoordinateMonitor_ReturnsThatMonitor()
    {
        var space = RealDesktop();

        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-500, 500).DeviceName);
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-1080, 0).DeviceName);
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-1, 1919).DeviceName);
    }

    [Theory]
    [InlineData(0, 0)]        // 两屏相接处，0 属于主屏
    [InlineData(0, 1000)]
    [InlineData(2047, 1151)]
    public void FindByPoint_ZeroXAndPrimaryArea_BelongToPrimary(int x, int y)
    {
        Assert.Equal(@"\\.\DISPLAY1", RealDesktop().FindByPoint(x, y).DeviceName);
    }

    [Fact]
    public void FindByPoint_BoundaryAtX0_IsStable()
    {
        var space = RealDesktop();

        // 副屏范围是 [-1080, 0)，主屏是 [0, 2048)，x=0 只属于主屏。
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-1, 500).DeviceName);
        Assert.Equal(@"\\.\DISPLAY1", space.FindByPoint(0, 500).DeviceName);
    }

    [Fact]
    public void FindByPoint_OutsideAllMonitors_ReturnsNearest()
    {
        var space = RealDesktop();

        // 副屏左侧 1 像素：副屏最近。
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-1081, 500).DeviceName);
        // 主屏右侧之外：仍是主屏最近。
        Assert.Equal(@"\\.\DISPLAY1", space.FindByPoint(3000, 500).DeviceName);
        // 主屏下方很远，但副屏更高，副屏更近。
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(500, 5000).DeviceName);
    }

    [Fact]
    public void FindByPoint_FarBelowPrimaryButBesideSecondary_FollowsNearestRule()
    {
        var space = RealDesktop();

        // x=200 已在主屏右边界内，但 y 超出主屏；到主屏距离 348，到副屏距离 200。
        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(200, 1500).DeviceName);
        // x=500 时到副屏距离 500，到主屏仍是 348，主屏更近。
        Assert.Equal(@"\\.\DISPLAY1", space.FindByPoint(500, 1500).DeviceName);
    }

    [Fact]
    public void FindByPoint_DpiDoesNotChangeGeometry()
    {
        var space = new DisplaySpace(new[] { Primary(96), Secondary(144) });

        Assert.Equal(@"\\.\DISPLAY2", space.FindByPoint(-500, 500).DeviceName);
        Assert.Equal(144, space.FindByPoint(-500, 500).Dpi);
    }

    [Fact]
    public void Primary_ReturnsMarkedMonitor()
    {
        var space = new DisplaySpace(new[] { Secondary(), Primary() });

        Assert.Equal(@"\\.\DISPLAY1", space.Primary.DeviceName);
        Assert.True(space.Primary.IsPrimary);
    }

    [Fact]
    public void Primary_FallsBackToFirstMonitor_WhenNoneMarked()
    {
        var first = new MonitorInfo(@"\\.\DISPLAY1", new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100), 96, false);
        var second = new MonitorInfo(@"\\.\DISPLAY2", new PixelRect(100, 0, 100, 100), new PixelRect(100, 0, 100, 100), 96, false);
        var space = new DisplaySpace(new[] { first, second });

        Assert.Same(first, space.Primary);
    }

    [Fact]
    public void Constructor_EmptyMonitorList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DisplaySpace(Array.Empty<MonitorInfo>()));
    }

    [Fact]
    public void Constructor_TwoPrimaryMonitors_Throws()
    {
        var secondPrimary = new MonitorInfo(
            @"\\.\DISPLAY3",
            new PixelRect(2048, 0, 100, 100),
            new PixelRect(2048, 0, 100, 100),
            96,
            isPrimary: true);

        Assert.Throws<ArgumentException>(() => new DisplaySpace(new[] { Primary(), secondPrimary }));
    }
}
