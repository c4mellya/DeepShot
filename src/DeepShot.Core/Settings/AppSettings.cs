using System.Text.Json.Serialization;

namespace DeepShot.Core.Settings;

/// <summary>界面语言（T31）。<see cref="System"/> = 跟随系统（首次启动的默认值）。</summary>
public enum AppLanguage
{
    /// <summary>跟随系统：中文系统用简体中文，其它一律用英文（PLAN：非中文默认英文）。</summary>
    System = 0,

    /// <summary>简体中文。</summary>
    ZhHans = 1,

    /// <summary>英文。</summary>
    English = 2,
}

/// <summary>截图唤起后的默认选区方式。</summary>
public enum CaptureSelectionMode
{
    /// <summary>悬停识别窗口，单击选中；仍可拖动自行框选。</summary>
    Window = 0,
    /// <summary>只用鼠标拖动框选矩形，不自动识别窗口。</summary>
    Rectangle = 1,
}

/// <summary>界面主题（T30）。<see cref="System"/> = 跟随系统（读注册表 <c>AppsUseLightTheme</c>）。</summary>
public enum AppTheme
{
    /// <summary>跟随系统。</summary>
    System = 0,

    /// <summary>浅色。</summary>
    Light = 1,

    /// <summary>深色。</summary>
    Dark = 2,
}

