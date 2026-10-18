using Xunit;
using System.Text;
using ZeepCut.Core;
using ZeepCut.Core.Settings;

namespace ZeepCut.Tests.Settings;

/// <summary>
/// T29：设置模型的默认值、非法值回退与快捷键校验。
/// </summary>
public sealed class AppSettingsTests
{
    [Fact]
    public void Default_HasPlanDefaults()
    {
        var settings = AppSettings.Default;

        Assert.Equal("Alt+A", settings.ScreenshotShortcut);
        Assert.Equal("Alt+S", settings.PinShortcut);
        Assert.Equal(AppLanguage.System, settings.Language);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.False(settings.LaunchAtStartup);
        Assert.Equal(1, settings.Version);
        Assert.True(settings.IsDefault);
        Assert.Equal(settings, new AppSettings());
    }

    [Fact]
    public void Default_ShortcutsAreValidAndDistinct()
    {
        var pair = AppSettings.Default.ShortcutPair(out var screenshot, out var pin);

        Assert.True(pair.IsValid);
        Assert.Equal("Alt+A", screenshot!.Format());
        Assert.Equal("Alt+S", pin!.Format());
        Assert.False(AppSettings.Default.HasShortcutConflict());
    }

    [Fact]
    public void Sanitize_KeepsValidValues()
    {
        var input = new AppSettings
        {
            ScreenshotShortcut = "Ctrl+Shift+F9",
            PinShortcut = "ctrl+alt+7",
            Language = AppLanguage.English,
            Theme = AppTheme.Dark,
            LaunchAtStartup = true,
        };

        var result = AppSettings.Sanitize(input);

        Assert.False(result.HasCorrections);
        // 快捷键统一成规范写法：修饰键固定顺序、主键大写。
        Assert.Equal("Ctrl+Shift+F9", result.Settings.ScreenshotShortcut);
        Assert.Equal("Ctrl+Alt+7", result.Settings.PinShortcut);
        Assert.Equal(AppLanguage.English, result.Settings.Language);
        Assert.Equal(AppTheme.Dark, result.Settings.Theme);
        Assert.True(result.Settings.LaunchAtStartup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]              // 缺修饰键
    [InlineData("Alt+")]           // 空段
    [InlineData("Alt+Foo")]        // 不认识的主键
    [InlineData("Alt+A+B")]        // 两个主键
    [InlineData("Alt+Ctrl+Shift+Win+F13")] // F13 超出支持范围
    [InlineData("Ctrl+Alt+Ctrl+A")] // 修饰键重复
    public void Sanitize_InvalidScreenshotShortcut_FallsBackToDefault_WithWarning(string value)
    {
        var result = AppSettings.Sanitize(new AppSettings { ScreenshotShortcut = value });

        Assert.Equal(AppSettings.DefaultScreenshotShortcut, result.Settings.ScreenshotShortcut);
        Assert.True(result.HasCorrections);
        Assert.Single(result.Warnings);
        Assert.Contains("截图快捷键", result.Warnings[0]);
    }

    [Fact]
    public void Sanitize_NullShortcuts_FallBackToDefaults()
    {
        var result = AppSettings.Sanitize(new AppSettings
        {
            ScreenshotShortcut = null!,
            PinShortcut = null!,
        });

        Assert.Equal(AppSettings.DefaultScreenshotShortcut, result.Settings.ScreenshotShortcut);
        Assert.Equal(AppSettings.DefaultPinShortcut, result.Settings.PinShortcut);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Theory]
    [InlineData(0, AppLanguage.System)]
    [InlineData(1, AppLanguage.ZhHans)]
    [InlineData(2, AppLanguage.English)]
    public void Sanitize_KnownLanguageValues_Pass(int raw, AppLanguage expected)
    {
        var result = AppSettings.Sanitize(new AppSettings { Language = (AppLanguage)raw });

        Assert.Equal(expected, result.Settings.Language);
        Assert.False(result.HasCorrections);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Sanitize_UnknownLanguage_FallsBackToSystem(int raw)
    {
        var result = AppSettings.Sanitize(new AppSettings { Language = (AppLanguage)raw });

        Assert.Equal(AppLanguage.System, result.Settings.Language);
        Assert.Contains(result.Warnings, warning => warning.Contains("语言"));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(3)]
    public void Sanitize_UnknownTheme_FallsBackToSystem(int raw)
    {
        var result = AppSettings.Sanitize(new AppSettings { Theme = (AppTheme)raw });

        Assert.Equal(AppTheme.System, result.Settings.Theme);
        Assert.Contains(result.Warnings, warning => warning.Contains("主题"));
    }

    [Fact]
    public void Sanitize_NullSettings_ReturnsDefaults()
    {
        var result = AppSettings.Sanitize(null);

        Assert.Equal(AppSettings.Default, result.Settings);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Sanitize_ZeroVersion_ResetToOne()
    {
        var result = AppSettings.Sanitize(new AppSettings { Version = 0 });

        Assert.Equal(1, result.Settings.Version);
    }

    [Fact]
    public void ShortcutPair_IdenticalShortcuts_ReportsConflict()
    {
        var settings = new AppSettings { ScreenshotShortcut = "Alt+A", PinShortcut = "alt+a" };

        var pair = settings.ShortcutPair(out _, out _);

        Assert.Equal(ShortcutNamesOutcome.Conflict, pair.Outcome);
        Assert.True(settings.HasShortcutConflict());
        Assert.Contains("Alt+A", pair.Error);
    }

    [Fact]
    public void ShortcutPair_InvalidPin_ReportsInvalid()
    {
        var settings = new AppSettings { PinShortcut = "nonsense" };

        var pair = settings.ShortcutPair(out var screenshot, out _);

        Assert.Equal(ShortcutNamesOutcome.Invalid, pair.Outcome);
        Assert.NotNull(pair.Error);
        Assert.NotNull(screenshot);
    }

    [Fact]
    public void ValidateShortcut_AcceptsNewCombination()
    {
        var ok = AppSettings.ValidateShortcut(
            "Ctrl+Shift+7",
            ZeepCut.Core.Shortcuts.ShortcutSpec.Parse(AppSettings.DefaultPinShortcut),
            out var shortcut,
            out var error);

        Assert.True(ok);
        Assert.Equal("Ctrl+Shift+7", shortcut!.Format());
        Assert.Null(error);
    }

    [Fact]
    public void ValidateShortcut_RejectsConflictWithOtherFunction()
    {
        var other = ZeepCut.Core.Shortcuts.ShortcutSpec.Parse("Alt+S");

        var ok = AppSettings.ValidateShortcut("alt+s", other, out var shortcut, out var error);

        Assert.False(ok);
        Assert.Null(shortcut);
        Assert.Contains("已经被另一个功能使用", error);
    }

    [Fact]
    public void ValidateShortcut_RejectsKeyboardWithoutModifier()
    {
        var ok = AppSettings.ValidateShortcut("F5", null, out _, out var error);

        Assert.False(ok);
        Assert.Contains("修饰键", error);
    }

    [Fact]
    public void WithMethods_ReturnNewInstances()
    {
        var settings = AppSettings.Default;

        Assert.Equal("Ctrl+Alt+A", settings.WithScreenshotShortcut("Ctrl+Alt+A").ScreenshotShortcut);
        Assert.Equal("Ctrl+Alt+S", settings.WithPinShortcut("Ctrl+Alt+S").PinShortcut);
        Assert.Equal(AppLanguage.English, settings.WithLanguage(AppLanguage.English).Language);
        Assert.Equal(AppTheme.Dark, settings.WithTheme(AppTheme.Dark).Theme);
        Assert.True(settings.WithLaunchAtStartup(true).LaunchAtStartup);

        // 原实例不变（不可变 record）。
        Assert.Equal("Alt+A", settings.ScreenshotShortcut);
        Assert.False(settings.LaunchAtStartup);
    }

    [Fact]
    public void Settings_AreValueComparable()
    {
        var a = new AppSettings { Theme = AppTheme.Dark, LaunchAtStartup = true };
        var b = new AppSettings { Theme = AppTheme.Dark, LaunchAtStartup = true };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a.WithTheme(AppTheme.Light));
    }

    [Fact]
    public void ToString_ContainsAllFields()
    {
        var text = new AppSettings { Theme = AppTheme.Dark }.ToString();

        Assert.Contains("Alt+A", text);
        Assert.Contains("Alt+S", text);
        Assert.Contains("Dark", text);
        Assert.Contains("开机启动", text);
    }
}
