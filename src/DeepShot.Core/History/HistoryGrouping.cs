using System.Globalization;

namespace DeepShot.Core.History;

/// <summary>
/// 历史列表的日期粗分组（T25 图库顶部小标题用）：今天 / 昨天 / 更早。
/// </summary>
/// <remarks>
/// 图库实际按**具体日期**分节（见 <see cref="HistoryGrouping.GroupByDay"/>），
/// 本枚举只用于"今天/昨天"这类文案与测试断言；两套语义都以**本地时间**为准，
/// 因为用户看到的是本地时间的日期（UTC 会让凌晨的记录跑到前一天）。
/// </remarks>
public enum HistoryDateBucket
{
    /// <summary>今天。</summary>
    Today = 0,

    /// <summary>昨天。</summary>
    Yesterday = 1,

    /// <summary>更早（前天及以前）。</summary>
    Earlier = 2,
}

/// <summary>
/// 图库里的一个日期分组（标题 + 该天的记录，记录保持查询顺序）。
/// </summary>
/// <param name="Date">该组的本地日期。</param>
/// <param name="Title">分组标题（<c>今天</c> / <c>昨天</c> / <c>2026-10-16 星期五</c>）。</param>
/// <param name="Items">该天的记录（按创建时间倒序，组内第一条最新）。</param>
public sealed record HistoryDayGroup(DateOnly Date, string Title, IReadOnlyList<HistoryRecord> Items)
{
    /// <summary>该组记录数。</summary>
    public int Count => Items.Count;

    public override string ToString() =>
        $"{Title}（{Count} 条，{Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}）";
}

/// <summary>
/// 历史图库的日期分组规则（T25，纯逻辑、可单测）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么放 Core</b>："凌晨的记录算今天还是昨天""昨天到底怎么定义""分组内谁在前"
/// 这类规则最容易在 UI 里被写成一堆 <c>if</c> 然后悄悄错一天。它们只依赖本地时间，
/// 不依赖 WPF，所以放进 Core 由 xUnit 兜住（<c>HistoryGroupingTests</c>）。
/// </para>
/// <para>
/// <b>时间来源</b>：记录里的 <see cref="HistoryRecord.CreatedAtUtc"/> 是 UTC，
/// 分组时统一转本地时间（<see cref="HistoryRecord.CreatedAtLocal"/>）再取日期。
/// 因此 2026-10-18 23:59:59（本地）与 2026-10-19 00:00:00（本地）一定落在两组。
/// </para>
/// <para>
/// <b>排序</b>：分组结果按日期**倒序**（今天在最上面），组内按创建时间倒序。
/// 输入顺序不影响结果；同一毫秒的记录保持输入顺序（LINQ <c>OrderBy</c> 是稳定排序）。
/// </para>
/// </remarks>
public static class HistoryGrouping
{
    /// <summary>今天的标题。</summary>
    public const string TodayTitle = "今天";

    /// <summary>昨天的标题。</summary>
    public const string YesterdayTitle = "昨天";

    /// <summary>更早的日期标题格式（<c>2026-10-16 星期五</c>）。</summary>
    public const string DayTitleFormat = "yyyy-MM-dd";

    private static readonly string[] WeekdayNames =
    {
        "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六",
    };

    /// <summary>取本地时间的日期部分。</summary>
    public static DateOnly LocalDate(DateTime localTime) => DateOnly.FromDateTime(localTime);

    /// <summary>某个记录在本地时间下的日期。</summary>
    public static DateOnly DateOf(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return LocalDate(record.CreatedAtLocal);
    }

    /// <summary>
    /// 判断日期属于今天 / 昨天 / 更早。
    /// </summary>
    /// <param name="date">待判断的本地日期。</param>
    /// <param name="today">"今天"的本地日期（由调用方传入，便于测试）。</param>
    /// <remarks>
    /// 相差 0 天或**未来**（系统时钟被改过、记录了未来时间）都算今天：
    /// 图库不该出现"明天"这种分组。相差 1 天 = 昨天，其余 = 更早。
    /// </remarks>
    public static HistoryDateBucket BucketOf(DateOnly date, DateOnly today)
    {
        var days = today.DayNumber - date.DayNumber;
        return days switch
        {
            <= 0 => HistoryDateBucket.Today,
            1 => HistoryDateBucket.Yesterday,
            _ => HistoryDateBucket.Earlier,
        };
    }

