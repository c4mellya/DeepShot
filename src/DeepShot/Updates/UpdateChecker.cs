using System.Net;
using System.Net.Http;
using System.Reflection;
using DeepShot.Core;
using DeepShot.Core.Updates;
using DeepShot.Platform;

namespace DeepShot.Updates;

/// <summary>
/// 检查更新（T32）：Get 更新清单 → 交给 Core 的 <see cref="UpdateCheck"/> 出结论。
/// </summary>
/// <remarks>
/// <para>
/// <b>PLAN 的硬性要求</b>：未配置渠道时明确显示"尚未配置更新渠道"；配置后比对版本；
/// 网络失败要明确提示失败 —— **任何不确定的情况都不能显示成"已是最新版"**。
/// 这条规则在 Core（<see cref="UpdateCheck"/>）里单测，这里只负责网络与超时。
/// </para>
/// <para>
/// <b>手动触发</b>：只在用户点托盘"检查更新"时发请求，不做后台轮询、不自动下载安装。
/// </para>
/// <para>
/// <b>超时/代理</b>：超时固定 <see cref="Timeout"/>（10 秒），避免托盘点了没反应；
/// 使用系统默认代理（用户机器上常见本地代理），失败直接归类为由用户可读的原因。
/// </para>
/// </remarks>
public static class UpdateChecker
{
    /// <summary>网络请求超时。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>清单请求的 User-Agent（有的托管平台会拒绝空 UA）。</summary>
    private const string UserAgent = "DeepShot-UpdateCheck/1.0";

    /// <summary>
    /// 当前运行的应用版本（优先信息版本，保留预发布标识；读不到时返回 <c>0.0.0</c>）。
    /// </summary>
    public static string GetCurrentVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (AppVersion.TryParse(informational, out var informationVersion, out _))
            {
                return informationVersion.ToNormalizedString();
            }

            var version = assembly.GetName().Version;
            if (version is null)
            {
                return "0.0.0";
            }

            return AppVersion.TryParse(version.ToString(), out var parsed, out _)
                ? parsed.ToNormalizedString()
                : version.ToString();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Update", $"读取当前程序版本失败：{ex.Message}");
            return "0.0.0";
        }
    }

    /// <summary>
    /// 执行一次检查（异步，UI 线程可 await）。**不抛异常**：任何失败都变成
    /// <see cref="UpdateStatus.Failed"/> 的结果。
    /// </summary>
    /// <param name="manifestUrl">渠道地址；为空/非法时直接返回"尚未配置更新渠道"。</param>
    /// <param name="httpClient">可注入的 HttpClient（测试用）；为 null 时创建带超时的实例。</param>
    public static async Task<UpdateCheckResult> CheckAsync(
        string? manifestUrl,
        HttpClient? httpClient = null)
    {
        var current = GetCurrentVersion();

        if (!UpdateCheck.IsChannelConfigured(manifestUrl))
        {
            AppLog.Info("Update", $"检查更新：未配置更新渠道（AppInfo.UpdateManifestUrl=“{manifestUrl}”），当前版本 {current}");
            return UpdateCheck.NotConfigured(current);
        }

        var url = manifestUrl!.Trim();
        AppLog.Info("Update", $"检查更新：GET {url}（当前版本 {current}）");
        AppLog.Info("Update", $"UPDATE_MANIFEST_URL={url}");

        var ownsClient = httpClient is null;
        var client = httpClient ?? new HttpClient { Timeout = Timeout };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            var isGitHub = new Uri(url).Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
            request.Headers.TryAddWithoutValidation("Accept", isGitHub ? "application/vnd.github+json" : "application/json");
            if (isGitHub)
            {
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(true);

            if (!response.IsSuccessStatusCode)
            {
                var reason = DescribeStatus(response.StatusCode);
                AppLog.Warn("Update", $"检查更新失败：{reason}（渠道 {url}）");
                return UpdateCheck.Failed(current, url, reason);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            var result = UpdateCheck.Evaluate(json, current, url);

            AppLog.Info("Update", $"检查更新结果：{result}");
            if (result.Status == UpdateStatus.Failed)
            {
                AppLog.Warn("Update", $"检查更新失败（清单不可用，不能视为已是最新版）：{result.Detail}");
            }

            return result;
        }
        catch (TaskCanceledException ex)
        {
            var reason = $"请求超时（超过 {Timeout.TotalSeconds:0} 秒）";
            AppLog.Warn("Update", $"检查更新失败：{reason}（渠道 {url}）：{ex.Message}");
            return UpdateCheck.Failed(current, url, reason);
        }
        catch (HttpRequestException ex)
        {
            var reason = $"网络请求失败：{ex.Message}" +
                         (ex.InnerException is { } inner ? $"（{inner.Message}）" : string.Empty);
            AppLog.Warn("Update", $"检查更新失败：{reason}（渠道 {url}）");
            return UpdateCheck.Failed(current, url, reason);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update", $"检查更新时出现意外错误（渠道 {url}）", ex);
            return UpdateCheck.Failed(current, url, $"意外错误：{ex.Message}");
        }
        finally
        {
            if (ownsClient)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// 把结果写成日志里的固定前缀行，便于真实桌面验证脚本 grep（不依赖界面文字）。
    /// </summary>
    public static void LogResult(UpdateCheckResult result, string trigger)
    {
        AppLog.Info(
            "Update",
            $"UPDATE_RESULT trigger={trigger} status={result.Status} current={result.CurrentVersion} " +
            $"manifest={(result.Manifest?.VersionText ?? "-")} available={(result.AvailableVersion ?? "-")} " +
            $"download={(result.DownloadUrl ?? "-")} channel={(result.ManifestUrl ?? "(未配置)")} " +
            $"detail={result.Detail}");
    }

    /// <summary>把一条 HTTP 状态码描述成用户能看懂的中文（供需要更细提示的调用方使用）。</summary>
    public static string DescribeStatus(HttpStatusCode status) => (int)status switch
    {
        404 => "尚无可用的正式发布版本，或更新渠道不存在（HTTP 404）",
        403 => "更新请求被拒绝，可能触发 GitHub API 访问限制，请稍后重试（HTTP 403）",
        429 => "更新请求过于频繁，请稍后重试（HTTP 429）",
        >= 500 => $"更新渠道服务器出错（HTTP {(int)status}）",
        _ => $"更新渠道返回了意外的状态码 HTTP {(int)status}",
    };
}
