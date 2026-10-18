using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 日期分组规则测试（T25）：本地时间跨天、今天/昨天边界、组内顺序、未来时间。
/// </summary>
public class HistoryGroupingTests
{
    private static readonly DateTime Today = new(2026, 10, 18, 15, 30, 0, DateTimeKind.Local);

    // --- 今天 / 昨天 / 更早 ---

    [Fact]
    public void BucketOf_TodayIsToday()
    {
        Assert.Equal(
            HistoryDateBucket.Today,
            HistoryGrouping.BucketOf(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public void BucketOf_YesterdayIsYesterday()
    {
        Assert.Equal(
            HistoryDateBucket.Yesterday,
            HistoryGrouping.BucketOf(new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 18)));
    }

    [Theory]
    [InlineData(2026, 10, 16)]
    [InlineData(2026, 10, 1)]
    [InlineData(2025, 10, 18)]
    public void BucketOf_OlderIsEarlier(int year, int month, int day)
    {
        Assert.Equal(
            HistoryDateBucket.Earlier,
            HistoryGrouping.BucketOf(new DateOnly(year, month, day), new DateOnly(2026, 10, 18)));
    }

    [Theory]
    [InlineData(2026, 10, 19)]
    [InlineData(2026, 11, 1)]
    public void BucketOf_FutureDateCountsAsToday(int year, int month, int day)
    {
        // 系统时钟被改过 / 库里写了未来时间时，图库不该出现"明天"这种分组。
        Assert.Equal(
            HistoryDateBucket.Today,
            HistoryGrouping.BucketOf(new DateOnly(year, month, day), new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public void BucketOf_CrossesMonthAndYearBoundary()
    {
        var today = new DateOnly(2027, 1, 1);

        Assert.Equal(HistoryDateBucket.Yesterday, HistoryGrouping.BucketOf(new DateOnly(2026, 12, 31), today));
        Assert.Equal(HistoryDateBucket.Earlier, HistoryGrouping.BucketOf(new DateOnly(2026, 12, 30), today));
    }

    [Theory]
    [InlineData(HistoryDateBucket.Today, "今天")]
    [InlineData(HistoryDateBucket.Yesterday, "昨天")]
    [InlineData(HistoryDateBucket.Earlier, "更早")]
    public void DescribeBucket_IsChinese(HistoryDateBucket bucket, string expected)
    {
        Assert.Equal(expected, HistoryGrouping.DescribeBucket(bucket));
    }

    // --- 标题 ---

    [Fact]
    public void CreateTitle_TodayAndYesterdayUseShortTitles()
    {
        Assert.Equal("今天", HistoryGrouping.CreateTitle(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 18)));
        Assert.Equal("昨天", HistoryGrouping.CreateTitle(new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 18)));
    }

    [Fact]
    public void CreateTitle_OlderUsesDateAndWeekday()
    {
        // 2026-10-16 是星期五。
        Assert.Equal(
            "2026-10-16 星期五",
            HistoryGrouping.CreateTitle(new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 18)));
    }

    [Theory]
    [InlineData(2026, 10, 12, "星期一")]
    [InlineData(2026, 10, 13, "星期二")]
    [InlineData(2026, 10, 14, "星期三")]
    [InlineData(2026, 10, 15, "星期四")]
    [InlineData(2026, 10, 16, "星期五")]
    [InlineData(2026, 10, 17, "星期六")]
    [InlineData(2026, 10, 11, "星期日")]
    public void WeekdayName_IsChinese(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, HistoryGrouping.WeekdayName(new DateOnly(year, month, day)));
    }

    // --- 分组 ---

    [Fact]
    public void GroupByDay_EmptyInputReturnsEmptyList()
    {
        Assert.Empty(HistoryGrouping.GroupByDay(Array.Empty<HistoryRecord>(), Today));
    }

    [Fact]
    public void GroupByDay_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryGrouping.GroupByDay(null!, Today));
    }

    [Fact]
    public void GroupByDay_SplitsAcrossThreeGroupsAndSortsDatesDescending()
    {
        var records = new[]
        {
            MakeRecord("前天", new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Local)),
            MakeRecord("今天早", new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Local)),
            MakeRecord("昨天", new DateTime(2026, 10, 17, 23, 0, 0, DateTimeKind.Local)),
            MakeRecord("今天晚", new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Local)),
        };

        var groups = HistoryGrouping.GroupByDay(records, Today);

        Assert.Equal(3, groups.Count);
        Assert.Equal("今天", groups[0].Title);
        Assert.Equal(2, groups[0].Count);
        Assert.Equal("昨天", groups[1].Title);
        Assert.Single(groups[1].Items);
        Assert.Equal("2026-10-16 星期五", groups[2].Title);
    }

    [Fact]
    public void GroupByDay_OrdersItemsNewestFirstInsideGroup()
    {
        var records = new[]
        {
            MakeRecord("九点", new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Local)),
            MakeRecord("二十点", new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Local)),
            MakeRecord("十三点", new DateTime(2026, 10, 18, 13, 0, 0, DateTimeKind.Local)),
        };

        var group = Assert.Single(HistoryGrouping.GroupByDay(records, Today));

        Assert.Equal(new[] { "二十点", "十三点", "九点" }, group.Items.Select(item => item.Name));
    }

    [Fact]
    public void GroupByDay_KeepsInputOrderForSameTimestamp()
    {
        var stamp = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Local);
        var records = new[] { MakeRecord("A", stamp), MakeRecord("B", stamp), MakeRecord("C", stamp) };

        var group = Assert.Single(HistoryGrouping.GroupByDay(records, Today));

        Assert.Equal(new[] { "A", "B", "C" }, group.Items.Select(item => item.Name));
    }

    [Fact]
    public void GroupByDay_LocalMidnightSplitsIntoTwoGroups()
    {
        // 本地 23:59:59 与次日 00:00:00 必须是两组（跨天边界）。
        var records = new[]
        {
            MakeRecord("深夜", new DateTime(2026, 10, 17, 23, 59, 59, DateTimeKind.Local)),
            MakeRecord("凌晨", new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Local)),
        };

        var groups = HistoryGrouping.GroupByDay(records, Today);

        Assert.Equal(2, groups.Count);
        Assert.Equal("今天", groups[0].Title);
        Assert.Equal("昨天", groups[1].Title);
    }

    [Fact]
    public void GroupByDay_UsesLocalTimeNotUtc()
    {
        // UTC 10-17T23:30 在本地（UTC-4）是 10-17 19:30 → 昨天；
        // UTC 10-18T02:00 在本地是 10-17 22:00 → 也是昨天。
        // 只有把 UTC 直接取日期才会误判成"今天/明天"，这里锁住本地时间语义。
        var records = new[]
        {
            MakeRecordUtc("utc-0230", new DateTime(2026, 10, 18, 2, 30, 0, DateTimeKind.Utc)),
            MakeRecordUtc("utc-1730", new DateTime(2026, 10, 17, 17, 30, 0, DateTimeKind.Utc)),
        };

        var groups = HistoryGrouping.GroupByDay(records, Today);
        var expectedDate = HistoryGrouping.LocalDate(records[0].CreatedAtLocal);
        var utcDate = DateOnly.FromDateTime(records[0].CreatedAtUtc);

        Assert.Equal(expectedDate, groups[0].Date);
        // 只有在本地时区与 UTC 日期不同的机器上这条才有区分意义，因此做成条件断言。
        if (expectedDate != utcDate)
        {
            Assert.NotEqual(utcDate, groups[0].Date);
        }
    }

    [Fact]
    public void GroupByDay_IncludesFavoriteAndPlainRecords()
    {
        var records = new[]
        {
            MakeRecord("收藏的", new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Local), favorite: true),
            MakeRecord("普通的", new DateTime(2026, 10, 18, 11, 0, 0, DateTimeKind.Local)),
        };

        var group = Assert.Single(HistoryGrouping.GroupByDay(records, Today));

        Assert.Equal(2, group.Count);
        Assert.Contains(group.Items, item => item.IsFavorite);
    }

    [Fact]
    public void Flatten_RestoresDateDescendingOrder()
    {
        var records = new[]
        {
            MakeRecord("a", new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Local)),
            MakeRecord("b", new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Local)),
            MakeRecord("c", new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Local)),
        };

        var flat = HistoryGrouping.Flatten(HistoryGrouping.GroupByDay(records, Today));

        Assert.Equal(new[] { "b", "c", "a" }, flat.Select(item => item.Name));
    }

    [Fact]
    public void Flatten_EmptyGroupsIsEmpty()
    {
        Assert.Empty(HistoryGrouping.Flatten(Array.Empty<HistoryDayGroup>()));
        Assert.Throws<ArgumentNullException>(() => HistoryGrouping.Flatten(null!));
    }

    [Fact]
    public void HistoryDayGroup_ToStringMentionsTitleAndCount()
    {
        var group = Assert.Single(HistoryGrouping.GroupByDay(
            new[] { MakeRecord("x", new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Local)) },
            Today));

        Assert.Contains("今天", group.ToString());
        Assert.Contains("1 条", group.ToString());
    }

    [Fact]
    public void DateOf_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryGrouping.DateOf(null!));
    }

    [Fact]
    public void LocalDate_IgnoresTimeOfDay()
    {
        Assert.Equal(
            new DateOnly(2026, 10, 18),
            HistoryGrouping.LocalDate(new DateTime(2026, 10, 18, 23, 59, 59, DateTimeKind.Local)));
    }

    private static HistoryRecord MakeRecord(string name, DateTime localTime, bool favorite = false) => new(
        Guid.NewGuid(),
        name,
        HistoryRecord.NormalizeUtc(localTime),
        HistoryRecord.NormalizeUtc(localTime),
        favorite,
        "images/x.png",
        null,
        10,
        10,
        HistorySource.Screenshot);

    private static HistoryRecord MakeRecordUtc(string name, DateTime utcTime) => new(
        Guid.NewGuid(),
        name,
        utcTime,
        utcTime,
        false,
        "images/x.png",
        null,
        10,
        10,
        HistorySource.Screenshot);
}
