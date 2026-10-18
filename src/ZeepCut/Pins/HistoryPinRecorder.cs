using System.Globalization;
using ZeepCut.Core.History;
using ZeepCut.Core.Pins;
using ZeepCut.History;
using ZeepCut.Platform;

namespace ZeepCut.Pins;

/// <summary>
/// T24 的真实实现：外部剪贴板贴图（<see cref="PinSource.Clipboard"/>）成功后写一条
/// <c>Source=Clipboard</c> 的历史。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么内部截图与历史贴图不记录</b>：规则在 Core 的
/// <see cref="PinSourceText.ShouldRecordHistory"/>（T22 已单测）。内部截图贴图的图片
/// 就是截图输出图，T11/T24 的 <c>ICaptureSink</c> 已经记过一次；历史贴图的记录本身
/// 就在库里。这两条路径如果也写，一次操作就会出现两条历史。
/// </para>
/// <para>
/// <b>失败不影响贴图</b>：异常全部吞掉，只写日志并设置 <see cref="LastFailure"/>；
/// 贴图窗口已经显示出来了，历史失败最多让用户少一条记录。
/// </para>
/// <para>
/// 名称前缀用 <c>贴图_</c>（<see cref="HistoryName.DefaultPrefixFor"/>），与截图记录的
/// <c>截图_</c> 区分，图库里一眼能看出这张是外部贴过来的。
/// </para>
/// </remarks>
public sealed class HistoryPinRecorder : IPinHistoryRecorder
{
    private readonly HistoryStore? _store;
    private readonly Func<IReadOnlyList<string>>? _nameProvider;

    /// <summary>用历史写入器构造（正常启动路径）。</summary>
    /// <param name="store">历史写入器；null 表示历史不可用（跳过记录）。</param>
    public HistoryPinRecorder(HistoryStore? store)
    {
        _store = store;
        _nameProvider = store is null ? null : store.Repository.GetAllNames;
    }

    /// <summary>测试用：注入固定的已有名称集合。</summary>
    public HistoryPinRecorder(HistoryStore? store, Func<IReadOnlyList<string>>? nameProvider)
    {
        _store = store;
        _nameProvider = nameProvider;
    }

    /// <summary>最近一次写入失败的原因；成功后清空。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>最近一次成功写入的记录（诊断/验证用）。</summary>
    public HistoryRecord? LastRecord { get; private set; }

    /// <inheritdoc />
    public void OnPinCreated(PinHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var source = PinSourceText.Describe(entry.Source);
        LastFailure = null;

        if (!PinSourceText.ShouldRecordHistory(entry.Source))
        {
            AppLog.Info(
                "Pins",
                $"贴图历史联动：来源={source} 不记录（内部截图已由 ICaptureSink 记录 / 历史贴图不重复记录），" +
                $"尺寸={entry.PixelWidth}x{entry.PixelHeight} 位置={entry.Bounds}");
            return;
        }

        if (_store is null)
        {
            LastFailure = "历史存储未初始化";
            AppLog.Warn("Pins", $"贴图历史未记录（历史存储不可用）：来源={source}");
            return;
        }

        try
        {
            var name = ResolveName(entry.CreatedAtLocal);
            var record = _store.Add(
                entry.Image,
                name,
                entry.CreatedAtLocal,
                HistorySource.Clipboard);

            LastRecord = record;
            AppLog.Info(
                "Pins",
                $"贴图历史联动：来源={source} 已记录一次 名称={record.Name} Id={record.Id} " +
                $"图像={record.Width}x{record.Height} 位置={entry.Bounds} " +
                $"时间={entry.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex)
        {
            LastFailure = ex.Message;
            AppLog.Error(
                "Pins",
                $"贴图历史保存失败（贴图本身已创建，不影响显示）：来源={source} " +
                $"{ex.GetType().Name}: {ex.Message}",
                ex);
        }
    }

    private string ResolveName(DateTime createdAtLocal)
    {
        var baseName = HistoryName.CreateDefaultFor(HistorySource.Clipboard, createdAtLocal);
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
            AppLog.Warn("Pins", $"读取已有名称失败，本次直接用默认名称 {baseName}：{ex.Message}");
            return baseName;
        }
    }
}
