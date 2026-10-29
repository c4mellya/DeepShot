using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using DeepShot.Core.History;
using DeepShot.Platform;

namespace DeepShot.History;

/// <summary>
/// 应用层的历史写入口（T23/T24）：把一张 <see cref="BitmapSource"/> 变成
/// "PNG 文件 + 缩略图 + SQLite 记录"。
/// </summary>
/// <remarks>
/// <para>
/// 分成两半：Core 的 <see cref="HistoryRepository"/> 负责文件与数据库（可单测），
/// 本类只负责"像素 → PNG 字节 / 缩略图字节"这段 WPF 编解码。这样存储层的测试
/// 不需要图像库，图像编码也能被真实桌面流程验证。
/// </para>
/// <para>
/// <b>失败语义</b>：本类不吞异常 —— 它把 <see cref="HistoryStoreException"/> 交给
/// 调用方（<c>HistoryCaptureSink</c>），由调用方写日志并提示"已复制，但历史保存失败"。
/// 复制/保存/贴图本身必须已经成功，历史失败不能反悔主操作。
/// </para>
/// <para>
/// <b>句柄</b>：<see cref="BitmapSource"/> 由调用方持有（截图输出图/贴图内容），
/// 本类只读它；写文件走仓储的短生命周期 <c>FileStream</c>。因此写完立刻可以删除这些文件。
/// </para>
/// </remarks>
public sealed class HistoryStore
{
    /// <summary>预览缓存容量：约一页卡片（每张缩略图最长边 ≤ 640px，约 1MB）。</summary>
    private const int PreviewCacheCapacity = 96;

    private readonly HistoryRepository _repository;
    private readonly object _previewCacheLock = new();
    private readonly object _upgradeLock = new();
    private readonly Dictionary<Guid, LinkedListNode<PreviewCacheEntry>> _previewCache = new();
    private readonly LinkedList<PreviewCacheEntry> _previewLru = new();

    private sealed record PreviewCacheEntry(Guid Id, DateTime UpdatedAtUtc, BitmapSource Image);

