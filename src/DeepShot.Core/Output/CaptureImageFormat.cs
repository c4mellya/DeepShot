namespace DeepShot.Core.Output;

/// <summary>
/// 截图输出格式。首版只支持 PNG 与 JPEG：
/// PNG 无损且支持透明；JPEG 不支持透明、需要编码质量，因此编码时必须去掉 alpha。
/// </summary>
public enum CaptureImageFormat
{
    /// <summary>PNG（默认），无损，保留透明通道。</summary>
    Png = 0,

    /// <summary>JPEG，有损，不支持透明，按质量等级编码。</summary>
    Jpeg = 1,
}
