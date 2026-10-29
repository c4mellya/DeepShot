using System.Windows.Media;
using Microsoft.Win32;
using DeepShot.Core.Settings;

namespace DeepShot.Ui;

/// <summary>
/// Windows 11 Fluent 配色：系统中性色阶 + 用户在"个性化 → 颜色"里选的强调色。
/// </summary>
/// <remarks>
/// <para>
/// 中性色取自 WinUI 3 的 Mica/Layer/Card 填充与描边（浅色 #F3F3F3 窗底 / #FBFBFB 卡片，
/// 深色 #202020 / #2B2B2B），所以应用和资源管理器、设置放在一起不显得"自成一派"。
/// </para>
/// <para>
/// 强调色读 <c>Explorer\Accent\AccentPalette</c>：浅色用 AccentDark1、深色用 AccentLight2，
/// 与 WinUI 的 <c>AccentFillColorDefault</c> 同一取法；读不到时回落到 Windows 默认蓝。
/// <see cref="ThemeBrand"/> 只为兼容旧设置保留，不再影响颜色。
/// </para>
/// <para>
/// 遮罩工具条（Overlay*）与提示窗（Toast*）压在冻结画面上，始终使用深色亚克力。
/// </para>
/// </remarks>
internal static class ThemePalettes
{
    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    private static readonly Color DefaultAccentLight = Color.FromRgb(0x00, 0x5F, 0xB8);
    private static readonly Color DefaultAccentDark = Color.FromRgb(0x60, 0xCD, 0xFF);

    private static readonly object Gate = new();
    private static ThemePalette? _light;
    private static ThemePalette? _dark;
    private static (Color Light, Color Dark)? _accent;

    /// <summary>默认浅色（启动初始值）。</summary>
    internal static ThemePalette DeepSeekLight => For(ThemeBrand.DeepSeek, AppTheme.Light);

    /// <summary>按实际明暗取配色（<paramref name="brand"/> 已不影响颜色）。</summary>
    internal static ThemePalette For(ThemeBrand brand, AppTheme effectiveTheme)
    {
        lock (Gate)
        {
            var accent = _accent ??= ReadSystemAccent();
            return effectiveTheme == AppTheme.Dark
                ? _dark ??= BuildDark(accent.Dark)
                : _light ??= BuildLight(accent.Light);
        }
    }

    /// <summary>系统强调色可能已变：丢掉缓存，下次 <see cref="For"/> 重新读取。</summary>
    internal static void InvalidateAccent()
    {
        lock (Gate)
        {
            var next = ReadSystemAccent();
            if (_accent is { } current && current == next)
            {
                return;
            }

            _accent = next;
            _light = null;
            _dark = null;
        }
    }

