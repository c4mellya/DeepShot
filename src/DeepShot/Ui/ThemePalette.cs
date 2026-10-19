using System.Windows.Media;
using DeepShot.Core.Settings;

namespace DeepShot.Ui;

/// <summary>
/// 一套主题的外观（T30）：颜色 + 圆角。
/// </summary>
/// <remarks>
/// <para>
/// <b>类不是 record</b>：它需要可变的画刷缓存（按需冻结、全应用共享），record 的值相等语义
/// 对"同一套主题的同一个实例"没有意义，反而会与缓存不一致。
/// </para>
/// <para>
/// 全部字段都是"语义化"的名字（WindowBackground / CardBackground / Accent …），不是"灰色 3 号"，
/// 所以深色主题可以整体改色而不必动任何一个窗口的代码。
/// 之前的做法是把十六进制色值写在 <c>HistoryUi</c> 里各窗口互相引用，深色主题一接就会
/// "半边白半边黑"，这里统一收口。
/// </para>
/// <para>
/// <b>冻结</b>：所有画刷在构造时 <c>Freeze()</c>，可以跨线程安全共享（多个贴图/提示窗口都用同一份）。
/// </para>
/// </remarks>
public sealed class ThemePalette
{
    /// <summary>窗口标题栏是否应该用深色（用户通过 <see cref="DwmApi"/> 设置）。</summary>
    public required bool IsDark { get; init; }

    /// <summary>窗口背景（普通窗口的整窗底色）。</summary>
    public required Color WindowBackground { get; init; }

    /// <summary>内容卡片 / 侧栏 / 输入框的底色。</summary>
    public required Color Surface { get; init; }

    /// <summary>次要表面（缩略图占位、未选中的分段开关底、滚动区底）。</summary>
    public required Color SurfaceAlt { get; init; }

    /// <summary>卡片 / 控件边框。</summary>
    public required Color Border { get; init; }

    /// <summary>细分隔线（比 <see cref="Border"/> 更淡）。</summary>
    public required Color Divider { get; init; }

    /// <summary>主要文字。</summary>
    public required Color Foreground { get; init; }

    /// <summary>次要文字（时间/尺寸/状态/提示）。</summary>
    public required Color SecondaryForeground { get; init; }

    /// <summary>强调色（选中边框、主按钮底、分段开关选中项）。</summary>
    public required Color Accent { get; init; }

    /// <summary>强调色上的文字（主按钮文字）。</summary>
    public required Color OnAccent { get; init; }

    /// <summary>悬停时的浅强调底色。</summary>
    public required Color AccentHover { get; init; }

    /// <summary>悬停在普通按钮/卡片上时的底色。</summary>
    public required Color HoverBackground { get; init; }

    /// <summary>错误提示文字。</summary>
    public required Color Error { get; init; }

    /// <summary>成功/信息提示文字。</summary>
    public required Color Success { get; init; }

    /// <summary>警告提示文字。</summary>
    public required Color Warning { get; init; }

    /// <summary>收藏星标（金黄色）。</summary>
    public required Color Star { get; init; }

    /// <summary>未收藏星标。</summary>
    public required Color StarInactive { get; init; }

    /// <summary>遮罩工具条背景（半透明）。</summary>
    public required Color OverlayBar { get; init; }

    /// <summary>遮罩工具条边框。</summary>
    public required Color OverlayBarBorder { get; init; }

    /// <summary>遮罩工具条上的文字/图标（总是与工具条底色高对比）。</summary>
    public required Color OverlayForeground { get; init; }

    /// <summary>遮罩工具条按钮底色。</summary>
    public required Color OverlayButton { get; init; }

    /// <summary>遮罩工具条按钮边框。</summary>
    public required Color OverlayButtonBorder { get; init; }

    /// <summary>遮罩工具条按钮的悬停底色。</summary>
    public required Color OverlayButtonHover { get; init; }

    /// <summary>遮罩工具条里的选中态（当前工具/颜色）。</summary>
    public required Color OverlaySelected { get; init; }

    /// <summary>提示窗（Toast）背景（半透明）。</summary>
    public required Color ToastBackground { get; init; }

    /// <summary>提示窗边框。</summary>
    public required Color ToastBorder { get; init; }

