using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ZeepCut.Core.Settings;
using ZeepCut.Platform;

namespace ZeepCut.Ui;

/// <summary>
/// 界面语言入口（T31）：加载 <c>Resources\Lang\Strings.zh-Hans.xaml</c> / <c>Strings.en.xaml</c>，
/// 把文案放进应用资源字典，并在语言切换时通知已打开的窗口重新取文案。
/// </summary>
/// <remarks>
/// <para>
/// <b>运行时切换</b>：每次切换都重新 <see cref="XamlReader.Load(System.Xml.XmlReader)"/> 一份资源字典
/// 替换旧的。窗口里用 <c>DynamicResource</c> 引用的文字会立刻变；代码构建的窗口监听
/// <see cref="Changed"/> 重新给 <see cref="TextBlock.Text"/> / <c>Content</c> 赋值。
/// </para>
/// <para>
/// <b>首次跟随系统</b>：<see cref="AppLanguage.System"/> 由 <see cref="SystemEnvironment.ResolveLanguage"/>
/// 解析成中文或英文（中文系统 → 简体中文，其它 → 英文）。
/// </para>
/// <para>
/// <b>缺失键</b>：<see cref="Format"/> 找不到键时返回 <c>[key]</c> 并把键名记进
/// <see cref="MissingKeys"/>，这样"漏翻译"会以 <c>[S.Xxx.Yyy]</c> 的形式**肉眼可见**地出现在界面上，
/// 而不是悄悄显示中文（也不抛异常把窗口搞崩）。启动时会检查两份字典的键是否一一对应并写日志。
/// </para>
/// </remarks>
public static partial class StringsUi
{
    /// <summary>所有资源键的前缀（与主题的 <c>Theme.*</c> 区分）。</summary>
    public const string KeyPrefix = "S.";

    /// <summary>字典在输出目录里的相对位置。</summary>
    public const string RelativeFolder = @"Resources\Lang";

