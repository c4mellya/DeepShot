using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 图库视图状态测试（T25/T26）：搜索大小写/部分匹配/空搜索、只看收藏、两者组合、
/// 查询翻译、空状态文案。
/// </summary>
public class HistoryViewTests
{
    // --- 默认值 ---

    [Fact]
    public void Default_HasNoFilterAndNewestFirst()
    {
        var view = HistoryView.Default;

        Assert.Null(view.NormalizedSearchText);
        Assert.False(view.FavoritesOnly);
        Assert.False(view.HasFilter);
        Assert.True(view.IsDefault);
        Assert.Equal(HistorySort.CreatedAtDescending, view.Sort);
        Assert.Equal(HistoryView.DefaultPageSize, view.PageSize);
        Assert.Equal(HistoryFavoriteFilter.All, view.FavoriteFilter);
    }

    [Theory]
    [InlineData("  会议  ", "会议")]
    [InlineData("会议", "会议")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizedSearchText_TrimsAndDropsBlank(string? input, string? expected)
    {
        Assert.Equal(expected, new HistoryView { SearchText = input }.NormalizedSearchText);
    }

    [Theory]
    [InlineData("   ", false)]
    [InlineData("会议", true)]
    [InlineData(null, false)]
    public void HasFilter_DependsOnSearchAndFavorites(string? search, bool expected)
    {
        Assert.Equal(expected, new HistoryView { SearchText = search }.HasFilter);
        Assert.True(new HistoryView { FavoritesOnly = true }.HasFilter);
    }

    // --- 组合 / 修改 ---

    [Fact]
    public void ToggleFavoritesOnly_FlipsAndKeepsSearch()
    {
        var view = new HistoryView { SearchText = "会议" };
        var toggled = view.ToggleFavoritesOnly();

        Assert.True(toggled.FavoritesOnly);
        Assert.Equal("会议", toggled.SearchText);
        Assert.Equal(HistoryFavoriteFilter.FavoritesOnly, toggled.FavoriteFilter);

        var back = toggled.ToggleFavoritesOnly();
        Assert.False(back.FavoritesOnly);
        Assert.Equal("会议", back.SearchText);
        Assert.Equal(HistoryFavoriteFilter.All, back.FavoriteFilter);
    }

    [Fact]
    public void WithSearch_IsImmutableAndKeepsFavoritesFlag()
    {
        var original = new HistoryView { FavoritesOnly = true };
        var changed = original.WithSearch("海报");

        Assert.Null(original.SearchText);
        Assert.Equal("海报", changed.SearchText);
        Assert.True(changed.FavoritesOnly);
    }

    [Fact]
    public void ClearFilters_ResetsSearchAndFavoritesButKeepsSort()
    {
        var view = new HistoryView
        {
            SearchText = "会议",
            FavoritesOnly = true,
            Sort = HistorySort.NameAscending,
        };

        var cleared = view.ClearFilters();

        Assert.Null(cleared.SearchText);
        Assert.False(cleared.FavoritesOnly);
        Assert.False(cleared.HasFilter);
        Assert.Equal(HistorySort.NameAscending, cleared.Sort);
    }

    [Fact]
    public void IsDefault_IsFalseWhenOnlyFavoritesIsOn()
    {
        Assert.False(new HistoryView { FavoritesOnly = true }.IsDefault);
        Assert.False(new HistoryView { SearchText = "x" }.IsDefault);
        Assert.False(new HistoryView { Sort = HistorySort.NameAscending }.IsDefault);
    }

    // --- 查询翻译 ---

    [Fact]
    public void ToQuery_CarriesSearchFavoritesSortAndPaging()
    {
        var query = new HistoryView
        {
            SearchText = " 会议 ",
            FavoritesOnly = true,
            Sort = HistorySort.NameAscending,
            PageSize = 20,
        }.ToQuery(offset: 40);

        Assert.Equal("会议", query.NormalizedSearchText);
        Assert.Equal(HistoryFavoriteFilter.FavoritesOnly, query.FavoriteFilter);
        Assert.Equal(HistorySort.NameAscending, query.Sort);
        Assert.Equal(40, query.NormalizedOffset);
        Assert.Equal(20, query.NormalizedLimit);
    }

    [Fact]
    public void ToQuery_DefaultHasNoSearchAndAllFavorites()
    {
        var query = HistoryView.Default.ToQuery();

        Assert.Null(query.NormalizedSearchText);
        Assert.Equal(HistoryFavoriteFilter.All, query.FavoriteFilter);
        Assert.Equal(0, query.NormalizedOffset);
    }

    // --- 内存匹配（与 SQL 语义一致） ---

    [Theory]
    [InlineData("会议截图", "会议", true)]
    [InlineData("会议截图", "会议截图", true)]
    [InlineData("MeetingNotes", "meeting", true)]
    [InlineData("MeetingNotes", "MEETING", true)]
    [InlineData("MeetingNotes", "notes", true)]
    [InlineData("会议截图", "海报", false)]
    [InlineData("会议截图", "", true)]
    [InlineData("会议截图", "   ", true)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    public void Matches_BySubstringIgnoringCase(string name, string? search, bool expected)
    {
        Assert.Equal(expected, new HistoryView { SearchText = search }.Matches(MakeRecord(name)));
    }

    [Fact]
    public void Matches_FavoritesOnlyExcludesPlainRecords()
    {
        var view = new HistoryView { FavoritesOnly = true };

        Assert.True(view.Matches(MakeRecord("收藏的", favorite: true)));
        Assert.False(view.Matches(MakeRecord("普通的")));
    }

    [Fact]
    public void Matches_SearchAndFavoritesCombine()
    {
        var view = new HistoryView { SearchText = "会议", FavoritesOnly = true };

        Assert.True(view.Matches(MakeRecord("会议A", favorite: true)));
        Assert.False(view.Matches(MakeRecord("会议B")));            // 命中搜索但没收藏
        Assert.False(view.Matches(MakeRecord("海报C", favorite: true))); // 收藏但不命中搜索
        Assert.False(view.Matches(MakeRecord("海报D")));
    }

    [Fact]
    public void Matches_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryView.Default.Matches(null!));
    }

