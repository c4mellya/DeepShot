using System.Globalization;
using DeepShot.Core.Output;

namespace DeepShot.Core.History;

/// <summary>
/// 一条历史记录的元数据（T23）。对应 SQLite <c>history</c> 表的一行。
/// </summary>
/// <remarks>
/// <para>
/// <b>路径一律是相对数据根目录的路径</b>（<c>images/xxx.png</c>、<c>thumbs/xxx.png</c>），
/// 由 <see cref="HistoryPaths"/> 换算成绝对路径。这样数据目录可以整体搬走，
/// 也不会把 <c>C:\Users\...</c> 写进数据库。
/// </para>
/// <para>
/// 这是不可变的 <c>record</c>：改名/收藏/覆盖编辑都产生新实例（见
/// <see cref="WithName"/>、<see cref="WithFavorite"/>、<see cref="WithImage"/>），
/// 便于测试与 UI 绑定，避免半更新状态。
/// </para>
/// </remarks>
public sealed record HistoryRecord
{
    /// <summary>数据库里存储的时间格式（ISO-8601，UTC，带 <c>Z</c>）。</summary>
    public const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>必需参数：Id、名称、时间、图片相对路径、图片尺寸、来源。</summary>
    public HistoryRecord(
        Guid id,
        string name,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        bool isFavorite,
        string relativeImagePath,
        string? relativeThumbnailPath,
        int width,
        int height,
        HistorySource source)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("历史记录 Id 不能是空 Guid。", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(relativeImagePath))
        {
            throw new ArgumentException("历史记录的图片相对路径不能为空。", nameof(relativeImagePath));
        }

        Id = id;
        Name = HistoryName.Sanitize(name);
        CreatedAtUtc = NormalizeUtc(createdAtUtc);
        UpdatedAtUtc = NormalizeUtc(updatedAtUtc);
        IsFavorite = isFavorite;
        RelativeImagePath = relativeImagePath.Trim();
        RelativeThumbnailPath = string.IsNullOrWhiteSpace(relativeThumbnailPath)
            ? null
            : relativeThumbnailPath.Trim();
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Source = source;
    }

    /// <summary>记录 Id（GUID，主键）。</summary>
    public Guid Id { get; init; }

    /// <summary>显示名称（已清洗，可直接显示/搜索；不含扩展名）。</summary>
    public string Name { get; init; }

    /// <summary>创建时间（UTC，毫秒精度）。30 天清理按它计算。</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>最近修改时间（UTC）。编辑与重命名会更新它，但**不延长 30 天期限**。</summary>
    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>是否收藏（收藏豁免自动清理）。</summary>
    public bool IsFavorite { get; init; }

    /// <summary>图片相对路径（相对数据根目录，如 <c>images/20261018-081500-123-ab12cd34.png</c>）。</summary>
    public string RelativeImagePath { get; init; }

    /// <summary>缩略图相对路径；缩略图生成失败时为 null（图库要能容忍）。</summary>
    public string? RelativeThumbnailPath { get; init; }

    /// <summary>图片宽度（物理像素）。</summary>
    public int Width { get; init; }

    /// <summary>图片高度（物理像素）。</summary>
    public int Height { get; init; }

    /// <summary>来源。</summary>
    public HistorySource Source { get; init; }

    /// <summary>图片期望的像素数（宽 × 高），覆盖保存时用来校验新文件。</summary>
    public long PixelCount => (long)Width * Height;

    /// <summary>创建时间的本地时间（界面显示用）。</summary>
    public DateTime CreatedAtLocal => CreatedAtUtc.ToLocalTime();

    /// <summary>修改时间的本地时间（界面显示用）。</summary>
    public DateTime UpdatedAtLocal => UpdatedAtUtc.ToLocalTime();

    /// <summary>
    /// 生成默认名称：<c>截图_yyyy-MM-dd_HHmmss</c>（复用 T11 的 <see cref="CaptureFileName"/>，
    /// 去掉扩展名 —— 历史名称不带扩展名，扩展名由图片文件决定）。
    /// </summary>
    /// <param name="localTime">本地时间（调用方传入，便于测试）。</param>
    /// <param name="baseName">名称前缀；为空时用 <c>截图</c>。</param>
    public static string CreateDefaultName(DateTime localTime, string? baseName = null)
    {
        var withExtension = CaptureFileName.CreateDefault(localTime, CaptureImageFormat.Png, baseName);
        var dot = withExtension.LastIndexOf('.');
        return dot > 0 ? withExtension[..dot] : withExtension;
    }

    /// <summary>改名（同时刷新 <see cref="UpdatedAtUtc"/>；30 天期限仍按创建时间）。</summary>
    public HistoryRecord WithName(string name, DateTime updatedAtUtc) =>
        this with { Name = HistoryName.Sanitize(name), UpdatedAtUtc = NormalizeUtc(updatedAtUtc) };

    /// <summary>切换收藏（不改变图片；刷新修改时间）。</summary>
    public HistoryRecord WithFavorite(bool favorite, DateTime updatedAtUtc) =>
        this with { IsFavorite = favorite, UpdatedAtUtc = NormalizeUtc(updatedAtUtc) };

    /// <summary>覆盖编辑（T28）：换图片与尺寸，保留 Id / 名称 / 创建时间 / 收藏。</summary>
    public HistoryRecord WithImage(
        string relativeImagePath,
        string? relativeThumbnailPath,
        int width,
        int height,
        DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(relativeImagePath))
        {
            throw new ArgumentException("历史记录的图片相对路径不能为空。", nameof(relativeImagePath));
        }

        return this with
        {
            RelativeImagePath = relativeImagePath.Trim(),
            RelativeThumbnailPath = string.IsNullOrWhiteSpace(relativeThumbnailPath)
                ? null
                : relativeThumbnailPath.Trim(),
            Width = Math.Max(0, width),
            Height = Math.Max(0, height),
            UpdatedAtUtc = NormalizeUtc(updatedAtUtc),
        };
    }

    /// <summary>把时间归一成 UTC 毫米精度（库里只存到这个精度，避免"写进去读回来不相等"）。</summary>
    public static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        // SQLite 的字符串里只有 3 位小数；先截断，保证 record 相等性比较与库里一致。
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    /// <summary>格式化成库里用的 ISO-8601 UTC 文本。</summary>
    public static string FormatUtc(DateTime value) =>
        NormalizeUtc(value).ToString(UtcFormat, CultureInfo.InvariantCulture);

    /// <summary>解析库里的 ISO-8601 文本；失败返回 false（调用方决定回退策略）。</summary>
    public static bool TryParseUtc(string? text, out DateTime utc)
    {
        utc = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!DateTime.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return false;
        }

        utc = NormalizeUtc(parsed);
        return true;
    }

    public override string ToString() =>
        $"{Name} [{Source}] {Width}x{Height} 收藏={IsFavorite} 创建={FormatUtc(CreatedAtUtc)}";
}
