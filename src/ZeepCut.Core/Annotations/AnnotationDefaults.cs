namespace ZeepCut.Core.Annotations;

/// <summary>
/// 标注属性的默认值与合法范围，以及统一的钳制规则。
/// </summary>
/// <remarks>
/// 越界值一律**静默钳制**到范围内（不抛异常），这样 UI 的滑杆、配置导入或旧数据
/// 都不会因为一个越界数字让整个标注创建失败。需要严格校验的调用方应在赋值前自行判断，
/// 或用返回的钳制结果回写 UI。
/// </remarks>
public static class AnnotationDefaults
{
    /// <summary>最小粗细（像素）。</summary>
    public const int MinThickness = 1;

    /// <summary>最大粗细（像素）。</summary>
    public const int MaxThickness = 20;

    /// <summary>默认粗细（像素）。</summary>
    public const int DefaultThickness = 3;

    /// <summary>最小字号（像素）。</summary>
    public const int MinFontSize = 8;

    /// <summary>最大字号（像素）。</summary>
    public const int MaxFontSize = 72;

    /// <summary>默认字号（像素）。</summary>
    public const int DefaultFontSize = 18;

    /// <summary>最小马赛克块大小（像素）。</summary>
    public const int MinMosaicBlockSize = 4;

    /// <summary>最大马赛克块大小（像素）。</summary>
    public const int MaxMosaicBlockSize = 64;

    /// <summary>默认马赛克块大小（像素）。</summary>
    public const int DefaultMosaicBlockSize = 12;

    /// <summary>最小马赛克强度（0 表示不处理）。</summary>
    public const double MinMosaicStrength = 0d;

    /// <summary>最大马赛克强度。</summary>
    public const double MaxMosaicStrength = 1d;

    /// <summary>默认马赛克强度。</summary>
    public const double DefaultMosaicStrength = 0.75d;

    /// <summary>钳制粗细到 <see cref="MinThickness"/>–<see cref="MaxThickness"/>。</summary>
    public static int ClampThickness(int value) => Math.Clamp(value, MinThickness, MaxThickness);

    /// <summary>钳制字号到 <see cref="MinFontSize"/>–<see cref="MaxFontSize"/>。</summary>
    public static int ClampFontSize(int value) => Math.Clamp(value, MinFontSize, MaxFontSize);

    /// <summary>钳制马赛克块大小到 <see cref="MinMosaicBlockSize"/>–<see cref="MaxMosaicBlockSize"/>。</summary>
    public static int ClampMosaicBlockSize(int value) => Math.Clamp(value, MinMosaicBlockSize, MaxMosaicBlockSize);

    /// <summary>
    /// 钳制马赛克强度到 <see cref="MinMosaicStrength"/>–<see cref="MaxMosaicStrength"/>。
    /// <see cref="double.NaN"/> 视为非法输入，回退到 <see cref="DefaultMosaicStrength"/>。
    /// </summary>
    public static double ClampMosaicStrength(double value) =>
        double.IsNaN(value) ? DefaultMosaicStrength : Math.Clamp(value, MinMosaicStrength, MaxMosaicStrength);
}