    // --- 空状态文案 ---

    [Fact]
    public void DescribeEmptyState_NoHistoryAtAll()
    {
        var text = HistoryView.Default.DescribeEmptyState(0);

        Assert.Contains("还没有历史记录", text);
        Assert.Contains("Alt+A", text);
    }

    [Fact]
    public void DescribeEmptyState_SearchOnly()
    {
        var text = new HistoryView { SearchText = "海报" }.DescribeEmptyState(5);

        Assert.Contains("海报", text);
        Assert.Contains("清空搜索", text);
    }

    [Fact]
    public void DescribeEmptyState_FavoritesOnly()
    {
        var text = new HistoryView { FavoritesOnly = true }.DescribeEmptyState(5);

        Assert.Contains("收藏", text);
    }

    [Fact]
    public void DescribeEmptyState_SearchAndFavorites()
    {
        var text = new HistoryView { SearchText = "会议", FavoritesOnly = true }.DescribeEmptyState(5);

        Assert.Contains("会议", text);
        Assert.Contains("只看收藏", text);
    }

    [Fact]
    public void ToString_MentionsAllFilters()
    {
        var text = new HistoryView { SearchText = "会议", FavoritesOnly = true, PageSize = 20 }.ToString();

        Assert.Contains("会议", text);
        Assert.Contains("只看收藏=True", text);
        Assert.Contains("每页=20", text);
    }

    [Fact]
    public void PageSize_OutOfRangeIsClampedOnQuery()
    {
        Assert.Equal(1, new HistoryView { PageSize = 0 }.ToQuery().NormalizedLimit);
        Assert.Equal(HistoryQuery.MaxLimit, new HistoryView { PageSize = 99_999 }.ToQuery().NormalizedLimit);
    }

    private static HistoryRecord MakeRecord(string name, bool favorite = false) => new(
        Guid.NewGuid(),
        name,
        DateTime.UtcNow,
        DateTime.UtcNow,
        favorite,
        "images/x.png",
        null,
        10,
        10,
        HistorySource.Screenshot);
}
