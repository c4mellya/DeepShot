using System.Globalization;
using System.Windows.Threading;
using DeepShot.Core.History;
using DeepShot.Platform;

namespace DeepShot.History;

/// <summary>
/// 30 天自动清理的调度器（T27）：启动时清一次，跨日时再清一次。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要定时器</b>：应用是托盘常驻的，可能连着开好几天都不重启。如果只在启动时清理，
/// 一条昨天到期的记录要等到下次开机才会被删掉 —— 与 PLAN 的"启动及跨日时清理"不符。
/// 这里用一个 <see cref="DispatcherTimer"/> 每分钟看一次"本地日期有没有变化"，
/// 日期一变就再清一次。用日期比较而不是"每 24 小时跑一次"：跨日是用户能理解的定义，
/// 也不会因为休眠/唤醒导致计时漂移。
/// </para>
/// <para>
/// <b>清理只做一件小事</b>：<see cref="HistoryRepository.PurgeExpired"/>。规则本身
/// （按创建时间算 30 天、收藏豁免）在 Core 的 <see cref="HistoryRetentionPolicy"/> 里，
/// 有单元测试；这里只负责"什么时候调"与"记日志/刷新图库"。
/// </para>
/// <para>
/// <b>节流</b>：两次清理之间至少隔 <see cref="MinimumInterval"/>，避免用户手动改系统时间
/// 来回跳日期时连续触发（清理本身幂等，但没必要反复读库）。
/// </para>
/// </remarks>
internal sealed class HistoryRetentionScheduler : IDisposable
{
    /// <summary>检查日期变化的间隔。</summary>
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>两次清理之间的最小间隔（手动改系统时间的抖动不计入）。</summary>
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(5);

    private readonly HistoryStore _store;
    private readonly Func<int> _retentionDays;
    private Action<int, HistoryRetentionResult>? _onPurged;
    private readonly DispatcherTimer _timer;
    private DateTime _lastCheckDate;
    private DateTime _lastPurgeUtc = DateTime.MinValue;
    private bool _disposed;

    /// <param name="store">历史写入器（启动时初始化成功的那一个）。</param>
    /// <param name="onPurged">清理完成回调（删了 N 条时调用，用于刷新图库/提示）；参数为删除条数与结果。</param>
    /// <param name="checkInterval">检查间隔（测试可注入更短的值；默认 1 分钟）。</param>
    internal HistoryRetentionScheduler(
        HistoryStore store,
        Action<int, HistoryRetentionResult>? onPurged = null,
        TimeSpan? checkInterval = null,
        Func<int>? retentionDays = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retentionDays = retentionDays ?? (() => HistoryRetentionPolicy.DefaultRetentionDays);
        _onPurged = onPurged;
        _lastCheckDate = DateTime.Now.Date;

        _timer = new DispatcherTimer { Interval = checkInterval ?? CheckInterval };
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>最近一次清理结果（未清理过为 null，供验证/诊断读取）。</summary>
    internal HistoryRetentionResult? LastResult { get; private set; }

    /// <summary>定时器是否在跑。</summary>
    internal bool IsRunning => _timer.IsEnabled;

    /// <summary>上一次执行清理的时间（UTC，未执行过为 <see cref="DateTime.MinValue"/>）。</summary>
    internal DateTime LastPurgeUtc => _lastPurgeUtc;

    /// <summary>
    /// 启动时调用：立刻清理一次，然后开始按日期变化检查。清理失败只记日志，绝不阻塞启动。
    /// </summary>
    internal void Start()
    {
        Purge("启动");
        _timer.Start();
        AppLog.Info(
            "History",
            $"历史自动清理调度器已启动：每 {CheckInterval.TotalMinutes:0} 分钟检查日期变化，" +
            $"当前日期={_lastCheckDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
    }

    /// <summary>手动触发一次清理（图库"刷新"时可选调用；结果只写日志）。</summary>
    internal HistoryRetentionResult? PurgeNow(string trigger) => Purge(trigger);

    private void OnTick()
    {
        var today = DateTime.Now.Date;
        if (today == _lastCheckDate)
        {
            return;
        }

        var previous = _lastCheckDate;
        _lastCheckDate = today;
        AppLog.Info(
            "History",
            $"检测到跨日：{previous.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} → " +
            $"{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}，执行 历史自动清理检查");

        // 跨日必然已经超过 MinimumInterval（默认 5 分钟），这里的节流主要挡住"系统时间回拨"。
        if (DateTime.UtcNow - _lastPurgeUtc < MinimumInterval)
        {
            AppLog.Info("History", "跨日清理距上次清理不足最小间隔，本次跳过（清理是幂等的）");
            return;
        }

        Purge("跨日");
    }

    /// <summary>
    /// 执行一次清理。**异常一律吞掉只记日志**：一条记录删不掉不该让托盘程序出问题。
    /// </summary>
    internal HistoryRetentionResult? Purge(string trigger)
    {
        _lastPurgeUtc = DateTime.UtcNow;

        try
        {
            var now = DateTime.UtcNow;
            var result = _store.Repository.PurgeExpired(
                now,
                _retentionDays());
            LastResult = result;

            AppLog.Info("History", $"历史自动清理（{trigger}）：{result}");

            if (result.Failures.Count > 0)
            {
                foreach (var (id, reason) in result.Failures)
                {
                    AppLog.Warn("History", $"历史自动清理未能删除记录 Id={id}：{reason}");
                }
            }

            if (result.DeletedCount > 0)
            {
                TryNotify(result.DeletedCount, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            // 清理失败不影响任何其它功能；下次跨日/下次启动会再试。
            AppLog.Error("History", $"历史自动清理失败（{trigger}，本次放弃，下次再试）", ex);
            return null;
        }
    }

    private void TryNotify(int count, HistoryRetentionResult result)
    {
        try
        {
            _onPurged?.Invoke(count, result);
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"历史自动清理完成回调出错（忽略，记录已删除）：{ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _onPurged = null;
        AppLog.Info("History", "历史自动清理调度器已停止");
    }
}
