namespace DeepShot.Core.Annotations;

/// <summary>标注类型。用于日志、序列化判别与工具切换。</summary>
public enum AnnotationKind
{
    /// <summary>矩形框（非填充）。</summary>
    Rectangle,

    /// <summary>椭圆/圆（非填充）。</summary>
    Ellipse,

    /// <summary>箭头（起点 → 终点）。</summary>
    Arrow,

    /// <summary>直线（起点 → 终点）。</summary>
    Line,

    /// <summary>自由画笔（点列表）。</summary>
    Freehand,

    /// <summary>文字。</summary>
    Text,

    /// <summary>马赛克区域。</summary>
    Mosaic,
}
