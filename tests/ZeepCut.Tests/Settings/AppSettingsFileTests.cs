using Xunit;
using System.Text;
using ZeepCut.Core;
using ZeepCut.Core.Settings;

namespace ZeepCut.Tests.Settings;

/// <summary>
/// T29：settings.json 的 JSON 往返、坏文件容错与原子写入。
/// </summary>
/// <remarks>
/// 每个测试用独立临时目录，结束连目录一起删（同 History 测试的写法）。
/// </remarks>
public sealed class AppSettingsFileTests : IDisposable
{
    private readonly string _root;

    public AppSettingsFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zeepcut-settings-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string SettingsPath => Path.Combine(_root, AppSettings.FileName);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var result = AppSettingsFile.Load(SettingsPath, out var exists);

        Assert.False(exists);
        Assert.Equal(LoadStatus.Missing, result.Status);
        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.False(result.HasWarnings);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var original = new AppSettings
        {
            ScreenshotShortcut = "Ctrl+Shift+F8",
            PinShortcut = "Ctrl+Alt+Shift+Win+9",
            Language = AppLanguage.English,
            Theme = AppTheme.Dark,
            LaunchAtStartup = true,
        };

        AppSettingsFile.Save(SettingsPath, original);
        var result = AppSettingsFile.Load(SettingsPath, out var exists);

        Assert.True(exists);
        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(original, result.Settings);
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Save_WritesCamelCaseAndReadableEnumNames()
    {
        AppSettingsFile.Save(SettingsPath, new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.ZhHans });

        var json = File.ReadAllText(SettingsPath);

        Assert.Contains("\"screenshotShortcut\"", json);
        Assert.Contains("\"pinShortcut\"", json);
        Assert.Contains("\"language\"", json);
        Assert.Contains("\"theme\"", json);
        Assert.Contains("\"launchAtStartup\"", json);
        Assert.Contains("\"Dark\"", json);
        Assert.Contains("\"ZhHans\"", json);
        // 中文写进文件也不应该带 BOM（UTF-8 no BOM），否则某些工具读到 \uFEFF 会解析失败。
        Assert.False(File.ReadAllBytes(SettingsPath).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        AppSettingsFile.Save(SettingsPath, AppSettings.Default);
        AppSettingsFile.Save(SettingsPath, AppSettings.Default.WithTheme(AppTheme.Dark));

        Assert.False(File.Exists(SettingsPath + AtomicFile.TemporarySuffix));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndReportsCorruption()
    {
        File.WriteAllText(SettingsPath, "{ this is not json ");

        var result = AppSettingsFile.Load(SettingsPath, out var exists);

        Assert.True(exists);
        Assert.Equal(LoadStatus.Corrupted, result.Status);
        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.Contains(result.Warnings, warning => warning.Contains("不是有效的 JSON"));
    }

    [Fact]
    public void Load_EmptyFile_TreatedAsCorrupted()
    {
        File.WriteAllText(SettingsPath, "   ");

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Corrupted, result.Status);
        Assert.Equal(AppSettings.Default, result.Settings);
    }

    [Fact]
    public void Load_PartiallyInvalidFile_KeepsValidFields()
    {
        File.WriteAllText(
            SettingsPath,
            """
            {
              "screenshotShortcut": "Ctrl+Alt+Q",
              "pinShortcut": "not-a-shortcut",
              "language": "English",
              "theme": 99,
              "launchAtStartup": true
            }
            """);

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Corrected, result.Status);
        Assert.Equal("Ctrl+Alt+Q", result.Settings.ScreenshotShortcut);
        Assert.Equal(AppSettings.DefaultPinShortcut, result.Settings.PinShortcut);
        Assert.Equal(AppLanguage.English, result.Settings.Language);
        Assert.Equal(AppTheme.System, result.Settings.Theme);
        Assert.True(result.Settings.LaunchAtStartup);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Load_AcceptsLowercaseEnumNames()
    {
        File.WriteAllText(SettingsPath, """{ "theme": "dark", "language": "english" }""");

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(AppTheme.Dark, result.Settings.Theme);
        Assert.Equal(AppLanguage.English, result.Settings.Language);
    }

