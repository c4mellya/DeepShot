using System.Windows.Media.Imaging;
using DeepShot.Core.Geometry;

namespace DeepShot.Output;

/// <summary>
/// 一次**成功**的截图输出（复制 / 另存为 / 贴图）。取消与失败不会产生本记录。
/// </summary>
/// <remarks>
/// 这是 T23/T24 接入历史的接缝：届时由 <see cref="ICaptureSink"/> 的实现把
/// <see cref="Image"/> 写入 <c>images/</c>、生成缩略图并写入 SQLite 元数据；
/// 遮罩层本身不需要知道历史的存在。
/// </remarks>
/// <param name="Action">成功的动作类型。</param>
/// <param name="Image">裁剪后的输出图（物理像素，已冻结）。</param>
/// <param name="Selection">屏幕绝对物理像素选区。</param>
/// <param name="ScreenBounds">目标屏幕的物理矩形（副屏可为负坐标）。</param>
/// <param name="FilePath">另存为的目标文件；复制/贴图时为 null。</param>
/// <param name="CompletedAtLocal">完成时的本地时间（历史默认名称使用它）。</param>
/// <param name="Elapsed">从动作开始到成功的耗时。</param>
public sealed record CaptureCompletion(
    CaptureAction Action,
    BitmapSource Image,
    PixelRect Selection,
    PixelRect ScreenBounds,
    string? FilePath,
    DateTime CompletedAtLocal,
    TimeSpan Elapsed)
{
    /// <summary>输出图宽度（物理像素）。</summary>
    public int PixelWidth => Image.PixelWidth;

    /// <summary>输出图高度（物理像素）。</summary>
    public int PixelHeight => Image.PixelHeight;
}
