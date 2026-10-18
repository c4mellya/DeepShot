using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 标注共用的几何工具：矩形规范化、带容差的包含判断、点到线段的距离。
/// </summary>
internal static class AnnotationGeometry
{
    /// <summary>
    /// 把可能带负宽/负高的矩形规范化为正矩形（负宽高表示"从左/上边界的反向绘制"）。
    /// </summary>
    public static PixelRect Normalize(PixelRect rect) =>
        rect.Width < 0 || rect.Height < 0
            ? PixelRect.FromTwoPoints(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : rect;

    /// <summary>
    /// 点是否落在矩形内，边界包含，并向外扩展 <paramref name="tolerance"/> 像素。
    /// 负容差按 0 处理；零宽/零高的退化矩形仍然可以命中其所在的像素位置。
    /// </summary>
    public static bool ContainsExpanded(PixelRect rect, PixelPoint point, int tolerance)
    {
        var t = Math.Max(0, tolerance);
        return point.X >= rect.Left - t &&
               point.X <= rect.Right + t &&
               point.Y >= rect.Top - t &&
               point.Y <= rect.Bottom + t;
    }

    /// <summary>
    /// 点到线段的最短距离（像素）。线段退化成一个点时返回点到该点的距离。
    /// </summary>
    public static double DistanceToSegment(PixelPoint point, PixelPoint start, PixelPoint end)
    {
        var dx = (double)end.X - start.X;
        var dy = (double)end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared <= 0)
        {
            return point.DistanceTo(start);
        }

        var t = (((double)point.X - start.X) * dx + ((double)point.Y - start.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);

        var nearestX = start.X + t * dx;
        var nearestY = start.Y + t * dy;
        var offsetX = point.X - nearestX;
        var offsetY = point.Y - nearestY;
        return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
    }
}
