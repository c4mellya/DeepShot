namespace ZeepCut.Core.History;

/// <summary>
/// 覆盖编辑（T28）的纯逻辑辅助：把"打开时拿到的记录"与"现在库里的记录"对齐，
/// 决定保存时应该怎么写、以及给用户看什么文案。
/// </summary>
/// <remarks>
/// <para>
/// <b>覆盖语义</b>（PLAN：「历史再编辑使用独立窗口复用标注组件，旧标注已属于图片像素，
/// 只能追加」）：保存时**更新同一条记录**——保留 Id / 名称 / 创建时间 / 收藏状态，
/// 只换图片、缩略图、宽高与修改时间，**不生成新记录**。这些保留字段由
/// <see cref="HistoryRecord.WithImage"/> 保证，本类只负责"用哪一版记录作为基底"。
/// </para>
/// <para>
/// <b>编辑期间被改名/收藏</b>：用户在编辑器里画标注的同时，图库里可能把这条记录改了名或
/// 取消了收藏。保存时必须以**库里最新的一版**为基底（<see cref="MergeLatest"/>），
/// 否则会把编辑器打开时的旧名称/旧星标写回去，把用户后来的修改悄悄覆盖掉。
/// </para>
/// <para>
/// <b>编辑期间被删除</b>：库里已经查不到这条记录 → <see cref="EditState.Deleted"/>，
/// UI 提示"记录已不存在"并且**不写任何文件**（不重新创建一条同名记录）。
/// </para>
/// <para>
/// <b>贴图不受影响</b>（PLAN：贴图使用创建时快照，历史覆盖或清理不改变已有贴图）：
/// 贴图窗口持有的是打开时解码出来的冻结位图，与历史文件没有任何共享引用；
/// 覆盖写的是磁盘上的 <c>images/xxx.png</c>，不会触碰任何已显示的贴图。
/// 这一点在 <c>HistoryEditSession</c> 与 <c>PinWindows</c> 的注释里交叉记录。
/// </para>
/// </remarks>
public static class HistoryOverwritePlan
{
    /// <summary>编辑状态（决定保存按钮是否可用、提示什么）。</summary>
    public enum EditState
    {
        /// <summary>记录还在，可以保存并覆盖。</summary>
        Available = 0,

        /// <summary>记录在编辑期间被删除了：保存必须放弃。</summary>
        Deleted = 1,
    }

    /// <summary>查询记录当前状态（按 Id；库里查不到即视为已删除）。</summary>
    /// <param name="repository">仓储。</param>
    /// <param name="id">记录 Id。</param>
    public static EditState ResolveState(HistoryRepository repository, Guid id)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return repository.GetById(id) is null ? EditState.Deleted : EditState.Available;
    }

    /// <summary>
    /// 用"库里最新的那一版"作为覆盖基底：保留它现在的名称/收藏/创建时间，
    /// 只把图片相关字段交给 <see cref="HistoryRecord.WithImage"/>。
    /// </summary>
    /// <param name="original">打开编辑器时的记录（用于兜底与日志比对）。</param>
    /// <param name="latest">刚刚从库里读到的记录（必须与 <paramref name="original"/> 同 Id）。</param>
    /// <exception cref="ArgumentException">两条记录 Id 不一致。</exception>
    public static HistoryRecord MergeLatest(HistoryRecord original, HistoryRecord latest)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(latest);

        if (original.Id != latest.Id)
        {
            throw new ArgumentException(
                $"两条记录 Id 不一致（{original.Id} vs {latest.Id}），不能作为覆盖基底。",
                nameof(latest));
        }

        // WithImage 会保留 Id/Name/CreatedAtUtc/IsFavorite/Source/相对路径，
        // 因此把 latest 当作基底，等于"保留用户编辑期间在图库里做的改名/收藏变化"。
        return latest;
    }

    /// <summary>记录在编辑期间是否被改名或改过收藏（用于日志与"保存成功"提示）。</summary>
    public static bool WasModifiedElsewhere(HistoryRecord original, HistoryRecord latest)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(latest);

        return !string.Equals(original.Name, latest.Name, StringComparison.Ordinal) ||
               original.IsFavorite != latest.IsFavorite;
    }

    /// <summary>
    /// 保存并覆盖成功后的提示文案（明确说"没有新记录"——这是覆盖语义最容易被误解的地方）。
    /// </summary>
    /// <param name="record">覆盖后的记录（宽高为新的）。</param>
    /// <param name="newAnnotationCount">本次追加的标注数量（0 表示只是原样保存了一次）。</param>
    public static string DescribeSaved(HistoryRecord record, int newAnnotationCount) =>
        newAnnotationCount > 0
            ? $"已保存并覆盖“{record.Name}”（{record.Width}×{record.Height}，追加 {newAnnotationCount} 个标注），没有新增历史记录。"
            : $"已保存并覆盖“{record.Name}”（{record.Width}×{record.Height}），没有新增历史记录。";

    /// <summary>记录已被删除时的提示文案（保存应当放弃）。</summary>
    public static string DescribeDeleted(HistoryRecord record) =>
        $"“{record.Name}”在编辑期间已被删除，本次修改没有保存（原记录不存在，也不会新建一条）。";

    /// <summary>
    /// 首次使用覆盖编辑时的解释（PLAN：按钮明确为"保存并覆盖"，首次解释覆盖不会保留旧版本）。
    /// </summary>
    public static string DescribeOverwriteExplanation(string name) =>
        "覆盖编辑说明\n\n" +
        $"“{name}”现在的标注已经合并进图片像素，不能再拆分修改，只能在这张图上继续追加标注。\n\n" +
        "点“保存并覆盖”会用新图替换这条记录原来的图片：\n" +
        "· 名称、创建时间与收藏状态保持不变，不会新增一条历史记录；\n" +
        "· 旧版本图片与旧标注「不会被保留」，无法撤销回编辑前的样子。\n\n" +
        "想保留一份原图，请先取消，在图库里用“另存为”导出。";
}
