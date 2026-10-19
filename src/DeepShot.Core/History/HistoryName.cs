using System.Globalization;
using System.Text;
using DeepShot.Core.Output;

namespace DeepShot.Core.History;

/// <summary>
/// 历史记录名称的纯逻辑：默认名称生成、显示/搜索用的名称清洗、同名区分。
/// </summary>
/// <remarks>
/// <para>
/// 名称里**不含扩展名**（扩展名由图片文件本身决定），所以图库重命名时用户
/// 输入 <c>会议截图</c> 而不是 <c>会议截图.png</c>。改名时仍会走 Windows 文件名的
/// 非法字符清洗（<see cref="CaptureFileName.SanitizeBaseName"/>），这样名称将来
/// 直接拿去做"另存为"的默认名也不会失败。
/// </para>
/// <para>
/// 库里的名称是**显示名**，不要求唯一：同名记录用 <see cref="CreateUniqueName"/>
/// 追加 <c> (2)</c> 区分（PLAN 只要求按名称搜索，不要求唯一键）。
/// </para>
/// </remarks>
public static class HistoryName
{
    /// <summary>默认名称前缀。</summary>
    public const string DefaultPrefix = CaptureFileName.DefaultBaseName;

    /// <summary>名称最大长度（清洗后）。</summary>
    public const int MaxLength = CaptureFileName.MaxBaseNameLength;

    /// <summary>
    /// 默认名称：<c>截图_2026-10-18_081500</c>（本地时间，24 小时制，无扩展名）。
    /// 与 <see cref="HistoryRecord.CreateDefaultName"/> 等价，单独留一个入口给调用方读起来更直白。
    /// </summary>
    public static string CreateDefault(DateTime localTime, string? prefix = null) =>
        HistoryRecord.CreateDefaultName(localTime, prefix);

    /// <summary>按来源给默认前缀（剪贴板贴图用 <c>贴图</c>，其它用 <c>截图</c>）。</summary>
    public static string DefaultPrefixFor(HistorySource source) => source == HistorySource.Clipboard
        ? "贴图"
        : DefaultPrefix;

    /// <summary>默认名称（按来源选前缀）。</summary>
    public static string CreateDefaultFor(HistorySource source, DateTime localTime) =>
        CreateDefault(localTime, DefaultPrefixFor(source));

    /// <summary>
    /// 清洗用户输入的名称：替换 Windows 非法字符与控制字符为 <c>_</c>、去首尾空白、
    /// 去掉结尾的点/空格、超长截断；空名称回退到 <see cref="DefaultPrefix"/>。
    /// 用户顺手输入了 <c>.png</c> 也会去掉扩展名（名称本身不带扩展名）。
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DefaultPrefix;
        }

        var trimmed = name.Trim().TrimEnd('.', ' ');
        var extension = GetKnownExtension(trimmed);
        if (extension is not null)
        {
            trimmed = trimmed[..^extension.Length].TrimEnd('.', ' ');
        }

        if (trimmed.Length == 0)
        {
            return DefaultPrefix;
        }

        return CaptureFileName.SanitizeBaseName(trimmed);
    }

    /// <summary>
    /// 生成不重名的名称：<paramref name="baseName"/> 已存在时依次尝试
    /// <c>名称 (2)</c>、<c>名称 (3)</c>…（大小写不敏感，与 Windows 文件系统一致）。
    /// </summary>
    /// <param name="baseName">期望名称（会先被 <see cref="Sanitize"/>）。</param>
    /// <param name="existingNames">已存在的名称集合。</param>
    /// <param name="maxAttempts">最多尝试的序号（默认 999），用尽后追加时间戳兜底。</param>
    public static string CreateUniqueName(
        string baseName,
        IEnumerable<string> existingNames,
        int maxAttempts = 999)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "至少要尝试 1 次。");
        }

        var sanitized = Sanitize(baseName);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in existingNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                taken.Add(name.Trim());
            }
        }

        if (!taken.Contains(sanitized))
        {
            return sanitized;
        }

        for (var index = 2; index <= maxAttempts + 1; index++)
        {
            var candidate = WithSuffix(sanitized, index);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        // 极端情况（几百条同名）：用时间戳兜底，仍然保证返回一个可用名称。
        return WithSuffix(
            sanitized,
            int.Parse(
                DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture));
    }

    /// <summary>判断名称是否命中搜索关键字（大小写不敏感的子串匹配；空关键字命中全部）。</summary>
    public static bool MatchesSearch(string? name, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return !string.IsNullOrEmpty(name) &&
               name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>追加 <c> (n)</c> 后缀，并保证总长度不超过 <see cref="MaxLength"/>。</summary>
    private static string WithSuffix(string name, int index)
    {
        var suffix = $" ({index.ToString(CultureInfo.InvariantCulture)})";
        var room = MaxLength - suffix.Length;
        var head = name.Length > room ? name[..Math.Max(1, room)].TrimEnd('.', ' ') : name;
        return head + suffix;
    }

    /// <summary>只去掉已知的图片扩展名（不把名称里普通的点当成扩展名）。</summary>
    private static string? GetKnownExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return null;
        }

        var extension = name[dot..];
        return extension.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => extension,
            _ => null,
        };
    }
}
