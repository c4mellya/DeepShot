using System.Globalization;

namespace DeepShot.Core.History;

/// <summary>
/// 日期索引里的一天（图库右侧时间轴 / 日期跳转用，T25+）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Date"/> 是**本地日期**（与 <see cref="HistoryGrouping.GroupByDay"/> 的分组口径一致：
/// <c>record.CreatedAtUtc.ToLocalTime()</c> 的日期），所以凌晨的记录一定跟着用户看到的日期走，
/// 不会因为库里存的是 UTC 而跑到前一天。
/// </para>
/// <para>
/// <see cref="Offset"/> 是该天**第一条记录**在"当前过滤条件 + <c>CreatedAtUtc DESC, Id DESC</c>"
/// 结果集里的全局 0 基行号；图库"跳到某天"直接用
/// <see cref="HistoryQuery.Offset"/> = <see cref="Offset"/> 查一页（<c>LoadOffset</c>），
/// 拿到的第一张恰好就是那天的第一张。</para>
/// </remarks>
/// <param name="Date">本地日期。</param>
/// <param name="Count">该天的记录条数。</param>
/// <param name="Offset">该天第一条记录在结果集里的全局偏移（0 基）。</param>
public sealed record HistoryDateEntry(DateOnly Date, int Count, int Offset)
{
    /// <summary>该天最后一条记录在结果集里的偏移（闭区间；<see cref="Count"/> 为 0 时等于 <see cref="Offset"/>）。</summary>
    public int LastOffset => Offset + Math.Max(0, Count - 1);

    public override string ToString() =>
        $"{Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 共 {Count} 条 @{Offset}";
}

/// <summary>
/// 日期索引里的一个月（时间轴上的一年一格 / 月份跳转用）：年月、条数、该月最新一天。
/// </summary>
/// <remarks>
/// 月份是**按本地日期**汇总出来的，不是 SQL 的 UTC 年月；
/// <see cref="Offset"/> 是该月最新一天（也就是该月第一条记录）的全局偏移。
/// </remarks>
/// <param name="Year">年（本地时间）。</param>
/// <param name="Month">月（1-12，本地时间）。</param>
/// <param name="Count">该月记录条数。</param>
/// <param name="Offset">该月第一条记录在结果集里的全局偏移（0 基）。</param>
/// <param name="FirstDate">该月最新的一天（按月内日期倒序的第一天）。</param>
public sealed record HistoryMonthEntry(int Year, int Month, int Count, int Offset, DateOnly FirstDate)
{
    /// <summary><see cref="FirstDate"/> 的别名（"该月第一天"在不同调用方里的叫法）。</summary>
    public DateOnly FirstDay => FirstDate;

    public override string ToString() =>
        $"{Year.ToString("D4", CultureInfo.InvariantCulture)}-{Month.ToString("D2", CultureInfo.InvariantCulture)} " +
        $"共 {Count} 条 @{Offset}（最新 {FirstDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}）";
}

