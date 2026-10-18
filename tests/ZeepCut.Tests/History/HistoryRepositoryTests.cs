using System.Text;
using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 每个测试一个独立的临时数据目录（含独立的 history.db），测完删除。
/// </summary>
public sealed class TempHistoryDirectory : IDisposable
{
    public TempHistoryDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "zeepcut-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string DatabasePath => Path.Combine(Root, HistoryRepository.DatabaseFileName);

    public HistoryRepository CreateRepository() => new(Root);

    public void Dispose()
    {
        try
        {
            // SQLite 的 WAL/SHM 文件与图片都要能删掉：这也顺带验证了"没有句柄泄漏"。
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结论（系统临时目录会被回收）。
        }
    }
}

/// <summary>
/// 历史存储层测试（T23）：建库/迁移幂等、增删查改、名称搜索、收藏过滤、
/// 时间倒序、分页、缩略图可选、文件句柄释放。
/// </summary>
public class HistoryRepositoryTests
{
    private static readonly DateTime BaseLocal = new(2026, 10, 18, 8, 15, 0, DateTimeKind.Local);

    /// <summary>一张 1×1 PNG（最小合法文件，足以验证"写进去能读回来"）。</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private static HistoryRecord NewRecord(
        string name,
        DateTime? createdAtLocal = null,
        HistorySource source = HistorySource.Screenshot,
        int width = 640,
        int height = 480)
    {
        var created = createdAtLocal ?? BaseLocal;
        var record = HistoryRecordFactory.Create(name, created, width, height, source);
        return record;
    }

    private static HistoryRepository Initialized(TempHistoryDirectory directory)
    {
        var repository = directory.CreateRepository();
        repository.Initialize();
        return repository;
    }

    [Fact]
    public void Initialize_CreatesDatabaseImagesAndThumbs()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();

        var result = repository.Initialize();

        Assert.True(File.Exists(directory.DatabasePath));
        Assert.True(Directory.Exists(repository.ImagesDirectory));
        Assert.True(Directory.Exists(repository.ThumbsDirectory));
        Assert.True(result.Created);
        Assert.Equal(HistorySchema.CurrentVersion, result.VersionAfter);
        Assert.Equal(HistorySchema.CurrentVersion, repository.GetSchemaVersion());
    }

    [Fact]
    public void Initialize_IsIdempotentAndKeepsData()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var record = repository.Add(NewRecord("第一次"), TinyPng);

        var second = repository.Initialize();
        var third = repository.Initialize();

        Assert.Equal(1, repository.Count());
        Assert.NotNull(repository.GetById(record.Id));
        Assert.False(second.Created);
        Assert.False(third.Created);
        Assert.Equal(0, second.MigratedSteps);
        Assert.Equal(HistorySchema.CurrentVersion, repository.GetSchemaVersion());
    }

    [Fact]
    public void Initialize_OnExistingDatabaseWithData_DoesNotDropRows()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("a"), TinyPng);
        repository.Add(NewRecord("b"), TinyPng);

        // 新建一个仓储实例模拟"应用重启"。
        var reopened = directory.CreateRepository();
        reopened.Initialize();

        Assert.Equal(2, reopened.Count());
    }

    [Fact]
    public void Open_OnDatabaseWithNewerUserVersion_DoesNotDowngrade()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        // 手工把版本抬到未来版本（模拟"用户装过更新的版本又回退"）。
        using (var connection = HistoryRepositoryConnection.Open(directory.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var result = repository.Initialize();

        Assert.True(result.IsNewerThanSupported);
        Assert.Equal(99, result.VersionBefore);
        Assert.Equal(HistorySchema.CurrentVersion, result.VersionAfter);
        Assert.Equal(99, repository.GetSchemaVersion());
    }

    [Fact]
    public void Add_WritesImageFileAndCanReadItBack()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        var stored = repository.Add(NewRecord("截图 A"), TinyPng);
        var path = repository.GetImageAbsolutePath(stored);

        Assert.True(File.Exists(path));
        Assert.True(HistoryPaths.IsSafeImageRelativePath(stored.RelativeImagePath));
        Assert.Equal(TinyPng, File.ReadAllBytes(path));
        Assert.Equal(TinyPng, repository.ReadImageBytes(stored.Id));
    }

    [Fact]
    public void Add_WithoutThumbnailBytes_LeavesThumbnailPathNull()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        var stored = repository.Add(NewRecord("无缩略图"), TinyPng);

        Assert.Null(stored.RelativeThumbnailPath);
        Assert.Null(repository.ReadThumbnailBytes(stored.Id));
        Assert.NotNull(repository.GetById(stored.Id));
    }

    [Fact]
    public void Add_WithThumbnailBytes_WritesThumbFile()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var thumb = Encoding.UTF8.GetBytes("fake-thumb-bytes");

        var stored = repository.Add(NewRecord("有缩略图"), TinyPng, thumb);

        Assert.NotNull(stored.RelativeThumbnailPath);
        Assert.StartsWith("thumbs/", stored.RelativeThumbnailPath);
        Assert.Equal(
            thumb,
            File.ReadAllBytes(HistoryPaths.ToAbsolute(repository.DataRoot, stored.RelativeThumbnailPath!)));
        Assert.Equal(thumb, repository.ReadThumbnailBytes(stored.Id));
    }

    [Fact]
    public void Add_RoundTripsAllMetadata()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var created = new DateTime(2026, 10, 18, 9, 30, 45, 123, DateTimeKind.Local);
        var record = NewRecord("会议截图", created, HistorySource.Clipboard, 300, 200)
            .WithFavorite(true, created.AddMinutes(1));

        var stored = repository.Add(record, TinyPng, Encoding.UTF8.GetBytes("thumb"));
        var loaded = repository.GetById(stored.Id);

        Assert.NotNull(loaded);
        Assert.Equal(record.Id, loaded!.Id);
        Assert.Equal("会议截图", loaded.Name);
        Assert.True(loaded.IsFavorite);
        Assert.Equal(300, loaded.Width);
        Assert.Equal(200, loaded.Height);
        Assert.Equal(HistorySource.Clipboard, loaded.Source);
        Assert.Equal(record.RelativeImagePath, loaded.RelativeImagePath);
        Assert.Equal(stored.RelativeThumbnailPath, loaded.RelativeThumbnailPath);
        Assert.Equal(
            HistoryRecord.NormalizeUtc(created),
            loaded.CreatedAtUtc);
        Assert.Equal(
            HistoryRecord.NormalizeUtc(created.AddMinutes(1)),
            loaded.UpdatedAtUtc);
    }

    [Fact]
    public void Add_RejectsEmptyBytes()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.Throws<HistoryStoreException>(() =>
            repository.Add(NewRecord("空字节"), Array.Empty<byte>()));
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void Add_WhenTwoRecordsShareAPath_TheSecondGetsACounter()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var first = repository.Add(NewRecord("撞名 A"), TinyPng);

        // 一条新记录（不同 Id）但相对路径与已存在的完全相同：写入时必须顺延文件名，
        // 否则会静默覆盖上一条记录的图片。
        var colliding = HistoryRecordFactory.Create("撞名 B", BaseLocal, 640, 480, HistorySource.Screenshot)
            with { RelativeImagePath = first.RelativeImagePath };
        var second = repository.Add(colliding, TinyPng);

        Assert.NotEqual(first.RelativeImagePath, second.RelativeImagePath);
        Assert.EndsWith("-2.png", second.RelativeImagePath);
        Assert.True(File.Exists(HistoryPaths.ToAbsolute(repository.DataRoot, first.RelativeImagePath)));
        Assert.True(File.Exists(HistoryPaths.ToAbsolute(repository.DataRoot, second.RelativeImagePath)));
        Assert.Equal(2, repository.Count());
    }

    [Fact]
    public void Add_WhenGivenPathIsOccupiedByAFile_UsesAnotherName()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var record = NewRecord("占位");
        Directory.CreateDirectory(repository.ImagesDirectory);
        File.WriteAllBytes(
            HistoryPaths.ToAbsolute(repository.DataRoot, record.RelativeImagePath),
            new byte[] { 1, 2, 3 });

        var stored = repository.Add(record, TinyPng);

        Assert.NotEqual(record.RelativeImagePath, stored.RelativeImagePath);
        Assert.EndsWith("-2.png", stored.RelativeImagePath);
        Assert.Equal(TinyPng, File.ReadAllBytes(repository.GetImageAbsolutePath(stored)));
    }

    [Fact]
    public void GetById_ReturnsNullForUnknownId()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.Null(repository.GetById(Guid.NewGuid()));
    }

    [Fact]
    public void GetList_OrdersByCreatedAtDescending()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var first = repository.Add(NewRecord("最早", BaseLocal), TinyPng);
        var second = repository.Add(NewRecord("中间", BaseLocal.AddMinutes(5)), TinyPng);
        var third = repository.Add(NewRecord("最新", BaseLocal.AddMinutes(9)), TinyPng);

        var page = repository.GetList();

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { third.Id, second.Id, first.Id }, page.Items.Select(item => item.Id));
    }

    [Fact]
    public void GetList_AscendingSort_ReversesOrder()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var first = repository.Add(NewRecord("最早", BaseLocal), TinyPng);
        var second = repository.Add(NewRecord("最新", BaseLocal.AddMinutes(5)), TinyPng);

        var page = repository.GetList(new HistoryQuery { Sort = HistorySort.CreatedAtAscending });

        Assert.Equal(new[] { first.Id, second.Id }, page.Items.Select(item => item.Id));
    }

    [Fact]
    public void GetList_SortsByNameAscending()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("b", BaseLocal), TinyPng);
        repository.Add(NewRecord("A", BaseLocal.AddMinutes(1)), TinyPng);
        repository.Add(NewRecord("c", BaseLocal.AddMinutes(2)), TinyPng);

        var page = repository.GetList(new HistoryQuery { Sort = HistorySort.NameAscending });

        Assert.Equal(new[] { "A", "b", "c" }, page.Items.Select(item => item.Name));
    }

    [Fact]
    public void GetList_SearchMatchesNameSubstringIgnoringCase()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("会议截图_1", BaseLocal), TinyPng);
        repository.Add(NewRecord("会议截图_2", BaseLocal.AddMinutes(1)), TinyPng);
        repository.Add(NewRecord("桌面截图", BaseLocal.AddMinutes(2)), TinyPng);
        repository.Add(NewRecord("Poster", BaseLocal.AddMinutes(3)), TinyPng);

        var byName = repository.GetList(new HistoryQuery { SearchText = "会议" });
        var byCase = repository.GetList(new HistoryQuery { SearchText = "POSTER" });
        var noHit = repository.GetList(new HistoryQuery { SearchText = "不存在" });

        Assert.Equal(2, byName.TotalCount);
        Assert.Equal(2, byName.Count);
        Assert.Single(byCase.Items);
        Assert.Equal("Poster", byCase.Items[0].Name);
        Assert.Equal(0, noHit.TotalCount);
        Assert.Empty(noHit.Items);
    }

    [Fact]
    public void GetList_SearchEscapesLikeWildcards()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("进度 100% 完成", BaseLocal), TinyPng);
        repository.Add(NewRecord("进度 100 完成", BaseLocal.AddMinutes(1)), TinyPng);
        repository.Add(NewRecord("另一个 1000%", BaseLocal.AddMinutes(2)), TinyPng);

        var page = repository.GetList(new HistoryQuery { SearchText = "100%" });

        Assert.Single(page.Items);
        Assert.Equal("进度 100% 完成", page.Items[0].Name);
    }

    [Fact]
    public void GetList_FavoriteFilter_ReturnsOnlyMatchingRows()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var favorite = repository.Add(NewRecord("收藏的", BaseLocal), TinyPng);
        repository.Add(NewRecord("普通 1", BaseLocal.AddMinutes(1)), TinyPng);
        repository.Add(NewRecord("普通 2", BaseLocal.AddMinutes(2)), TinyPng);
        repository.SetFavorite(favorite.Id, true, DateTime.UtcNow);

        var favorites = repository.GetList(new HistoryQuery { FavoriteFilter = HistoryFavoriteFilter.FavoritesOnly });
        var ordinary = repository.GetList(new HistoryQuery { FavoriteFilter = HistoryFavoriteFilter.NotFavorite });
        var all = repository.GetList();

        Assert.Single(favorites.Items);
        Assert.Equal(favorite.Id, favorites.Items[0].Id);
        Assert.Equal(2, ordinary.TotalCount);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public void GetList_SearchAndFavoriteFilterCombine()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var target = repository.Add(NewRecord("会议 A", BaseLocal), TinyPng);
        repository.Add(NewRecord("会议 B", BaseLocal.AddMinutes(1)), TinyPng);
        repository.SetFavorite(target.Id, true, DateTime.UtcNow);

        var page = repository.GetList(new HistoryQuery
        {
            SearchText = "会议",
            FavoriteFilter = HistoryFavoriteFilter.FavoritesOnly,
        });

        Assert.Single(page.Items);
        Assert.Equal(target.Id, page.Items[0].Id);
    }

    [Fact]
    public void GetList_PagesWithOffsetAndLimit()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        for (var index = 0; index < 7; index++)
        {
            repository.Add(NewRecord($"记录 {index}", BaseLocal.AddMinutes(index)), TinyPng);
        }

        var first = repository.GetList(new HistoryQuery { Limit = 3 });
        var second = repository.GetList(first.NextPage()!);
        var third = repository.GetList(second.NextPage()!);

        Assert.Equal(7, first.TotalCount);
        Assert.Equal(3, first.Count);
        Assert.True(first.HasMore);
        Assert.Equal(3, second.Count);
        Assert.Equal(1, third.Count);
        Assert.False(third.HasMore);
        Assert.Null(third.NextPage());
        Assert.Equal(7, first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void GetList_ClampsLimitAndOffset()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("唯一", BaseLocal), TinyPng);

        var tooMany = repository.GetList(new HistoryQuery { Limit = int.MaxValue, Offset = -10 });

        Assert.Single(tooMany.Items);
        Assert.Equal(HistoryQuery.MaxLimit, tooMany.Query.NormalizedLimit);
        Assert.Equal(0, tooMany.Query.NormalizedOffset);
    }

    [Fact]
    public void GetList_OnEmptyDatabase_ReturnsEmptyPage()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        var page = repository.GetList();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.False(page.HasMore);
        Assert.Null(page.NextPage());
    }

    [Fact]
    public void Update_PersistsNameFavoriteAndTimestamp()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("原名"), TinyPng);
        var updatedAt = BaseLocal.ToUniversalTime().AddHours(2);

        var renamed = stored.WithName("改名", updatedAt).WithFavorite(true, updatedAt);
        Assert.True(repository.Update(renamed));

        var loaded = repository.GetById(stored.Id);
        Assert.NotNull(loaded);
        Assert.Equal("改名", loaded!.Name);
        Assert.True(loaded.IsFavorite);
        Assert.Equal(HistoryRecord.NormalizeUtc(updatedAt), loaded.UpdatedAtUtc);
        Assert.Equal(stored.CreatedAtUtc, loaded.CreatedAtUtc);
    }

    [Fact]
    public void Update_UnknownIdReturnsFalse()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.False(repository.Update(NewRecord("不存在")));
    }

    [Fact]
    public void SetFavorite_ReturnsUpdatedRecordOrNull()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("收藏我"), TinyPng);

        var favorite = repository.SetFavorite(stored.Id, true, DateTime.UtcNow);
        var missing = repository.SetFavorite(Guid.NewGuid(), true, DateTime.UtcNow);

        Assert.NotNull(favorite);
        Assert.True(favorite!.IsFavorite);
        Assert.True(repository.GetById(stored.Id)!.IsFavorite);
        Assert.Null(missing);
    }

    [Fact]
    public void Delete_RemovesRowAndFiles()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("待删除"), TinyPng, Encoding.UTF8.GetBytes("thumb"));
        var imagePath = repository.GetImageAbsolutePath(stored);
        var thumbPath = HistoryPaths.ToAbsolute(repository.DataRoot, stored.RelativeThumbnailPath!);

        Assert.True(repository.Delete(stored.Id));

        Assert.Null(repository.GetById(stored.Id));
        Assert.False(File.Exists(imagePath));
        Assert.False(File.Exists(thumbPath));
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void Delete_UnknownIdReturnsFalse()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.False(repository.Delete(Guid.NewGuid()));
    }

    [Fact]
    public void Delete_WhenFileAlreadyGone_StillRemovesRow()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("文件已消失"), TinyPng);
        File.Delete(repository.GetImageAbsolutePath(stored));

        Assert.True(repository.Delete(stored.Id));
        Assert.Equal(0, repository.Count());
    }

    [Fact]
    public void UpdateImage_ReplacesFileAndKeepsIdentity()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("要覆盖"), TinyPng);
        var newBytes = Encoding.UTF8.GetBytes("new-image-bytes");
        var path = repository.GetImageAbsolutePath(stored);

        var updated = repository.UpdateImage(stored.Id, newBytes, 800, 600);

        Assert.NotNull(updated);
        Assert.Equal(stored.Id, updated!.Id);
        Assert.Equal(stored.Name, updated.Name);
        Assert.Equal(stored.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(stored.RelativeImagePath, updated.RelativeImagePath);
        Assert.Equal(800, updated.Width);
        Assert.Equal(600, updated.Height);
        Assert.Equal(newBytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void UpdateImage_UnknownIdReturnsNull()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.Null(repository.UpdateImage(Guid.NewGuid(), TinyPng, 1, 1));
    }

    [Fact]
    public void ReadImageBytes_ReportsMissingFile()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("图丢失"), TinyPng);
        File.Delete(repository.GetImageAbsolutePath(stored));

        var ex = Assert.Throws<HistoryStoreException>(() => repository.ReadImageBytes(stored.Id));

        Assert.Contains("图片", ex.Message);
    }

    [Fact]
    public void ReadImageBytes_UnknownIdThrows()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);

        Assert.Throws<HistoryStoreException>(() => repository.ReadImageBytes(Guid.NewGuid()));
    }

    /// <summary>
    /// PLAN 要求"读图注意释放句柄，以便覆盖和清理"：写入/读取之后必须能立刻删除，
    /// 不能因为历史库持有一个 <c>FileStream</c> 而锁住文件。
    /// </summary>
    [Fact]
    public void FilesAreNotLockedAfterAddAndRead()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var thumb = Encoding.UTF8.GetBytes("thumb");
        var stored = repository.Add(NewRecord("句柄检查"), TinyPng, thumb);
        var imagePath = repository.GetImageAbsolutePath(stored);
        var thumbPath = HistoryPaths.ToAbsolute(repository.DataRoot, stored.RelativeThumbnailPath!);

        _ = repository.ReadImageBytes(stored.Id);
        _ = repository.ReadThumbnailBytes(stored.Id);

        // 能从别处打开写（说明没有独占句柄）……
        using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(stream.Length > 0);
        }

        // ……也能直接复制与删除。
        File.Copy(imagePath, imagePath + ".copy", overwrite: true);
        File.Delete(imagePath + ".copy");
        File.Delete(imagePath);
        File.Delete(thumbPath);

        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public void GetAllNames_ReturnsEveryNameNewestFirst()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        repository.Add(NewRecord("旧", BaseLocal), TinyPng);
        repository.Add(NewRecord("新", BaseLocal.AddMinutes(3)), TinyPng);

        var names = repository.GetAllNames();

        Assert.Equal(new[] { "新", "旧" }, names);
    }

    [Fact]
    public void ThumbnailPathNull_IsReadBackAsNull()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("无缩略图"), TinyPng);

        var loaded = repository.GetById(stored.Id)!;

        Assert.Null(loaded.RelativeThumbnailPath);
        Assert.Null(repository.ReadThumbnailBytes(loaded.Id));
    }

    [Fact]
    public void SourceRoundTripsThroughDatabase()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var screenshot = repository.Add(NewRecord("截图来源", BaseLocal, HistorySource.Screenshot), TinyPng);
        var clipboard = repository.Add(NewRecord("剪贴板来源", BaseLocal.AddMinutes(1), HistorySource.Clipboard), TinyPng);
        var history = repository.Add(NewRecord("历史来源", BaseLocal.AddMinutes(2), HistorySource.History), TinyPng);

        Assert.Equal(HistorySource.Screenshot, repository.GetById(screenshot.Id)!.Source);
        Assert.Equal(HistorySource.Clipboard, repository.GetById(clipboard.Id)!.Source);
        Assert.Equal(HistorySource.History, repository.GetById(history.Id)!.Source);
    }

    [Fact]
    public void Repository_SurvivesReopenFromAnotherConnection()
    {
        using var directory = new TempHistoryDirectory();
        var repository = Initialized(directory);
        var stored = repository.Add(NewRecord("跨连接"), TinyPng);

        var second = new HistoryRepository(directory.Root, directory.DatabasePath);
        var page = second.GetList();

        Assert.Single(page.Items);
        Assert.Equal(stored.Id, page.Items[0].Id);
    }

    [Fact]
    public void Constructor_RejectsBlankPaths()
    {
        Assert.Throws<ArgumentException>(() => new HistoryRepository(""));
        Assert.Throws<ArgumentException>(() => new HistoryRepository("/tmp/x", " "));
    }

    [Fact]
    public void DefaultDatabasePathIsUnderDataRoot()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();

        Assert.Equal(Path.Combine(repository.DataRoot, "history.db"), repository.DatabasePath);
        Assert.EndsWith("history.db", repository.DatabasePath);
    }
}