    public HistoryStore(HistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>底层仓储（图库 T25 直接用它读列表/读图片）。</summary>
    public HistoryRepository Repository => _repository;

    /// <summary>
    /// 新增一条历史：编码 PNG、生成缩略图、写文件、入库。
    /// </summary>
    /// <param name="image">输出图（已冻结，物理像素）。</param>
    /// <param name="name">显示名称；为空时按来源生成默认名称。</param>
    /// <param name="createdAtLocal">创建时间（本地时间）。</param>
    /// <param name="source">来源。</param>
    /// <param name="createThumbnail">是否生成缩略图（失败时不阻塞记录，缩略图路径置空）。</param>
    /// <returns>真正入库的记录。</returns>
    public HistoryRecord Add(
        BitmapSource image,
        string? name,
        DateTime createdAtLocal,
        HistorySource source,
        bool createThumbnail = true)
    {
        ArgumentNullException.ThrowIfNull(image);

        var record = HistoryRecordFactory.Create(
            name,
            createdAtLocal,
            image.PixelWidth,
            image.PixelHeight,
            source);

        var imageBytes = HistoryThumbnail.EncodePng(image);
        var thumbnailBytes = createThumbnail ? TryCreateThumbnailBytes(image) : null;

        var stored = _repository.Add(record, imageBytes, thumbnailBytes);

        AppLog.Info(
            "History",
            $"历史写入完成：名称={stored.Name} 来源={HistorySourceText.Describe(stored.Source)} " +
            $"尺寸={stored.Width}x{stored.Height} 图片={stored.RelativeImagePath} " +
            $"缩略图={stored.RelativeThumbnailPath ?? "-"} " +
            $"图片字节={imageBytes.Length} 缩略图字节={thumbnailBytes?.Length ?? 0} " +
            $"时间={stored.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

        return stored;
    }

    /// <summary>
    /// 生成缩略图字节；失败返回 null（记录仍然写入，图库回退到原图或占位）。
    /// 缩略图失败只是"不够快"，不该让整条历史失败。
    /// </summary>
    private static byte[]? TryCreateThumbnailBytes(BitmapSource image)
    {
        try
        {
            return HistoryThumbnail.CreatePngBytes(image);
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"缩略图生成失败（仍写入历史，缩略图置空）：{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 按相对路径读成图像（图库预览用）。必须用
    /// <see cref="BitmapCacheOption.OnLoad"/>：读完立刻关闭文件，否则删除/覆盖会被锁。
    /// </summary>
    /// <param name="relativePath">相对数据根目录的路径（<c>images/...</c> 或 <c>thumbs/...</c>）。</param>
    /// <returns>已冻结的位图；文件不存在或解码失败返回 null。</returns>
    public BitmapSource? TryLoad(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            var absolute = HistoryPaths.ToAbsolute(_repository.DataRoot, relativePath);
            if (!File.Exists(absolute))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // 读完即释放文件句柄
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(absolute, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"读取历史图片失败：{relativePath}（{ex.GetType().Name}: {ex.Message}）");
            return null;
        }
    }

    /// <summary>
    /// 一条记录的预览图：优先缩略图，读不到时回退原图（缩略图可能为空或丢失）。
    /// 命中内存缓存时不碰磁盘；会阻塞调用线程，图库列表请用 <see cref="LoadPreviewAsync"/>。
    /// </summary>
    public BitmapSource? TryLoadPreview(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (TryGetCachedPreview(record) is { } cached)
        {
            return cached;
        }

        var preview = LoadPreviewUncached(record);
        if (preview is not null)
        {
            CachePreview(record, preview);
        }

        return preview;
    }

    /// <summary>
    /// 在线程池里读预览图并返回已冻结的位图：解码和（旧缩略图的）升级都不占 UI 线程。
    /// 取消只影响还没开始解码的读取。
    /// </summary>
    public Task<BitmapSource?> LoadPreviewAsync(HistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return TryGetCachedPreview(record) is { } cached
            ? Task.FromResult<BitmapSource?>(cached)
            : Task.Run(() => TryLoadPreview(record), cancellationToken);
    }

    /// <summary>
    /// 只查内存缓存（不读磁盘）。键是 Id + 修改时间：覆盖编辑 / 改名 / 收藏都会刷新修改时间，旧图自然失效。
    /// </summary>
    public BitmapSource? TryGetCachedPreview(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_previewCacheLock)
        {
            if (!_previewCache.TryGetValue(record.Id, out var node))
            {
                return null;
            }

            if (node.Value.UpdatedAtUtc != record.UpdatedAtUtc)
            {
                _previewLru.Remove(node);
                _previewCache.Remove(record.Id);
                return null;
            }

            _previewLru.Remove(node);
            _previewLru.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <summary>清空预览缓存（图库关闭时调用，托盘常驻期间不占内存）。</summary>
    public void ClearPreviewCache()
    {
        lock (_previewCacheLock)
        {
            _previewCache.Clear();
            _previewLru.Clear();
        }
    }

    private void CachePreview(HistoryRecord record, BitmapSource image)
    {
        // 缩略图兜底回退到原图时可能是整张 4K 位图，不进缓存。
        if (Math.Max(image.PixelWidth, image.PixelHeight) > HistoryThumbnail.MaxEdge)
        {
            return;
        }

        lock (_previewCacheLock)
        {
            if (_previewCache.Remove(record.Id, out var old))
            {
                _previewLru.Remove(old);
            }

            _previewCache[record.Id] = _previewLru.AddFirst(new PreviewCacheEntry(record.Id, record.UpdatedAtUtc, image));

            while (_previewLru.Count > PreviewCacheCapacity && _previewLru.Last is { } last)
            {
                _previewLru.RemoveLast();
                _previewCache.Remove(last.Value.Id);
            }
        }
    }

    private BitmapSource? LoadPreviewUncached(HistoryRecord record)
    {
        var thumbnail = TryLoad(record.RelativeThumbnailPath);
        if (thumbnail is not null && !NeedsSharperThumbnail(record, thumbnail))
        {
            return thumbnail;
        }

        // 旧版本只存 320px 的缩略图，卡片里被放大就发糊：按需用原图重做一份（每条记录只做一次）。
        // 图库并发读取时同一条记录可能被两个线程同时撞到这里：串行化，并在拿到锁后重读一次，避免重复升级和 .tmp 争用。
        lock (_upgradeLock)
        {
            var current = TryLoad(record.RelativeThumbnailPath);
            if (current is not null && !NeedsSharperThumbnail(record, current))
            {
                return current;
            }

            return TryUpgradeThumbnail(record) ?? current ?? thumbnail ?? TryLoad(record.RelativeImagePath);
        }
    }

    private static bool NeedsSharperThumbnail(HistoryRecord record, BitmapSource thumbnail)
    {
        var thumbEdge = Math.Max(thumbnail.PixelWidth, thumbnail.PixelHeight);
        var originalEdge = Math.Max(record.Width, record.Height);
        return thumbEdge < HistoryThumbnail.MaxEdge && originalEdge > thumbEdge;
    }

    private BitmapSource? TryUpgradeThumbnail(HistoryRecord record)
    {
        try
        {
            var original = TryLoad(record.RelativeImagePath);
            if (original is null)
            {
                return null;
            }

            var scaled = HistoryThumbnail.Scale(original);
            if (!string.IsNullOrWhiteSpace(record.RelativeThumbnailPath))
            {
                var absolute = HistoryPaths.ToAbsolute(_repository.DataRoot, record.RelativeThumbnailPath);
                var temp = absolute + ".tmp";
                File.WriteAllBytes(temp, HistoryThumbnail.EncodePng(scaled));
                File.Move(temp, absolute, overwrite: true);
                AppLog.Info("History", $"缩略图已升级为 {scaled.PixelWidth}x{scaled.PixelHeight}：{record.RelativeThumbnailPath}");
            }

            return scaled;
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"升级缩略图失败（继续用旧缩略图）：{record.RelativeThumbnailPath}（{ex.GetType().Name}: {ex.Message}）");
            return null;
        }
    }

    /// <summary>图库列表页（T25 用；本轮只提供入口，没有 UI）。</summary>
    public HistoryPage GetPage(HistoryQuery? query = null) => _repository.GetList(query);

    /// <summary>
    /// 日期索引（图库右侧时间轴 / 日期跳转用）：按本地日期给出每天的条数与偏移。
    /// 直接转发给 <see cref="HistoryRepository.GetDateIndex"/>，不做额外缓存 ——
    /// 索引很小（每天一行），每次刷新重新算比维护失效逻辑更不容易错。
    /// </summary>
    public HistoryDateIndex GetDateIndex(HistoryQuery? query = null) => _repository.GetDateIndex(query);

    /// <summary>记录总数（启动日志/清理统计用）。</summary>
    public int Count() => _repository.Count();
}
