using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 图库改名规则测试（T26）：空名称拒绝、名称清洗、重名允许、无变化判断。
/// </summary>
public class HistoryRenameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryNormalize_RejectsEmptyInput(string? input)
    {
        Assert.False(HistoryRename.TryNormalize(input, out var name, out var error));

        Assert.Equal(string.Empty, name);
        Assert.Equal(HistoryRename.EmptyNameMessage, error);
    }

    [Theory]
    [InlineData("???")]
    [InlineData("***")]
    [InlineData("::::")]
    [InlineData("....")]
    [InlineData("   ...   ")]
    public void TryNormalize_RejectsInputWithNoUsableCharacter(string input)
    {
        Assert.False(HistoryRename.TryNormalize(input, out _, out var error));
        Assert.Equal(HistoryRename.EmptyNameMessage, error);
    }

    [Theory]
    [InlineData("会议截图", "会议截图")]
    [InlineData("  会议截图  ", "会议截图")]
    [InlineData("会议.png", "会议")]
    [InlineData("会议.jpg", "会议")]
    [InlineData("2026.10.18 版本", "2026.10.18 版本")]
    [InlineData("海报?v2", "海报_v2")]
    [InlineData("a/b\\c", "a_b_c")]
    public void TryNormalize_SanitizesLikeHistoryName(string input, string expected)
    {
        Assert.True(HistoryRename.TryNormalize(input, out var name, out var error));

        Assert.Equal(expected, name);
        Assert.Null(error);
    }

    [Fact]
    public void TryNormalize_AllowsDuplicateNames()
    {
        // PLAN 只要求按名称搜索，不要求唯一：同名两条都能存在（Id 才是主键）。
        Assert.True(HistoryRename.TryNormalize("会议截图", out var first, out _));
        Assert.True(HistoryRename.TryNormalize("会议截图", out var second, out _));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryNormalize_TruncatesOverlongNames()
    {
        Assert.True(HistoryRename.TryNormalize(new string('长', 200), out var name, out _));

        Assert.Equal(HistoryName.MaxLength, name.Length);
    }

    [Fact]
    public void TryNormalize_AcceptsDefaultPrefixItself()
    {
        // 用户可能真的想把名称改成"截图"——这是有效输入，不是"空名称"。
        Assert.True(HistoryRename.TryNormalize(HistoryName.DefaultPrefix, out var name, out _));
        Assert.Equal(HistoryName.DefaultPrefix, name);
    }

    [Theory]
    [InlineData("会议", "会议", true)]
    [InlineData("会议", "  会议  ", true)]
    [InlineData("会议", "会议.png", true)]
    [InlineData("会议", "会议2", false)]
    [InlineData("会议", "", false)]
    [InlineData("会议", "???", false)]
    public void IsUnchanged_ComparesSanitizedNames(string current, string? input, bool expected)
    {
        Assert.Equal(expected, HistoryRename.IsUnchanged(current, input));
    }

    [Theory]
    [InlineData("新名字", null, "新名字")]
    [InlineData("", null, HistoryName.DefaultPrefix)]
    [InlineData("???", null, HistoryName.DefaultPrefix)]
    [InlineData("???", "旧名字", "旧名字")]
    [InlineData(null, "旧名字", "旧名字")]
    public void NormalizeOrFallback_FallsBackWhenUnusable(string? input, string? fallback, string expected)
    {
        Assert.Equal(expected, HistoryRename.NormalizeOrFallback(input, fallback));
    }

    [Fact]
    public void NormalizeOrFallback_SanitizesFallback()
    {
        Assert.Equal("旧_名字", HistoryRename.NormalizeOrFallback(null, "旧?名字"));
    }
}
