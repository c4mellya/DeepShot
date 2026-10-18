using Xunit;
using ZeepCut.Core;
using ZeepCut.Core.Updates;

namespace ZeepCut.Tests.Updates;

/// <summary>
/// T32：清单解析、渠道判定与"绝不虚报最新版"的规则。
/// </summary>
public sealed class UpdateCheckTests
{
    private const string GoodManifest =
        """{ "version": "0.2.0", "url": "https://example.com/zeepcut/releases", "notes": "修复贴图缩放" }""";

    // --- 清单解析 ---

    [Fact]
    public void TryParse_GoodManifest_ReadsAllFields()
    {
        Assert.True(UpdateManifest.TryParse(GoodManifest, out var manifest, out var error), error);

        Assert.NotNull(manifest);
        Assert.Equal("0.2.0", manifest!.VersionText);
        Assert.Equal("0.2.0", manifest.Version!.Value.ToNormalizedString());
        Assert.Equal("https://example.com/zeepcut/releases", manifest.Url);
        Assert.Equal("修复贴图缩放", manifest.Notes);
    }

    [Fact]
    public void TryParse_UrlAndNotesAreOptional()
    {
        Assert.True(UpdateManifest.TryParse("""{ "version": "1.0.0" }""", out var manifest, out _));

        Assert.Null(manifest!.Url);
        Assert.Null(manifest.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{ "url": "https://example.com" }""")]
    [InlineData("""{ "version": "abc" }""")]
    [InlineData("""{ "version": "1.0", "url": "ftp://example.com/x" }""")]
    [InlineData("""{ "version": "1.0", "url": "not a url" }""")]
    public void TryParse_BadManifest_FailsWithReason(string json)
    {
        Assert.False(UpdateManifest.TryParse(json, out var manifest, out var error));

        Assert.Null(manifest);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_AcceptsHttpUrl()
    {
        Assert.True(UpdateManifest.TryParse(
            """{ "version": "1.0", "url": "http://192.168.1.10:8080/update.json" }""",
            out var manifest,
            out var error), error);
        Assert.Equal("http://192.168.1.10:8080/update.json", manifest!.Url);
    }

    // --- 渠道判定 ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/manifest.json")]
    public void IsChannelConfigured_FalseForMissingOrInvalid(string? url)
    {
        Assert.False(UpdateCheck.IsChannelConfigured(url));
    }

    [Theory]
    [InlineData("https://example.com/manifest.json")]
    [InlineData("http://localhost:9/manifest.json")]
    public void IsChannelConfigured_TrueForHttpAndHttps(string url)
    {
        Assert.True(UpdateCheck.IsChannelConfigured(url));
    }

    [Fact]
    public void AppInfo_DefaultChannelIsEmpty()
    {
        // 发布前待补：真实更新渠道。为空时 UI 必须显示"尚未配置更新渠道"。
        Assert.Equal(AppInfo.UpdateManifestUrl, string.Empty);
        Assert.False(UpdateCheck.IsChannelConfigured(AppInfo.UpdateManifestUrl));
    }

    [Fact]
    public void NotConfigured_NeverClaimsUpToDate()
    {
        var result = UpdateCheck.NotConfigured("0.1.0");

        Assert.Equal(UpdateStatus.NotConfigured, result.Status);
        Assert.False(result.HasUpdate);
        Assert.Equal("尚未配置更新渠道", result.DescribeTitle());
        // 详细说明里要明确告诉用户"无法查询"（用词不必与标题一字不差，但不能出现"已是最新版"）。
        Assert.Contains("还没有配置更新渠道", result.DescribeDetail());
        Assert.DoesNotContain("已是最新版", result.DescribeDetail());
        Assert.DoesNotContain("已是最新版", result.DescribeTitle());
    }

    [Fact]
    public void Failed_NeverClaimsUpToDate()
    {
        var result = UpdateCheck.Failed("0.1.0", "https://example.com/manifest.json", "连接超时");

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Equal("检查更新失败", result.DescribeTitle());
        Assert.Contains("连接超时", result.DescribeDetail());
        Assert.DoesNotContain("已是最新版", result.DescribeDetail());
    }

    // --- 版本比较结论 ---

    [Fact]
    public void Evaluate_NewerVersion_ReportsUpdate()
    {
        var result = UpdateCheck.Evaluate(GoodManifest, "0.1.0", "https://example.com/manifest.json");

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.True(result.HasUpdate);
        Assert.Equal("0.2.0", result.AvailableVersion);
        Assert.Equal("https://example.com/zeepcut/releases", result.DownloadUrl);
        Assert.Contains("发现新版本 0.2.0", result.DescribeTitle());
        Assert.Contains("修复贴图缩放", result.DescribeDetail());
    }

    [Fact]
    public void Evaluate_SameVersion_SaysUpToDate()
    {
        var result = UpdateCheck.Evaluate(GoodManifest, "0.2.0", "https://example.com/manifest.json");

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.False(result.HasUpdate);
        Assert.Equal("已是最新版", result.DescribeTitle());
    }

    [Fact]
    public void Evaluate_OlderManifestVersion_SaysUpToDate()
    {
        var result = UpdateCheck.Evaluate(GoodManifest, "1.0.0", "https://example.com/manifest.json");

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_AssemblyStyleVersionWithRevisionZero_IsNotNewer()
    {
        // 程序集版本通常是 0.1.0.0，清单写 0.1.0：不能因为第 4 段差异误报有更新。
        var result = UpdateCheck.Evaluate("""{ "version": "0.1.0" }""", "0.1.0.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_ManifestNewerRevision_ReportsUpdate()
    {
        var result = UpdateCheck.Evaluate("""{ "version": "0.1.0.1" }""", "0.1.0.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void Evaluate_BrokenManifest_IsFailureNotUpToDate()
    {
        var result = UpdateCheck.Evaluate("{ \"version\": ", "0.1.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.DoesNotContain("已是最新版", result.DescribeDetail());
        Assert.Contains("无法确认是否有新版本", result.DescribeDetail());
    }

    [Fact]
    public void Evaluate_ManifestWithoutVersion_IsFailure()
    {
        var result = UpdateCheck.Evaluate("""{ "url": "https://example.com" }""", "0.1.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("version", result.Detail);
    }

    [Fact]
    public void Evaluate_UnparseableCurrentVersion_IsFailure()
    {
        var result = UpdateCheck.Evaluate(GoodManifest, "development", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("当前程序版本", result.Detail);
    }

    [Fact]
    public void Evaluate_PreReleaseManifest_IsOlderThanRelease()
    {
        var result = UpdateCheck.Evaluate("""{ "version": "1.0.0-beta" }""", "1.0.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void Evaluate_PreReleasePatch_IsNewerThanRelease()
    {
        var result = UpdateCheck.Evaluate("""{ "version": "1.0.1-beta" }""", "1.0.0", "https://example.com/m.json");

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void DescribeTitle_CoversEveryStatus()
    {
        foreach (var status in Enum.GetValues<UpdateStatus>())
        {
            var result = new UpdateCheckResult(status, "0.1.0", null, null, "detail");
            Assert.False(string.IsNullOrWhiteSpace(result.DescribeTitle()));
            Assert.False(string.IsNullOrWhiteSpace(result.DescribeDetail()));
        }
    }

    [Fact]
    public void ToString_ContainsStatusAndChannel()
    {
        var text = UpdateCheck.Evaluate(GoodManifest, "0.1.0", "https://example.com/m.json").ToString();

        Assert.Contains("UpdateAvailable", text);
        Assert.Contains("0.2.0", text);
    }
}