/// <summary>
/// 用户设置模型（T29）：<c>%LOCALAPPDATA%\DeepShot\settings.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>不可变</b>：所有"修改"都返回新实例（与 <see cref="History.HistoryRecord"/> 一致），
/// 这样"保存失败就不生效"的语义不会因为某个字段被就地改坏而失效。
/// </para>
/// <para>
/// <b>容错优先</b>：<see cref="Sanitize"/> 会把任何非法值替换成默认值，并且**收集**每次替换的原因
/// （<see cref="SanitizeResult.Warnings"/>），启动日志里能看到"设置文件里的主题值 99 不认识，已回退为跟随系统"。
/// 一份坏掉的设置文件绝不能让程序起不来。
/// </para>
/// <para>
/// <b>语言/主题用 <c>System</c> 作为 0 值</b>：首次启动（没有 settings.json）时两台机器应该各自跟随自己的
/// 系统语言与主题，而不是写死一个。
/// </para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>默认截图快捷键。</summary>
    public const string DefaultScreenshotShortcut = "Alt+A";

    /// <summary>默认快速截图快捷键：框选结束直接复制。</summary>
    public const string DefaultQuickScreenshotShortcut = "Alt+Shift+A";

    /// <summary>默认剪贴板贴图快捷键。</summary>
    public const string DefaultPinShortcut = "Alt+S";

    public const string DefaultQuickViewShortcut = "Alt+Q";
    public const int CurrentSettingsVersion = 2;

    /// <summary>设置文件名。</summary>
    public const string FileName = "settings.json";

    /// <summary>截图快捷键（规范文本，例如 <c>Alt+A</c>；由 <see cref="Shortcuts.ShortcutSpec"/> 解析）。</summary>
    public string ScreenshotShortcut { get; init; } = DefaultScreenshotShortcut;

    /// <summary>快速截图快捷键：选区完成后直接复制到剪贴板。</summary>
    public string QuickScreenshotShortcut { get; init; } = DefaultQuickScreenshotShortcut;

    /// <summary>从剪贴板贴图的快捷键（规范文本，例如 <c>Alt+S</c>）。</summary>
    public string PinShortcut { get; init; } = DefaultPinShortcut;

    /// <summary>默认 Alt+Q；用户清除后仍可点击托盘打开。</summary>
    public string QuickViewShortcut { get; init; } = DefaultQuickViewShortcut;

    /// <summary>界面语言（默认跟随系统）。</summary>
    public AppLanguage Language { get; init; } = AppLanguage.System;

    /// <summary>界面主题（默认跟随系统）。</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>旧设置没有此字段时保持原有的窗口识别行为。</summary>
    public CaptureSelectionMode CaptureMode { get; init; } = CaptureSelectionMode.Window;

    /// <summary>
    /// 开机启动（当前用户方式：<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>）。
    /// </summary>
    /// <remarks>
    /// 系统里可能已经有一条"对得上"的启动项（用户自己加的，或上一次运行留下的），
    /// **库里的值是用户意图**，启动时以它为准同步注册表（见 <c>StartupRegistration.Sync</c>）。
    /// </remarks>
    public bool LaunchAtStartup { get; init; }

    /// <summary>设置版本；版本 2 将旧的空快览快捷键升级为 Alt+Q。</summary>
    public int Version { get; init; } = CurrentSettingsVersion;

    /// <summary>全新安装的默认设置（所有项都是 PLAN 里的默认值）。</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>所有属性都是默认值（用于日志里区分"用户改过"与"没改过"）。</summary>
    [JsonIgnore]
    public bool IsDefault =>
        this == Default;

    /// <summary>校验并修正：非法值回退为默认值，返回新实例 + 警告列表。永不抛异常。</summary>
    /// <param name="settings">待校验的设置（通常是 JSON 反序列化结果或用户输入）。</param>
    public static SanitizeResult Sanitize(AppSettings? settings)
    {
        var warnings = new List<string>();

        if (settings is null)
        {
            warnings.Add("设置内容为空，已全部使用默认值。");
            return new SanitizeResult(Default, warnings);
        }

        var normalized = new AppSettings
        {
            ScreenshotShortcut = NormalizeShortcut(
                settings.ScreenshotShortcut,
                DefaultScreenshotShortcut,
                "截图快捷键",
                warnings),
            QuickScreenshotShortcut = NormalizeShortcut(
                settings.QuickScreenshotShortcut, DefaultQuickScreenshotShortcut, "快速截图快捷键", warnings),
            PinShortcut = NormalizeShortcut(settings.PinShortcut, DefaultPinShortcut, "贴图快捷键", warnings),
            QuickViewShortcut = NormalizeOptionalShortcut(
                settings.Version < CurrentSettingsVersion && string.IsNullOrWhiteSpace(settings.QuickViewShortcut)
                    ? DefaultQuickViewShortcut : settings.QuickViewShortcut, warnings),
            Language = NormalizeLanguage(settings.Language, warnings),
            Theme = NormalizeTheme(settings.Theme, warnings),
            CaptureMode = NormalizeCaptureMode(settings.CaptureMode, warnings),
            LaunchAtStartup = settings.LaunchAtStartup,
            Version = Math.Max(settings.Version, CurrentSettingsVersion),
        };

        return new SanitizeResult(normalized, warnings);
    }

    /// <summary>任意两个功能是否使用相同快捷键；空的可选快捷键不参与检测。</summary>
    public bool HasShortcutConflict()
    {
        var shortcuts = new HashSet<Shortcuts.ShortcutSpec>();
        foreach (var text in new[] { ScreenshotShortcut, QuickScreenshotShortcut, PinShortcut, QuickViewShortcut })
        {
            if (Shortcuts.ShortcutSpec.TryParse(text, out var shortcut, out _) && !shortcuts.Add(shortcut))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 解析两个快捷键（截图 / 贴图），并判断它们是否重复。
    /// </summary>
    /// <remarks>
    /// 单独做成一个结果类型，是因为设置窗口与启动注册都需要同一套判定：
    /// 解析失败（非法）与"两个键一模一样"（冲突）是两种不同的错误提示。
    /// </remarks>
    public ShortcutNamesResult ShortcutPair(
        out Shortcuts.ShortcutSpec? screenshot,
        out Shortcuts.ShortcutSpec? pin)
    {
        screenshot = null;
        pin = null;

        var hasScreenshot = Shortcuts.ShortcutSpec.TryParse(ScreenshotShortcut, out var screenshotSpec, out var screenshotError);
        var hasPin = Shortcuts.ShortcutSpec.TryParse(PinShortcut, out var pinSpec, out var pinError);
        screenshot = screenshotSpec;
        pin = pinSpec;

        if (!hasScreenshot || !hasPin)
        {
            return new ShortcutNamesResult(
                ShortcutNamesOutcome.Invalid,
                hasScreenshot ? pinError : screenshotError);
        }

        return screenshot!.ConflictsWith(pin)
            ? new ShortcutNamesResult(ShortcutNamesOutcome.Conflict, $"截图与贴图使用了同一个快捷键 {screenshot.Format()}。")
            : new ShortcutNamesResult(ShortcutNamesOutcome.Ok, null);
    }

    /// <summary>修改截图快捷键（返回新实例；不校验，校验见 <see cref="ValidateShortcut"/>）。</summary>
    public AppSettings WithScreenshotShortcut(string shortcut) => this with { ScreenshotShortcut = shortcut };

    /// <summary>修改快速截图快捷键（返回新实例；不校验）。</summary>
    public AppSettings WithQuickScreenshotShortcut(string shortcut) => this with { QuickScreenshotShortcut = shortcut };

    /// <summary>修改贴图快捷键（返回新实例；不校验）。</summary>
    public AppSettings WithPinShortcut(string shortcut) => this with { PinShortcut = shortcut };

    public AppSettings WithQuickViewShortcut(string shortcut) => this with { QuickViewShortcut = shortcut };

    /// <summary>修改语言。</summary>
    public AppSettings WithLanguage(AppLanguage language) => this with { Language = language };

    /// <summary>修改主题。</summary>
    public AppSettings WithTheme(AppTheme theme) => this with { Theme = theme };

    /// <summary>修改默认截图选区方式。</summary>
    public AppSettings WithCaptureMode(CaptureSelectionMode mode) => this with { CaptureMode = mode };

    /// <summary>修改开机启动。</summary>
    public AppSettings WithLaunchAtStartup(bool enabled) => this with { LaunchAtStartup = enabled };

    /// <summary>
    /// 校验单个用户输入的快捷键文本（设置窗口"输入即捕获"后调用）。
    /// </summary>
    /// <param name="text">用户输入（例如 <c>Ctrl+Shift+7</c>）。</param>
    /// <param name="other">另一个快捷键（用于冲突检测；可为 null = 不检测冲突）。</param>
    /// <param name="shortcut">解析成功时的规范快捷键。</param>
    /// <param name="error">失败原因（面向用户，可直接显示）。</param>
    public static bool ValidateShortcut(
        string? text,
        Shortcuts.ShortcutSpec? other,
        out Shortcuts.ShortcutSpec? shortcut,
        out string? error)
    {
        if (!Shortcuts.ShortcutSpec.TryParse(text, out shortcut, out error))
        {
            return false;
        }

        if (other is not null && shortcut!.ConflictsWith(other))
        {
            error = $"快捷键 {shortcut.Format()} 已经被另一个功能使用，请换一个组合。";
            shortcut = null;
            return false;
        }

        return true;
    }

    public override string ToString() =>
        $"截图={ScreenshotShortcut} 快速截图={QuickScreenshotShortcut} 贴图={PinShortcut} 语言={Language} 主题={Theme} 截图方式={CaptureMode} 开机启动={LaunchAtStartup}";

    private static string NormalizeOptionalShortcut(string? value, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (Shortcuts.ShortcutSpec.TryParse(value, out var shortcut, out _) && shortcut is not null)
            return shortcut.Format();
        warnings.Add("快览快捷键无效，已取消注册；仍可从托盘打开快览。");
        return string.Empty;
    }

    private static string NormalizeShortcut(
        string? value,
        string fallback,
        string label,
        List<string> warnings)
    {
        if (Shortcuts.ShortcutSpec.TryParse(value, out var parsed, out var error) && parsed is not null)
        {
            // 统一成规范写法（Ctrl+Alt+F1 顺序固定、主键大写），避免 "alt+a" 与 "Alt+A" 两种写法各存一份。
            return parsed.Format();
        }

        warnings.Add($"{label}“{value}”无效（{error}），已回退为默认值 {fallback}。");
        return fallback;
    }

    private static AppLanguage NormalizeLanguage(AppLanguage language, List<string> warnings)
    {
        if (Enum.IsDefined(language))
        {
            return language;
        }

        warnings.Add($"语言值 {(int)language} 不认识，已回退为跟随系统。");
        return AppLanguage.System;
    }

    private static CaptureSelectionMode NormalizeCaptureMode(CaptureSelectionMode mode, List<string> warnings)
    {
        if (Enum.IsDefined(mode)) return mode;
        warnings.Add($"截图方式 {(int)mode} 不认识，已回退为窗口识别。");
        return CaptureSelectionMode.Window;
    }

    private static AppTheme NormalizeTheme(AppTheme theme, List<string> warnings)
    {
        if (Enum.IsDefined(theme))
        {
            return theme;
        }

        warnings.Add($"主题值 {(int)theme} 不认识，已回退为跟随系统。");
        return AppTheme.System;
    }
}

/// <summary>快捷键检查结果类型。</summary>
public enum ShortcutNamesOutcome
{
    /// <summary>两个快捷键都合法且互不重复。</summary>
    Ok = 0,

    /// <summary>至少一个无法解析。</summary>
    Invalid = 1,

    /// <summary>两个快捷键完全相同。</summary>
    Conflict = 2,
}

/// <summary>两个快捷键的检查结果。</summary>
/// <param name="Outcome">结论。</param>
/// <param name="Error">面向用户的原因（<see cref="ShortcutNamesOutcome.Ok"/> 时为 null）。</param>
public sealed record ShortcutNamesResult(ShortcutNamesOutcome Outcome, string? Error)
{
    /// <summary>是否可以直接注册。</summary>
    public bool IsValid => Outcome == ShortcutNamesOutcome.Ok;
}

/// <summary><see cref="AppSettings.Sanitize"/> 的结果。</summary>
/// <param name="Settings">校验过的设置（永不包含非法值）。</param>
/// <param name="Warnings">被替换掉的非法值说明（空列表 = 文件完全合法）。</param>
public sealed record SanitizeResult(AppSettings Settings, IReadOnlyList<string> Warnings)
{
    /// <summary>是否做过修改。</summary>
    public bool HasCorrections => Warnings.Count > 0;
}
