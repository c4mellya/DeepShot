using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepShot.Core.Settings;

/// <summary>
/// 设置的加载 / 保存（T29）：<c>%LOCALAPPDATA%\DeepShot\settings.json</c>。
/// </summary>
/// <remarks>
/// <para>
/// <b>JSON 约定</b>：属性名 camelCase（<c>screenshotShortcut</c>、<c>pinShortcut</c>、<c>language</c>、
/// <c>theme</c>、<c>launchAtStartup</c>、<c>version</c>）；枚举写成可读的字符串
/// （<c>"Language": "ZhHans"</c> / <c>"Theme": "Dark"</c>），文件被用户打开时也能看懂；
/// 数字、大小写不同的写法（<c>"dark"</c>）同样能读回来。
/// </para>
/// <para>
/// <b>从不抛异常</b>：文件不存在 = 用默认值；内容坏掉 = 用默认值 + 警告；
/// 单个字段非法 = 只有那个字段回退 + 警告（<see cref="AppSettings.Sanitize"/>）。
/// 调用方（<c>SettingsStore</c> 在 App 层）负责把警告写进日志。
/// </para>
/// <para>
/// <b>保存走 <see cref="AtomicFile"/></b>：临时文件 → 替换，写失败时旧文件原封不动。
/// </para>
/// </remarks>
public static class AppSettingsFile
{
    /// <summary>序列化选项（与读取共用，避免两侧命名策略分叉）。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) },
    };

    /// <summary>读出设置文件里的原始 JSON（不校验；测试 JSON 往返用）。</summary>
    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    /// <summary>设置模型在磁盘上的文件名。</summary>
    public static string GetFileName(string dataRoot) => Path.Combine(dataRoot, AppSettings.FileName);

    /// <summary>
    /// 加载设置。<b>永不抛异常</b>。
    /// </summary>
    /// <param name="path">settings.json 的完整路径。</param>
    /// <param name="exists">文件是否存在（用于日志区分"首次启动"与"读失败"）。</param>
    public static AppSettingsLoadResult Load(string path, out bool exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        exists = false;

        try
        {
            if (!File.Exists(path))
            {
                return new AppSettingsLoadResult(AppSettings.Default, Array.Empty<string>(), LoadStatus.Missing);
            }

            exists = true;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettingsLoadResult(
                    AppSettings.Default,
                    new[] { "设置文件是空的，已全部使用默认值。" },
                    LoadStatus.Corrupted);
            }

            AppSettings? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                return new AppSettingsLoadResult(
                    AppSettings.Default,
                    new[] { $"设置文件不是有效的 JSON（{ex.Message}），已全部使用默认值。" },
                    LoadStatus.Corrupted);
            }

            var sanitized = AppSettings.Sanitize(parsed);
            return new AppSettingsLoadResult(
                sanitized.Settings,
                sanitized.Warnings,
                sanitized.HasCorrections ? LoadStatus.Corrected : LoadStatus.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 读不到（权限/被占用/路径是目录）：当作没有设置文件，但**保留** existed 的实际情况。
            return new AppSettingsLoadResult(
                AppSettings.Default,
                new[] { $"读取设置文件失败（{ex.Message}），已使用默认值。" },
                exists ? LoadStatus.Corrupted : LoadStatus.Missing);
        }
    }

    /// <summary>
    /// 保存设置（原子写）。失败时抛异常，**目标文件保持原样**。
    /// </summary>
    /// <param name="path">settings.json 的完整路径。</param>
    /// <param name="settings">要保存的设置（保存前会 <see cref="AppSettings.Sanitize"/>，绝不写出非法值）。</param>
    public static void Save(string path, AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);

        var sanitized = AppSettings.Sanitize(settings).Settings;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        AtomicFile.WriteAllText(path, Serialize(sanitized));
    }
}

/// <summary>设置加载结果的分类（只用于日志与测试断言）。</summary>
public enum LoadStatus
{
    /// <summary>文件不存在：首次启动，全部默认值。</summary>
    Missing = 0,

    /// <summary>文件存在且完全合法。</summary>
    Ok = 1,

    /// <summary>文件可读但个别字段非法：已逐项回退为默认值。</summary>
    Corrected = 2,

    /// <summary>文件不可读/不是 JSON/是空的：全部使用默认值。</summary>
    Corrupted = 3,
}

/// <summary>设置加载结果。</summary>
/// <param name="Settings">校验过的设置（永不包含非法值）。</param>
/// <param name="Warnings">被回退的项说明（空 = 完全合法）。</param>
/// <param name="Status">分类。</param>
public sealed record AppSettingsLoadResult(AppSettings Settings, IReadOnlyList<string> Warnings, LoadStatus Status)
{
    /// <summary>是否发生过任何回退。</summary>
    public bool HasWarnings => Warnings.Count > 0;

    public override string ToString() =>
        $"状态={Status} 设置=[{Settings}] 警告={Warnings.Count}";
}
