using DeepShot.Core.Geometry;

namespace DeepShot.Core.Annotations;

/// <summary>
/// 文字标注：锚点（左上角）+ 文本 + 字号，可选背景色。
/// </summary>
/// <remarks>
/// <para>
/// Core 不引用 WPF，也就无法做真正的文字测量。这里用确定性的近似估算包围盒，
/// 供命中测试、渲染裁剪与文档包围盒使用：
/// 行高 = <c>FontSize × 1.25</c>（向上取整），半角字符宽度 ≈ <c>FontSize × 0.6</c>，
/// 全角/中日韩字符宽度 ≈ <c>FontSize</c>。渲染层仍应按真实字体测量排版，
/// 命中测试的包围盒允许有这个量级的偏差。
/// </para>
/// <para>
/// 支持多行（<c>\n</c>、<c>\r\n</c>、<c>\r</c>），空文本仍然有一个行高，
/// 宽度为 0，可用容差命中锚点附近。
/// </para>
/// </remarks>
public sealed class TextAnnotation : Annotation
{
    /// <summary>行高相对字号的倍数。</summary>
    public const double LineHeightFactor = 1.25d;

    /// <summary>半角字符宽度相对字号的倍数（估算值）。</summary>
    public const double NarrowCharWidthFactor = 0.6d;

    private int _fontSize;
    private string _text;

    /// <param name="position">文字块左上角（物理像素）。</param>
    /// <param name="text">文本内容；为空时按空字符串处理。</param>
    /// <param name="fontSize">字号（像素），越界会被钳制到 8–72。</param>
    /// <param name="color">文字颜色。</param>
    /// <param name="backgroundColor">背景色；为空表示透明背景。</param>
    public TextAnnotation(
        PixelPoint position,
        string text,
        int fontSize = AnnotationDefaults.DefaultFontSize,
        AnnotationColor? color = null,
        AnnotationColor? backgroundColor = null,
        int thickness = AnnotationDefaults.DefaultThickness,
        Guid? id = null)
        : base(color, thickness, id)
    {
        Position = position;
        _text = text ?? string.Empty;
        FontSize = fontSize;
        BackgroundColor = backgroundColor;
    }

    /// <summary>文字块左上角（物理像素）。</summary>
    public PixelPoint Position { get; set; }

    /// <summary>文本内容（保留原始换行符）。</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>字号（像素），赋值时钳制到 8–72。</summary>
    public int FontSize
    {
        get => _fontSize;
        set => _fontSize = AnnotationDefaults.ClampFontSize(value);
    }

    /// <summary>背景色；为空表示透明背景。</summary>
    public AnnotationColor? BackgroundColor { get; set; }

    /// <summary>单行高度（像素），由字号估算。</summary>
    public int LineHeight => (int)Math.Ceiling(FontSize * LineHeightFactor);

    /// <summary>行数（空文本按 1 行计）。</summary>
    public int LineCount => SplitLines().Length;

    public override AnnotationKind Kind => AnnotationKind.Text;

    public override PixelRect Bounds
    {
        get
        {
            var lines = SplitLines();
            var width = 0;

            foreach (var line in lines)
            {
                width = Math.Max(width, MeasureLine(line, FontSize));
            }

            return new PixelRect(Position.X, Position.Y, width, LineHeight * lines.Length);
        }
    }

    public override void Move(int dx, int dy) => Position = Position.Offset(dx, dy);

    public override bool HitTest(PixelPoint point, int tolerance) =>
        AnnotationGeometry.ContainsExpanded(Bounds, point, tolerance);

    /// <summary>
    /// 估算一行文字的像素宽度：半角字符 <c>FontSize × 0.6</c>，全角/ CJK 字符
    /// <c>FontSize</c>，最后向上取整。结果只用于包围盒，不是真实排版宽度。
    /// </summary>
    public static int MeasureLine(string? line, int fontSize)
    {
        if (string.IsNullOrEmpty(line))
        {
            return 0;
        }

        var size = AnnotationDefaults.ClampFontSize(fontSize);
        var width = 0d;

        foreach (var ch in line)
        {
            width += IsWideCharacter(ch) ? size : size * NarrowCharWidthFactor;
        }

        return (int)Math.Ceiling(width);
    }

    /// <summary>
    /// 判断字符是否按全角宽度估算（CJK、假名、韩文、全角标点与兼容汉字）。
    /// </summary>
    internal static bool IsWideCharacter(char ch) =>
        ch is >= '\u1100' and <= '\u115F' ||
        ch is >= '\u2E80' and <= '\uA4CF' ||
        ch is >= '\uAC00' and <= '\uD7A3' ||
        ch is >= '\uF900' and <= '\uFAFF' ||
        ch is >= '\uFE30' and <= '\uFE4F' ||
        ch is >= '\uFF00' and <= '\uFF60' ||
        ch is >= '\uFFE0' and <= '\uFFE6';

    private string[] SplitLines() =>
        Text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}
