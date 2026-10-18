namespace ZeepCut.Output;

/// <summary>结束截图的成功动作类型。取消与失败不属于任何动作，不会产生记录。</summary>
public enum CaptureAction
{
    /// <summary>复制到剪贴板。</summary>
    Copy = 0,

    /// <summary>另存为文件（PNG/JPEG）。</summary>
    SaveAs = 1,

    /// <summary>创建贴图窗口。</summary>
    Pin = 2,
}