/// <summary>
/// 历史图库的日期索引（T25+）：按**本地日期**分天的条数与偏移，外加按月的汇总。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不放 SQL 里</b>：<c>CreatedAtUtc</c> 是 ISO-8601 UTC 文本，SQLite 的
/// <c>date()</c>/<c>strftime()</c> 只能给出 UTC 日期；而图库是按**本地日期**分组的
/// （<see cref="HistoryGrouping.GroupByDay"/>）。两者在时区非零的区域必然错开一格
/// （例如 UTC+8 下 UTC 那天 16:30 之后的记录属于本地第二天）。所以日期分组在 C# 里算，
/// SQL 只负责"按创建时间倒序把时间列吐出来"。
/// </para>
/// <para>
/// <b>Offset 怎么来的</b>：结果集按 <c>CreatedAtUtc DESC, Id DESC</c> 流式读取，按顺序
/// 遇到某天的第一条记录时，把"它前面已经读过的行数"记为这一天的 <see cref="HistoryDateEntry.Offset"/>。
/// 因为所有更新的记录都排在它前面，这个行号就是该天第一张图在整份列表里的全局位置
/// （与 <c>HistoryRepository.GetList</c> 的 <c>OFFSET</c> 可直接对齐）。
/// 同一天的后续记录只累加条数、不改偏移。
/// </para>
/// <para>
/// <b>过滤规则</b>：索引只尊重当前的搜索 / 收藏过滤（与 <c>GetList</c> 用**同一个 WHERE**），
/// 但**忽略排序与分页** —— 无论界面怎么排序，"跳到某天"得到的偏移始终对应创建时间倒序。
/// </para>
/// <para>
/// <b>不可变</b>：实例构造后只读，可以跨线程共享（写入历史后重新 <c>GetDateIndex</c> 拿新实例）。
/// 天列表 / 月列表都是日期倒序（最新在前），与图库分组顺序一致。
/// </para>
/// </remarks>
public sealed class HistoryDateIndex
{
    private readonly HistoryDateEntry[] _days;
    private readonly HistoryMonthEntry[] _months;
    private readonly Dictionary<DateOnly, int> _dayPositions;
    private readonly Dictionary<(int Year, int Month), int> _monthPositions;
    private readonly int _totalCount;

    private HistoryDateIndex(
        HistoryDateEntry[] days,
        HistoryMonthEntry[] months,
        Dictionary<DateOnly, int> dayPositions,
        Dictionary<(int Year, int Month), int> monthPositions,
        int totalCount)
    {
        _days = days;
        _months = months;
        _dayPositions = dayPositions;
        _monthPositions = monthPositions;
        _totalCount = totalCount;
    }

    /// <summary>空索引（库为空 / 过滤后没有记录）；<see cref="IsEmpty"/> 为 true。</summary>
    public static HistoryDateIndex Empty { get; } = new(
        Array.Empty<HistoryDateEntry>(),
        Array.Empty<HistoryMonthEntry>(),
        new Dictionary<DateOnly, int>(),
        new Dictionary<(int Year, int Month), int>(),
        0);

    /// <summary>按本地日期倒序的天列表（最新在前）。空索引是空列表，不是 null。</summary>
    public IReadOnlyList<HistoryDateEntry> Days => _days;

    /// <summary>按年月倒序的月列表（最新在前）。空索引是空列表，不是 null。</summary>
    public IReadOnlyList<HistoryMonthEntry> Months => _months;

    /// <summary>是否没有任何记录（等价于 <see cref="TotalCount"/> == 0）。</summary>
    public bool IsEmpty => _days.Length == 0;

    /// <summary>索引覆盖的记录总数（等于各天条数之和）。</summary>
    public int TotalCount => _totalCount;

    /// <summary>天数。</summary>
    public int DayCount => _days.Length;

    /// <summary>月数。</summary>
    public int MonthCount => _months.Length;

    /// <summary>找某一天；那天没有记录返回 null。</summary>
    public HistoryDateEntry? FindDay(DateOnly date) =>
        _dayPositions.TryGetValue(date, out var position) ? _days[position] : null;

    /// <summary>找某一年月；那个月没有记录返回 null。</summary>
    public HistoryMonthEntry? FindMonth(int year, int month) =>
        _monthPositions.TryGetValue((year, month), out var position) ? _months[position] : null;

    /// <summary>
    /// 离给定日期最近的**有记录**的一天：先找同一天；没有再找最近的更旧一天；
    /// 连更旧都没有（例如日期早于所有记录）就找最近的新一天。空索引返回 null。
    /// </summary>
    /// <remarks>
    /// 只返回 null 的场合是空索引；只要索引里有任何一天，就一定返回一条（不会因为
    /// 目标日期越界而返回 null）—— 调用方（时间轴跳转）因此不需要再处理"越界"分支。
    /// </remarks>
    public HistoryDateEntry? NearestDay(DateOnly date)
    {
        if (_days.Length == 0)
        {
            return null;
        }

        if (_dayPositions.TryGetValue(date, out var exact))
        {
            return _days[exact];
        }

        // 天列表日期倒序：二分找第一个"比目标更旧"的位置，它就是最近的更旧一天。
        var index = FirstOlderThan(date);
        return index < _days.Length ? _days[index] : _days[^1];
    }

