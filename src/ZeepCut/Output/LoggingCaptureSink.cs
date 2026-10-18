using System.Globalization;
using ZeepCut.Core.Selection;
using ZeepCut.Platform;

namespace ZeepCut.Output;

/// <summary>
/// T11 的成功记录实现：只写一条含动作类型、尺寸、耗时的 INFO 日志。
/// </summary>
/// <remarks>
/// <para>
/// T24 起正常运行走 <see cref="HistoryCaptureSink"/>（日志 + 写一条历史）；本类退居两个用途：
/// <list type="bullet">
/// <item>历史库初始化失败时的降级 sink（截图照常完成，只是不记录）。</item>
/// <item><see cref="Describe"/> 仍被遮罩与历史 sink 复用，作为动作类型的中文描述。</item>
/// </list>
/// </para>
/// <para>
/// 两条路径都会写"输出完成"这条 INFO，所以验证脚本按该行判断成功时不受选哪个 sink 影响。
/// </para>
/// </remarks>
public sealed class LoggingCaptureSink : ICaptureSink
{
    /// <inheritdoc />
    public void OnCaptureCompleted(CaptureCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        AppLog.Info(
            "Output",
            $"输出完成：动作={Describe(completion.Action)} 选区={completion.Selection} " +
            $"尺寸={SelectionOverlayLayout.FormatSize(completion.PixelWidth, completion.PixelHeight)}（物理像素） " +
            $"耗时={completion.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}ms " +
            $"文件={completion.FilePath ?? "-"} " +
            $"时间={completion.CompletedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}" +
            "（历史记录尚未实现，T24 由 ICaptureSink 写入）");
    }

    /// <summary>动作类型的中文描述，供日志与提示复用。</summary>
    public static string Describe(CaptureAction action) => action switch
    {
        CaptureAction.Copy => "复制到剪贴板",
        CaptureAction.SaveAs => "另存为",
        CaptureAction.Pin => "贴图",
        _ => action.ToString(),
    };
}