    private static readonly object Gate = new();
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);
    private static ResourceDictionary _current = new();
    private static List<string>? _allKeys;

    /// <summary>当前实际生效的语言（只会是 <see cref="AppLanguage.ZhHans"/> 或 <see cref="AppLanguage.English"/>）。</summary>
    public static AppLanguage Current { get; private set; } = AppLanguage.ZhHans;

    /// <summary>用户选择的语言（<see cref="AppLanguage.System"/> 表示跟随系统）。</summary>
    public static AppLanguage Selected { get; private set; } = AppLanguage.System;

    /// <summary>当前语言是否中文（日志/验证用）。</summary>
    public static bool IsChinese => Current == AppLanguage.ZhHans;

    /// <summary>语言切换后触发（窗口用它重新设置文案与窗口标题）。</summary>
    public static event Action<AppLanguage>? Changed;

    /// <summary>界面上出现过的缺失键（诊断用，启动后写日志）。</summary>
    public static IReadOnlyCollection<string> MissingKeys
    {
        get
        {
            lock (Gate)
            {
                return Missing.ToArray();
            }
        }
    }

    /// <summary>当前语言字典里的全部键（不存在时返回空）。</summary>
    public static IReadOnlyList<string> AllKeys
    {
        get
        {
            lock (Gate)
            {
                if (_allKeys is not null)
                {
                    return _allKeys;
                }

                _allKeys = _current.Keys.OfType<string>().OrderBy(key => key, StringComparer.Ordinal).ToList();
                return _allKeys;
            }
        }
    }

    /// <summary>取一条文案（缺失时返回 <c>[key]</c> 并记录）。</summary>
    /// <remarks>
    /// <b>T32 实测发现的坑</b>：XAML 里的字符串字面量**不能**写真正的换行 ——
    /// 按 XML 规范，元素内容里的换行会被规范化成空格；<c>&amp;#x0A;</c> 同样被吃掉；
    /// 而 <c>\n</c> 会被原样保留成两个字符（对话框里真的显示成 "\n\n"）。
    /// 所以资源文件里统一写 <c>\n</c>，由这里在取值时还原成换行。
    /// </remarks>
    public static string Get(string key)
    {
        lock (Gate)
        {
            if (_current[key] is string text)
            {
                return Unescape(text);
            }
        }

        RecordMissing(key);
        return $"[{key}]";
    }

    /// <summary>把资源文本里的 <c>\n</c> 还原成换行（只认这一个转义，不做其它解释）。</summary>
    internal static string Unescape(string text) =>
        text.Contains('\\', StringComparison.Ordinal)
            ? text.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\r", string.Empty, StringComparison.Ordinal)
            : text;

    /// <summary>取一条带占位符的文案并按 <see cref="string.Format(string, object?)"/> 填充。</summary>
    public static string Format(string key, params object?[] arguments)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // 占位符写错时不能把界面搞崩：原样显示模板，并把问题记进日志。
            AppLog.Warn("Lang", $"文案“{key}”的占位符与参数不匹配：{template}");
            return template;
        }
    }

    /// <summary>判断两个文案键是否存在（诊断用）。</summary>
    public static bool HasKey(string key)
    {
        lock (Gate)
        {
            return _current.Contains(key);
        }
    }

    /// <summary>
    /// 初始化：加载两份字典并应用 <paramref name="language"/>。
    /// </summary>
    /// <param name="application">当前应用（<c>Application.Current</c>）。</param>
    /// <param name="language">用户选择的语言（可为跟随系统）。</param>
    /// <param name="baseDirectory">资源根目录（默认是程序目录）。</param>
    public static void Initialize(Application? application, AppLanguage language, string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;

        // 预加载两份字典做键一致性检查（不影响当前语言，纯诊断）。
        try
        {
            CheckKeyParity(root);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lang", $"语言资源自检失败（忽略）：{ex.Message}");
        }

        Apply(application, language);
    }

    /// <summary>切换语言并就地替换应用资源字典。</summary>
    /// <param name="application">当前应用（可为 null：只更新内部字典，测试用）。</param>
    /// <param name="language">目标语言选择。</param>
    /// <param name="baseDirectory">资源根目录（默认是程序目录）。</param>
    /// <returns>实际生效的语言。</returns>
    public static AppLanguage Apply(Application? application, AppLanguage language, string? baseDirectory = null)
    {
        var effective = SystemEnvironment.ResolveLanguage(language);
        var dictionary = LoadDictionary(baseDirectory ?? AppContext.BaseDirectory, effective);
        ResourceDictionary? previous;

        lock (Gate)
        {
            previous = _current;
            _current = dictionary;
            _allKeys = null;
            Selected = language;
            Current = effective;
            Missing.Clear();
        }

        if (application is not null)
        {
            try
            {
                if (previous is not null && application.Resources.MergedDictionaries.Contains(previous))
                {
                    application.Resources.MergedDictionaries.Remove(previous);
                }

                application.Resources.MergedDictionaries.Add(dictionary);
            }
            catch (Exception ex)
            {
                AppLog.Error("Lang", "替换语言资源字典失败（界面可能仍显示旧语言）", ex);
            }
        }

        // 让日期分组标题跟着语言走（中文"今天/昨天"、英文"Today/Yesterday"）。
        Core.History.HistoryGrouping.Localize = IsChinese ? null : CreateEnglishLocalizer();

        AppLog.Info(
            "Lang",
            $"界面语言已切换：选择={language} 生效={effective}（{(IsChinese ? "简体中文" : "English")}）" +
            $" 文案条数={AllKeys.Count}");

        try
        {
            Changed?.Invoke(effective);
        }
        catch (Exception ex)
        {
            AppLog.Error("Lang", "语言变更通知的处理过程中出错", ex);
        }

        return effective;
    }

    /// <summary>资源字典的完整路径。</summary>
    public static string GetDictionaryPath(string baseDirectory, AppLanguage effectiveLanguage)
    {
        var file = effectiveLanguage == AppLanguage.English ? "Strings.en.xaml" : "Strings.zh-Hans.xaml";
        return Path.Combine(baseDirectory, RelativeFolder, file);
    }

    /// <summary>读一份字典（出错时返回空字典，界面显示 <c>[key]</c> 而不是整窗崩溃）。</summary>
    public static ResourceDictionary LoadDictionary(string baseDirectory, AppLanguage effectiveLanguage)
    {
        var path = GetDictionaryPath(baseDirectory, effectiveLanguage);
        try
        {
            if (!File.Exists(path))
            {
                AppLog.Error("Lang", $"语言资源文件不存在：{path}（界面将显示 [键名]）");
                return new ResourceDictionary();
            }

            using var stream = File.OpenRead(path);
            using var reader = System.Xml.XmlReader.Create(stream);
            return XamlReader.Load(reader) as ResourceDictionary ?? new ResourceDictionary();
        }
        catch (Exception ex)
        {
            AppLog.Error("Lang", $"读取语言资源失败：{path}（界面将显示 [键名]）", ex);
            return new ResourceDictionary();
        }
    }

    /// <summary>
    /// 在一棵控件树里找出所有缺失的文案键（启动日志/验证脚本用）。
    /// </summary>
    public static IReadOnlyList<string> DescribeMissing() => MissingKeys.Select(key => $"[{key}]").ToArray();

    /// <summary>
    /// 把一段文字按当前语言取出来：<paramref name="key"/> 存在则用字典里的文案，否则原样返回
    /// （用于日志之类的"内部字符串"，它们不是界面文案）。
    /// </summary>
    public static string Translate(string key) => HasKey(key) ? Get(key) : key;

    /// <summary>取一个 <see cref="TextBlock"/> 的文案（缺失键可见）。</summary>
    public static TextBlock Create(string key) => new() { Text = Get(key) };

    /// <summary>英文日期分组措辞（<c>Today</c> / <c>Yesterday</c> / <c>2026-10-16 Friday</c>）。</summary>
    public static string LocalizeDateTitle(DateOnly date, DateOnly today)
    {
        if (date == today)
        {
            return Get("S.History.Today");
        }

        if (date == today.AddDays(-1))
        {
            return Get("S.History.Yesterday");
        }

        var culture = CultureInfo.GetCultureInfo("en-US");
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + date.ToString("dddd", culture);
    }

    /// <summary>英文星期名（<see cref="Core.History.HistoryGrouping.WeekdayName"/> 用）。</summary>
    public static string EnglishWeekdayName(DateOnly date) =>
        date.ToString("dddd", CultureInfo.GetCultureInfo("en-US"));

    private static Func<DateOnly, DateOnly, string> CreateEnglishLocalizer() =>
        (date, today) => LocalizeDateTitle(date, today);

    private static void RecordMissing(string key)
    {
        lock (Gate)
        {
            if (Missing.Add(key))
            {
                AppLog.Warn("Lang", $"界面文案缺失：{key}（当前语言 {Current}）");
            }
        }
    }

    /// <summary>检查两份字典的键是否一一对应，把差异写进日志（漏翻译会被发现）。</summary>
    private static void CheckKeyParity(string baseDirectory)
    {
        var zh = LoadDictionary(baseDirectory, AppLanguage.ZhHans);
        var en = LoadDictionary(baseDirectory, AppLanguage.English);

        var zhKeys = zh.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        var enKeys = en.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);

        var missingInEnglish = zhKeys.Except(enKeys).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var missingInChinese = enKeys.Except(zhKeys).OrderBy(key => key, StringComparer.Ordinal).ToList();

        if (missingInEnglish.Count == 0 && missingInChinese.Count == 0)
        {
            AppLog.Info("Lang", $"语言资源键一致：中英各 {zhKeys.Count} 条");
            return;
        }

        AppLog.Warn(
            "Lang",
            $"语言资源键不一致：英文缺 {missingInEnglish.Count} 条 [{string.Join(", ", missingInEnglish)}]，" +
            $"中文缺 {missingInChinese.Count} 条 [{string.Join(", ", missingInChinese)}]");
    }
}
