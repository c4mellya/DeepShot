using Xunit;
using ZeepCut.Core.History;

namespace ZeepCut.Tests.History;

/// <summary>
/// 历史记录的纯逻辑测试（T23）：默认名称、名称清洗、相对路径、时间格式化、
/// 不可变修改方法。不碰数据库与文件系统。
/// </summary>
public class HistoryRecordTests
{
    private static readonly DateTime Sample = new(2026, 10, 18, 8, 15, 0, DateTimeKind.Local);

    private static HistoryRecord NewRecord(string name = "截图_2026-10-18_081500") =>
        new(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            name,
            Sample.ToUniversalTime(),
            Sample.ToUniversalTime(),
            isFavorite: false,
            "images/20261018-081500-000-11111111.png",
            "thumbs/20261018-081500-000-11111111.thumb.png",
            640,
            480,
            HistorySource.Screenshot);

    [Fact]
    public void Constructor_StoresAllFields()
    {
        var record = NewRecord();

        Assert.Equal("截图_2026-10-18_081500", record.Name);
        Assert.False(record.IsFavorite);
        Assert.Equal(640, record.Width);
        Assert.Equal(480, record.Height);
        Assert.Equal(307200, record.PixelCount);
        Assert.Equal(HistorySource.Screenshot, record.Source);
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        var ex = Assert.Throws<ArgumentException>(() => new HistoryRecord(
            Guid.Empty, "a", Sample, Sample, false, "images/a.png", null, 1, 1, HistorySource.Screenshot));

        Assert.Contains("Guid", ex.Message);
    }

    [Fact]
    public void Constructor_RejectsEmptyImagePath()
    {
        Assert.Throws<ArgumentException>(() => new HistoryRecord(
            Guid.NewGuid(), "a", Sample, Sample, false, "  ", null, 1, 1, HistorySource.Screenshot));
    }

    [Fact]
    public void Constructor_TreatsBlankThumbnailAsNull()
    {
        var record = new HistoryRecord(
            Guid.NewGuid(), "a", Sample, Sample, false, "images/a.png", "   ", 1, 1, HistorySource.Screenshot);

        Assert.Null(record.RelativeThumbnailPath);
    }

    [Fact]
    public void Constructor_ClampsNegativeDimensionsToZero()
    {
        var record = new HistoryRecord(
            Guid.NewGuid(), "a", Sample, Sample, false, "images/a.png", null, -5, -7, HistorySource.Screenshot);

        Assert.Equal(0, record.Width);
        Assert.Equal(0, record.Height);
    }

    [Fact]
    public void Constructor_SanitizesName()
    {
        var record = NewRecord("a/b:c*d");

        Assert.Equal("a_b_c_d", record.Name);
    }

    [Fact]
    public void Constructor_BlankNameFallsBackToDefaultPrefix()
    {
        var record = NewRecord("   ");

        Assert.Equal(HistoryName.DefaultPrefix, record.Name);
    }

