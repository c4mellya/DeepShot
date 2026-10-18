namespace ZeepCut.Core.Geometry;

/// <summary>
/// 整数像素矩形。坐标轴与屏幕物理像素一致：X 向右增大，Y 向下增大。
/// 宽或高非正时视为空矩形（<see cref="IsEmpty"/>）。
/// </summary>
/// <remarks>
/// 边界采用半开区间：<see cref="Left"/> 包含、<see cref="Right"/> 不包含，
/// 与 Win32 的 RECT（right/bottom 为开边界）和像素索引习惯一致。
/// 因此相邻两块屏幕在 x=0 处不会同时命中。
/// </remarks>
public readonly record struct PixelRect
{
    public PixelRect(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    /// <summary>左边界（包含）。</summary>
    public int Left { get; }

    /// <summary>上边界（包含）。</summary>
    public int Top { get; }

    /// <summary>宽度，像素。</summary>
    public int Width { get; }

    /// <summary>高度，像素。</summary>
    public int Height { get; }

    /// <summary>右边界（不包含），即 <see cref="Left"/> + <see cref="Width"/>。</summary>
    public int Right => Left + Width;

    /// <summary>下边界（不包含），即 <see cref="Top"/> + <see cref="Height"/>。</summary>
    public int Bottom => Top + Height;

    /// <summary>宽或高小于等于 0 时为真。</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>空矩形（0,0,0,0）。</summary>
    public static PixelRect Empty => default;

    /// <summary>
    /// 由任意两个对角点得到正矩形：宽高取两点差的绝对值。
    /// 支持任意拖拽方向（右下、左上、右上、左下），两点重合时返回空矩形。
    /// </summary>
    public static PixelRect FromTwoPoints(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>
    /// 判断像素点是否在矩形内。右/下边界不包含，空矩形不包含任何点。
    /// </summary>
    public bool Contains(int x, int y) =>
        !IsEmpty && x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>判断另一矩形是否完全落在本矩形内（空矩形一律返回 false）。</summary>
    public bool Contains(PixelRect other) =>
        !IsEmpty && !other.IsEmpty &&
        other.Left >= Left && other.Top >= Top &&
        other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>
    /// 求两个矩形的重叠部分。没有重叠（含仅边界相接）时返回 <see cref="Empty"/>。
    /// </summary>
    public PixelRect Intersect(PixelRect other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return Empty;
        }

        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        // 半开区间下 right == left 表示没有共享像素。
        return right <= left || bottom <= top
            ? Empty
            : new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 把本矩形整体夹取到 <paramref name="target"/> 内：优先平移，尺寸不变；
    /// 只有本矩形比目标还大时才缩小到目标尺寸。
    /// 即使完全落在目标外，也会被平移到最近的边缘（而不是变成空矩形），
    /// 这样移动选区越界时会"停"在屏幕边缘，不会莫名消失。
    /// </summary>
    /// <remarks>
    /// 若需要"只保留重叠部分"的语义，请使用 <see cref="Intersect"/>。
    /// 目标为空或本矩形为空时返回 <see cref="Empty"/>。
    /// </remarks>
    public PixelRect Clamp(PixelRect target)
    {
        if (IsEmpty || target.IsEmpty)
        {
            return Empty;
        }

        var width = Math.Min(Width, target.Width);
        var height = Math.Min(Height, target.Height);
        var left = Math.Clamp(Left, target.Left, target.Right - width);
        var top = Math.Clamp(Top, target.Top, target.Bottom - height);

        return new PixelRect(left, top, width, height);
    }

    /// <summary>按给定偏移量平移，尺寸不变。</summary>
    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Width, Height);

    public override string ToString() => $"({Left},{Top}) {Width}x{Height}";
}
