namespace DeepShot.Core.Annotations;

/// <summary>
/// 编辑态下被抓住的部位：区域型标注的八个缩放方向、线段型标注的两个端点，
/// 以及整体平移。
/// </summary>
/// <remarks>
/// 与选区（<c>SelectionHandle</c>）是两套语义：选区手柄调整的是"截图范围"，
/// 这里调整的是"某个标注"。两者不要混用，否则 125% 下的换算与锁定规则都会错。
/// </remarks>
public enum AnnotationHandle
{
    /// <summary>没有抓住任何部位。</summary>
    None = 0,

    /// <summary>整体平移（点在标注内部或包围盒内）。</summary>
    Move,

    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,

    /// <summary>线段起点（箭头/直线）。</summary>
    StartPoint,

    /// <summary>线段终点（箭头/直线）。</summary>
    EndPoint,
}

/// <summary>
/// 一次标注拖拽的语义：平移、区域缩放、拖起点、拖终点。
/// </summary>
public enum AnnotationEditMode
{
    /// <summary>没有拖拽。</summary>
    None = 0,

    /// <summary>整体平移。</summary>
    Move,

    /// <summary>区域型标注缩放（矩形/椭圆/马赛克）。</summary>
    Resize,

    /// <summary>拖动线段起点。</summary>
    StartPoint,

    /// <summary>拖动线段终点。</summary>
    EndPoint,
}
