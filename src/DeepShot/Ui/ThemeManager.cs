using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using DeepShot.Core.Settings;

namespace DeepShot.Ui;

/// <summary>深色标题栏所需的 P/Invoke（dwmapi）。</summary>
internal static class DwmApi
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 20H1+/Win11）。</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Win10 1809–1909 上的同一个属性编号（旧编号）。</summary>
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int SetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 让窗口标题栏跟随主题（深色主题下白字深底）。失败时返回 false（旧系统没有这个属性），
    /// 由调用方决定是否记日志 —— 绝不能因此让窗口打不开。
    /// </summary>
    /// <summary>Win11：让无边框窗口使用系统圆角（旧系统无此属性，静默失败）。</summary>
    public static bool TrySetRoundedCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var value = 2; // DWMWCP_ROUND
        return SetWindowAttribute(handle, 33, ref value, sizeof(int)) == 0;
    }

    public static bool TrySetDarkTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var value = dark ? 1 : 0;
        if (SetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) == 0)
        {
            return true;
        }

        value = dark ? 1 : 0;
        return SetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int)) == 0;
    }
}

/// <summary>
/// 读取 Windows 的"应用使用浅色主题"开关与系统语言（T30/T31 的系统跟随）。
/// </summary>
internal static class SystemEnvironment
{
    /// <summary>个性化设置里的"应用模式"键。</summary>
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>应用模式的注册表值名。</summary>
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>系统语言/区域所在的注册表键。</summary>
    private const string InternationalKeyPath = @"Control Panel\International";

    /// <summary>系统区域的注册表值名（<c>zh-CN</c> 这种格式）。</summary>
    private const string LocaleNameValue = "LocaleName";

