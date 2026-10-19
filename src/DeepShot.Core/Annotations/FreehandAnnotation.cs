using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>
/// 自由画笔标注：按顺序记录轨迹点，<see cref="Smooth"/> 表示渲染时是否做平滑。
/// </summary>
/// <remarks>
/// 点列表可以只有一个点（单击画笔，画一个点），也可以是空列表（理论上不产生，
/// 但从反序列化数据来时允许）。命中测试按点到各段折线的距离 + 容差 + 描边半宽。
/// </remarks>
public sealed class FreehandAnnotation : Annotation
{
    private readonly List<PixelPoint> _points;

    /// <param name="points">轨迹点（按绘制顺序）。</param>
    /// <param name="smooth">是否平滑渲染。</param>
    public FreehandAnnotation(
        IEnumerable<PixelPoint> points,
        bool smooth = false,
        AnnotationColor? color = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(color, thickness, id)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = new List<PixelPoint>(points);
        Smooth = smooth;
    }

    /// <summary>轨迹点（只读视图，按绘制顺序）。</summary>
    public IReadOnlyList<PixelPoint> Points => _points;

    /// <summary>渲染时是否平滑（由渲染层解释，模型只保存开关）。</summary>
    public bool Smooth { get; set; }

    public override AnnotationKind Kind => AnnotationKind.Freehand;

    public override PixelRect Bounds
    {
        get
        {
            if (_points.Count == 0)
            {
                return PixelRect.Empty;
            }

            var minX = _points[0].X;
            var minY = _points[0].Y;
            var maxX = minX;
            var maxY = minY;

            foreach (var point in _points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            return new PixelRect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    /// <summary>追加一个轨迹点。</summary>
    public void AddPoint(PixelPoint point) => _points.Add(point);

    public override void Move(int dx, int dy)
    {
        for (var i = 0; i < _points.Count; i++)
        {
            _points[i] = _points[i].Offset(dx, dy);
        }
    }

    public override bool HitTest(PixelPoint point, int tolerance)
    {
        var limit = Math.Max(0, tolerance) + Thickness / 2d;

        if (_points.Count == 0)
        {
            return false;
        }

        if (_points.Count == 1)
        {
            return _points[0].DistanceTo(point) <= limit;
        }

        for (var i = 1; i < _points.Count; i++)
        {
            if (AnnotationGeometry.DistanceToSegment(point, _points[i - 1], _points[i]) <= limit)
            {
                return true;
            }
        }

        return false;
    }
}