    /// <summary>提示窗文字。</summary>
    public required Color ToastForeground { get; init; }

    /// <summary>大圆角（窗口内的卡容器、工具条）。</summary>
    public required double RadiusLarge { get; init; }

    /// <summary>中圆角（卡片、按钮、分段开关）。</summary>
    public required double RadiusMedium { get; init; }

    /// <summary>小圆角（徽标、色块）。</summary>
    public required double RadiusSmall { get; init; }

    // --- 冻结画刷（构造时生成，全应用共享）---

    /// <summary>窗口背景。</summary>
    public SolidColorBrush WindowBackgroundBrush => Get(nameof(WindowBackground));

    /// <summary>卡片/侧栏底色。</summary>
    public SolidColorBrush SurfaceBrush => Get(nameof(Surface));

    /// <summary>次要表面。</summary>
    public SolidColorBrush SurfaceAltBrush => Get(nameof(SurfaceAlt));

    /// <summary>边框。</summary>
    public SolidColorBrush BorderBrush => Get(nameof(Border));

    /// <summary>分隔线。</summary>
    public SolidColorBrush DividerBrush => Get(nameof(Divider));

    /// <summary>主要文字。</summary>
    public SolidColorBrush ForegroundBrush => Get(nameof(Foreground));

    /// <summary>次要文字。</summary>
    public SolidColorBrush SecondaryForegroundBrush => Get(nameof(SecondaryForeground));

    /// <summary>强调色。</summary>
    public SolidColorBrush AccentBrush => Get(nameof(Accent));

    /// <summary>强调色上的文字。</summary>
    public SolidColorBrush OnAccentBrush => Get(nameof(OnAccent));

    /// <summary>悬停底色。</summary>
    public SolidColorBrush HoverBackgroundBrush => Get(nameof(HoverBackground));

    /// <summary>错误文字。</summary>
    public SolidColorBrush ErrorBrush => Get(nameof(Error));

    /// <summary>成功文字。</summary>
    public SolidColorBrush SuccessBrush => Get(nameof(Success));

    /// <summary>警告文字。</summary>
    public SolidColorBrush WarningBrush => Get(nameof(Warning));

    /// <summary>收藏星标。</summary>
    public SolidColorBrush StarBrush => Get(nameof(Star));

    /// <summary>未收藏星标。</summary>
    public SolidColorBrush StarInactiveBrush => Get(nameof(StarInactive));

    /// <summary>遮罩工具条背景。</summary>
    public SolidColorBrush OverlayBarBrush => Get(nameof(OverlayBar));

    /// <summary>遮罩工具条边框。</summary>
    public SolidColorBrush OverlayBarBorderBrush => Get(nameof(OverlayBarBorder));

    /// <summary>遮罩工具条文字。</summary>
    public SolidColorBrush OverlayForegroundBrush => Get(nameof(OverlayForeground));

    /// <summary>遮罩工具条按钮底色。</summary>
    public SolidColorBrush OverlayButtonBrush => Get(nameof(OverlayButton));

    /// <summary>遮罩工具条按钮边框。</summary>
    public SolidColorBrush OverlayButtonBorderBrush => Get(nameof(OverlayButtonBorder));

    /// <summary>遮罩工具条按钮悬停。</summary>
    public SolidColorBrush OverlayButtonHoverBrush => Get(nameof(OverlayButtonHover));

    /// <summary>遮罩工具条选中态。</summary>
    public SolidColorBrush OverlaySelectedBrush => Get(nameof(OverlaySelected));

    /// <summary>提示窗背景。</summary>
    public SolidColorBrush ToastBackgroundBrush => Get(nameof(ToastBackground));

    /// <summary>提示窗边框。</summary>
    public SolidColorBrush ToastBorderBrush => Get(nameof(ToastBorder));

    /// <summary>提示窗文字。</summary>
    public SolidColorBrush ToastForegroundBrush => Get(nameof(ToastForeground));

