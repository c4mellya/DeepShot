namespace ZeepCut.Core.Annotations;

/// <summary>标注工具。UI 工具按钮与绘制草稿共用这一套判别值。</summary>
public enum AnnotationTool
{
    /// <summary>矩形框。</summary>
    Rectangle,

    /// <summary>椭圆/圆。</summary>
    Ellipse,

    /// <summary>箭头。</summary>
    Arrow,

    /// <summary>直线。</summary>
    Line,

    /// <summary>自由画笔。</summary>
    Freehand,

    /// <summary>文字（点击后输入，不走拖拽草稿）。</summary>
    Text,

    /// <summary>马赛克区域。</summary>
    Mosaic,
}

/// <summary>
/// 标注工具栏的预设：6 个颜色、4 档粗细与默认字号。
/// </summary>
/// <remarks>
/// 预设值放在 Core 便于单测（数量、透明度、取值范围），UI 只负责画按钮。
/// 颜色都是不透明色，避免用户选到半透明后看不清标注。
/// </remarks>
public static class AnnotationPalette
{
    /// <summary>工具栏提供的 6 个预设颜色。</summary>
    public static IReadOnlyList<AnnotationColor> Colors { get; } = new[]
    {
        AnnotationColor.Default,                                              // 红 #FFFF3B30
        AnnotationColor.FromRgb(0xFF, 0xCC, 0x00),                            // 黄
        AnnotationColor.FromRgb(0x34, 0xC7, 0x59),                            // 绿
        AnnotationColor.FromRgb(0x0A, 0x84, 0xFF),                            // 蓝
        AnnotationColor.White,                                                // 白
        AnnotationColor.Black,                                                // 黑
    };

    /// <summary>工具栏提供的 4 档粗细（物理像素）。</summary>
    public static IReadOnlyList<int> Thicknesses { get; } = new[] { 1, 3, 6, 10 };

    /// <summary>文字工具默认字号（物理像素）。</summary>
    public const int DefaultFontSize = AnnotationDefaults.DefaultFontSize;
}

/// <summary>
/// 当前标注工具的设置（工具、颜色、粗细、字号）。越界值静默钳制，与标注模型一致。
/// </summary>
/// <remarks>
/// 设置只影响**之后**创建的标注；已经画好的标注保持自身颜色/粗细（修改已有标注是 T16 的范围）。
/// </remarks>
public sealed class AnnotationToolSettings
{
    private AnnotationTool _tool = AnnotationTool.Rectangle;
    private AnnotationColor _color = AnnotationColor.Default;
    private int _thickness = AnnotationDefaults.DefaultThickness;
    private int _fontSize = AnnotationPalette.DefaultFontSize;

    /// <summary>当前工具；未定义枚举值会被拒绝。</summary>
    public AnnotationTool Tool
    {
        get => _tool;
        set => _tool = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "未知标注工具。");
    }

    /// <summary>新标注使用的颜色。</summary>
    public AnnotationColor Color
    {
        get => _color;
        set => _color = value;
    }

    /// <summary>新标注使用的粗细（物理像素），钳制到 1–20。</summary>
    public int Thickness
    {
        get => _thickness;
        set => _thickness = AnnotationDefaults.ClampThickness(value);
    }

    /// <summary>文字字号（物理像素），钳制到 8–72。</summary>
    public int FontSize
    {
        get => _fontSize;
        set => _fontSize = AnnotationDefaults.ClampFontSize(value);
    }
}
