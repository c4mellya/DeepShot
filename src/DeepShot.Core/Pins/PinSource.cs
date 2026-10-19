namespace DeepShot.Core.Pins;

/// <summary>
/// 贴图的来源（T22 历史联动用）。
/// </summary>
/// <remarks>
/// 来源决定"要不要新增一条历史"：
/// <list type="bullet">
/// <item><see cref="Screenshot"/>：内部截图后点“贴图”，已经由 T11 的 <c>ICaptureSink</c>
/// 记过一次，**贴图本身不再记录**（否则一次操作两条历史）。</item>
/// <item><see cref="Clipboard"/>：外部剪贴板图片贴图，没有任何截图动作记录过它，
/// 所以**记录一次**（T24 接真实历史，本轮是日志占位）。</item>
/// <item><see cref="History"/>：从历史图库再贴图，原记录已经存在，
/// **不新增记录**（历史包含它自己，贴图只是又一次显示）。</item>
/// </list>
/// </remarks>
public enum PinSource
{
    /// <summary>外部剪贴板图片（Alt+S / 托盘“从剪贴板贴图”）。</summary>
    Clipboard = 0,

    /// <summary>历史图库中的既有记录（T25/T28 接入；本轮只有接缝）。</summary>
    History = 1,

    /// <summary>截图遮罩里的“贴图”按钮（内部来源，历史已由 ICaptureSink 记录）。</summary>
    Screenshot = 2,
}

/// <summary><see cref="PinSource"/> 的中文描述，供日志与提示复用。</summary>
public static class PinSourceText
{
    public static string Describe(PinSource source) => source switch
    {
        PinSource.Clipboard => "外部剪贴板",
        PinSource.History => "历史记录",
        PinSource.Screenshot => "内部截图",
        _ => source.ToString(),
    };

    /// <summary>该来源的贴图是否应该新增一条历史记录（T22 规则）。</summary>
    public static bool ShouldRecordHistory(PinSource source) => source == PinSource.Clipboard;
}
