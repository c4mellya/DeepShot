namespace DeepShot.Core.Geometry;

/// <summary>
/// 以 WPF 设备无关单位（DIP，1/96 英寸）表示的点。
/// 只用于把屏幕物理像素/图像物理像素换算到 WPF 窗口坐标，不参与像素级裁剪。
/// </summary>
public readonly record struct DipPoint(double X, double Y)
{
    /// <summary>原点 (0,0)。</summary>
    public static DipPoint Empty => default;

    public override string ToString() => $"({X:0.##},{Y:0.##}) DIP";
}
