using System.Globalization;

namespace DeepShot.Core.History;

/// <summary>
/// 列表排序方式（T23 只实现两种：创建时间倒序 = 图库默认；名称升序供搜索视图用）。
/// </summary>
public enum HistorySort
{
    /// <summary>按创建时间倒序（最新在前，图库默认）。</summary>
    CreatedAtDescending = 0,

    /// <summary>按创建时间正序（调试/导出用）。</summary>
    CreatedAtAscending = 1,

    /// <summary>按名称升序（忽略大小写），同名的按创建时间倒序。</summary>
    NameAscending = 2,
}

/// <summary>
/// 是否收藏的过滤条件（三态：不过滤 / 只看收藏 / 只看非收藏）。
/// </summary>
public enum HistoryFavoriteFilter
{
    /// <summary>不过滤。</summary>
    All = 0,

    /// <summary>只看收藏。</summary>
    FavoritesOnly = 1,

    /// <summary>只看未收藏（自动清理会处理的那批）。</summary>
    NotFavorite = 2,
}

/// <summary>
/// 历史列表查询参数（T23）：名称搜索 + 收藏过滤 + 分页 + 排序。
/// </summary>
/// <remarks>
/// 默认值就是图库的默认视图：第 1 页、每页 100 条、按创建时间倒序、不过滤。
/// 分页用 <see cref="Offset"/>/<see cref="Limit"/>（图库按日期分组滚动加载）。
/// 名称搜索是**子串匹配、大小写不敏感**（PLAN：按名称搜索，不做图片文字识别）。
/// </remarks>
public sealed record HistoryQuery
{
    /// <summary>默认每页条数。</summary>
    public const int DefaultLimit = 100;

    /// <summary>每页上限（防止调用方传 int.MaxValue 把整库读进内存）。</summary>
    public const int MaxLimit = 1000;

    /// <summary>空查询：默认图库视图。</summary>
    public static HistoryQuery Default { get; } = new();

    /// <summary>名称搜索关键字；空/空白表示不过滤。</summary>
    public string? SearchText { get; init; }

    /// <summary>收藏过滤。</summary>
    public HistoryFavoriteFilter FavoriteFilter { get; init; } = HistoryFavoriteFilter.All;

    /// <summary>排序方式。</summary>
    public HistorySort Sort { get; init; } = HistorySort.CreatedAtDescending;

    /// <summary>跳过的条数（分页偏移，负数按 0）。</summary>
    public int Offset { get; init; }

    /// <summary>本页最多返回多少条（钳到 1–<see cref="MaxLimit"/>）。</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>规范化后的关键字（去首尾空白；空白 → null）。</summary>
    public string? NormalizedSearchText =>
        string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

    /// <summary>规范化后的偏移。</summary>
    public int NormalizedOffset => Math.Max(0, Offset);

    /// <summary>规范化后的条数上限。</summary>
    public int NormalizedLimit => Math.Clamp(Limit, 1, MaxLimit);

    /// <summary>
    /// 生成 LIKE 模式：关键字里的 <c>%</c>、<c>_</c>、<c>\</c> 要转义，
    /// 否则用户搜 "100%" 会匹配到所有记录。
    /// </summary>
    public static string BuildLikePattern(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        var builder = new System.Text.StringBuilder(keyword.Length + 2);
        builder.Append('%');
        foreach (var ch in keyword)
        {
            if (ch is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch);
        }

        builder.Append('%');
        return builder.ToString();
    }

    public override string ToString()
    {
        var parts = new List<string>
        {
            $"搜索={NormalizedSearchText ?? "-"}",
            $"收藏={FavoriteFilter}",
            $"排序={Sort}",
            $"分页={NormalizedOffset}+{NormalizedLimit}",
        };

        return string.Join(" ", parts.Cast<string>().Select(p => p));
    }
}

/// <summary>一页历史记录 + 匹配总数（图库需要显示"共 N 条"并决定是否继续加载）。</summary>
/// <param name="Items">本页记录（已按 <see cref="HistoryQuery.Sort"/> 排好）。</param>
/// <param name="TotalCount">符合过滤条件的总条数（不受分页限制）。</param>
/// <param name="Query">产生本页的查询（便于分页续查与日志）。</param>
public sealed record HistoryPage(IReadOnlyList<HistoryRecord> Items, int TotalCount, HistoryQuery Query)
{
    /// <summary>空页（查询失败或没有匹配）。</summary>
    public static HistoryPage Empty(HistoryQuery? query = null) =>
        new(Array.Empty<HistoryRecord>(), 0, query ?? HistoryQuery.Default);

    /// <summary>本页条数。</summary>
    public int Count => Items.Count;

    /// <summary>是否还有下一页。</summary>
    public bool HasMore => Query.NormalizedOffset + Count < TotalCount;

    /// <summary>下一页的查询（已推进 offset）；没有下一页时返回 null。</summary>
    public HistoryQuery? NextPage()
    {
        if (!HasMore)
        {
            return null;
        }

        return Query with { Offset = Query.NormalizedOffset + Count };
    }

    public override string ToString() =>
        $"{Count}/{TotalCount} 条（{Query.NormalizedOffset.ToString(CultureInfo.InvariantCulture)}+{Query.NormalizedLimit.ToString(CultureInfo.InvariantCulture)}，{Query}）";
}
