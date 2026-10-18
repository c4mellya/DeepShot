using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// T28 覆盖语义的纯逻辑测试：保存时用哪一版记录作基底、编辑期间被改名/收藏/删除时的行为、
/// 面向用户的文案（"保存并覆盖""没有新增记录""首次解释不保留旧版本"）。
/// </summary>
public class HistoryOverwritePlanTests
{
    private static readonly DateTime NowUtc = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static HistoryRecord Record(
        string name = "截图_原记录",
        bool favorite = false,
        Guid? id = null,
        int width = 640,
        int height = 480,
        DateTime? createdUtc = null)
    {
        var created = createdUtc ?? NowUtc - TimeSpan.FromDays(3);
        var recordId = id ?? Guid.NewGuid();
        return new HistoryRecord(
            recordId,
            name,
            created,
            created,
            favorite,
            HistoryPaths.CreateImageRelativePath(created.ToLocalTime(), recordId),
            HistoryPaths.CreateThumbnailRelativePath(
                HistoryPaths.CreateImageRelativePath(created.ToLocalTime(), recordId)),
            width,
            height,
            HistorySource.Screenshot);
    }

    // --- 基底选择：保留身份字段 ---

    [Fact]
    public void MergeLatest_KeepsLatestNameAndFavorite()
    {
        var original = Record("旧名称", favorite: false);
        var latest = original.WithName("图库里刚改的名字", NowUtc).WithFavorite(true, NowUtc);

        var merged = HistoryOverwritePlan.MergeLatest(original, latest);

        Assert.Equal(original.Id, merged.Id);
        Assert.Equal("图库里刚改的名字", merged.Name);
        Assert.True(merged.IsFavorite);
        Assert.Equal(original.CreatedAtUtc, merged.CreatedAtUtc);
    }

    [Fact]
    public void MergeLatest_RejectsMismatchedIds()
    {
        var a = Record();
        var b = Record();

        Assert.Throws<ArgumentException>(() => HistoryOverwritePlan.MergeLatest(a, b));
    }

    [Fact]
    public void MergeLatest_Null_Throws()
    {
        var a = Record();

        Assert.Throws<ArgumentNullException>(() => HistoryOverwritePlan.MergeLatest(null!, a));
        Assert.Throws<ArgumentNullException>(() => HistoryOverwritePlan.MergeLatest(a, null!));
    }

    /// <summary>真正的写入落点是 <see cref="HistoryRecord.WithImage"/>：它必须保留身份字段。</summary>
    [Fact]
    public void WithImage_PreservesIdentityAndOnlyChangesImageFields()
    {
        var original = Record("保留我", favorite: true);
        var newImagePath = "images/20261018-120000-000-deadbeef.png";
        var newThumbPath = "thumbs/20261018-120000-000-deadbeef.thumb.png";

        var overwritten = original.WithImage(newImagePath, newThumbPath, 800, 600, NowUtc);

        Assert.Equal(original.Id, overwritten.Id);
        Assert.Equal("保留我", overwritten.Name);
        Assert.Equal(original.CreatedAtUtc, overwritten.CreatedAtUtc);
        Assert.True(overwritten.IsFavorite);
        Assert.Equal(original.Source, overwritten.Source);
        Assert.Equal(newImagePath, overwritten.RelativeImagePath);
        Assert.Equal(newThumbPath, overwritten.RelativeThumbnailPath);
        Assert.Equal(800, overwritten.Width);
        Assert.Equal(600, overwritten.Height);
        Assert.Equal(HistoryRecord.NormalizeUtc(NowUtc), overwritten.UpdatedAtUtc);
    }

    /// <summary>覆盖编辑仍按创建时间算 30 天（T27）：改了图也不会重新活 30 天。</summary>
    [Fact]
    public void WithImage_DoesNotExtendRetention()
    {
        var created = NowUtc - TimeSpan.FromDays(40);
        var old = Record(createdUtc: created, favorite: true);
        var overwritten = old.WithImage("images/x.png", null, 640, 480, NowUtc);

        Assert.Equal(old.CreatedAtUtc, overwritten.CreatedAtUtc);
        Assert.True(HistoryRetentionPolicy.IsExpired(overwritten, NowUtc));
    }

    // --- 记录是否还在 ---

    [Fact]
    public void ResolveState_ExistingRecordIsAvailable()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();
        var stored = repository.Add(
            HistoryRecordFactory.Create("还在", NowUtc.ToLocalTime(), 10, 10, HistorySource.Screenshot),
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg=="));

        Assert.Equal(HistoryOverwritePlan.EditState.Available, HistoryOverwritePlan.ResolveState(repository, stored.Id));
    }

    [Fact]
    public void ResolveState_MissingRecordIsDeleted()
    {
        using var directory = new TempHistoryDirectory();
        var repository = directory.CreateRepository();
        repository.Initialize();

        Assert.Equal(HistoryOverwritePlan.EditState.Deleted, HistoryOverwritePlan.ResolveState(repository, Guid.NewGuid()));
    }

    [Fact]
    public void ResolveState_NullRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HistoryOverwritePlan.ResolveState(null!, Guid.NewGuid()));
    }

    // --- 改名/收藏变化检测 ---

    [Fact]
    public void WasModifiedElsewhere_DetectsRenameAndFavorite()
    {
        var original = Record("名称");

        Assert.False(HistoryOverwritePlan.WasModifiedElsewhere(original, original));
        Assert.True(HistoryOverwritePlan.WasModifiedElsewhere(
            original,
            original.WithName("改了名", NowUtc)));
        Assert.True(HistoryOverwritePlan.WasModifiedElsewhere(
            original,
            original.WithFavorite(true, NowUtc)));
    }

    // --- 文案 ---

    [Fact]
    public void DescribeSaved_SaysNoNewRecord()
    {
        var record = Record("结果");

        var text = HistoryOverwritePlan.DescribeSaved(record, newAnnotationCount: 2);

        Assert.Contains("保存并覆盖", text);
        Assert.Contains("没有新增历史记录", text);
        Assert.Contains("2 个标注", text);
    }

    [Fact]
    public void DescribeSaved_WithoutAnnotations_StillSaysNoNewRecord()
    {
        var text = HistoryOverwritePlan.DescribeSaved(Record("原样保存"), 0);

        Assert.Contains("没有新增历史记录", text);
        Assert.DoesNotContain("追加", text);
    }

    [Fact]
    public void DescribeDeleted_ExplainsNothingWasWritten()
    {
        var text = HistoryOverwritePlan.DescribeDeleted(Record("被删掉的"));

        Assert.Contains("被删掉的", text);
        Assert.Contains("已被删除", text);
        Assert.Contains("不会新建", text);
    }

    [Fact]
    public void DescribeOverwriteExplanation_CoversPlanRequirements()
    {
        var text = HistoryOverwritePlan.DescribeOverwriteExplanation("某记录");

        Assert.Contains("保存并覆盖", text);      // 按钮文案
        Assert.Contains("不会被保留", text);      // 覆盖不保留旧版本
        Assert.Contains("不会新增一条历史记录", text); // 覆盖语义
        Assert.Contains("名称、创建时间与收藏状态保持不变", text); // 保留字段
        Assert.Contains("另存为", text);          // 想保留原图怎么办
    }
}
