namespace DeepShot.Core.Annotations;

/// <summary>
/// 标注颜色：32 位 ARGB 值，布局与 WPF <c>Color</c>、GDI <c>COLORREF</c> 的 ARGB 一致，
/// 但**不引用**任何 WPF 类型，保证 Core 可在无桌面环境下测试。
/// </summary>
/// <remarks>
/// 内部只保存一个 <see cref="Argb"/>，因此可以直接写入序列化字段（如整数列），
/// 值相等按 ARGB 比较。
/// </remarks>
public readonly record struct AnnotationColor(uint Argb)
{
    /// <summary>默认标注颜色：亮红 <c>#FFFF3B30</c>（不透明）。</summary>
    public const uint DefaultArgb = 0xFFFF3B30;

    /// <summary>默认标注颜色。</summary>
    public static AnnotationColor Default => new(DefaultArgb);

    /// <summary>完全透明。</summary>
    public static AnnotationColor Transparent => new(0x00000000);

    /// <summary>不透明黑。</summary>
    public static AnnotationColor Black => new(0xFF000000);

    /// <summary>不透明白。</summary>
    public static AnnotationColor White => new(0xFFFFFFFF);

    /// <summary>Alpha 通道（0 透明，255 不透明）。</summary>
    public byte Alpha => (byte)(Argb >> 24);

    /// <summary>红通道。</summary>
    public byte Red => (byte)(Argb >> 16);

    /// <summary>绿通道。</summary>
    public byte Green => (byte)(Argb >> 8);

    /// <summary>蓝通道。</summary>
    public byte Blue => (byte)Argb;

    /// <summary>由四个通道值构造颜色。</summary>
    public static AnnotationColor FromArgb(byte alpha, byte red, byte green, byte blue) =>
        new(((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue);

    /// <summary>由 RGB 构造不透明颜色（Alpha 固定为 255）。</summary>
    public static AnnotationColor FromRgb(byte red, byte green, byte blue) =>
        FromArgb(byte.MaxValue, red, green, blue);

    public override string ToString() => $"#{Argb:X8}";
}
