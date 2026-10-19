using System.Globalization;

namespace DeepShot.Core.History;

/// <summary>
/// 历史图库的视图状态（T25/T26）：搜索关键字 + "只看收藏"开关 + 排序 + 每页条数。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么单独一个类型</b>：图库窗口需要把"当前视图"这个整体在多个地方传递
/// （翻译成 SQL 查询、生成日志、刷新后恢复、生成空状态文案），散着传三个参数
/// 很容易漏掉其中一个（典型 bug：搜索 + 只看收藏组合时只带了搜索）。
/// 本类型是**不可变 record**，修改返回新实例，并且能直接翻译成
/// <see cref="HistoryQuery"/>（存储层的唯一查询入口）。
/// </para>
/// <para>
/// <b>组合语义</b>（PLAN：按名称搜索不涉及图片文字识别）：
/// <list type="bullet">
/// <item>搜索是**名称子串、大小写不敏感**（与 <see cref="HistoryName.MatchesSearch"/>、
/// SQL 的 <c>LIKE + COLLATE NOCASE</c> 语义一致）；</item>
/// <item>"只看收藏"与搜索是**与**关系（两个都满足才显示）；</item>
/// <item>空白搜索等于没搜索（等价于默认视图）。</item>
/// </list>
/// </para>
/// </remarks>
public sealed record HistoryView
{
    /// <summary>默认每页条数（与 <see cref="HistoryQuery.DefaultLimit"/> 一致）。</summary>
    public const int DefaultPageSize = HistoryQuery.DefaultLimit;

    /// <summary>默认视图：无搜索、不筛选收藏、按创建时间倒序。</summary>
    public static HistoryView Default { get; } = new();

    /// <summary>名称搜索关键字；空/空白表示不搜索。</summary>
    public string? SearchText { get; init; }

    /// <summary>是否"只看收藏"（T26）。</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>排序方式（默认创建时间倒序；重命名后可切到名称升序）。</summary>
    public HistorySort Sort { get; init; } = HistorySort.CreatedAtDescending;

    /// <summary>每页条数（图库一次加载多少条）。</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>规范化后的关键字（去首尾空白；空白 → null）。</summary>
    public string? NormalizedSearchText =>
        string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

    /// <summary>是否有任何过滤条件（决定空状态文案与"清除筛选"按钮是否可用）。</summary>
    public bool HasFilter => NormalizedSearchText is not null || FavoritesOnly;

    /// <summary>是否就是默认视图。</summary>
    public bool IsDefault => !HasFilter && Sort == HistorySort.CreatedAtDescending;

    /// <summary>翻译成收藏过滤枚举。</summary>
    public HistoryFavoriteFilter FavoriteFilter =>
        FavoritesOnly ? HistoryFavoriteFilter.FavoritesOnly : HistoryFavoriteFilter.All;

    /// <summary>修改搜索关键字（保留其它条件）。</summary>
    public HistoryView WithSearch(string? searchText) => this with { SearchText = searchText };

    /// <summary>设置"只看收藏"（保留其它条件）。</summary>
    public HistoryView WithFavoritesOnly(bool favoritesOnly) =>
        this with { FavoritesOnly = favoritesOnly };

    /// <summary>切换"只看收藏"。</summary>
    public HistoryView ToggleFavoritesOnly() => WithFavoritesOnly(!FavoritesOnly);

    /// <summary>清空全部筛选（排序保持不变）。</summary>
    public HistoryView ClearFilters() => this with { SearchText = null, FavoritesOnly = false };

    /// <summary>
    /// 翻译成存储层查询。分页偏移由调用方给（图库"加载更多"时推进）。
    /// </summary>
    public HistoryQuery ToQuery(int offset = 0) => new()
    {
        SearchText = NormalizedSearchText,
        FavoriteFilter = FavoriteFilter,
        Sort = Sort,
        Offset = offset,
        Limit = PageSize,
    };

    /// <summary>
    /// 内存里的匹配判断（不查库）：与 SQL 的搜索 + 收藏过滤**语义一致**，
    /// 供 UI 对已加载的记录做二次过滤/断言，也便于单测覆盖组合规则。
    /// </summary>
    public bool Matches(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (FavoritesOnly && !record.IsFavorite)
        {
            return false;
        }

        return HistoryName.MatchesSearch(record.Name, NormalizedSearchText);
    }

    /// <summary>
    /// 空状态提示：区分"一条历史都没有"（引导去截图）与"有历史但被筛掉了"
    /// （提示清除筛选）——两者给用户看的话术完全不同。
    /// 任何含全角/半角引号的文案都直接写在这里（不进日志），避免影响日志解析。
    /// </summary>
    /// <param name="totalCount">库里（不过滤）的记录总数。</param>
    public string DescribeEmptyState(int totalCount)
    {
        if (totalCount <= 0)
        {
            return "还没有历史记录。按 Alt+A 截个图，或在托盘菜单里从剪贴板贴图，成功后会出现在这里。";
        }

        if (FavoritesOnly && NormalizedSearchText is not null)
        {
            return $"没有同时匹配“{NormalizedSearchText}”且已收藏的记录。可以关掉“只看收藏”或清空搜索。";
        }

        if (FavoritesOnly)
        {
            return "还没有收藏的记录。点击缩略图左上角的星标即可收藏。";
        }

        if (NormalizedSearchText is not null)
        {
            return $"没有名称包含“{NormalizedSearchText}”的记录。换个关键字试试，或清空搜索。";
        }

        return "没有可显示的记录。";
    }

    public override string ToString()
    {
        var parts = new List<string>
        {
            $"搜索={NormalizedSearchText ?? "-"}",
            $"只看收藏={FavoritesOnly}",
            $"排序={Sort}",
            $"每页={Math.Clamp(PageSize, 1, HistoryQuery.MaxLimit).ToString(CultureInfo.InvariantCulture)}",
        };

        return string.Join(" ", parts);
    }
}
