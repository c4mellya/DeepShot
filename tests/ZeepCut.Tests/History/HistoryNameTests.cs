using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 历史名称与相对路径的纯逻辑测试（T23）：默认名称、非法字符清洗、
/// 同名区分、搜索匹配、路径布局与安全校验。
/// </summary>
public class HistoryNameTests
{
    private static readonly DateTime Sample = new(2026, 10, 18, 8, 15, 0, DateTimeKind.Local);

    [Fact]
    public void CreateDefault_UsesCaptureFileNameConvention()
    {
        Assert.Equal("截图_2026-10-18_081500", HistoryName.CreateDefault(Sample));
    }

    [Fact]
    public void CreateDefaultFor_ClipboardUsesPinPrefix()
    {
        Assert.Equal("贴图_2026-10-18_081500", HistoryName.CreateDefaultFor(HistorySource.Clipboard, Sample));
        Assert.Equal("截图_2026-10-18_081500", HistoryName.CreateDefaultFor(HistorySource.Screenshot, Sample));
        Assert.Equal("截图_2026-10-18_081500", HistoryName.CreateDefaultFor(HistorySource.History, Sample));
    }

    [Fact]
    public void DefaultPrefixFor_IsStable()
    {
        Assert.Equal("贴图", HistoryName.DefaultPrefixFor(HistorySource.Clipboard));
        Assert.Equal("截图", HistoryName.DefaultPrefixFor(HistorySource.Screenshot));
    }

    [Theory]
    [InlineData("我的截图", "我的截图")]
    [InlineData("  空格  ", "空格")]
    [InlineData("a/b", "a_b")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("结尾点...", "结尾点")]
    [InlineData("a\u0001b", "a_b")]
    public void Sanitize_ReplacesIllegalCharacters(string input, string expected)
    {
        Assert.Equal(expected, HistoryName.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sanitize_BlankFallsBackToDefault(string? input)
    {
        Assert.Equal(HistoryName.DefaultPrefix, HistoryName.Sanitize(input));
    }

    [Theory]
    [InlineData("会议截图.png", "会议截图")]
    [InlineData("会议截图.PNG", "会议截图")]
    [InlineData("会议截图.jpg", "会议截图")]
    public void Sanitize_StripsKnownImageExtension(string input, string expected)
    {
        Assert.Equal(expected, HistoryName.Sanitize(input));
    }

    [Fact]
    public void Sanitize_KeepsDotsThatAreNotExtensions()
    {
        Assert.Equal("版本.1.2", HistoryName.Sanitize("版本.1.2"));
    }

    [Fact]
    public void Sanitize_TruncatesLongNames()
    {
        var longName = new string('长', HistoryName.MaxLength + 50);

        var sanitized = HistoryName.Sanitize(longName);

        Assert.Equal(HistoryName.MaxLength, sanitized.Length);
    }

    [Fact]
    public void CreateUniqueName_ReturnsSameNameWhenFree()
    {
        Assert.Equal("海报", HistoryName.CreateUniqueName("海报", new[] { "别的" }));
    }

    [Fact]
    public void CreateUniqueName_AppendsCounterWhenTaken()
    {
        Assert.Equal("海报 (2)", HistoryName.CreateUniqueName("海报", new[] { "海报" }));
        Assert.Equal("海报 (3)", HistoryName.CreateUniqueName("海报", new[] { "海报", "海报 (2)" }));
    }

    [Fact]
    public void CreateUniqueName_IsCaseInsensitiveLikeWindows()
    {
        Assert.Equal("poster (2)", HistoryName.CreateUniqueName("poster", new[] { "Poster" }));
    }

    [Fact]
    public void CreateUniqueName_SanitizesAndSkipsExisting()
    {
        var unique = HistoryName.CreateUniqueName("a/b", new[] { "a_b" });

        Assert.Equal("a_b (2)", unique);
    }

    [Fact]
    public void CreateUniqueName_KeepsLengthBounded()
    {
        var longName = new string('x', HistoryName.MaxLength);

        var unique = HistoryName.CreateUniqueName(longName, new[] { longName });

        Assert.True(unique.Length <= HistoryName.MaxLength, $"名称长度 {unique.Length} 超出上限");
        Assert.EndsWith(" (2)", unique);
    }

    [Fact]
    public void CreateUniqueName_EmptyExistingSetWorks()
    {
        Assert.Equal("海报", HistoryName.CreateUniqueName("海报", Array.Empty<string>()));
    }

    [Fact]
    public void CreateUniqueName_RejectsBadAttemptCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HistoryName.CreateUniqueName("a", Array.Empty<string>(), maxAttempts: 0));
    }

