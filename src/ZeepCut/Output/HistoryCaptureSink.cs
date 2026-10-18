using System.Globalization;
using ZeepCut.Core.History;
using ZeepCut.Core.Selection;
using ZeepCut.History;
using ZeepCut.Platform;

namespace ZeepCut.Output;

/// <summary>
/// T24 的 <see cref="ICaptureSink"/> 实现：复制 / 另存为 / 贴图**成功后**写一条历史。
/// </summary>
/// <remarks>
/// <para>
/// 取代 T11 的 <c>LoggingCaptureSink</c>（原类保留为纯日志辅助，见
/// <see cref="LoggingCaptureSink"/> 的说明，动作描述 <c>Describe</c> 仍被遮罩日志复用）。
/// 遮罩只在成功路径调用一次 <see cref="OnCaptureCompleted"/>，所以**一次完成动作只产生一条记录**；
/// 取消、失败、取消保存对话框都不会走到这里（T11 已有实现保证）。
/// </para>
/// <para>
/// <b>失败不影响主操作</b>（PLAN：历史缺失不阻塞本地功能）：本方法内部吞掉所有异常，
/// 只写 ERROR 日志并设置 <see cref="LastFailure"/>，让调用方（App）能弹一条
/// "已复制，但历史保存失败"的提示。**绝不向遮罩抛异常** —— 否则用户会看到
/// "复制失败"，而实际上剪贴板里已经有图了。
/// </para>
/// <para>
/// 名称规则：默认 <c>截图_yyyy-MM-dd_HHmmss</c>（复用 Core 的
/// <see cref="HistoryName"/> / <c>CaptureFileName</c>），与已有记录同名时追加 <c> (2)</c>。
/// 查询已有名称失败时不阻塞写入（直接用默认名称）。
/// </para>
/// </remarks>
public sealed class HistoryCaptureSink : ICaptureSink
{
    private readonly HistoryStore? _store;
    private readonly Func<IReadOnlyList<string>>? _nameProvider;

    /// <summary>
    /// 用历史写入器构造（正常启动路径）。
    /// </summary>
    /// <param name="store">历史写入器；null 表示历史不可用（写日志并跳过，用于降级）。</param>
    public HistoryCaptureSink(HistoryStore? store)
    {
        _store = store;
        _nameProvider = store is null ? null : store.Repository.GetAllNames;
    }

    /// <summary>
    /// 用"历史写入器 + 名称提供者"构造（测试用：可注入固定的已有名称集合）。
    /// </summary>
    public HistoryCaptureSink(HistoryStore? store, Func<IReadOnlyList<string>>? nameProvider)
    {
        _store = store;
        _nameProvider = nameProvider;
    }

    /// <summary>最近一次写入失败的原因；成功后清空。供 App 决定是否提示用户。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>最近一次成功写入的记录（诊断/验证用）。</summary>
    public HistoryRecord? LastRecord { get; private set; }

    /// <inheritdoc />
    public void OnCaptureCompleted(CaptureCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var action = LoggingCaptureSink.Describe(completion.Action);
        LastFailure = null;

        // T11 的完成日志保留：历史是否可用都应该有一条"这次输出完成了"的记录。
        AppLog.Info(
            "Output",
            $"输出完成：动作={action} 选区={completion.Selection} " +
            $"尺寸={SelectionOverlayLayout.FormatSize(completion.PixelWidth, completion.PixelHeight)}（物理像素） " +
            $"耗时={completion.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms " +
            $"文件={completion.FilePath ?? "-"} " +
            $"时间={completion.CompletedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

        if (_store is null)
        {
            LastFailure = "历史存储未初始化";
            AppLog.Warn("History", $"历史保存跳过（历史存储不可用）：动作={action}");
            return;
        }

        try
        {
            var name = ResolveName(completion.CompletedAtLocal);
            var record = _store.Add(
                completion.Image,
                name,
                completion.CompletedAtLocal,
                HistorySource.Screenshot);

            LastRecord = record;
            AppLog.Info(
                "History",
                $"截图已记录到历史：动作={action} 名称={record.Name} Id={record.Id} " +
                $"尺寸={record.Width}x{record.Height} 来源={HistorySourceText.Describe(record.Source)}");
        }
        catch (Exception ex)
        {
            LastFailure = ex.Message;
            AppLog.Error(
                "History",
                $"历史保存失败（{action} 本身已成功，不影响剪贴板/文件/贴图）：{ex.GetType().Name}: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// 默认名称 + 同名区分。名称查询失败时退回默认名称（不因为"查不到名字"而放弃记录）。
    /// </summary>
    private string ResolveName(DateTime completedAtLocal)
    {
        var baseName = HistoryName.CreateDefaultFor(HistorySource.Screenshot, completedAtLocal);

        if (_nameProvider is null)
        {
            return baseName;
        }

        try
        {
            return HistoryName.CreateUniqueName(baseName, _nameProvider());
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"读取已有名称失败，本次直接用默认名称 {baseName}：{ex.Message}");
            return baseName;
        }
    }
}
