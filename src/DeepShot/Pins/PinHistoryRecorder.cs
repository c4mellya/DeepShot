using System.Globalization;
using System.Windows.Media.Imaging;
using DeepShot.Core.Pins;
using DeepShot.Platform;

namespace DeepShot.Pins;

/// <summary>
/// 一次"贴图创建"的记录请求（T22 历史接缝）。
/// </summary>
/// <param name="Source">贴图来源（决定是否真的落库，见 <see cref="PinSourceText.ShouldRecordHistory"/>）。</param>
/// <param name="Image">贴图内容（已冻结，与贴图窗口共用同一实例；记录方**不得**修改或持有句柄不放）。</param>
/// <param name="Bounds">贴图创建后的屏幕物理矩形。</param>
/// <param name="CreatedAtLocal">创建时间（本地时间，历史默认名称使用它）。</param>
public sealed record PinHistoryEntry(
    PinSource Source,
    BitmapSource Image,
    DeepShot.Core.Geometry.PixelRect Bounds,
    DateTime CreatedAtLocal)
{
    /// <summary>图像宽度（物理像素）。</summary>
    public int PixelWidth => Image.PixelWidth;

    /// <summary>图像高度（物理像素）。</summary>
    public int PixelHeight => Image.PixelHeight;
}

/// <summary>
/// 贴图 → 历史的接缝（T22）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本轮（T22 接缝）</b>：只有 <see cref="LoggingPinHistoryRecorder"/>，它把"本该记录什么"
/// 写进日志。真实的历史存储是 **T23（SQLite + 图片文件）** 与 **T24（完成动作写入）**；
/// 届时新增一个实现本接口的类型注入 <c>PinWindows</c> / <c>PinWindow</c> 即可，
/// 贴图窗口与剪贴板流程都不需要改动。
/// </para>
/// <para>
/// <b>记录规则（PLAN：历史贴图不新增历史；外部剪贴板创建贴图记录一次；内部操作避免重复）</b>：
/// <list type="bullet">
/// <item>内部截图贴图（<see cref="PinSource.Screenshot"/>）→ 不记录（T11 的 <c>ICaptureSink</c> 已记）。</item>
/// <item>外部剪贴板贴图（<see cref="PinSource.Clipboard"/>）→ 记录一次。</item>
/// <item>历史图库贴图（<see cref="PinSource.History"/>）→ 不记录。</item>
/// </list>
/// 判定由 <see cref="PinSourceText.ShouldRecordHistory"/> 统一下沉到 Core，
/// 实现方不必各自复制一份规则。
/// </para>
/// </remarks>
public interface IPinHistoryRecorder
{
    /// <summary>
    /// 记录一次贴图创建。<see cref="PinHistoryEntry.Source"/> 为
    /// <see cref="PinSource.Screenshot"/> / <see cref="PinSource.History"/> 时实现应直接跳过。
    /// 实现不应抛异常影响贴图创建（调用方会兜底）。
    /// </summary>
    void OnPinCreated(PinHistoryEntry entry);
}

/// <summary>
/// T22 的占位实现：只写一条 INFO 日志，**不落库、不写图片文件**。
/// </summary>
/// <remarks>
/// 日志里刻意带上 <c>T24</c> 字样，方便在 <c>%LOCALAPPDATA%\DeepShot\logs</c> 里核对
/// "哪些贴图会在 T24 之后变成历史记录"。T24 接入真实历史后，这个类型可以保留作为
/// 调试用的旁路实现（或替换为 SQLite 实现）。
/// </remarks>
public sealed class LoggingPinHistoryRecorder : IPinHistoryRecorder
{
    /// <inheritdoc />
    public void OnPinCreated(PinHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var source = PinSourceText.Describe(entry.Source);
        if (!PinSourceText.ShouldRecordHistory(entry.Source))
        {
            AppLog.Info(
                "Pins",
                $"贴图历史联动：来源={source} 不记录（内部截图已由 ICaptureSink 记录 / 历史贴图不重复记录），" +
                $"尺寸={entry.PixelWidth}x{entry.PixelHeight} 位置={entry.Bounds}");
            return;
        }

        AppLog.Info(
            "Pins",
            $"贴图历史联动：来源={source} 应记录一次（T24 接真实历史，本轮仅日志占位）" +
            $" 图像={entry.PixelWidth}x{entry.PixelHeight} 位置={entry.Bounds} " +
            $"时间={entry.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} " +
            $"建议名称=贴图_{entry.CreatedAtLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture)}.png");
    }
}