    /// <summary>
    /// 离给定日期最近的**有记录**的一个月：同一天口径（同一月 → 最近的更早月份 →
    /// 没有更早则最近的更晚月份）。空索引返回 null。
    /// </summary>
    public HistoryMonthEntry? NearestMonth(DateOnly date)
    {
        if (_months.Length == 0)
        {
            return null;
        }

        if (_monthPositions.TryGetValue((date.Year, date.Month), out var exact))
        {
            return _months[exact];
        }

        var index = FirstOlderMonthThan(date.Year, date.Month);
        return index < _months.Length ? _months[index] : _months[^1];
    }

    /// <summary>
    /// 从"UTC 日期聚合行"构建索引（便于单测与外部核对）：
    /// 每行是（时间，条数）。真实查询请用
    /// <see cref="HistoryRepository.GetDateIndex"/>（逐行流式读取，不物化整表）。
    /// </summary>
    /// <remarks>
    /// 输入顺序不限：如果日期不是一路递减的，构建时会重新按日期倒序并重算偏移。
    /// 行里的时间按 <c>ToLocalTime()</c> 取本地日期，所以"每个 UTC 日期一行"的聚合输入
    /// 会被整体归到该 UTC 日期起点时刻的本地日期（时区非整日偏移下这是一种近似，
    /// 真实索引永远由仓储逐条记录计算，不受影响）。
    /// </remarks>
    /// <param name="dayRows">（UTC 时间，该桶条数）序列；条数 &lt;= 0 的行忽略。</param>
    public static HistoryDateIndex Create(IEnumerable<(DateTime CreatedAtUtc, int Count)> dayRows)
    {
        ArgumentNullException.ThrowIfNull(dayRows);

        var builder = new HistoryDateIndexBuilder();
        foreach (var (createdAtUtc, count) in dayRows)
        {
            builder.Add(createdAtUtc, count);
        }

        return builder.Build();
    }

    /// <summary>仓储用的增量构建器（先按查询顺序喂行，最后 <see cref="HistoryDateIndexBuilder.Build"/>）。</summary>
    internal static HistoryDateIndexBuilder CreateBuilder() => new();