    /// <summary>
    /// 应用当前是否使用浅色主题（注册表 <c>AppsUseLightTheme</c> = 1）。
    /// 读不到时按浅色处理（Windows 默认外观），并返回 <c>null</c> 让调用方记日志。
    /// </summary>
    public static bool? TryIsSystemUsingLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue(AppsUseLightThemeValue) is int value)
            {
                return value != 0;
            }
        }
        catch (Exception)
        {
            // 注册表被策略锁定/键被删：按默认外观继续，绝不阻塞启动。
        }

        return null;
    }

    /// <summary>
    /// 系统界面语言是否中文（T31：中文系统用简体中文，其它一律英文）。
    /// </summary>
    /// <remarks>
    /// 三级回退：注册表 <c>LocaleName</c>（最贴近"系统界面语言"）→ <see cref="System.Globalization.CultureInfo.InstalledUICulture"/>
    /// → 当前 UI Culture。任何一级命中 zh 就按中文处理，否则英文。
    /// </remarks>
    public static bool IsSystemLanguageChinese()
    {
        var candidates = new List<string>(3);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternationalKeyPath);
            if (key?.GetValue(LocaleNameValue) is string locale && !string.IsNullOrWhiteSpace(locale))
            {
                candidates.Add(locale);
            }
        }
        catch (Exception)
        {
            // 忽略，继续用 CultureInfo。
        }

        candidates.Add(System.Globalization.CultureInfo.InstalledUICulture.Name);
        candidates.Add(System.Globalization.CultureInfo.CurrentUICulture.Name);

        foreach (var candidate in candidates)
        {
            if (candidate.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>把 <see cref="AppLanguage"/> 解析成实际语言（系统语言跟随者按系统决定）。</summary>
    public static AppLanguage ResolveLanguage(AppLanguage language) => language switch
    {
        AppLanguage.ZhHans => AppLanguage.ZhHans,
        AppLanguage.English => AppLanguage.English,
        _ => IsSystemLanguageChinese() ? AppLanguage.ZhHans : AppLanguage.English,
    };
}

/// <summary>
/// 主题入口（T30）：当前调色板、系统浅深色判断、"跟随系统"解析与变更通知。
/// </summary>
/// <remarks>
/// <para>
/// <b>切换立即生效不重启</b>：<see cref="ThemeManager"/> 不重新加载 XAML 字典，而是持有**可写的动态资源字典**
/// （<see cref="BrushKeys"/> 里的每个键都指向一个 <see cref="SolidColorBrush"/>），切换主题时就地替换
/// 画刷的颜色。所有用 <c>DynamicResource</c> 引用这些键的控件会立刻重绘；代码里创建的控件
/// 通过 <see cref="Changed"/> 事件重建/刷新自己的颜色。
/// </para>
/// <para>
/// <b>为什么不用两套 XAML 字典</b>：仓库 UI 全是代码构建，没有 XAML 窗口；两套字典要么每个窗口
/// 都要写 <c>FindResource</c>，要么就得把窗口改成 XAML（会引入两套命名空间与 partial 类）。
/// 动态资源 + 语义色键是这类"代码建 UI"最省事、也最容易验证的做法。
/// </para>
/// </remarks>
public static class ThemeManager
{
    /// <summary>主题资源键（<see cref="SolidColorBrush"/>）。</summary>
    public static class BrushKeys
    {
        public const string WindowBackground = "Theme.WindowBackground";
        public const string Surface = "Theme.Surface";
        public const string SurfaceAlt = "Theme.SurfaceAlt";
        public const string Border = "Theme.Border";
        public const string Divider = "Theme.Divider";
        public const string Foreground = "Theme.Foreground";
        public const string SecondaryForeground = "Theme.SecondaryForeground";
        public const string Accent = "Theme.Accent";
        public const string OnAccent = "Theme.OnAccent";
        public const string HoverBackground = "Theme.HoverBackground";
        public const string Error = "Theme.Error";
        public const string Success = "Theme.Success";
        public const string Warning = "Theme.Warning";
        public const string Star = "Theme.Star";
        public const string StarInactive = "Theme.StarInactive";
        public const string OverlayBar = "Theme.OverlayBar";
        public const string OverlayBarBorder = "Theme.OverlayBarBorder";
        public const string OverlayForeground = "Theme.OverlayForeground";
        public const string OverlayButton = "Theme.OverlayButton";
        public const string OverlayButtonBorder = "Theme.OverlayButtonBorder";
        public const string OverlayButtonHover = "Theme.OverlayButtonHover";
        public const string OverlaySelected = "Theme.OverlaySelected";
        public const string ToastBackground = "Theme.ToastBackground";
        public const string ToastBorder = "Theme.ToastBorder";
        public const string ToastForeground = "Theme.ToastForeground";
    }

    /// <summary>圆角资源键（<see cref="double"/>）。</summary>
    public static class RadiusKeys
    {
        public const string Large = "Theme.RadiusLarge";
        public const string Medium = "Theme.RadiusMedium";
        public const string Small = "Theme.RadiusSmall";
    }

    /// <summary>字体族资源键（<see cref="System.Windows.Media.FontFamily"/>）。</summary>
    public const string FontFamilyKey = "Theme.FontFamily";

    /// <summary>全应用统一的字体族（中英文都用它；缺中文字形时 WPF 会自动回退）。</summary>
    public const string FontFamilyName = "Segoe UI";

    private static readonly object Gate = new();
    private static ResourceDictionary? _dynamic;
    private static ThemePalette _palette = ThemePalette.Light;

    /// <summary>用户选择的主题（<see cref="AppTheme.System"/> 时实际颜色由系统决定）。</summary>
    public static AppTheme SelectedTheme { get; private set; } = AppTheme.System;

    /// <summary>当前实际生效的主题（只会是 <see cref="AppTheme.Light"/> 或 <see cref="AppTheme.Dark"/>）。</summary>
    public static AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    /// <summary>当前调色板（代码建 UI 时直接取颜色/圆角）。</summary>
    public static ThemePalette Palette => _palette;

    /// <summary>系统是否使用浅色主题（读注册表；读不到按浅色）。</summary>
    public static bool IsSystemUsingLightTheme() => SystemEnvironment.TryIsSystemUsingLightTheme() ?? true;

    /// <summary>主题变化后触发（参数是实际生效的浅/深）。窗口用它就地刷新颜色。</summary>
    public static event Action<AppTheme>? Changed;

    /// <summary>
    /// 初始化：把主题资源键写进应用资源字典（只做一次），然后应用 <see cref="AppSettings.Theme"/>。
    /// </summary>
    /// <param name="application">当前应用（<c>Application.Current</c>；测试里可为 null）。</param>
    /// <param name="theme">用户选择的主题。</param>
    /// <returns>本次是否真的改变了颜色（首次初始化一定为 true）。</returns>
    public static bool Initialize(Application? application, AppTheme theme)
    {
        lock (Gate)
        {
            if (_dynamic is null)
            {
                _dynamic = new ResourceDictionary();
                foreach (var key in AllBrushKeys)
                {
                    // 初始给浅色，随后 Apply 会整体替换。
                    _dynamic[key] = ThemePalette.Light.Get(ColorPropertyOf(key) ?? "Foreground");
                }

                _dynamic[RadiusKeys.Large] = ThemePalette.Light.RadiusLarge;
                _dynamic[RadiusKeys.Medium] = ThemePalette.Light.RadiusMedium;
                _dynamic[RadiusKeys.Small] = ThemePalette.Light.RadiusSmall;
                _dynamic[FontFamilyKey] = new System.Windows.Media.FontFamily(FontFamilyName);

                if (application is not null)
                {
                    application.Resources.MergedDictionaries.Add(_dynamic);
                    UiStyles.Install(application);
                }
            }
        }

        return Apply(theme);
    }

    /// <summary>
    /// 应用主题：解析"跟随系统"、就地替换所有主题画刷与圆角，并触发 <see cref="Changed"/>。
    /// </summary>
    /// <returns>实际生效的主题是否变化（颜色一定被重新写入）。</returns>
    public static bool Apply(AppTheme theme)
    {
        AppTheme effective;
        var changed = false;

        lock (Gate)
        {
            SelectedTheme = theme;
            effective = ThemePalette.ResolveEffective(theme);
            changed = effective != EffectiveTheme || _palette is null;
            EffectiveTheme = effective;
            _palette = ThemePalette.ForEffective(effective);

            if (_dynamic is not null)
            {
                foreach (var key in AllBrushKeys)
                {
                    var property = ColorPropertyOf(key);
                    if (property is null)
                    {
                        continue;
                    }

                    // 就地替换画刷：DynamicResource 的引用或重新解析都会拿到新画刷。
                    _dynamic[key] = _palette.Get(property);
                }

                _dynamic[RadiusKeys.Large] = _palette.RadiusLarge;
                _dynamic[RadiusKeys.Medium] = _palette.RadiusMedium;
                _dynamic[RadiusKeys.Small] = _palette.RadiusSmall;
            }
        }

        try
        {
            Changed?.Invoke(effective);
        }
        catch (Exception ex)
        {
            DeepShot.Platform.AppLog.Error("Theme", "主题变更通知的处理过程中出错", ex);
        }

        return changed;
    }

    /// <summary>主题资源键 → <see cref="ThemePalette"/> 上的颜色属性名。</summary>
    private static string? ColorPropertyOf(string key) => key switch
    {
        BrushKeys.WindowBackground => nameof(ThemePalette.WindowBackground),
        BrushKeys.Surface => nameof(ThemePalette.Surface),
        BrushKeys.SurfaceAlt => nameof(ThemePalette.SurfaceAlt),
        BrushKeys.Border => nameof(ThemePalette.Border),
        BrushKeys.Divider => nameof(ThemePalette.Divider),
        BrushKeys.Foreground => nameof(ThemePalette.Foreground),
        BrushKeys.SecondaryForeground => nameof(ThemePalette.SecondaryForeground),
        BrushKeys.Accent => nameof(ThemePalette.Accent),
        BrushKeys.OnAccent => nameof(ThemePalette.OnAccent),
        BrushKeys.HoverBackground => nameof(ThemePalette.HoverBackground),
        BrushKeys.Error => nameof(ThemePalette.Error),
        BrushKeys.Success => nameof(ThemePalette.Success),
        BrushKeys.Warning => nameof(ThemePalette.Warning),
        BrushKeys.Star => nameof(ThemePalette.Star),
        BrushKeys.StarInactive => nameof(ThemePalette.StarInactive),
        BrushKeys.OverlayBar => nameof(ThemePalette.OverlayBar),
        BrushKeys.OverlayBarBorder => nameof(ThemePalette.OverlayBarBorder),
        BrushKeys.OverlayForeground => nameof(ThemePalette.OverlayForeground),
        BrushKeys.OverlayButton => nameof(ThemePalette.OverlayButton),
        BrushKeys.OverlayButtonBorder => nameof(ThemePalette.OverlayButtonBorder),
        BrushKeys.OverlayButtonHover => nameof(ThemePalette.OverlayButtonHover),
        BrushKeys.OverlaySelected => nameof(ThemePalette.OverlaySelected),
        BrushKeys.ToastBackground => nameof(ThemePalette.ToastBackground),
        BrushKeys.ToastBorder => nameof(ThemePalette.ToastBorder),
        BrushKeys.ToastForeground => nameof(ThemePalette.ToastForeground),
        _ => null,
    };

    private static readonly string[] AllBrushKeys =
    {
        BrushKeys.WindowBackground,
        BrushKeys.Surface,
        BrushKeys.SurfaceAlt,
        BrushKeys.Border,
        BrushKeys.Divider,
        BrushKeys.Foreground,
        BrushKeys.SecondaryForeground,
        BrushKeys.Accent,
        BrushKeys.OnAccent,
        BrushKeys.HoverBackground,
        BrushKeys.Error,
        BrushKeys.Success,
        BrushKeys.Warning,
        BrushKeys.Star,
        BrushKeys.StarInactive,
        BrushKeys.OverlayBar,
        BrushKeys.OverlayBarBorder,
        BrushKeys.OverlayForeground,
        BrushKeys.OverlayButton,
        BrushKeys.OverlayButtonBorder,
        BrushKeys.OverlayButtonHover,
        BrushKeys.OverlaySelected,
        BrushKeys.ToastBackground,
        BrushKeys.ToastBorder,
        BrushKeys.ToastForeground,
    };
}