    private static (Color Light, Color Dark) ReadSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKeyPath);
            // 8 个 RGBA：Light3 Light2 Light1 Base Dark1 Dark2 Dark3 (Unused)
            if (key?.GetValue("AccentPalette") is byte[] { Length: >= 32 } p)
            {
                return (Color.FromRgb(p[16], p[17], p[18]), Color.FromRgb(p[4], p[5], p[6]));
            }
        }
        catch (Exception)
        {
            // 策略锁定或键不存在：回落默认蓝。
        }

        return (DefaultAccentLight, DefaultAccentDark);
    }

    private static ThemePalette BuildLight(Color accent) => Build(
        dark: false,
        window: Rgb(0xF3, 0xF3, 0xF3),
        surface: Rgb(0xFB, 0xFB, 0xFB),
        surfaceAlt: Rgb(0xF5, 0xF5, 0xF5),
        border: Rgb(0xE5, 0xE5, 0xE5),
        divider: Rgb(0xEB, 0xEB, 0xEB),
        foreground: Rgb(0x1B, 0x1B, 0x1B),
        secondary: Rgb(0x5C, 0x5C, 0x5C),
        tertiary: Rgb(0x8A, 0x8A, 0x8A),
        accent: accent,
        onAccent: Rgb(0xFF, 0xFF, 0xFF),
        accentHover: Mix(accent, Rgb(0xFB, 0xFB, 0xFB), 0.10),
        accentSoft: Mix(accent, Rgb(0xFB, 0xFB, 0xFB), 0.86),
        hover: Argb(0x0A, 0x00, 0x00, 0x00),
        pressed: Argb(0x06, 0x00, 0x00, 0x00),
        focusRing: Argb(0xE4, 0x00, 0x00, 0x00));

    private static ThemePalette BuildDark(Color accent) => Build(
        dark: true,
        window: Rgb(0x20, 0x20, 0x20),
        surface: Rgb(0x2B, 0x2B, 0x2B),
        surfaceAlt: Rgb(0x32, 0x32, 0x32),
        border: Rgb(0x3A, 0x3A, 0x3A),
        divider: Rgb(0x34, 0x34, 0x34),
        foreground: Rgb(0xFF, 0xFF, 0xFF),
        secondary: Rgb(0xC5, 0xC5, 0xC5),
        tertiary: Rgb(0x9A, 0x9A, 0x9A),
        accent: accent,
        onAccent: Rgb(0x00, 0x00, 0x00),
        accentHover: Mix(accent, Rgb(0x20, 0x20, 0x20), 0.10),
        accentSoft: Mix(accent, Rgb(0x2B, 0x2B, 0x2B), 0.80),
        hover: Argb(0x0F, 0xFF, 0xFF, 0xFF),
        pressed: Argb(0x0A, 0xFF, 0xFF, 0xFF),
        focusRing: Rgb(0xFF, 0xFF, 0xFF));

    private static ThemePalette Build(
        bool dark,
        Color window,
        Color surface,
        Color surfaceAlt,
        Color border,
        Color divider,
        Color foreground,
        Color secondary,
        Color tertiary,
        Color accent,
        Color onAccent,
        Color accentHover,
        Color accentSoft,
        Color hover,
        Color pressed,
        Color focusRing)
    {
        // WinUI 系统状态色（SystemFillColorCritical / Success / Caution）。
        var error = dark ? Rgb(0xFF, 0x99, 0xA4) : Rgb(0xC4, 0x2B, 0x1C);
        var success = dark ? Rgb(0x6C, 0xCB, 0x5F) : Rgb(0x0F, 0x7B, 0x0F);
        var warning = dark ? Rgb(0xFC, 0xE1, 0x00) : Rgb(0x9D, 0x5D, 0x00);
        var star = dark ? Rgb(0xFF, 0xC8, 0x3D) : Rgb(0xE8, 0xA2, 0x00);
        var starInactive = dark ? Rgb(0x6E, 0x6E, 0x6E) : Rgb(0xB0, 0xB0, 0xB0);

        return new ThemePalette
        {
            IsDark = dark,
            WindowBackground = window,
            Surface = surface,
            SurfaceAlt = surfaceAlt,
            Border = border,
            Divider = divider,
            Foreground = foreground,
            SecondaryForeground = secondary,
            TertiaryForeground = tertiary,
            Accent = accent,
            OnAccent = onAccent,
            AccentHover = accentHover,
            HoverBackground = hover,
            PressedBackground = pressed,
            FocusRing = Colors.Transparent, // 全局禁用键盘焦点环（产品决定：不显示 Tab 选中框）
            AccentSoft = accentSoft,
            Error = error,
            Success = success,
            Warning = warning,
            Star = star,
            StarInactive = starInactive,

            // 深色亚克力：截图遮罩工具条与 Toast 不随明暗变化。
            OverlayBar = Argb(0xF0, 0x2C, 0x2C, 0x2C),
            OverlayBarBorder = Argb(0x33, 0xFF, 0xFF, 0xFF),
            OverlayForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),
            OverlayButton = Argb(0x00, 0xFF, 0xFF, 0xFF),
            OverlayButtonBorder = Argb(0x00, 0xFF, 0xFF, 0xFF),
            OverlayButtonHover = Argb(0x1A, 0xFF, 0xFF, 0xFF),
            OverlaySelected = Argb(0x2E, 0xFF, 0xFF, 0xFF),
            ToastBackground = Argb(0xF2, 0x2C, 0x2C, 0x2C),
            ToastBorder = Argb(0x33, 0xFF, 0xFF, 0xFF),
            ToastForeground = Argb(0xFF, 0xFF, 0xFF, 0xFF),

            // Fluent 圆角：窗口/卡片 8，控件 4。
            RadiusLarge = 8,
            RadiusMedium = 4,
            RadiusSmall = 4,
        };
    }

    /// <summary>a 与 b 混合，<paramref name="towardB"/> 为 b 的占比。</summary>
    private static Color Mix(Color a, Color b, double towardB) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * towardB),
        (byte)Math.Round(a.G + (b.G - a.G) * towardB),
        (byte)Math.Round(a.B + (b.B - a.B) * towardB));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
