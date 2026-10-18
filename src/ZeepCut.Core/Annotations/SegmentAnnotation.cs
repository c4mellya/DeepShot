using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;

/// <summary>
/// 带两个端点的标注（箭头/直线）的公共基类：提供端点、包围盒、
/// 点到线段距离的命中测试与整体平移。
/// </summary>
public abstract class SegmentAnnotation : Annotation
{
    /// <param name="start">起点。</param>
    /// <param name="end">终点。</param>
    protected SegmentAnnotation(
        PixelPoint start,
        PixelPoint end,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(color, thickness, id)
    {
        Start = start;
        End = end;
    }

    /// <summary>起点（物理像素）。</summary>
    public PixelPoint Start { get; private set; }

    /// <summary>终点（物理像素）。</summary>
    public PixelPoint End { get; private set; }

    /// <summary>
    /// 两端点构成的包围盒。水平/垂直线段的宽或高为 0（<see cref="PixelRect.IsEmpty"/> 为真），
    /// 零长度线段为 (x,y,0,0)，这是预期行为；命中测试不看 IsEmpty。
    /// </summary>
    public override PixelRect Bounds => PixelRect.FromTwoPoints(Start.X, Start.Y, End.X, End.Y);

    public override bool CanResize => false;

    /// <summary>重新设置两个端点。</summary>
    public void SetEndpoints(PixelPoint start, PixelPoint end)
    {
        Start = start;
        End = end;
    }

    public override void Move(int dx, int dy)
    {
        Start = Start.Offset(dx, dy);
        End = End.Offset(dx, dy);
    }

    public override bool HitTest(PixelPoint point, int tolerance)
    {
        var limit = Math.Max(0, tolerance) + Thickness / 2d;
        return AnnotationGeometry.DistanceToSegment(point, Start, End) <= limit;
    }
}
