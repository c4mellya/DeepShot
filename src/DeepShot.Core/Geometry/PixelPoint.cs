namespace DeepShot.Core.Geometry;

/// <summary>
/// 整数像素点。坐标轴与屏幕物理像素一致：X 向右增大，Y 向下增大。
/// </summary>
/// <remarks>
/// 用于标注模型的端点、文字锚点与画笔轨迹。与 <see cref="PixelRect"/> 一样是纯值类型，
/// 不含任何 WPF/Win32 依赖。
/// </remarks>
public readonly record struct PixelPoint(int X, int Y)
{
    /// <summary>原点 (0,0)。</summary>
    public static PixelPoint Empty => default;

    /// <summary>按给定偏移量平移，返回新点。</summary>
    public PixelPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    /// <summary>到另一点的欧氏距离（像素）。</summary>
    public double DistanceTo(PixelPoint other)
    {
        var dx = (double)other.X - X;
        var dy = (double)other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"({X},{Y})";
}
