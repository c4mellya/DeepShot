using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 标注画布的坐标换算：遮罩窗口内 DIP ↔ 截图图像内物理像素。
/// </summary>
/// <remarks>
/// <para>
/// 标注坐标的唯一约定（T12）：<b>相对截图图像左上角的物理像素</b>。遮罩窗口的鼠标事件是
/// DIP，因此每次都要先按目标屏幕 DPI 换回物理像素、减去屏幕原点，再减去选区原点：
/// </para>
/// <code>
/// 图像像素 = 屏幕原点 + round(窗口 DIP × dpi/96) − 选区原点
/// 窗口 DIP = (屏幕原点 − 选区原点 + 图像像素) × 96/dpi
/// </code>
/// <para>
/// 主屏 125%（选区原点 300,300）与副屏 100%（屏幕原点 −1080,0）都在这一个类型里处理，
/// 窗口代码不再自己算比例，导出（T17）也能用同一套规则按选区裁剪。
/// </para>
/// </remarks>
public readonly struct AnnotationCanvasTransform
{
    /// <param name="screen">目标屏幕的物理像素矩形（副屏原点为负）。</param>
    /// <param name="dpi">目标屏幕 DPI（96/120/144…）。</param>
    /// <param name="selection">已定稿的选区（屏幕物理像素）。</param>
    /// <exception cref="ArgumentException">屏幕或选区为空。</exception>
    public AnnotationCanvasTransform(PixelRect screen, int dpi, PixelRect selection)
    {
        if (screen.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screen));
        }

        if (selection.IsEmpty)
        {
            throw new ArgumentException("选区不能为空。", nameof(selection));
        }

        _ = new DpiTransform(dpi); // 复用 DPI 校验（必须为正数）。

        Screen = screen;
        Dpi = dpi;
        Selection = selection;
    }

    /// <summary>目标屏幕的物理像素矩形。</summary>
    public PixelRect Screen { get; }

    /// <summary>目标屏幕 DPI。</summary>
    public int Dpi { get; }

    /// <summary>已定稿的选区（屏幕物理像素）。</summary>
    public PixelRect Selection { get; }

    /// <summary>选区的左上角在窗口内的 DIP 坐标。</summary>
    public DipPoint SelectionOrigin => ToDip(new PixelPoint(0, 0));

    /// <summary>选区在窗口内的 DIP 矩形（标注层的摆放位置与尺寸）。</summary>
    public DipRect SelectionDip
    {
        get
        {
            var transform = new DpiTransform(Dpi);
            return new DipRect(
                transform.PixelToDip(Selection.Left - Screen.Left),
                transform.PixelToDip(Selection.Top - Screen.Top),
                transform.PixelToDip(Selection.Width),
                transform.PixelToDip(Selection.Height));
        }
    }

    /// <summary>
    /// 遮罩窗口内的 DIP 坐标 → 图像内物理像素（可能为负或超出选区，不做夹取）。
    /// </summary>
    public PixelPoint ToImage(double dipX, double dipY)
    {
        var transform = new DpiTransform(Dpi);
        return new PixelPoint(
            Screen.Left + transform.DipToPixel(dipX) - Selection.Left,
            Screen.Top + transform.DipToPixel(dipY) - Selection.Top);
    }

    /// <summary>
    /// 同 <see cref="ToImage"/>，但把结果夹取到 <c>[0, Width] × [0, Height]</c>，
    /// 让拖拽终点可以正好落在选区的右边/下边（半开区间的边界）。
    /// </summary>
    public PixelPoint ToImageClamped(double dipX, double dipY)
    {
        var point = ToImage(dipX, dipY);
        return new PixelPoint(
            Math.Clamp(point.X, 0, Selection.Width),
            Math.Clamp(point.Y, 0, Selection.Height));
    }

    /// <summary>屏幕绝对物理像素 → 图像内物理像素（不做夹取）。</summary>
    public PixelPoint ScreenToImage(int screenX, int screenY) =>
        new(screenX - Selection.Left, screenY - Selection.Top);

    /// <summary>图像内物理像素 → 遮罩窗口内的 DIP 坐标（用于定位标注层/文字输入框）。</summary>
    public DipPoint ToDip(PixelPoint image)
    {
        var transform = new DpiTransform(Dpi);
        return new DipPoint(
            transform.PixelToDip(Selection.Left - Screen.Left + image.X),
            transform.PixelToDip(Selection.Top - Screen.Top + image.Y));
    }

    /// <summary>图像内像素长度 → 窗口 DIP 长度（标注层的缩放因子 = 96/dpi）。</summary>
    public double ToDipLength(int pixels) => new DpiTransform(Dpi).PixelToDip(pixels);

    /// <summary>把图像内的矩形换算成窗口 DIP 矩形。</summary>
    public DipRect ToDipRect(PixelRect imageRect) => new DpiTransform(Dpi).ToDip(imageRect);
}
