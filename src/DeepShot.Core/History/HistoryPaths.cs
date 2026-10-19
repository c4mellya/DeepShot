using System.Globalization;
using System.IO;
using System.Text;

namespace DeepShot.Core.History;

/// <summary>
/// 历史数据文件布局（T23）：图片放 <c>images/</c>、缩略图放 <c>thumbs/</c>，
/// 数据库里**只存相对路径**，绝对路径由本类按数据根目录换算。
/// </summary>
/// <remarks>
/// <para>
/// 文件命名固定为 <c>{yyyyMMdd-HHmmss-fff}-{guid8}.png</c>：时间在前便于人眼排查，
/// 8 位 GUID 前缀避免同一毫秒内多次截图撞名；扩展名固定 <c>.png</c>
/// （历史内部一律存 PNG，用户另存为时再决定格式）。
/// </para>
/// <para>
/// 全部方法都是纯路径运算，不碰文件系统（<see cref="EnsureUniqueRelativePath"/>
/// 接受"已占用集合"而不是去读目录），所以能在无桌面的单元测试里跑。
/// </para>
/// </remarks>
public static class HistoryPaths
{
    /// <summary>图片子目录名。</summary>
    public const string ImagesFolder = "images";

    /// <summary>缩略图子目录名。</summary>
    public const string ThumbsFolder = "thumbs";

    /// <summary>历史图片扩展名（内部固定 PNG）。</summary>
    public const string ImageExtension = ".png";

    /// <summary>文件名里的时间戳格式。</summary>
    public const string FileTimestampFormat = "yyyyMMdd-HHmmss-fff";

    /// <summary>缩略图文件名的固定后缀（<c>xxx.thumb.png</c>），便于一眼区分。</summary>
    public const string ThumbnailSuffix = ".thumb";

    /// <summary>
    /// 生成图片的相对路径：<c>images/20261018-081500-123-ab12cd34.png</c>。
    /// </summary>
    /// <param name="createdAtLocal">创建时间（本地时间，用于文件名，便于人眼定位）。</param>
    /// <param name="id">记录 Id，取前 8 位十六进制作为防撞名后缀。</param>
    public static string CreateImageRelativePath(DateTime createdAtLocal, Guid id)
    {
        var token = ShortId(id);
        var name = createdAtLocal.ToString(FileTimestampFormat, CultureInfo.InvariantCulture) + "-" + token +
                   ImageExtension;
        return ImagesFolder + "/" + name;
    }

    /// <summary>
    /// 生成对应的缩略图相对路径：<c>images/xxx.png</c> →
    /// <c>thumbs/xxx.thumb.png</c>（去掉 <c>images/</c> 前缀后换目录与后缀）。
    /// </summary>
    /// <param name="relativeImagePath">图片相对路径（<c>images/...</c>）。</param>
    public static string CreateThumbnailRelativePath(string relativeImagePath)
    {
        if (string.IsNullOrWhiteSpace(relativeImagePath))
        {
            throw new ArgumentException("图片相对路径不能为空。", nameof(relativeImagePath));
        }

        var normalized = Normalize(relativeImagePath);
        var fileName = GetFileName(normalized);
        var stem = fileName.EndsWith(ImageExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ImageExtension.Length]
            : fileName;

        return ThumbsFolder + "/" + stem + ThumbnailSuffix + ImageExtension;
    }

    /// <summary>把相对路径换算成绝对路径；数据根目录为空时抛异常。</summary>
    /// <param name="dataRoot">数据根目录（<c>%LOCALAPPDATA%\DeepShot</c>）。</param>
    /// <param name="relativePath">相对路径（可能含 <c>/</c> 或 <c>\</c>）。</param>
    public static string ToAbsolute(string dataRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("数据根目录不能为空。", nameof(dataRoot));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("相对路径不能为空。", nameof(relativePath));
        }

        var parts = Normalize(relativePath).Split('/');
        var combined = dataRoot;
        foreach (var part in parts)
        {
            combined = Path.Combine(combined, part);
        }

        return combined;
    }

    /// <summary>
    /// 校验相对路径只能落在 <c>images/</c> 下：拒绝绝对路径、盘符、<c>..</c> 与反斜杠穿越。
    /// 数据库被手工改坏时，图库不应该去读 <c>C:\Windows\...</c>。
    /// </summary>
    public static bool IsSafeImageRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = Normalize(relativePath);
        if (normalized.StartsWith('/') || normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = normalized.Split('/');
        if (parts.Length < 2 || parts[0] != ImagesFolder)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 如果相对路径已被占用（同一毫秒 + 同一 Id 前缀理论上不会发生，但数据目录
    /// 可能被手工塞了文件），依次追加 <c>-2</c>、<c>-3</c>…
    /// </summary>
    /// <param name="relativePath">期望的相对路径。</param>
    /// <param name="isTaken">判断"这个相对路径是否已存在"的回调（大小写不敏感地比较）。</param>
    public static string EnsureUniqueRelativePath(string relativePath, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("相对路径不能为空。", nameof(relativePath));
        }

        var normalized = Normalize(relativePath);
        if (!isTaken(normalized))
        {
            return normalized;
        }

        var directory = normalized[..(normalized.LastIndexOf('/') + 1)];
        var fileName = GetFileName(normalized);
        var stem = fileName.EndsWith(ImageExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ImageExtension.Length]
            : fileName;

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = directory + stem + "-" + index.ToString(CultureInfo.InvariantCulture) + ImageExtension;
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"无法为 {relativePath} 找到可用的文件名。");
    }

    /// <summary>把 <c>\</c> 统一成 <c>/</c>，去掉首尾分隔符（库里统一用正斜杠）。</summary>
    public static string Normalize(string relativePath)
    {
        var builder = new StringBuilder(relativePath.Length);
        foreach (var ch in relativePath.Trim())
        {
            builder.Append(ch == '\\' ? '/' : ch);
        }

        return builder.ToString().Trim('/');
    }

    /// <summary>取相对路径的文件名部分（<c>images/a.png</c> → <c>a.png</c>）。</summary>
    public static string GetFileName(string relativePath)
    {
        var normalized = Normalize(relativePath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    /// <summary>取 Id 的 8 位小写十六进制（文件名后缀）。</summary>
    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
