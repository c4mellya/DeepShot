namespace ZeepCut.Core.History;

/// <summary>
/// 30 天保留策略（T27，纯逻辑、可单测）：给定一批记录与"现在"，算出应该自动清理的记录。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一的期限依据是 <see cref="HistoryRecord.CreatedAtUtc"/></b>。PLAN 明确写了
/// "按创建时间计算 30 天，编辑与重命名不延长期限"，所以这里**不看**
/// <see cref="HistoryRecord.UpdatedAtUtc"/> —— 覆盖编辑、改名、收藏只会刷新修改时间，
/// 不会让一条老记录重新活 30 天。这一点写成测试锁住（<c>HistoryRetentionPolicyTests</c>）。
/// </para>
/// <para>
/// <b>收藏豁免</b>：<see cref="HistoryRecord.IsFavorite"/> 为真的一律不清理，哪怕已经过期很久。
/// 但"过期"这件事仍然可以被问到（<see cref="IsExpired"/>）：图库要用它决定
/// "取消收藏时是否需要提示将会被清理"（PLAN：对超过期限的收藏取消收藏时，告知即将清理并允许取消）。
/// </para>
/// <para>
/// <b>边界</b>：到期时刻 = <c>CreatedAtUtc + 30 天</c>；**正好满 30 天即视为过期**（含边界），
/// 29 天 23 小时 59 分 59 秒仍保留，30 天 1 秒清理。比较用 <c>&gt;=</c>，与"满 30 天就清"一致。
/// </para>
/// <para>
/// <b>不碰用户文件</b>：本类只给出 Id；实际删除走
/// <see cref="HistoryRepository.Delete(Guid)"/>（删行 + <c>images/</c> + <c>thumbs/</c> 里的内部文件），
/// 用户"另存为"出去的文件不在数据根目录内，永远不会被这里提到。
/// </para>
/// </remarks>
public static class HistoryRetentionPolicy
{
    /// <summary>默认保留天数（PLAN：普通记录最近 30 天）。</summary>
    public const int DefaultRetentionDays = 30;

    /// <summary>保留期（默认 30 天），便于日志里写"保留期=30 天"。</summary>
    public static TimeSpan RetentionPeriod => TimeSpan.FromDays(DefaultRetentionDays);

    /// <summary>
    /// 记录是否已超过保留期（**不考虑收藏**，只看创建时间）。
    /// </summary>
    /// <param name="record">记录。</param>
    /// <param name="nowUtc">"现在"（UTC，任意 Kind 都会被归一）。</param>
    /// <param name="retentionDays">保留天数（默认 30；小于 0 时抛异常）。</param>
    public static bool IsExpired(HistoryRecord record, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(record);
        return IsCreatedAtExpired(record.CreatedAtUtc, nowUtc, retentionDays);
    }

    /// <summary>
    /// 记录是否应当被**自动清理**：已过期且不是收藏。
    /// </summary>
    public static bool ShouldPurge(HistoryRecord record, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !record.IsFavorite && IsCreatedAtExpired(record.CreatedAtUtc, nowUtc, retentionDays);
    }

    /// <summary>
    /// 创建时间是否已过期。单独暴露给 UI：取消收藏前的确认框要判断"这条过期了吗"
    /// （此时记录还是收藏状态，<see cref="ShouldPurge"/> 会一律返回 false）。
    /// </summary>
    public static bool IsCreatedAtExpired(DateTime createdAtUtc, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        if (retentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), retentionDays, "保留天数不能为负数。");
        }

        var created = HistoryRecord.NormalizeUtc(createdAtUtc);
        var now = HistoryRecord.NormalizeUtc(nowUtc);

        // 正好满 30 天算过期；29 天 23:59:59.999 还没到。
        return now >= created.AddDays(retentionDays);
    }

    /// <summary>
    /// 从一批记录里选出应当自动清理的 Id（顺序与输入一致，便于日志/测试比对）。
    /// </summary>
    /// <param name="records">候选记录（通常是库里全部记录或查询结果）。</param>
    /// <param name="nowUtc">"现在"。</param>
    /// <param name="retentionDays">保留天数（默认 30）。</param>
    public static IReadOnlyList<Guid> SelectExpired(
        IEnumerable<HistoryRecord> records,
        DateTime nowUtc,
        int retentionDays = DefaultRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(records);

        var result = new List<Guid>();
        foreach (var record in records)
        {
            if (record is not null && ShouldPurge(record, nowUtc, retentionDays))
            {
                result.Add(record.Id);
            }
        }

        return result;
    }

    /// <summary>
    /// 到期时刻（UTC）：超过这个时刻就会在下一次清理时被删掉。展示给用户看（设置窗口/提示框）。
    /// </summary>
    public static DateTime GetExpiryUtc(HistoryRecord record, int retentionDays = DefaultRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (retentionDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), retentionDays, "保留天数不能为负数。");
        }

        return HistoryRecord.NormalizeUtc(record.CreatedAtUtc.AddDays(retentionDays));
    }

    /// <summary>剩余天数（向上取整，已过期返回 0），供图库/设置窗口显示。</summary>
    public static int GetRemainingDays(HistoryRecord record, DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        var expiry = GetExpiryUtc(record, retentionDays);
        var now = HistoryRecord.NormalizeUtc(nowUtc);
        if (now >= expiry)
        {
            return 0;
        }

        return (int)Math.Ceiling((expiry - now).TotalDays);
    }

    /// <summary>规则说明（设置窗口 T29 与首次引导 T33 共用同一句文案）。</summary>
    public static string DescribeRule(int retentionDays = DefaultRetentionDays) =>
        $"普通记录保留最近 {retentionDays} 天（按创建时间计算，编辑与重命名不延长期限）；" +
        "收藏的记录不会被自动清理。";

    /// <summary>
    /// 取消收藏前的提示文案：只对**已过期**的记录有意义（未过期不需要提示）。
    /// 返回 <c>null</c> 表示不必提示。
    /// </summary>
    public static string? DescribeFavoriteRemovalWarning(
        HistoryRecord record,
        DateTime nowUtc,
        int retentionDays = DefaultRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsExpired(record, nowUtc, retentionDays))
        {
            return null;
        }

        var days = (int)(HistoryRecord.NormalizeUtc(nowUtc) - HistoryRecord.NormalizeUtc(record.CreatedAtUtc)).TotalDays;
        return $"“{record.Name}”创建于 {days} 天前，已经超过 {retentionDays} 天保留期。\n\n" +
               "取消收藏后，这条记录会在下次清理（启动或跨日）时被删除，图片、缩略图与记录都会永久移除。\n\n" +
               "确定取消收藏吗？选择“取消”可以保持收藏、什么都不改。";
    }
}
