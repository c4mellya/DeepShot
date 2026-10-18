using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// T27 30 天保留策略测试（纯逻辑）：按创建时间计算、编辑/重命名不延期、收藏豁免、
/// 边界（正好 30 天、29 天 23 小时、30 天 1 秒）、取消收藏前的提示文案。
/// </summary>
public class HistoryRetentionPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static HistoryRecord Record(
        TimeSpan age,
        bool favorite = false,
        string name = "截图_测试",
        TimeSpan? updatedAgo = null,
        Guid? id = null)
    {
        var createdUtc = NowUtc - age;
        var updatedUtc = updatedAgo.HasValue ? NowUtc - updatedAgo.Value : createdUtc;

        return new HistoryRecord(
            id ?? Guid.NewGuid(),
            name,
            createdUtc,
            updatedUtc,
            favorite,
            HistoryPaths.CreateImageRelativePath(createdUtc.ToLocalTime(), id ?? Guid.NewGuid()),
            null,
            640,
            480,
            HistorySource.Screenshot);
    }

    // --- 边界 ---

    [Fact]
    public void IsExpired_ExactlyThirtyDays_IsExpired()
    {
        Assert.True(HistoryRetentionPolicy.IsExpired(Record(TimeSpan.FromDays(30)), NowUtc));
    }

    [Fact]
    public void IsExpired_TwentyNineDaysTwentyThreeHours_IsNotExpired()
    {
        var record = Record(TimeSpan.FromDays(29) + TimeSpan.FromHours(23));

        Assert.False(HistoryRetentionPolicy.IsExpired(record, NowUtc));
        Assert.False(HistoryRetentionPolicy.ShouldPurge(record, NowUtc));
    }

    [Fact]
    public void IsExpired_ThirtyDaysPlusOneSecond_IsExpired()
    {
        var record = Record(TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1));

        Assert.True(HistoryRetentionPolicy.IsExpired(record, NowUtc));
        Assert.True(HistoryRetentionPolicy.ShouldPurge(record, NowUtc));
    }

    [Fact]
    public void IsExpired_OneMillisecondBeforeThirtyDays_IsNotExpired()
    {
        var record = Record(TimeSpan.FromDays(30) - TimeSpan.FromMilliseconds(1));

        Assert.False(HistoryRetentionPolicy.IsExpired(record, NowUtc));
    }

    [Fact]
    public void IsExpired_SixtyDays_IsExpired()
    {
        Assert.True(HistoryRetentionPolicy.IsExpired(Record(TimeSpan.FromDays(60)), NowUtc));
    }

    [Fact]
    public void IsExpired_FutureTimestamp_IsNotExpired()
    {
        // 系统时间回拨/时钟不同步：创建时间在未来时不能当成过期删掉。
        Assert.False(HistoryRetentionPolicy.IsExpired(Record(TimeSpan.FromDays(-5)), NowUtc));
    }

    // --- 收藏豁免 ---

    [Fact]
    public void ShouldPurge_FavoriteIsExemptEvenWhenVeryOld()
    {
        var record = Record(TimeSpan.FromDays(40), favorite: true);

        Assert.True(HistoryRetentionPolicy.IsExpired(record, NowUtc));
        Assert.False(HistoryRetentionPolicy.ShouldPurge(record, NowUtc));
    }

    [Fact]
    public void ShouldPurge_FavoriteWithinRetentionIsKept()
    {
        Assert.False(HistoryRetentionPolicy.ShouldPurge(Record(TimeSpan.FromDays(3), favorite: true), NowUtc));
    }

    [Fact]
    public void ShouldPurge_FreshRecordIsKept()
    {
        Assert.False(HistoryRetentionPolicy.ShouldPurge(Record(TimeSpan.FromHours(1)), NowUtc));
    }

    // --- 编辑/重命名不延长期限 ---

    [Fact]
    public void ShouldPurge_RecentlyUpdatedOldRecord_StillExpires()
    {
        // 40 天前创建、昨天刚改过名/覆盖过图：期限仍按创建时间 → 应清理。
        var record = Record(TimeSpan.FromDays(40), updatedAgo: TimeSpan.FromHours(12));

        Assert.True(HistoryRetentionPolicy.ShouldPurge(record, NowUtc));
    }

    [Fact]
    public void ShouldPurge_UsesCreatedAtNotUpdatedAt_ForYoungRecord()
    {
        // 10 天前创建、一分钟前刚改：远未到期。
        var record = Record(TimeSpan.FromDays(10), updatedAgo: TimeSpan.FromMinutes(1));

        Assert.False(HistoryRetentionPolicy.ShouldPurge(record, NowUtc));
    }

    // --- 批量选择 ---

    [Fact]
    public void SelectExpired_ReturnsOnlyExpiredNonFavoritesInInputOrder()
    {
        var expired = Record(TimeSpan.FromDays(31), name: "31 天");
        var young = Record(TimeSpan.FromDays(29), name: "29 天");
        var favorite = Record(TimeSpan.FromDays(40), favorite: true, name: "40 天收藏");
        var boundary = Record(TimeSpan.FromDays(30), name: "正好 30 天");

        var selected = HistoryRetentionPolicy.SelectExpired(
            new[] { expired, young, favorite, boundary },
            NowUtc);

        Assert.Equal(new[] { expired.Id, boundary.Id }, selected);
    }

    [Fact]
    public void SelectExpired_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(HistoryRetentionPolicy.SelectExpired(Array.Empty<HistoryRecord>(), NowUtc));
    }

    [Fact]
    public void SelectExpired_AllFavorites_ReturnsEmpty()
    {
        var records = new[] { Record(TimeSpan.FromDays(100), favorite: true) };

        Assert.Empty(HistoryRetentionPolicy.SelectExpired(records, NowUtc));
    }

    // --- 时间归一 ---

    [Fact]
    public void IsExpired_LocalNow_IsConvertedToUtc()
    {
        var createdUtc = NowUtc - TimeSpan.FromDays(31);
        var record = Record(TimeSpan.FromDays(31));

        Assert.True(HistoryRetentionPolicy.IsExpired(record, NowUtc.ToLocalTime()));
        Assert.Equal(createdUtc, record.CreatedAtUtc);
    }

    [Fact]
    public void IsExpired_UnspecifiedKind_IsTreatedAsUtc()
    {
        var record = Record(TimeSpan.FromDays(31));
        var unspecified = DateTime.SpecifyKind(NowUtc, DateTimeKind.Unspecified);

        Assert.True(HistoryRetentionPolicy.IsExpired(record, unspecified));
    }

    // --- 自定义天数与非法参数 ---

    [Fact]
    public void IsExpired_CustomRetentionDays_IsRespected()
    {
        var record = Record(TimeSpan.FromDays(8));

        Assert.True(HistoryRetentionPolicy.IsExpired(record, NowUtc, retentionDays: 7));
        Assert.False(HistoryRetentionPolicy.IsExpired(record, NowUtc, retentionDays: 10));
    }

    [Fact]
    public void IsExpired_ZeroRetentionDays_EverythingPastIsExpired()
    {
        Assert.True(HistoryRetentionPolicy.IsExpired(Record(TimeSpan.FromMilliseconds(1)), NowUtc, 0));
    }

    [Fact]
    public void IsExpired_NegativeRetention_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HistoryRetentionPolicy.IsExpired(Record(TimeSpan.FromDays(1)), NowUtc, -1));
    }

    [Fact]
    public void IsExpired_NullRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryRetentionPolicy.IsExpired(null!, NowUtc));
    }

    [Fact]
    public void SelectExpired_NullCollection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryRetentionPolicy.SelectExpired(null!, NowUtc));
    }

    // --- 到期时刻 / 剩余天数 / 规则文案 ---

    [Fact]
    public void GetExpiryUtc_IsExactlyThirtyDaysAfterCreation()
    {
        var record = Record(TimeSpan.FromDays(40));

        Assert.Equal(record.CreatedAtUtc.AddDays(30), HistoryRetentionPolicy.GetExpiryUtc(record));
    }

    [Fact]
    public void GetRemainingDays_CeilsAndClampsAtZero()
    {
        Assert.Equal(1, HistoryRetentionPolicy.GetRemainingDays(Record(TimeSpan.FromDays(29.5)), NowUtc, 30));
        Assert.Equal(30, HistoryRetentionPolicy.GetRemainingDays(Record(TimeSpan.Zero), NowUtc, 30));
        Assert.Equal(0, HistoryRetentionPolicy.GetRemainingDays(Record(TimeSpan.FromDays(31)), NowUtc, 30));
    }

    [Fact]
    public void DescribeRule_MentionsThirtyDaysFavoritesAndCreatedAt()
    {
        var text = HistoryRetentionPolicy.DescribeRule();

        Assert.Contains("30 天", text);
        Assert.Contains("创建时间", text);
        Assert.Contains("收藏", text);
    }

    // --- 取消收藏前的提示 ---

    [Fact]
    public void DescribeFavoriteRemovalWarning_ExpiredFavorite_ExplainsUpcomingPurge()
    {
        var record = Record(TimeSpan.FromDays(40), favorite: true, name: "老收藏");

        var message = HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(record, NowUtc);

        Assert.NotNull(message);
        Assert.Contains("老收藏", message!);
        Assert.Contains("40 天前", message!);
        Assert.Contains("取消收藏后", message!);
        Assert.Contains("删除", message!);
        Assert.Contains("取消", message!); // "选择“取消”可以保持收藏"
    }

    [Fact]
    public void DescribeFavoriteRemovalWarning_FreshFavorite_ReturnsNull()
    {
        var record = Record(TimeSpan.FromDays(5), favorite: true);

        Assert.Null(HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(record, NowUtc));
    }

    [Fact]
    public void DescribeFavoriteRemovalWarning_BoundaryExactlyThirtyDays_Warns()
    {
        var record = Record(TimeSpan.FromDays(30), favorite: true);

        Assert.NotNull(HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(record, NowUtc));
    }

    [Fact]
    public void DescribeFavoriteRemovalWarning_NullRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(null!, NowUtc));
    }
}
