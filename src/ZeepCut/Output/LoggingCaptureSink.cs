using System.Globalization;
using ZeepCut.Core.Selection;
using ZeepCut.Platform;

namespace ZeepCut.Output;

/// <summary>
/// T11 的成功记录实现：只写一条含动作类型、尺寸、耗时的 INFO 日志。
/// T23/T24 引入历史后，可改为"日志 + 历史"的组合实现，或由历史实现内部再写日志。
/// </summary>
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
