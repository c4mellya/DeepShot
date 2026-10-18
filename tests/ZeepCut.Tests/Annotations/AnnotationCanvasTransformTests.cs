using Xunit;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Tests.Annotations;

public class AnnotationCanvasTransformTests
{
    // 主屏：物理 2560×1440 @ (0,0)，DPI 120（125%），选区 (300,300) 640×480。
    private static readonly PixelRect PrimaryScreen = new(0, 0, 2560, 1440);
    private static readonly PixelRect PrimarySelection = new(300, 300, 640, 480);
    private static readonly AnnotationCanvasTransform Primary =
        new(PrimaryScreen, 120, PrimarySelection);

    // 副屏：物理 1080×1920 @ (-1080,0)，DPI 96（100%），选区 (-980,300) 640×480。
    private static readonly PixelRect SecondaryScreen = new(-1080, 0, 1080, 1920);
    private static readonly PixelRect SecondarySelection = new(-980, 300, 640, 480);
    private static readonly AnnotationCanvasTransform Secondary =
        new(SecondaryScreen, 96, SecondarySelection);

    [Fact]
    public void PrimarySelectionOrigin_MapsToImageOrigin()
    {
        // 选区物理左上角 (300,300) 在 125% 下是窗口内 DIP (240,240)。
        Assert.Equal(new PixelPoint(0, 0), Primary.ToImage(240, 240));
    }

    [Fact]
    public void PrimaryDipToImage_AppliesDpiScaleAndSubtractsSelectionOrigin()
    {
        // DIP (320,280) → 物理 (400,350) → 图像 (100,50)。
        Assert.Equal(new PixelPoint(100, 50), Primary.ToImage(320, 280));
    }

    [Fact]
    public void PrimaryImageToDip_IsExactInverseOfDipToImage()
    {
        Assert.Equal(new DipPoint(320, 280), Primary.ToDip(new PixelPoint(100, 50)));
    }

    [Fact]
    public void PrimaryRoundTrip_DipToImageToDip()
    {
        for (var dip = 0; dip <= 800; dip += 40)
        {
            var image = Primary.ToImage(dip, dip);
            var back = Primary.ToDip(image);
            Assert.Equal(image, Primary.ToImage(back.X, back.Y));
        }
    }

    [Fact]
    public void PrimaryToImage_OutsideSelection_ReturnsNegativeImagePixel()
    {
        // 窗口左上角在选区 (300,300) 之外 → 图像坐标 (-300,-300)，不做夹取。
        Assert.Equal(new PixelPoint(-300, -300), Primary.ToImage(0, 0));
    }

    [Fact]
    public void ToImageClamped_ClampsToSelectionIncludingRightBottomEdges()
    {
        Assert.Equal(new PixelPoint(0, 0), Primary.ToImageClamped(0, 0));
        Assert.Equal(new PixelPoint(640, 480), Primary.ToImageClamped(2048, 1152));
        // DIP 500 → 物理 625（125%）→ 图像 625−300 = 325，仍在选区内。
        Assert.Equal(new PixelPoint(325, 325), Primary.ToImageClamped(500, 500));
    }

    [Fact]
    public void PrimarySelectionDip_PlacesLayerAtSelectionOriginInDip()
    {
        Assert.Equal(new DipRect(240, 240, 512, 384), Primary.SelectionDip);
        Assert.Equal(new DipPoint(240, 240), Primary.SelectionOrigin);
    }

    [Fact]
    public void PrimaryToDipLength_ConvertsPixelsToDip()
    {
        Assert.Equal(80, Primary.ToDipLength(100));
        Assert.Equal(512, Primary.ToDipLength(640));
    }

    [Fact]
    public void SecondaryNegativeOrigin_SelectionOriginMapsToImageOrigin()
    {
        // 副屏窗口覆盖 (-1080,0)，窗口内 DIP (100,300) 就是屏幕物理 (-980,300)。
        Assert.Equal(new PixelPoint(0, 0), Secondary.ToImage(100, 300));
    }

    [Fact]
    public void SecondaryNegativeOrigin_DipToImageAndBack()
    {
        Assert.Equal(new PixelPoint(100, 100), Secondary.ToImage(200, 400));
        Assert.Equal(new DipPoint(200, 400), Secondary.ToDip(new PixelPoint(100, 100)));
        Assert.Equal(new DipRect(100, 300, 640, 480), Secondary.SelectionDip);
    }

    [Fact]
    public void SecondaryClamp_UsesSelectionSizeRegardlessOfNegativeScreenOrigin()
    {
        Assert.Equal(new PixelPoint(640, 480), Secondary.ToImageClamped(5000, 5000));
        Assert.Equal(new PixelPoint(0, 0), Secondary.ToImageClamped(-5000, -5000));
    }

    [Fact]
    public void ScreenToImage_SubtractsSelectionOrigin()
    {
        Assert.Equal(new PixelPoint(100, 200), Primary.ScreenToImage(400, 500));
        Assert.Equal(new PixelPoint(100, 200), Secondary.ScreenToImage(-880, 500));
    }

    [Fact]
    public void Dpi150Percent_UsesOnePointFiveScale()
    {
        var transform = new AnnotationCanvasTransform(new PixelRect(0, 0, 2880, 1620), 144, new PixelRect(360, 270, 640, 480));

        Assert.Equal(new PixelPoint(0, 0), transform.ToImage(240, 180));
        // DIP (320,220) → 物理 (480,330)，减去选区原点 (360,270) 得图像 (120,60)。
        Assert.Equal(new PixelPoint(120, 60), transform.ToImage(320, 220));
        Assert.Equal(new DipPoint(320, 220), transform.ToDip(new PixelPoint(120, 60)));
    }

    [Fact]
    public void Constructor_RejectsEmptyScreenOrSelection()
    {
        Assert.Throws<ArgumentException>(() =>
            new AnnotationCanvasTransform(PixelRect.Empty, 96, new PixelRect(0, 0, 10, 10)));
        Assert.Throws<ArgumentException>(() =>
            new AnnotationCanvasTransform(new PixelRect(0, 0, 100, 100), 96, PixelRect.Empty));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveDpi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnnotationCanvasTransform(new PixelRect(0, 0, 100, 100), 0, new PixelRect(0, 0, 10, 10)));
    }
}