    /// <summary>取某个颜色对应的冻结画刷（按属性名缓存；只对上面列出的颜色属性有效）。</summary>
    public SolidColorBrush Get(string propertyName)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(propertyName, out var cached))
            {
                return cached;
            }

            var color = GetType().GetProperty(propertyName)?.GetValue(this) as Color? ??
                        throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "主题里没有这个颜色。");
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _cache[propertyName] = brush;
            return brush;
        }
    }

    private readonly Dictionary<string, SolidColorBrush> _cache = new(StringComparer.Ordinal);

    /// <summary>浅色主题：白底 + 浅灰卡片 + 蓝色强调。</summary>
    public static ThemePalette Light { get; } = new()
    {
        IsDark = false,
        WindowBackground = Rgb(0xF6, 0xF6, 0xF9),
        Surface = Rgb(0xFF, 0xFF, 0xFF),
        SurfaceAlt = Rgb(0xEC, 0xEC, 0xF1),
        Border = Rgb(0xD5, 0xD5, 0xDD),
        Divider = Rgb(0xE4, 0xE4, 0xEA),
        Foreground = Rgb(0x1C, 0x1C, 0x20),
        SecondaryForeground = Rgb(0x63, 0x63, 0x6E),
        Accent = Rgb(0x1D, 0x74, 0xD8),
        OnAccent = Rgb(0xFF, 0xFF, 0xFF),
        AccentHover = Rgb(0x1A, 0x66, 0xBF),
        HoverBackground = Rgb(0xE8, 0xEF, 0xFA),
        Error = Rgb(0xC4, 0x2B, 0x1C),
        Success = Rgb(0x1B, 0x7F, 0x3B),
        Warning = Rgb(0xA0, 0x64, 0x00),
        Star = Rgb(0xE5, 0xA5, 0x00),
        StarInactive = Rgb(0xAF, 0xAF, 0xB9),
        // 遮罩用深色工具条：它是压在"冻结画面"上的浮层，深浅主题下都保持同样的高对比。
        OverlayBar = Argb(0xEE, 0x1E, 0x1E, 0x22),
        OverlayBarBorder = Argb(0x59, 0xFF, 0xFF, 0xFF),
        OverlayForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),
        OverlayButton = Argb(0x22, 0xFF, 0xFF, 0xFF),
        OverlayButtonBorder = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        OverlayButtonHover = Argb(0x44, 0xFF, 0xFF, 0xFF),
        OverlaySelected = Argb(0x80, 0x2E, 0x9B, 0xF0),
        ToastBackground = Argb(0xF2, 0x20, 0x20, 0x24),
        ToastBorder = Argb(0x99, 0xFF, 0xFF, 0xFF),
        ToastForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),
        RadiusLarge = 10,
        RadiusMedium = 6,
        RadiusSmall = 4,
    };

    /// <summary>深色主题：深灰底 + 稍亮卡片 + 更亮的蓝色强调。</summary>
    public static ThemePalette Dark { get; } = CopyOf(
        Light,
        new ThemePalette
    {
        IsDark = true,
        WindowBackground = Rgb(0x1E, 0x1E, 0x22),
        Surface = Rgb(0x2A, 0x2A, 0x30),
        SurfaceAlt = Rgb(0x34, 0x34, 0x3B),
        Border = Rgb(0x44, 0x44, 0x4E),
        Divider = Rgb(0x35, 0x35, 0x3C),
        Foreground = Rgb(0xF2, 0xF2, 0xF6),
        SecondaryForeground = Rgb(0xA8, 0xA8, 0xB4),
        Accent = Rgb(0x4C, 0x9A, 0xF0),
        OnAccent = Rgb(0x0E, 0x1B, 0x2B),
        AccentHover = Rgb(0x62, 0xA9, 0xF5),
        HoverBackground = Rgb(0x38, 0x38, 0x42),
        Error = Rgb(0xFF, 0x8A, 0x7A),
        Success = Rgb(0x6E, 0xD3, 0x8B),
        Warning = Rgb(0xF0, 0xC0, 0x60),
        Star = Rgb(0xFF, 0xC4, 0x3D),
        StarInactive = Rgb(0x6E, 0x6E, 0x7A),
        // 深色主题下工具条更暗一点，与深色背景区分开。
        OverlayBar = Argb(0xF2, 0x14, 0x14, 0x17),
        OverlayBarBorder = Argb(0x59, 0xFF, 0xFF, 0xFF),
        OverlayForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),
        OverlayButton = Argb(0x22, 0xFF, 0xFF, 0xFF),
        OverlayButtonBorder = Argb(0x4D, 0xFF, 0xFF, 0xFF),
        OverlayButtonHover = Argb(0x44, 0xFF, 0xFF, 0xFF),
        OverlaySelected = Argb(0x80, 0x4C, 0x9A, 0xF0),
        ToastBackground = Argb(0xF2, 0x2A, 0x2A, 0x30),
        ToastBorder = Argb(0x99, 0xFF, 0xFF, 0xFF),
        ToastForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),
        RadiusLarge = Light.RadiusLarge,
        RadiusMedium = Light.RadiusMedium,
        RadiusSmall = Light.RadiusSmall,
    });

    /// <summary>
    /// 以 <paramref name="basePalette"/> 为底、用 <paramref name="overrides"/> 里**显式设置过**的属性覆盖，
    /// 生成一套新主题（深色主题只写与浅色不同的那几项，圆角等共用）。
    /// </summary>
    /// <remarks>
    /// 用显式拷贝而不是 <c>with</c>：<see cref="ThemePalette"/> 不是 record（它需要可变的画刷缓存），
    /// 这里用 <c>???</c> 判断"哪些属性被设置过"的方式不可行，所以由调用方把要覆盖的值全部写出来，
    /// 剩下的从 <paramref name="basePalette"/> 取。<c>null</c> 属性（引用类型）表示"沿用底版"。
    /// </remarks>
    private static ThemePalette CopyOf(ThemePalette basePalette, ThemePalette overrides) => new()
    {
        IsDark = overrides.IsDark,
        WindowBackground = overrides.WindowBackground,
        Surface = overrides.Surface,
        SurfaceAlt = overrides.SurfaceAlt,
        Border = overrides.Border,
        Divider = overrides.Divider,
        Foreground = overrides.Foreground,
        SecondaryForeground = overrides.SecondaryForeground,
        Accent = overrides.Accent,
        OnAccent = overrides.OnAccent,
        AccentHover = overrides.AccentHover,
        HoverBackground = overrides.HoverBackground,
        Error = overrides.Error,
        Success = overrides.Success,
        Warning = overrides.Warning,
        Star = overrides.Star,
        StarInactive = overrides.StarInactive,
        OverlayBar = overrides.OverlayBar,
        OverlayBarBorder = overrides.OverlayBarBorder,
        OverlayForeground = overrides.OverlayForeground,
        OverlayButton = overrides.OverlayButton,
        OverlayButtonBorder = overrides.OverlayButtonBorder,
        OverlayButtonHover = overrides.OverlayButtonHover,
        OverlaySelected = overrides.OverlaySelected,
        ToastBackground = overrides.ToastBackground,
        ToastBorder = overrides.ToastBorder,
        ToastForeground = overrides.ToastForeground,
        RadiusLarge = overrides.RadiusLarge,
        RadiusMedium = overrides.RadiusMedium,
        RadiusSmall = overrides.RadiusSmall,
    };

    /// <summary>按主题枚举取调色板（<see cref="AppTheme.System"/> 由调用方先解析成实际主题）。</summary>
    public static ThemePalette For(AppTheme theme) => theme == AppTheme.Light ? Light : Dark;

    /// <summary>把 <see cref="AppTheme"/> 解析成实际生效的浅/深（跟随系统时读注册表）。</summary>
    public static AppTheme ResolveEffective(AppTheme theme) => theme switch
    {
        AppTheme.Light => AppTheme.Light,
        AppTheme.Dark => AppTheme.Dark,
        _ => ThemeManager.IsSystemUsingLightTheme() ? AppTheme.Light : AppTheme.Dark,
    };

    /// <summary>按实际主题取调色板。</summary>
    public static ThemePalette ForEffective(AppTheme effectiveTheme) =>
        For(effectiveTheme == AppTheme.Light ? AppTheme.Light : AppTheme.Dark);

    public override string ToString() =>
        $"{(IsDark ? "深色" : "浅色")} 圆角 大{RadiusLarge}/中{RadiusMedium}/小{RadiusSmall}";

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
