using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Output;

/// <summary>
/// 把"屏幕绝对物理像素"的选区换算成冻结捕获图内部的裁剪矩形。
/// </summary>
/// <remarks>
/// <para>
/// 选区（<c>SelectionController</c> 的输出）用的是虚拟桌面绝对坐标：
/// 主屏从 (0,0) 开始，左侧副屏可以是负坐标（本机副屏原点为 (-1080,0)）。
/// 冻结图只包含目标屏幕自己，左上角恒为 (0,0)，因此裁剪前必须减去目标屏幕原点的
/// **物理像素**坐标；缩放比例（125%/100%）不参与这里的换算——选区与图片都是物理像素。
/// </para>
/// <para>
/// 若选区只有一部分落在屏幕内（正常交互不会发生，但导入外部选区或将来跨屏时可能出现），
/// 只保留与屏幕的交集，避免 <c>CroppedBitmap</c> 越界。完全在屏幕外时返回空矩形。
/// </para>
/// </remarks>
public static class SelectionCrop
{
    /// <summary>
    /// 选区物理矩形 → 图像内裁剪矩形（相对冻结图左上角）。
    /// 空选区、空屏幕或与屏幕无交集时返回 <see cref="PixelRect.Empty"/>。
    /// </summary>
    /// <param name="selection">屏幕绝对物理像素选区。</param>
    /// <param name="screenBounds">目标屏幕的物理像素矩形，副屏 Left/Top 可为负。</param>
    public static PixelRect ToImageRect(PixelRect selection, PixelRect screenBounds)
    {
        if (selection.IsEmpty || screenBounds.IsEmpty)
        {
            return PixelRect.Empty;
        }

        var visible = selection.Intersect(screenBounds);
        if (visible.IsEmpty)
        {
            return PixelRect.Empty;
        }

        return new PixelRect(
            visible.Left - screenBounds.Left,
            visible.Top - screenBounds.Top,
            visible.Width,
            visible.Height);
    }

    /// <summary>
    /// 裁剪矩形是否完整落在冻结图内（半开区间：Right/Bottom 可以正好等于图片宽高）。
    /// </summary>
    public static bool IsInsideImage(PixelRect imageRect, int imagePixelWidth, int imagePixelHeight) =>
        !imageRect.IsEmpty &&
        imagePixelWidth > 0 &&
        imagePixelHeight > 0 &&
        imageRect.Left >= 0 &&
        imageRect.Top >= 0 &&
        imageRect.Right <= imagePixelWidth &&
        imageRect.Bottom <= imagePixelHeight;
}
