using Xunit;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Geometry;

public class DpiTransformTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    public void Scale_IsDpiOver96(int dpi, double expectedScale)
    {
        Assert.Equal(expectedScale, new DpiTransform(dpi).Scale, precision: 10);
    }

    [Fact]
    public void PixelToDip_At100Percent_IsIdentity()
    {
        var transform = new DpiTransform(96);

        Assert.Equal(10.0, transform.PixelToDip(10), precision: 10);
        Assert.Equal(10, transform.DipToPixel(10));
    }

    [Fact]
    public void PixelToDip_At125Percent()
    {
        var transform = new DpiTransform(120);

        Assert.Equal(100.0, transform.PixelToDip(125), precision: 10);
        Assert.Equal(2.4, transform.PixelToDip(3), precision: 10);
        Assert.Equal(0.8, transform.PixelToDip(1), precision: 10);
    }

    [Fact]
    public void PixelToDip_At150Percent()
    {
        var transform = new DpiTransform(144);

        Assert.Equal(100.0, transform.PixelToDip(150), precision: 10);
        Assert.Equal(2.0, transform.PixelToDip(3), precision: 10);
    }

    [Fact]
    public void DipToPixel_At125Percent_RoundsAwayFromZero()
    {
        var transform = new DpiTransform(120);

        Assert.Equal(125, transform.DipToPixel(100));
        Assert.Equal(1, transform.DipToPixel(0.5));   // 0.625
        Assert.Equal(3, transform.DipToPixel(2.4));
        Assert.Equal(156, transform.DipToPixel(125)); // 156.25
    }

    [Fact]
    public void DipToPixel_At100Percent_RoundsHalfAwayFromZero()
    {
        var transform = new DpiTransform(96);

        Assert.Equal(11, transform.DipToPixel(10.5));
        Assert.Equal(-11, transform.DipToPixel(-10.5));
        Assert.Equal(10, transform.DipToPixel(10.4));
    }

    [Theory]
    [InlineData(96, 0)]
    [InlineData(96, 1)]
    [InlineData(96, 13)]
    [InlineData(120, 1)]
    [InlineData(120, 3)]
    [InlineData(120, 7)]
    [InlineData(120, 100)]
    [InlineData(120, 1919)]
    [InlineData(144, 1)]
    [InlineData(144, 3)]
    [InlineData(144, 1080)]
    [InlineData(144, 1919)]
    [InlineData(192, 255)]
    public void PixelToDipThenBack_IsRoundTripConsistent(int dpi, int pixels)
    {
        var transform = new DpiTransform(dpi);

        var dip = transform.PixelToDip(pixels);
        var back = transform.DipToPixel(dip);

        Assert.Equal(pixels, back);
    }

    [Fact]
    public void ToDip_ConvertsRectAt144Percent()
    {
        var transform = new DpiTransform(144);

        var dip = transform.ToDip(new PixelRect(0, 0, 1920, 1080));

        Assert.Equal(0.0, dip.Left, precision: 10);
        Assert.Equal(0.0, dip.Top, precision: 10);
        Assert.Equal(1280.0, dip.Width, precision: 10);
        Assert.Equal(720.0, dip.Height, precision: 10);
    }

    [Fact]
    public void ToDip_ConvertsRectAt125Percent()
    {
        var transform = new DpiTransform(120);

        var dip = transform.ToDip(new PixelRect(0, 0, 1920, 1080));

        Assert.Equal(1536.0, dip.Width, precision: 10);
        Assert.Equal(864.0, dip.Height, precision: 10);
    }

    [Fact]
    public void ToPixels_ConvertsRectAt125Percent()
    {
        var transform = new DpiTransform(120);

        var pixels = transform.ToPixels(new DipRect(0, 0, 100, 50));

        // 基准边换算：right=125，bottom=62.5 → 63（远离零），故高为 63。
        Assert.Equal(new PixelRect(0, 0, 125, 63), pixels);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void RectConversion_WithNegativeCoordinates_RoundTrips(int dpi)
    {
        var transform = new DpiTransform(dpi);
        var original = new PixelRect(-1080, -240, 1080, 1920);

        var back = transform.ToPixels(transform.ToDip(original));

        Assert.Equal(original, back);
    }

    [Fact]
    public void EmptyRects_ConvertToEmpty()
    {
        var transform = new DpiTransform(120);

        Assert.True(transform.ToDip(PixelRect.Empty).IsEmpty);
        Assert.True(transform.ToPixels(DipRect.Empty).IsEmpty);
    }

    [Fact]
    public void FromMonitor_UsesMonitorDpi()
    {
        var monitor = new MonitorInfo(
            @"\\.\DISPLAY1",
            new PixelRect(0, 0, 2048, 1152),
            new PixelRect(0, 0, 2048, 1100),
            144,
            isPrimary: true);

        var transform = DpiTransform.FromMonitor(monitor);

        Assert.Equal(144, transform.Dpi);
        Assert.Equal(1.5, transform.Scale, precision: 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    public void Constructor_InvalidDpi_Throws(int dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DpiTransform(dpi));
    }
}
