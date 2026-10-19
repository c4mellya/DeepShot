namespace DeepShot.Output;

/// <summary>
/// 截图成功输出的接收端（T24 历史写入的接缝）。
/// </summary>
/// <remarks>
/// 遮罩窗口只在复制/另存为/贴图**成功后**调用一次；取消、失败、取消保存对话框都不会调用。
/// T11 只有 <see cref="LoggingCaptureSink"/>（写一条完成日志）；T23/T24 时可替换成
/// 写图片文件 + SQLite 历史的实现，而不需要改动遮罩的任何逻辑。
/// </remarks>
public interface ICaptureSink
{
    /// <summary>记录一次成功的截图输出。实现不应抛异常影响截图收尾（调用方会兜底）。</summary>
    void OnCaptureCompleted(CaptureCompletion completion);
}
