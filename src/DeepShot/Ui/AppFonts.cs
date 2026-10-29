using System.Windows;
using System.Windows.Media;

namespace DeepShot.Ui;

/// <summary>
/// 全应用字体：Windows 11 原生界面字体 Segoe UI Variable，中文逐字回退到微软雅黑 UI。
/// </summary>
/// <remarks>
/// 不再内嵌第三方字体：与资源管理器、设置使用同一套字形，ClearType 与 DPI 缩放也由系统负责。
/// Windows 10 没有 Segoe UI Variable，自动落到 Segoe UI。
/// </remarks>
public static class AppFonts
{
    private const string Stack = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";

    /// <summary>全应用统一字体族（中英文、界面与标注渲染共用同一个实例）。</summary>
    public static FontFamily Family { get; } = new(Stack);

    /// <summary>正文字重。</summary>
    public static FontWeight Regular => FontWeights.Normal;

    /// <summary>强调字重：标题、按钮、选中项。</summary>
    public static FontWeight Semibold => FontWeights.SemiBold;

    /// <summary>与 <see cref="Family"/> 同字体的常规字形（<see cref="FormattedText"/> 等需要 <see cref="Typeface"/> 的地方用）。</summary>
    public static Typeface Typeface { get; } = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
}
