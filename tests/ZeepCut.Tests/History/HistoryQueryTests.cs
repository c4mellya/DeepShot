using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 查询参数与分页结果的纯逻辑测试（T23）。数据库层面的行为见
/// <see cref="HistoryRepositoryTests"/>。
/// </summary>
public class HistoryQueryTests
{
    [Fact]
    public void Default_IsNewestFirstWithDefaultPageSize()
    {
        var query = HistoryQuery.Default;

        Assert.Null(query.SearchText);
        Assert.Null(query.NormalizedSearchText);
        Assert.Equal(HistoryFavoriteFilter.All, query.FavoriteFilter);
        Assert.Equal(HistorySort.CreatedAtDescending, query.Sort);
        Assert.Equal(0, query.NormalizedOffset);
        Assert.Equal(HistoryQuery.DefaultLimit, query.NormalizedLimit);
    }

    [Theory]
    [InlineData("  会议  ", "会议")]
    [InlineData("会议", "会议")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizedSearchText_TrimsAndDropsBlank(string? input, string? expected)
    {
        Assert.Equal(expected, new HistoryQuery { SearchText = input }.NormalizedSearchText);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(-1)]
    public void NormalizedOffset_NeverNegative(int offset)
    {
        Assert.Equal(0, new HistoryQuery { Offset = offset }.NormalizedOffset);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    [InlineData(5000, HistoryQuery.MaxLimit)]
    public void NormalizedLimit_IsClamped(int limit, int expected)
    {
        Assert.Equal(expected, new HistoryQuery { Limit = limit }.NormalizedLimit);
    }

    [Theory]
    [InlineData("会议", "%会议%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("c\\d", "%c\\\\d%")]
    [InlineData("", "%%")]
    public void BuildLikePattern_EscapesWildcards(string keyword, string expected)
    {
        Assert.Equal(expected, HistoryQuery.BuildLikePattern(keyword));
    }

    [Fact]
    public void BuildLikePattern_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryQuery.BuildLikePattern(null!));
    }

    [Fact]
    public void HistoryPage_ReportsHasMoreAndNextPage()
    {
        var query = new HistoryQuery { Limit = 10, Offset = 0 };
        var page = new HistoryPage(new[] { MakeRecord("a") }, TotalCount: 25, query);

        Assert.True(page.HasMore);
        var next = page.NextPage();
        Assert.NotNull(next);
        Assert.Equal(1, next!.NormalizedOffset);
        Assert.Equal(10, next.NormalizedLimit);
    }

    [Fact]
    public void HistoryPage_LastPageHasNoNext()
    {
        var query = new HistoryQuery { Limit = 10, Offset = 20 };
        var page = new HistoryPage(new[] { MakeRecord("a") }, TotalCount: 21, query);

        Assert.False(page.HasMore);
        Assert.Null(page.NextPage());
    }

    [Fact]
    public void HistoryPage_EmptyIsEmpty()
    {
        var page = HistoryPage.Empty();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.False(page.HasMore);
        Assert.Null(page.NextPage());
    }

    [Fact]
    public void HistoryPage_EmptyKeepsGivenQuery()
    {
        var query = new HistoryQuery { SearchText = "x", Limit = 5 };

        Assert.Equal(query, HistoryPage.Empty(query).Query);
    }

    [Fact]
    public void HistoryQuery_ToStringMentionsAllFilters()
    {
        var text = new HistoryQuery
        {
            SearchText = "会议",
            FavoriteFilter = HistoryFavoriteFilter.FavoritesOnly,
            Sort = HistorySort.NameAscending,
            Offset = 5,
            Limit = 20,
        }.ToString();

        Assert.Contains("会议", text);
        Assert.Contains("FavoritesOnly", text);
        Assert.Contains("NameAscending", text);
        Assert.Contains("5+20", text);
    }

    private static HistoryRecord MakeRecord(string name) => new(
        Guid.NewGuid(), name, DateTime.UtcNow, DateTime.UtcNow, false,
        "images/x.png", null, 1, 1, HistorySource.Screenshot);
}
