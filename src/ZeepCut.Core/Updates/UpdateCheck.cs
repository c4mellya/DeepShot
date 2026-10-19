using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeepCut.Core.Updates;

/// <summary>
/// 一次"检查更新"的结论（T32）。<b>只有 <see cref="UpdateStatus.UpdateAvailable"/> 与
/// <see cref="UpdateStatus.UpToDate"/> 是"确定"的结论</b>，其余都必须如实告诉用户没查成。
/// </summary>
public enum UpdateStatus
{
    /// <summary>没有配置更新渠道（<see cref="AppInfo.UpdateManifestUrl"/> 为空）：**绝不能**说"已是最新版"。</summary>
    NotConfigured = 0,

    /// <summary>清单下载/解析失败（网络、HTTP 状态、JSON 格式）。同样不能说"已是最新版"。</summary>
    Failed = 1,

    /// <summary>清单合法，且清单版本不高于当前版本。</summary>
    UpToDate = 2,

    /// <summary>清单合法，且清单版本高于当前版本。</summary>
    UpdateAvailable = 3,
}

/// <summary>
/// 更新清单（约定格式）：<c>{ "version": "0.2.0", "url": "https://…", "notes": "…" }</c>。
/// </summary>
/// <param name="VersionText">清单里的原始版本文本。</param>
/// <param name="Version">解析后的版本（解析失败时为 null，由 <see cref="UpdateCheckResult"/> 判定为失败）。</param>
/// <param name="Url">下载页地址（可空；没有时只显示版本号，不给下载按钮）。</param>
/// <param name="Notes">更新说明（可空）。</param>
[method: JsonConstructor]
public sealed record UpdateManifest(string? VersionText, AppVersion? Version, string? Url, string? Notes)
{
    /// <summary>清单 JSON 的读取选项（camelCase、容忍大小写、跳过注释）。</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 解析清单 JSON。<b>永不抛异常</b>：任何问题都通过 <paramref name="error"/> 报告。
    /// </summary>
    public static bool TryParse(string? json, out UpdateManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "更新清单是空的。";
            return false;
        }

        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"更新清单不是有效的 JSON：{ex.Message}";
            return false;
        }

        if (dto is null)
        {
            error = "更新清单内容为空（null）。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.Version))
        {
            error = "更新清单缺少 version 字段。";
            return false;
        }

        if (!AppVersion.TryParse(dto.Version, out var version, out var versionError))
        {
            error = $"更新清单的 version 不合法：{versionError}";
            return false;
        }

        string? url = null;
        if (!string.IsNullOrWhiteSpace(dto.Url))
        {
            if (!Uri.TryCreate(dto.Url.Trim(), UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = $"更新清单的 url 不是有效的 http/https 地址：“{dto.Url}”。";
                return false;
            }

            url = parsed.ToString();
        }

        manifest = new UpdateManifest(dto.Version.Trim(), version, url, string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim());
        return true;
    }

    public override string ToString() => $"{VersionText}（{Url ?? "无下载页"}）{Notes ?? string.Empty}";

    /// <summary>清单 JSON 的形状（只用于反序列化，属性全部可空以便区分"缺失"与"空串"）。</summary>
    private sealed record ManifestDto(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("notes")] string? Notes);
}