    [Theory]
    [InlineData("截图_2026-10-18_081500", "截图", true)]
    [InlineData("截图_2026-10-18_081500", "0815", true)]
    [InlineData("截图_2026-10-18_081500", "SCREEN", false)]
    [InlineData("Poster", "poster", true)]
    [InlineData("Poster", "  ", true)]
    [InlineData("Poster", null, true)]
    [InlineData("Poster", "xyz", false)]
    [InlineData("", "x", false)]
    public void MatchesSearch_DoesCaseInsensitiveSubstring(string? name, string? keyword, bool expected)
    {
        Assert.Equal(expected, HistoryName.MatchesSearch(name, keyword));
    }

    // --- 相对路径布局 ---

    [Fact]
    public void CreateImageRelativePath_UsesImagesFolderAndTimestamp()
    {
        var id = Guid.Parse("abcdef12-0000-0000-0000-000000000000");

        var path = HistoryPaths.CreateImageRelativePath(Sample, id);

        Assert.Equal("images/20261018-081500-000-abcdef12.png", path);
        Assert.True(HistoryPaths.IsSafeImageRelativePath(path));
    }

    [Fact]
    public void CreateThumbnailRelativePath_SwitchesFolderAndAddsSuffix()
    {
        var path = HistoryPaths.CreateThumbnailRelativePath("images/20261018-081500-000-abcdef12.png");

        Assert.Equal("thumbs/20261018-081500-000-abcdef12.thumb.png", path);
    }

    [Fact]
    public void CreateThumbnailRelativePath_AcceptsBackslashes()
    {
        Assert.Equal(
            "thumbs/a.thumb.png",
            HistoryPaths.CreateThumbnailRelativePath(@"images\a.png"));
    }

    [Fact]
    public void CreateThumbnailRelativePath_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() => HistoryPaths.CreateThumbnailRelativePath(" "));
    }

    [Fact]
    public void EnsureUniqueRelativePath_AppendsCounterForTakenNames()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "images/a.png" };

        var unique = HistoryPaths.EnsureUniqueRelativePath(
            "images/a.png",
            candidate => taken.Contains(candidate));

        Assert.Equal("images/a-2.png", unique);
    }

    [Fact]
    public void EnsureUniqueRelativePath_ReturnsInputWhenFree()
    {
        Assert.Equal(
            "images/a.png",
            HistoryPaths.EnsureUniqueRelativePath("images/a.png", _ => false));
    }

    [Fact]
    public void ToAbsolute_CombinesSegments()
    {
        var absolute = HistoryPaths.ToAbsolute("/data/root", "images/a.png");

        Assert.Equal(Path.Combine("/data/root", "images", "a.png"), absolute);
    }

    [Fact]
    public void ToAbsolute_RejectsEmptyParts()
    {
        Assert.Throws<ArgumentException>(() => HistoryPaths.ToAbsolute("", "images/a.png"));
        Assert.Throws<ArgumentException>(() => HistoryPaths.ToAbsolute("/root", " "));
    }

    [Theory]
    [InlineData("images/a.png", true)]
    [InlineData("images/sub/a.png", true)]
    [InlineData("thumbs/a.png", false)]
    [InlineData("a.png", false)]
    [InlineData("../images/a.png", false)]
    [InlineData("images/../../a.png", false)]
    [InlineData("C:/Windows/a.png", false)]
    [InlineData("/etc/a.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeImageRelativePath_RejectsEscapes(string? path, bool expected)
    {
        Assert.Equal(expected, HistoryPaths.IsSafeImageRelativePath(path));
    }

    [Fact]
    public void GetFileName_ReturnsLastSegment()
    {
        Assert.Equal("a.png", HistoryPaths.GetFileName(@"images\sub\a.png"));
        Assert.Equal("a.png", HistoryPaths.GetFileName("a.png"));
    }
}