    [Fact]
    public void Constructor_NormalizesLocalTimeToUtc()
    {
        var local = new DateTime(2026, 10, 18, 8, 15, 0, 123, DateTimeKind.Local);
        var record = new HistoryRecord(
            Guid.NewGuid(), "a", local, local, false, "images/a.png", null, 1, 1, HistorySource.Screenshot);

        Assert.Equal(DateTimeKind.Utc, record.CreatedAtUtc.Kind);
        Assert.Equal(local.ToUniversalTime().Ticks, record.CreatedAtUtc.Ticks);
        Assert.Equal(local, record.CreatedAtLocal, precision: TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void NormalizeUtc_TruncatesToMilliseconds_BecauseThatIsWhatSqliteStores()
    {
        var utc = new DateTime(2026, 10, 18, 12, 30, 15, DateTimeKind.Utc).AddTicks(9_999_999);

        var normalized = HistoryRecord.NormalizeUtc(utc);

        Assert.Equal(999, normalized.Millisecond);
        Assert.Equal(0, normalized.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(normalized, HistoryRecord.NormalizeUtc(normalized));
    }

    [Fact]
    public void NormalizeUtc_TreatsUnspecifiedAsUtc()
    {
        var unspecified = new DateTime(2026, 10, 18, 1, 2, 3, DateTimeKind.Unspecified);

        var normalized = HistoryRecord.NormalizeUtc(unspecified);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(unspecified.Ticks, normalized.Ticks);
    }

    [Theory]
    [InlineData(2026, 10, 18, 8, 15, 0, "截图_2026-10-18_081500")]
    [InlineData(2026, 1, 2, 23, 59, 59, "截图_2026-01-02_235959")]
    [InlineData(2025, 12, 31, 0, 0, 0, "截图_2025-12-31_000000")]
    public void CreateDefaultName_UsesLocalTimestamp(int y, int mo, int d, int h, int mi, int s, string expected)
    {
        var name = HistoryRecord.CreateDefaultName(new DateTime(y, mo, d, h, mi, s, DateTimeKind.Local));

        Assert.Equal(expected, name);
        Assert.DoesNotContain('.', name);
    }

    [Fact]
    public void CreateDefaultName_UsesCustomPrefix()
    {
        var name = HistoryRecord.CreateDefaultName(Sample, "贴图");

        Assert.StartsWith("贴图_", name);
    }

    [Fact]
    public void CreateDefaultName_SanitizesPrefix()
    {
        var name = HistoryRecord.CreateDefaultName(Sample, "a/b");

        Assert.StartsWith("a_b_", name);
    }

    [Fact]
    public void WithName_ChangesNameAndUpdatedOnly()
    {
        var record = NewRecord();
        var updatedAt = record.UpdatedAtUtc.AddMinutes(5);

        var renamed = record.WithName("新名称", updatedAt);

        Assert.Equal("新名称", renamed.Name);
        Assert.Equal(updatedAt, renamed.UpdatedAtUtc);
        Assert.Equal(record.CreatedAtUtc, renamed.CreatedAtUtc);
        Assert.Equal(record.Id, renamed.Id);
        Assert.Equal("截图_2026-10-18_081500", record.Name);
    }

    [Fact]
    public void WithFavorite_SetsFlagAndKeepsCreatedAt()
    {
        var record = NewRecord();

        var favorite = record.WithFavorite(true, record.UpdatedAtUtc.AddHours(1));

        Assert.True(favorite.IsFavorite);
        Assert.Equal(record.CreatedAtUtc, favorite.CreatedAtUtc);
        Assert.False(record.IsFavorite);
    }

    [Fact]
    public void WithImage_KeepsIdNameCreatedAtFavorite()
    {
        var record = NewRecord().WithFavorite(true, Sample.ToUniversalTime());

        var replaced = record.WithImage(
            "images/new.png",
            "thumbs/new.thumb.png",
            800,
            600,
            record.UpdatedAtUtc.AddMinutes(1));

        Assert.Equal(record.Id, replaced.Id);
        Assert.Equal(record.Name, replaced.Name);
        Assert.Equal(record.CreatedAtUtc, replaced.CreatedAtUtc);
        Assert.True(replaced.IsFavorite);
        Assert.Equal("images/new.png", replaced.RelativeImagePath);
        Assert.Equal(800, replaced.Width);
        Assert.Equal(600, replaced.Height);
    }

    [Fact]
    public void WithImage_RejectsEmptyPath()
    {
        var record = NewRecord();

        Assert.Throws<ArgumentException>(() =>
            record.WithImage("", null, 1, 1, record.UpdatedAtUtc));
    }

    [Theory]
    [InlineData("2026-10-18T12:30:15.123Z", 2026, 10, 18, 12, 30, 15, 123)]
    [InlineData("2026-10-18T12:30:15Z", 2026, 10, 18, 12, 30, 15, 0)]
    [InlineData("2026-10-18 12:30:15", 2026, 10, 18, 12, 30, 15, 0)]
    public void TryParseUtc_ParsesIsoStrings(
        string text, int y, int mo, int d, int h, int mi, int s, int ms)
    {
        Assert.True(HistoryRecord.TryParseUtc(text, out var parsed));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s, ms, DateTimeKind.Utc), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a date")]
    public void TryParseUtc_RejectsGarbage(string? text)
    {
        Assert.False(HistoryRecord.TryParseUtc(text, out _));
    }

    [Fact]
    public void FormatUtc_RoundTrips()
    {
        var utc = new DateTime(2026, 10, 18, 12, 30, 15, 456, DateTimeKind.Utc);

        var text = HistoryRecord.FormatUtc(utc);

        Assert.Equal("2026-10-18T12:30:15.456Z", text);
        Assert.True(HistoryRecord.TryParseUtc(text, out var parsed));
        Assert.Equal(utc, parsed);
    }

    [Fact]
    public void Equality_ComparesAllFields()
    {
        var a = NewRecord();
        var b = NewRecord();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.WithFavorite(true, a.UpdatedAtUtc));
    }

    [Fact]
    public void ToString_ContainsNameSizeAndSource()
    {
        var text = NewRecord().ToString();

        Assert.Contains("截图_2026-10-18_081500", text);
        Assert.Contains("640x480", text);
        Assert.Contains("Screenshot", text);
    }

    [Theory]
    [InlineData(HistorySource.Screenshot, "截图")]
    [InlineData(HistorySource.Clipboard, "剪贴板贴图")]
    [InlineData(HistorySource.History, "历史记录")]
    public void SourceText_DescribeReturnsChineseLabel(HistorySource source, string expected)
    {
        Assert.Equal(expected, HistorySourceText.Describe(source));
    }

    [Theory]
    [InlineData(HistorySource.Screenshot, "Screenshot")]
    [InlineData(HistorySource.Clipboard, "Clipboard")]
    [InlineData(HistorySource.History, "History")]
    public void SourceText_RoundTripsThroughStorageValue(HistorySource source, string stored)
    {
        Assert.Equal(stored, HistorySourceText.ToStorageValue(source));
        Assert.True(HistorySourceText.TryParse(stored, out var parsed));
        Assert.Equal(source, parsed);
    }

    [Theory]
    [InlineData("clipboard", HistorySource.Clipboard)]
    [InlineData(" HISTORY ", HistorySource.History)]
    public void SourceText_TryParseIsCaseInsensitiveAndTrims(string text, HistorySource expected)
    {
        Assert.True(HistorySourceText.TryParse(text, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Nope")]
    [InlineData("99")]
    public void SourceText_TryParseRejectsUnknownValues(string? text)
    {
        Assert.False(HistorySourceText.TryParse(text, out var parsed));
        Assert.Equal(HistorySource.Screenshot, parsed);
    }

    [Fact]
    public void RecordFactory_BuildsDefaultNameAndPaths()
    {
        var record = HistoryRecordFactory.Create(null, Sample, 300, 200, HistorySource.Clipboard);

        Assert.StartsWith("贴图_2026-10-18_081500", record.Name);
        Assert.StartsWith("images/", record.RelativeImagePath);
        Assert.EndsWith(".png", record.RelativeImagePath);
        Assert.StartsWith("thumbs/", record.RelativeThumbnailPath);
        Assert.True(HistoryPaths.IsSafeImageRelativePath(record.RelativeImagePath));
        Assert.Equal(300, record.Width);
        Assert.Equal(200, record.Height);
        Assert.Equal(HistorySource.Clipboard, record.Source);
        Assert.False(record.IsFavorite);
        Assert.Equal(record.CreatedAtUtc, record.UpdatedAtUtc);
    }

    [Fact]
    public void RecordFactory_UsesGivenName()
    {
        var record = HistoryRecordFactory.Create("  自定义  ", Sample, 10, 10, HistorySource.Screenshot);

        Assert.Equal("自定义", record.Name);
    }

    [Fact]
    public void RecordFactory_GeneratesUniqueIds()
    {
        var a = HistoryRecordFactory.Create(null, Sample, 10, 10, HistorySource.Screenshot);
        var b = HistoryRecordFactory.Create(null, Sample, 10, 10, HistorySource.Screenshot);

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.RelativeImagePath, b.RelativeImagePath);
    }
}