    /// <summary>粗分组的界面文案。</summary>
    public static string DescribeBucket(HistoryDateBucket bucket) => bucket switch
    {
        HistoryDateBucket.Today => TodayTitle,
        HistoryDateBucket.Yesterday => YesterdayTitle,
        _ => "更早",
    };

    /// <summary>
    /// 语言钩子（T31）：为 null 时是中文（<c>今天</c> / <c>2026-10-16 星期五</c>，默认行为不变）；
    /// 应用层切到英文时把它设成英文措辞。放在这里而不是把 WPF 资源塞进 Core：
    /// Core 保持纯逻辑、可单测，默认中文也保证既有 1014 个测试的断言不变。
    /// </summary>
    public static Func<DateOnly, DateOnly, string>? Localize { get; set; }

    /// <summary>分组标题：今天 / 昨天 / <c>2026-10-16 星期五</c>（英文界面下是 Today / Yesterday / 2026-10-16 Friday）。</summary>
    public static string CreateTitle(DateOnly date, DateOnly today)
    {
        if (Localize is { } localize)
        {
            return localize(date, today);
        }

        var bucket = BucketOf(date, today);
        if (bucket != HistoryDateBucket.Earlier)
        {
            return DescribeBucket(bucket);
        }

        return date.ToString(DayTitleFormat, CultureInfo.InvariantCulture) + " " + WeekdayName(date);
    }

    /// <summary>星期几的中文名（英文界面由 <see cref="Localize"/> 提供，不走这里）。</summary>
    public static string WeekdayName(DateOnly date)
    {
        var index = (int)date.DayOfWeek;
        return index >= 0 && index < WeekdayNames.Length ? WeekdayNames[index] : string.Empty;
    }

    /// <summary>
    /// 把记录按本地日期分组（日期倒序、组内创建时间倒序）。
    /// </summary>
    /// <param name="records">记录集合（顺序无关）。</param>
    /// <param name="now">"现在"的本地时间，用于判断今天/昨天（测试传入固定值）。</param>
    /// <returns>日期分组列表；空输入返回空列表。</returns>
    public static IReadOnlyList<HistoryDayGroup> GroupByDay(IEnumerable<HistoryRecord> records, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(records);

        var today = LocalDate(now);

        // OrderBy 是稳定排序：同一毫秒的记录保持查询（Repository）给出的顺序。
        var ordered = records.OrderByDescending(record => record.CreatedAtUtc).ToList();
        if (ordered.Count == 0)
        {
            return Array.Empty<HistoryDayGroup>();
        }

        // 先攒成 (日期 → 该天记录) 的有序桶，最后一次性构造不可变的 group 列表。
        var buckets = new List<(DateOnly Date, List<HistoryRecord> Items)>();
        var positions = new Dictionary<DateOnly, int>();
        foreach (var record in ordered)
        {
            var date = DateOf(record);
            if (!positions.TryGetValue(date, out var position))
            {
                position = buckets.Count;
                positions[date] = position;
                buckets.Add((date, new List<HistoryRecord>()));
            }

            buckets[position].Items.Add(record);
        }

        var groups = new List<HistoryDayGroup>(buckets.Count);
        foreach (var (date, items) in buckets)
        {
            groups.Add(new HistoryDayGroup(date, CreateTitle(date, today), items));
        }

        return groups;
    }

    /// <summary>把分组摊平回记录列表（标题渲染/调试用）。</summary>
    public static IReadOnlyList<HistoryRecord> Flatten(IEnumerable<HistoryDayGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var result = new List<HistoryRecord>();
        foreach (var group in groups)
        {
            result.AddRange(group.Items);
        }

        return result;
    }
}
