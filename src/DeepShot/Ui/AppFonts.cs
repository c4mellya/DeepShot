using System.Windows;
using System.Windows.Media;

namespace DeepShot.Ui;

/// <summary>
/// 全应用字体：内嵌 MiSans（小米 MiSans 字体，按《MiSans 字体知识产权许可协议》随软件内嵌使用）。
/// </summary>
/// <remarks>
/// <para>
/// <b>只内嵌两个字重</b>：<c>MiSans-Regular</c>（OpenType 字重 330）与 <c>MiSans-Semibold</c>（520）。
/// MiSans 的字重刻度比常规字体轻一档，WPF 按"最近字重"匹配：如果再塞进 Medium(380)，
/// <see cref="FontWeights.Normal"/>(400) 会被匹配到 Medium，整个界面都会偏粗；塞进 Bold(630)，
/// <see cref="FontWeights.SemiBold"/>(600) 又会被匹配到 Bold。两个字重时：Normal → Regular，
/// Medium/SemiBold → Semibold，正好对应界面上的"正文/强调"两级。
/// </para>
/// <para>
/// 因此界面代码<b>只用</b> <see cref="Regular"/> 与 <see cref="Semibold"/>（或等价的
/// <see cref="FontWeights.Normal"/> / <see cref="FontWeights.SemiBold"/>）；不要用 <see cref="FontWeights.Bold"/>，
/// 它会退化成 Regular + 算法加粗。
/// </para>
/// <para>
/// <b>回退链</b>：内嵌 MiSans → 系统已安装的 MiSans → 微软雅黑 UI → Segoe UI。
/// 许可协议禁止修改字体，所以字体文件原样内嵌，不做子集化。
/// </para>
/// </remarks>
public static class AppFonts
{
    /// <summary>内嵌字体所在的包内目录（相对本程序集）。</summary>
    private const string FontFolder = "pack://application:,,,/DeepShot;component/Assets/Fonts/";

    /// <summary>全应用统一字体族（中英文、界面与标注渲染共用同一个实例）。</summary>
    public static FontFamily Family { get; } = Create();

    /// <summary>正文字重（MiSans Regular）。</summary>
    public static FontWeight Regular => FontWeights.Normal;

    /// <summary>强调字重：标题、按钮、选中项（MiSans Semibold）。</summary>
    public static FontWeight Semibold => FontWeights.SemiBold;

    /// <summary>与 <see cref="Family"/> 同字体的常规字形（<see cref="FormattedText"/> 等需要 <see cref="Typeface"/> 的地方用）。</summary>
    public static Typeface Typeface { get; } = new(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static FontFamily Create()
    {
        try
        {
            return new FontFamily(new Uri(FontFolder), "./#MiSans, MiSans, Microsoft YaHei UI, Segoe UI");
        }
        catch (Exception)
        {
            // pack:// 方案尚未注册（极早期 / 非 WPF 宿主）：退回系统字体，绝不能因为字体让界面起不来。
            return new FontFamily("MiSans, Microsoft YaHei UI, Segoe UI");
        }
    }
}