/// <summary>
/// 一次检查更新的完整结果（T32）：结论 + 当前版本 + 清单 + 面向用户的说明。
/// </summary>
/// <param name="Status">结论。</param>
/// <param name="CurrentVersion">当前运行的版本。</param>
/// <param name="Manifest">清单（<see cref="UpdateStatus.Failed"/> / <see cref="UpdateStatus.NotConfigured"/> 时为 null）。</param>
/// <param name="ManifestUrl">本次查询用的渠道地址（未配置时为 null）。</param>
/// <param name="Detail">失败原因或附加说明（成功时为空字符串）。</param>
public sealed record UpdateCheckResult(
    UpdateStatus Status,
    string CurrentVersion,
    UpdateManifest? Manifest,
    string? ManifestUrl,
    string Detail)
{
    /// <summary>是否有可下载的新版本。</summary>
    public bool HasUpdate => Status == UpdateStatus.UpdateAvailable;

    /// <summary>新版本号文本（无更新时为 null）。</summary>
    public string? AvailableVersion => HasUpdate ? Manifest?.VersionText : null;

    /// <summary>下载页地址（没有时调用方不显示下载按钮）。</summary>
    public string? DownloadUrl => HasUpdate ? Manifest?.Url : null;

    /// <summary>
    /// 面向用户的结论短语（不含句号）。设置/Toast/日志共用同一份文案，避免各处措辞不一致。
    /// </summary>
    public string DescribeTitle() => Status switch
    {
        UpdateStatus.NotConfigured => "尚未配置更新渠道",
        UpdateStatus.Failed => "检查更新失败",
        UpdateStatus.UpToDate => "已是最新版",
        UpdateStatus.UpdateAvailable => $"发现新版本 {AvailableVersion}",
        _ => "检查更新",
    };

    /// <summary>面向用户的详细说明（多行）。</summary>
    public string DescribeDetail() => Status switch
    {
        UpdateStatus.NotConfigured =>
            $"当前版本 {CurrentVersion}。\n\n" +
            "本程序还没有配置更新渠道（AppInfo.UpdateManifestUrl 为空），因此无法查询是否有新版本。\n" +
            "这不影响任何本地功能；请通过你获取 deep-cut 的渠道了解更新（本程序不会自行下载或安装）。",
        UpdateStatus.Failed =>
            $"当前版本 {CurrentVersion}。\n\n无法确认是否有新版本：{Detail}\n" +
            "请检查网络或更新渠道地址后重试。",
        UpdateStatus.UpToDate =>
            $"已是最新版：当前版本 {CurrentVersion}，更新渠道的最新版本是 {Manifest?.VersionText}。",
        UpdateStatus.UpdateAvailable =>
            string.IsNullOrWhiteSpace(Manifest?.Notes)
                ? $"发现新版本 {AvailableVersion}（当前版本 {CurrentVersion}）。"
                : $"发现新版本 {AvailableVersion}（当前版本 {CurrentVersion}）。\n\n更新说明：{Manifest!.Notes}",
        _ => Detail,
    };

    public override string ToString() =>
        $"状态={Status} 当前={CurrentVersion} 清单={Manifest?.VersionText ?? "-"} 渠道={ManifestUrl ?? "(未配置)"} 详情={Detail}";
}

/// <summary>
/// 检查更新的纯逻辑（T32）：判断渠道是否配置、解析清单、比较版本。
/// 网络请求本身在应用层（<c>UpdateChecker</c>），这样这里的规则可以在无网络下单测。
/// </summary>
public static class UpdateCheck
{
    /// <summary>渠道地址是否可用（非空白且是 http/https 绝对地址）。</summary>
    public static bool IsChannelConfigured(string? manifestUrl) =>
        !string.IsNullOrWhiteSpace(manifestUrl) &&
        Uri.TryCreate(manifestUrl.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// 用清单文本与当前版本号做比较。清单无法解析时返回 <see cref="UpdateStatus.Failed"/>，
    /// **绝不**返回"已是最新版"。
    /// </summary>
    /// <param name="manifestJson">清单 JSON 文本。</param>
    /// <param name="currentVersion">当前版本（例如程序集版本）。</param>
    /// <param name="manifestUrl">渠道地址（只用于记录）。</param>
    public static UpdateCheckResult Evaluate(string? manifestJson, string currentVersion, string? manifestUrl)
    {
        var currentText = NormalizeCurrentVersion(currentVersion);

        if (!UpdateManifest.TryParse(manifestJson, out var manifest, out var parseError) || manifest is null)
        {
            return new UpdateCheckResult(
                UpdateStatus.Failed,
                currentText,
                null,
                manifestUrl,
                parseError ?? "更新清单无法解析。");
        }

        if (!AppVersion.TryParse(currentVersion, out var current, out var currentError))
        {
            return new UpdateCheckResult(
                UpdateStatus.Failed,
                currentText,
                manifest,
                manifestUrl,
                $"无法解析当前程序版本“{currentVersion}”：{currentError}");
        }

        var newer = manifest.Version is { } remote && remote > current;
        return new UpdateCheckResult(
            newer ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate,
            current.ToNormalizedString(),
            manifest,
            manifestUrl,
            newer ? string.Empty : $"清单版本 {manifest.VersionText} 不高于当前版本 {current.ToNormalizedString()}。");
    }

    /// <summary>未配置渠道时的结果（固定 <see cref="UpdateStatus.NotConfigured"/>）。</summary>
    public static UpdateCheckResult NotConfigured(string currentVersion) => new(
        UpdateStatus.NotConfigured,
        NormalizeCurrentVersion(currentVersion),
        null,
        null,
        "AppInfo.UpdateManifestUrl 为空");

    /// <summary>网络/HTTP 层失败时的结果（固定 <see cref="UpdateStatus.Failed"/>）。</summary>
    public static UpdateCheckResult Failed(string currentVersion, string? manifestUrl, string reason) => new(
        UpdateStatus.Failed,
        NormalizeCurrentVersion(currentVersion),
        null,
        manifestUrl,
        reason);

    /// <summary>当前版本文本统一成 <c>0.1.0</c> 形态（程序集版本常带第 4 段 0）。</summary>
    private static string NormalizeCurrentVersion(string? currentVersion) =>
        AppVersion.TryParse(currentVersion, out var parsed, out _) ? parsed.ToNormalizedString() : (currentVersion ?? "?");
}
