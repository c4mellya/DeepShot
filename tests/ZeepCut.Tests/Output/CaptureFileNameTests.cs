using Xunit;
using ZeepCut.Core.Output;

namespace ZeepCut.Tests.Output;

public class CaptureFileNameTests
{
    private static readonly DateTime SampleTime = new(2026, 10, 18, 5, 0, 0, DateTimeKind.Local);

    [Fact]
    public void CreateDefault_Png_UsesLocalDateAndTime()
    {
        Assert.Equal(
            "截图_2026-10-18_050000.png",
            CaptureFileName.CreateDefault(SampleTime, CaptureImageFormat.Png));
    }

    [Fact]
    public void CreateDefault_Jpeg_UsesJpgExtension()
    {
        Assert.Equal(
            "截图_2026-10-18_050000.jpg",
            CaptureFileName.CreateDefault(SampleTime, CaptureImageFormat.Jpeg));
    }

    [Fact]
    public void CreateDefault_DefaultsToPng()
    {
        Assert.EndsWith(".png", CaptureFileName.CreateDefault(SampleTime), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 1, 2, 3, 4, 5, "截图_2026-01-02_030405.png")]
    [InlineData(2026, 12, 31, 23, 59, 59, "截图_2026-12-31_235959.png")]
    [InlineData(2027, 2, 9, 0, 0, 0, "截图_2027-02-09_000000.png")]
    public void CreateDefault_PadsAllFields(int year, int month, int day, int hour, int minute, int second, string expected)
    {
        var localTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);

        Assert.Equal(expected, CaptureFileName.CreateDefault(localTime, CaptureImageFormat.Png));
    }

    [Fact]
    public void CreateDefault_UsesProvidedBaseName()
    {
        Assert.Equal(
            "区域_2026-10-18_050000.png",
            CaptureFileName.CreateDefault(SampleTime, CaptureImageFormat.Png, "区域"));
    }

    [Fact]
    public void CreateDefault_DoesNotConvertTimeZone()
    {
        // 调用方给的是本地时间，生成名称时不能做 UTC 平移。
        var unspecified = new DateTime(2026, 10, 18, 5, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            "截图_2026-10-18_050000.png",
            CaptureFileName.CreateDefault(unspecified, CaptureImageFormat.Png));
    }

    [Fact]
    public void SanitizeBaseName_ReplacesWindowsInvalidCharacters()
    {
        const string raw = "a<b>c:d\"e/f\\g|h?i*j";

        Assert.Equal("a_b_c_d_e_f_g_h_i_j", CaptureFileName.SanitizeBaseName(raw));
    }

    [Fact]
    public void SanitizeBaseName_ReplacesControlCharacters()
    {
        var raw = "a\tb\nc\rd\u0001e";

        Assert.Equal("a_b_c_d_e", CaptureFileName.SanitizeBaseName(raw));
    }

    [Fact]
    public void SanitizeBaseName_TrimsWhitespace()
    {
        Assert.Equal("区域", CaptureFileName.SanitizeBaseName("  区域  "));
    }

    [Fact]
    public void SanitizeBaseName_RemovesTrailingDotsAndSpaces()
    {
        // Windows 不允许文件名以点或空格结尾；这里清洗的是时间戳前的前缀。
        Assert.Equal("区域", CaptureFileName.SanitizeBaseName("区域. . "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void SanitizeBaseName_EmptyOrWhitespace_FallsBackToDefault(string? raw)
    {
        Assert.Equal(CaptureFileName.DefaultBaseName, CaptureFileName.SanitizeBaseName(raw));
    }

    [Fact]
    public void SanitizeBaseName_OnlyInvalidCharacters_FallsBackToDefault()
    {
        Assert.Equal(CaptureFileName.DefaultBaseName, CaptureFileName.SanitizeBaseName("...   "));
    }

    [Fact]
    public void SanitizeBaseName_TruncatesLongNames()
    {
        var raw = new string('a', CaptureFileName.MaxBaseNameLength + 50);

        var result = CaptureFileName.SanitizeBaseName(raw);

        Assert.Equal(CaptureFileName.MaxBaseNameLength, result.Length);
        Assert.All(result, ch => Assert.Equal('a', ch));
    }

    [Fact]
    public void CreateDefault_WithDirtyBaseName_ProducesValidFileName()
    {
        var result = CaptureFileName.CreateDefault(SampleTime, CaptureImageFormat.Png, " a/b:c*d ");

        Assert.Equal("a_b_c_d_2026-10-18_050000.png", result);
        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain(':', result);
        Assert.DoesNotContain('*', result);
        Assert.DoesNotContain('?', result);
        Assert.False(result.EndsWith('.'), "文件名不能以点结尾");
        Assert.False(result.EndsWith(' '), "文件名不能以空格结尾");
    }

    [Theory]
    [InlineData("shot.png", CaptureImageFormat.Png)]
    [InlineData("shot.PNG", CaptureImageFormat.Png)]
    [InlineData("shot.jpg", CaptureImageFormat.Jpeg)]
    [InlineData("shot.JPEG", CaptureImageFormat.Jpeg)]
    [InlineData(@"C:\temp\shot.jpeg", CaptureImageFormat.Jpeg)]
    public void TryGetFormatFromExtension_RecognizesSupportedFormats(string path, CaptureImageFormat expected)
    {
        Assert.True(CaptureFileName.TryGetFormatFromExtension(path, out var format));
        Assert.Equal(expected, format);
    }

    [Theory]
    [InlineData("shot")]
    [InlineData("shot.bmp")]
    [InlineData("shot.png.tmp")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetFormatFromExtension_UnsupportedOrMissing_ReturnsFalse(string? path)
    {
        Assert.False(CaptureFileName.TryGetFormatFromExtension(path, out _));
    }
}
