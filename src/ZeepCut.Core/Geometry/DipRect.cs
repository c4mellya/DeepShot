namespace ZeepCut.Core.Geometry;

/// <summary>
/// 以 WPF 设备无关单位（DIP，1/96 英寸）表示的矩形。
/// 仅用于把 <see cref="PixelRect"/> 换算到 WPF 窗口坐标，不参与像素级裁剪。
/// </summary>
public readonly record struct DipRect(double Left, double Top, double Width, double Height)
{
    /// <summary>右边界（不包含），即 <see cref="Left"/> + <see cref="Width"/>。</summary>
    public double Right => Left + Width;

    /// <summary>下边界（不包含），即 <see cref="Top"/> + <see cref="Height"/>。</summary>
    public double Bottom => Top + Height;

    /// <summary>宽或高小于等于 0 时为真。</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>空矩形（0,0,0,0）。</summary>
    public static DipRect Empty => default;

    public override string ToString() => $"({Left},{Top}) {Width}x{Height} DIP";
}