    /// <summary>二分：日期倒序的天列表里第一个"比 <paramref name="date"/> 更旧"的下标；没有返回长度。</summary>
    private int FirstOlderThan(DateOnly date)
    {
        var low = 0;
        var high = _days.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (_days[middle].Date < date)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    /// <summary>二分：年月倒序的月列表里第一个"比给定年月更早"的下标；没有返回长度。</summary>
    private int FirstOlderMonthThan(int year, int month)
    {
        var low = 0;
        var high = _months.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (CompareMonth(_months[middle].Year, _months[middle].Month, year, month) < 0)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    private static int CompareMonth(int leftYear, int leftMonth, int rightYear, int rightMonth) =>
        leftYear != rightYear ? leftYear.CompareTo(rightYear) : leftMonth.CompareTo(rightMonth);

    public override string ToString() =>
        $"{DayCount.ToString(CultureInfo.InvariantCulture)} 天 / " +
        $"{MonthCount.ToString(CultureInfo.InvariantCulture)} 月 / " +
        $"{TotalCount.ToString(CultureInfo.InvariantCulture)} 条";

    /// <summary>
    /// 日期索引的增量构建器：仓储按 <c>CreatedAtUtc DESC, Id DESC</c> 的顺序逐行喂进来，
    /// 只在内存里保留"每天一行"（日期 + 条数 + 偏移），不物化任何 <see cref="HistoryRecord"/>。
    /// </summary>
    /// <remarks>
    /// <b>顺序契约</b>：<see cref="AddRow"/> 必须按查询顺序调用（行号递增）；
    /// <see cref="Add"/> 适合一次给一批同桶记录。两者都可以安全混用，构建时会检查
    /// 日期是否一路递减，不满足就重排并重算偏移 —— 这样即使 SQL 排序被外力改掉，
    /// 偏移也不会悄悄错位。
    /// </remarks>
    internal sealed class HistoryDateIndexBuilder
    {
        private readonly List<DateOnly> _dates = new();
        private readonly List<int> _counts = new();
        private readonly List<int> _offsets = new();
        private readonly Dictionary<DateOnly, int> _positions = new();
        private int _total;
        private bool _descending = true;

        /// <summary>
        /// 添加若干条（同一时刻或同一聚合桶）记录；新日期的偏移按"已累计条数"推算。
        /// </summary>
        public void Add(DateTime createdAtUtc, int count)
        {
            if (count <= 0)
            {
                return;
            }

            AddCore(createdAtUtc, count, explicitOffset: null);
        }

        /// <summary>
        /// 添加一条按查询顺序读到的记录行（<paramref name="rowOffset"/> 是它在结果集里的 0 基行号，
        /// 该天首次出现时用它当 <see cref="HistoryDateEntry.Offset"/>）。
        /// </summary>
        public void AddRow(DateTime createdAtUtc, int rowOffset) =>
            AddCore(createdAtUtc, 1, Math.Max(0, rowOffset));

        private void AddCore(DateTime createdAtUtc, int count, int? explicitOffset)
        {
            var localTime = createdAtUtc.Kind == DateTimeKind.Local
                ? createdAtUtc
                : createdAtUtc.ToLocalTime();
            var date = DateOnly.FromDateTime(localTime);

            if (_positions.TryGetValue(date, out var position))
            {
                _counts[position] += count;
                _total += count;
                return;
            }

            _dates.Add(date);
            _counts.Add(count);
            _offsets.Add(explicitOffset ?? _total);
            _positions[date] = _dates.Count - 1;

            // 新日期只有比前一个（更旧的）日期更早，才算"一路递减"；
            // 不是的话 Build 会重排并重算偏移（见类注释的"顺序契约"）。
            if (_dates.Count > 1 && _dates[^2] < date)
            {
                _descending = false;
            }

            _total += count;
        }

        /// <summary>构建不可变索引（构建后请丢弃本构建器）。空数据返回 <see cref="HistoryDateIndex.Empty"/>。</summary>
        public HistoryDateIndex Build()
        {
            if (_dates.Count == 0)
            {
                return HistoryDateIndex.Empty;
            }

            // 插入顺序就是日期倒序时直接用；否则按日期倒序重排，并重算每天偏移
            // （偏移 = 比它新的所有天条数之和）。
            var order = new int[_dates.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            if (!_descending)
            {
                Array.Sort(order, (left, right) => _dates[right].CompareTo(_dates[left]));

                var running = 0;
                foreach (var source in order)
                {
                    _offsets[source] = running;
                    running += _counts[source];
                }
            }

            var days = new HistoryDateEntry[order.Length];
            var dayPositions = new Dictionary<DateOnly, int>(order.Length);
            for (var i = 0; i < order.Length; i++)
            {
                var source = order[i];
                days[i] = new HistoryDateEntry(_dates[source], _counts[source], _offsets[source]);
                dayPositions[days[i].Date] = i;
            }

            // 月份：日期倒序遍历，每月第一次出现的那天就是该月最新的一天（偏移与条数口径同天）。
            var months = new List<HistoryMonthEntry>();
            var monthPositions = new Dictionary<(int Year, int Month), int>();
            foreach (var day in days)
            {
                var key = (day.Date.Year, day.Date.Month);
                if (monthPositions.TryGetValue(key, out var position))
                {
                    var month = months[position];
                    months[position] = month with { Count = month.Count + day.Count };
                    continue;
                }

                monthPositions[key] = months.Count;
                months.Add(new HistoryMonthEntry(key.Year, key.Month, day.Count, day.Offset, day.Date));
            }

            return new HistoryDateIndex(
                days,
                months.ToArray(),
                dayPositions,
                monthPositions,
                _total);
        }
    }
}
