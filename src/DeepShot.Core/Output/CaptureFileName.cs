using System.Globalization;
using System.IO;
using System.Text;

namespace DeepShot.Core.Output;

/// <summary>
/// 截图文件名的纯逻辑生成：默认名使用本地时间，例如
/// <c>截图_2026-10-18_050000.png</c>；用户自定义名称中的 Windows 非法字符会被替换，
/// 避免"另存为"时因为文件名不可用而失败。
/// </summary>
/// <remarks>
/// 时间戳格式固定为 <c>yyyy-MM-dd_HHmmss</c>（本地时间，24 小时制），扩展名由
/// <see cref="CaptureImageFormat"/> 决定（PNG 为 <c>.png</c>，JPEG 为 <c>.jpg</c>）。
/// 非法字符集合显式列出而不使用 <see cref="Path.GetInvalidFileNameChars"/>，
/// 因为后者在 .NET 的 Unix 实现里只包含 <c>'\0'</c> 与 <c>'/'</c>，会让测试与
/// Windows 行为不一致；本应用只面向 Windows。
/// </remarks>
public static class CaptureFileName
{
    /// <summary>默认名称前缀（本地化文案；资源化留给 T31）。</summary>
    public const string DefaultBaseName = "截图";

    /// <summary>名称中的时间戳格式。</summary>
    public const string TimestampFormat = "yyyy-MM-dd_HHmmss";

    /// <summary>自定义名称部分的最大长度，给时间戳与扩展名留出空间。</summary>
    public const int MaxBaseNameLength = 80;

    /// <summary>Windows 文件名不允许的字符（不含控制字符，控制字符另行判断）。</summary>
    private const string InvalidFileNameChars = "<>:\"/\\|?*";

    /// <summary>格式对应的扩展名（含点号）。</summary>
    public static string GetExtension(CaptureImageFormat format) => format == CaptureImageFormat.Jpeg
        ? ".jpg"
        : ".png";

    /// <summary>
    /// 生成默认文件名：<c>{名称}_{yyyy-MM-dd_HHmmss}{扩展名}</c>。
    /// </summary>
    /// <param name="localTime">本地时间（调用方传 <see cref="DateTime.Now"/>，不在这里取当前时间以便测试）。</param>
    /// <param name="format">输出格式，决定扩展名。</param>
    /// <param name="baseName">名称前缀；为空时使用 <see cref="DefaultBaseName"/>。</param>
    public static string CreateDefault(
        DateTime localTime,
        CaptureImageFormat format = CaptureImageFormat.Png,
        string? baseName = null) =>
        SanitizeBaseName(baseName) +
        "_" +
        localTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) +
        GetExtension(format);

    /// <summary>
    /// 清洗名称前缀：替换 Windows 非法字符与控制字符为 <c>_</c>，
    /// 去掉首尾空白与结尾的点/空格（Windows 不允许文件名以点或空格结尾），
    /// 超长时截断。清洗后为空则回退到 <see cref="DefaultBaseName"/>。
    /// </summary>
    public static string SanitizeBaseName(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return DefaultBaseName;
        }

        var builder = new StringBuilder(baseName.Length);
        foreach (var ch in baseName)
        {
            builder.Append(char.IsControl(ch) || InvalidFileNameChars.Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim();
        sanitized = sanitized.TrimEnd('.', ' ');

        if (sanitized.Length > MaxBaseNameLength)
        {
            sanitized = sanitized[..MaxBaseNameLength].TrimEnd('.', ' ');
        }

        return sanitized.Length == 0 ? DefaultBaseName : sanitized;
    }

    /// <summary>
    /// 从路径扩展名推断输出格式：<c>.png</c> → PNG，<c>.jpg</c>/<c>.jpeg</c> → JPEG（忽略大小写）。
    /// 其它扩展名或没有扩展名时返回 false，由调用方按对话框的过滤器决定。
    /// </summary>
    public static bool TryGetFormatFromExtension(string? path, out CaptureImageFormat format)
    {
        format = CaptureImageFormat.Png;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            format = CaptureImageFormat.Png;
            return true;
        }

        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            format = CaptureImageFormat.Jpeg;
            return true;
        }

        return false;
    }
}
