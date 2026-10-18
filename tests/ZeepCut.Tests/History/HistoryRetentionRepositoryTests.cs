using System.Text;
using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// T27 清理与 T28 覆盖语义的**存储层**测试：真删文件、真改行。
/// </summary>
/// <remarks>
/// 用 <see cref="TempHistoryDirectory"/>（每个测试独立数据目录 + 独立 history.db），
/// 图片字节用任意内容即可 —— 存储层不解析图片。
/// </remarks>
public class HistoryRetentionRepositoryTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] ImageBytes = Encoding.UTF8.GetBytes("old-image-bytes");

    private static readonly byte[] ThumbBytes = Encoding.UTF8.GetBytes("old-thumb-bytes");

    /// <summary>造一条"多少天前创建"的记录（可直接写库：仓储的 Add 用记录里的时间）。</summary>
    private static HistoryRecord AddAged(
        HistoryRepository repository,
        string name,
        TimeSpan age,
        bool favorite = false)
    {
        var createdUtc = NowUtc - age;
        var factory = HistoryRecordFactory.Create(
            name,
            createdUtc.ToLocalTime(),
            640,
            480,
            HistorySource.Screenshot);

        var record = favorite ? factory.WithFavorite(true, createdUtc) : factory;
        var stored = repository.Add(record, ImageBytes, ThumbBytes);
        Assert.Equal(favorite, stored.IsFavorite);
        return stored;
    }

    private static int CountPng(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly).Length
            : 0;

    // --- 清理 ---

    [Fact]
    public void PurgeExpired_DeletesOldKeepsYoungAndFavorite()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();

        var old = AddAged(repository, "31 天前", TimeSpan.FromDays(31));
        var young = AddAged(repository, "29 天前", TimeSpan.FromDays(29));
        var favorite = AddAged(repository, "40 天前收藏", TimeSpan.FromDays(40), favorite: true);

        Assert.Equal(3, repository.Count());
        Assert.Equal(3, CountPng(repository.ImagesDirectory));
        Assert.Equal(3, CountPng(repository.ThumbsDirectory));

        var result = repository.PurgeExpired(NowUtc);

        Assert.Equal(3, result.TotalBefore);
        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(new[] { old.Id }, result.DeletedIds);
        Assert.Empty(result.Failures);

        var remaining = repository.GetAll();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, r => r.Id == old.Id);
        Assert.Contains(remaining, r => r.Id == young.Id);
        Assert.Contains(remaining, r => r.Id == favorite.Id);
        Assert.True(repository.GetById(favorite.Id)!.IsFavorite);

        // 行、图片、缩略图三样同步减少（3 → 2）。
        Assert.Equal(2, CountPng(repository.ImagesDirectory));
        Assert.Equal(2, CountPng(repository.ThumbsDirectory));
        Assert.False(File.Exists(HistoryPaths.ToAbsolute(directory.Root, old.RelativeImagePath)));
        Assert.False(File.Exists(HistoryPaths.ToAbsolute(directory.Root, old.RelativeThumbnailPath!)));
        Assert.True(File.Exists(HistoryPaths.ToAbsolute(directory.Root, young.RelativeImagePath)));
    }

    [Fact]
    public void PurgeExpired_WhenNothingExpired_IsNoOp()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        AddAged(repository, "新记录", TimeSpan.FromDays(1));

        var result = repository.PurgeExpired(NowUtc);

        Assert.Equal(0, result.DeletedCount);
        Assert.Empty(result.Failures);
        Assert.Equal(1, repository.Count());
    }

    [Fact]
    public void PurgeExpired_EmptyDatabase_IsNoOp()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();

        var result = repository.PurgeExpired(NowUtc);

        Assert.True(result.IsEmpty);
        Assert.Equal(0, result.TotalBefore);
        Assert.Equal(0, repository.Count());
    }

    /// <summary>PLAN：清理要能处理文件已缺失的情况（只删行、不报错）。</summary>
    [Fact]
    public void PurgeExpired_MissingFiles_DeletesRowOnly()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var old = AddAged(repository, "文件已丢", TimeSpan.FromDays(45));

        File.Delete(HistoryPaths.ToAbsolute(directory.Root, old.RelativeImagePath));
        File.Delete(HistoryPaths.ToAbsolute(directory.Root, old.RelativeThumbnailPath!));

        var result = repository.PurgeExpired(NowUtc);

        Assert.Equal(1, result.DeletedCount);
        Assert.Empty(result.Failures);
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void PurgeExpired_DeletesExactlyAtThirtyDays()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();

        var boundary = AddAged(repository, "正好 30 天", TimeSpan.FromDays(30));
        var justInside = AddAged(
            repository,
            "29 天 23 小时",
            TimeSpan.FromDays(29) + TimeSpan.FromHours(23));

        var result = repository.PurgeExpired(NowUtc);

        Assert.Equal(new[] { boundary.Id }, result.DeletedIds);
        Assert.Null(repository.GetById(boundary.Id));
        Assert.NotNull(repository.GetById(justInside.Id));
    }

    /// <summary>编辑/重命名不延期：40 天前创建、刚刚改名过 → 仍然被清理。</summary>
    [Fact]
    public void PurgeExpired_RecentlyRenamedOldRecord_StillDeleted()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var old = AddAged(repository, "老记录", TimeSpan.FromDays(40));

        var renamed = old.WithName("刚改过的老记录", NowUtc - TimeSpan.FromMinutes(1));
        Assert.True(repository.Update(renamed));
        Assert.NotEqual(old.UpdatedAtUtc, renamed.UpdatedAtUtc);

        var result = repository.PurgeExpired(NowUtc);

        Assert.Equal(new[] { old.Id }, result.DeletedIds);
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void PurgeExpired_CustomRetentionDays_IsRespected()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        AddAged(repository, "8 天前", TimeSpan.FromDays(8));

        Assert.Equal(0, repository.PurgeExpired(NowUtc, retentionDays: 30).DeletedCount);
        Assert.Equal(1, repository.PurgeExpired(NowUtc, retentionDays: 7).DeletedCount);
    }

    /// <summary>PLAN：自动清理只删内部历史；用户另存为到数据目录外的文件不受影响。</summary>
    [Fact]
    public void PurgeExpired_DoesNotTouchFilesOutsideDataRoot()
    {
        using var directory = new TempHistoryDirectory();
        using var elsewhere = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        AddAged(repository, "31 天前", TimeSpan.FromDays(31));

        // 模拟用户"另存为"：把同一张图存到另一个目录。
        var userFile = Path.Combine(elsewhere.Root, "用户的另存为.png");
        File.WriteAllBytes(userFile, ImageBytes);

        repository.PurgeExpired(NowUtc);

        Assert.True(File.Exists(userFile));
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void PurgeExpired_ResultToString_DescribesCounts()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        AddAged(repository, "31 天前", TimeSpan.FromDays(31));

        var text = repository.PurgeExpired(NowUtc).ToString();

        Assert.Contains("已删除=1", text);
        Assert.Contains("保留=30", text);
    }

    // --- 覆盖语义（T28） ---

    [Fact]
    public void UpdateImage_KeepsIdNameCreatedFavorite_AndChangesUpdatedAndThumb()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var created = NowUtc - TimeSpan.FromDays(3);
        var record = HistoryRecordFactory
            .Create("覆盖前", created.ToLocalTime(), 640, 480, HistorySource.Screenshot)
            .WithFavorite(true, created);
        var stored = repository.Add(record, ImageBytes, ThumbBytes);

        var newImage = Encoding.UTF8.GetBytes("new-image-with-annotation");
        var newThumb = Encoding.UTF8.GetBytes("new-thumb");
        var updatedAt = NowUtc;
        var countBefore = repository.Count();

        var updated = repository.UpdateImage(stored.Id, newImage, 800, 600, newThumb, updatedAt);

        Assert.NotNull(updated);
        Assert.Equal(stored.Id, updated!.Id);
        Assert.Equal("覆盖前", updated.Name);
        Assert.Equal(stored.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.IsFavorite);
        Assert.Equal(stored.RelativeImagePath, updated.RelativeImagePath);
        Assert.Equal(stored.RelativeThumbnailPath, updated.RelativeThumbnailPath);
        Assert.Equal(800, updated.Width);
        Assert.Equal(600, updated.Height);
        Assert.Equal(HistoryRecord.NormalizeUtc(updatedAt), updated.UpdatedAtUtc);
        Assert.Equal(countBefore, repository.Count());
        Assert.Equal(newImage, repository.ReadImageBytes(stored.Id));
        Assert.Equal(newThumb, repository.ReadThumbnailBytes(stored.Id));

        // 文件数不变（原地覆盖同一个文件，不留 .tmp、不留孤儿）。
        Assert.Equal(1, CountPng(repository.ImagesDirectory));
        Assert.Equal(1, CountPng(repository.ThumbsDirectory));
        var imagePath = repository.GetImageAbsolutePath(stored);
        Assert.False(File.Exists(imagePath + ".tmp"));
    }

    [Fact]
    public void UpdateImage_UnknownId_ReturnsNullWithoutCreatingFiles()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();

        Assert.Null(repository.UpdateImage(Guid.NewGuid(), Encoding.UTF8.GetBytes("x"), 10, 10));
        Assert.Equal(0, CountPng(repository.ImagesDirectory));
    }

    [Fact]
    public void UpdateImage_EmptyBytes_ThrowsAndKeepsOriginal()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var stored = repository.Add(
            HistoryRecordFactory.Create("原图", NowUtc.ToLocalTime(), 10, 10, HistorySource.Screenshot),
            ImageBytes,
            ThumbBytes);

        Assert.Throws<HistoryStoreException>(() => repository.UpdateImage(stored.Id, Array.Empty<byte>(), 1, 1));
        Assert.Equal(ImageBytes, repository.ReadImageBytes(stored.Id));
    }

    /// <summary>
    /// PLAN：保存失败不先销毁原图。制造失败的方式：在 <c>xxx.png.tmp</c> 上先建一个目录，
    /// 于是写临时文件必然失败 → 原图与元数据都必须保持原样。
    /// </summary>
    [Fact]
    public void UpdateImage_WhenTemporaryWriteFails_OriginalStaysIntact()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var stored = repository.Add(
            HistoryRecordFactory.Create("原图", NowUtc.ToLocalTime(), 10, 10, HistorySource.Screenshot),
            ImageBytes,
            ThumbBytes);

        var imagePath = repository.GetImageAbsolutePath(stored);
        Directory.CreateDirectory(imagePath + ".tmp");

        try
        {
            Assert.Throws<HistoryStoreException>(
                () => repository.UpdateImage(stored.Id, Encoding.UTF8.GetBytes("new"), 10, 10));

            Assert.Equal(ImageBytes, File.ReadAllBytes(imagePath));
            Assert.Equal(ImageBytes, repository.ReadImageBytes(stored.Id));
            Assert.Equal(stored.Width, repository.GetById(stored.Id)!.Width);
            Assert.Equal(stored.UpdatedAtUtc, repository.GetById(stored.Id)!.UpdatedAtUtc);
        }
        finally
        {
            Directory.Delete(imagePath + ".tmp");
        }
    }

    [Fact]
    public void UpdateImage_WithoutThumbnailBytes_KeepsExistingThumbnail()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var stored = repository.Add(
            HistoryRecordFactory.Create("带缩略图", NowUtc.ToLocalTime(), 10, 10, HistorySource.Screenshot),
            ImageBytes,
            ThumbBytes);

        var updated = repository.UpdateImage(stored.Id, Encoding.UTF8.GetBytes("new"), 20, 20);

        Assert.NotNull(updated);
        Assert.Equal(stored.RelativeThumbnailPath, updated!.RelativeThumbnailPath);
        Assert.Equal(ThumbBytes, repository.ReadThumbnailBytes(stored.Id));
    }

    [Fact]
    public void Delete_AfterOverwrite_RemovesTheOverwrittenImage()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var stored = repository.Add(
            HistoryRecordFactory.Create("覆盖后删除", NowUtc.ToLocalTime(), 10, 10, HistorySource.Screenshot),
            ImageBytes,
            ThumbBytes);
        repository.UpdateImage(stored.Id, Encoding.UTF8.GetBytes("new"), 20, 20, ThumbBytes);

        Assert.True(repository.Delete(stored.Id));

        Assert.Equal(0, repository.Count());
        Assert.Equal(0, CountPng(repository.ImagesDirectory));
        Assert.Equal(0, CountPng(repository.ThumbsDirectory));
    }
}