    [Fact]
    public void Load_AcceptsNumberEnumsAndTrailingCommas()
    {
        File.WriteAllText(SettingsPath, """{ "theme": 1, "language": 2, }""");

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(AppTheme.Light, result.Settings.Theme);
        Assert.Equal(AppLanguage.English, result.Settings.Language);
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        File.WriteAllText(SettingsPath, """{ "theme": "Dark", "futureOption": { "nested": [1,2,3] } }""");

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(AppTheme.Dark, result.Settings.Theme);
    }

    [Fact]
    public void Save_SanitizesInvalidValuesBeforeWriting()
    {
        AppSettingsFile.Save(SettingsPath, new AppSettings { ScreenshotShortcut = "garbage" });

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(AppSettings.DefaultScreenshotShortcut, result.Settings.ScreenshotShortcut);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_root, "a", "b", "c");
        var path = Path.Combine(nested, AppSettings.FileName);

        AppSettingsFile.Save(path, AppSettings.Default.WithTheme(AppTheme.Dark));

        Assert.True(File.Exists(path));
    }

    // --- 原子写入 ---

    [Fact]
    public void AtomicWrite_ReplacesExistingContent()
    {
        File.WriteAllText(SettingsPath, "old content");

        AtomicFile.WriteAllText(SettingsPath, "new content");

        Assert.Equal("new content", File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + AtomicFile.TemporarySuffix));
    }

    [Fact]
    public void AtomicWrite_WhenTargetIsADirectory_FailsAndKeepsOldFileIntact()
    {
        File.WriteAllText(SettingsPath, "old content");
        // 让临时文件无法创建：同名的 .tmp 是个目录（写临时文件就会失败）。
        Directory.CreateDirectory(SettingsPath + AtomicFile.TemporarySuffix);

        var exception = Record.Exception(() => AtomicFile.WriteAllText(SettingsPath, "new content"));

        Assert.NotNull(exception);
        Assert.Equal("old content", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void AtomicWrite_WhenFlushFails_OldFileIsNotTouched()
    {
        File.WriteAllText(SettingsPath, "old content");

        // 把目标文件的父目录设成只读不是可靠的跨环境手段；这里用"临时文件名被占成目录"模拟写入失败，
        // 关键在于旧内容必须完好、临时文件被清理。
        Directory.CreateDirectory(SettingsPath + AtomicFile.TemporarySuffix);

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(SettingsPath, "new content"));
        Assert.Equal("old content", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void AtomicWrite_FirstWriteCreatesFile()
    {
        AtomicFile.WriteAllText(SettingsPath, "hello");

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + AtomicFile.TemporarySuffix));
        Assert.Equal("hello", File.ReadAllText(SettingsPath, Encoding.UTF8));
    }

    [Fact]
    public void AtomicWrite_EmptyStringIsWritten()
    {
        AtomicFile.WriteAllText(SettingsPath, "x");
        AtomicFile.WriteAllText(SettingsPath, string.Empty);

        Assert.Equal(string.Empty, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void AtomicWrite_RejectsBlankPath()
    {
        Assert.ThrowsAny<ArgumentException>(() => AtomicFile.WriteAllText("  ", "x"));
        Assert.ThrowsAny<ArgumentException>(() => AtomicFile.WriteAllText(SettingsPath, null!));
    }

    [Fact]
    public void TryDelete_MissingFile_DoesNotThrow()
    {
        AtomicFile.TryDelete(null);
        AtomicFile.TryDelete(Path.Combine(_root, "nope.tmp"));
    }

    [Fact]
    public void RepeatedSaves_AlwaysLeaveReadableFile()
    {
        for (var i = 1; i <= 25; i++)
        {
            AppSettingsFile.Save(SettingsPath, new AppSettings { Theme = i % 2 == 0 ? AppTheme.Dark : AppTheme.Light });
        }

        var result = AppSettingsFile.Load(SettingsPath, out _);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.Equal(AppTheme.Light, result.Settings.Theme);
    }
}
