using Xunit;
using ZeepCut.Core.Updates;

namespace ZeepCut.Tests.Updates;

/// <summary>T32：版本号解析与比较规则。</summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0", 0, 1, 0, 0)]
    [InlineData("1.2.3.4", 1, 2, 3, 4)]
    [InlineData("v0.2.0", 0, 2, 0, 0)]
    [InlineData("V2", 2, 0, 0, 0)]
    [InlineData("  1.5  ", 1, 5, 0, 0)]
    [InlineData("0.2", 0, 2, 0, 0)]
    public void TryParse_AcceptsCommonForms(string text, int major, int minor, int build, int revision)
    {
        Assert.True(AppVersion.TryParse(text, out var version, out var error), error);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
        Assert.Equal(revision, version.Revision);
        Assert.False(version.IsPreRelease);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("1.0-beta", 1, 0, 0, "beta")]
    [InlineData("1.0.0-rc.2", 1, 0, 0, "rc.2")]
    [InlineData("V2.1-alpha", 2, 1, 0, "alpha")]
    public void TryParse_AcceptsPreReleaseSuffix(string text, int major, int minor, int build, string preRelease)
    {
        Assert.True(AppVersion.TryParse(text, out var version, out var error), error);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
        Assert.Equal(preRelease, version.PreRelease);
        Assert.True(version.IsPreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1..2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.-2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta!")]
    public void TryParse_RejectsInvalidText(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parse_InvalidText_ThrowsFormatExceptionWithReadableMessage()
    {
        var exception = Assert.Throws<FormatException>(() => AppVersion.Parse("nope"));
        Assert.Contains("nope", exception.Message);
    }

    [Fact]
    public void Comparison_NumericSegments()
    {
        Assert.True(AppVersion.Parse("0.2.0") > AppVersion.Parse("0.1.0"));
        Assert.True(AppVersion.Parse("1.0.0") > AppVersion.Parse("0.99.99"));
        Assert.True(AppVersion.Parse("1.0.1") > AppVersion.Parse("1.0.0"));
        Assert.True(AppVersion.Parse("2.0.0.1") > AppVersion.Parse("2.0.0.0"));
        Assert.False(AppVersion.Parse("0.1.0") > AppVersion.Parse("0.1.0"));
    }

    [Fact]
    public void Comparison_MissingSegmentsCountAsZero()
    {
        Assert.Equal(AppVersion.Parse("0.2"), AppVersion.Parse("0.2.0.0"));
        Assert.Equal(AppVersion.Parse("1"), AppVersion.Parse("1.0.0.0"));
        Assert.True(AppVersion.Parse("1.0.0.1") > AppVersion.Parse("1.0"));
    }

    [Fact]
    public void Comparison_ReleaseBeatsPreRelease()
    {
        Assert.True(AppVersion.Parse("1.0.0") > AppVersion.Parse("1.0.0-beta"));
        Assert.True(AppVersion.Parse("1.0.0-beta") < AppVersion.Parse("1.0.0"));
    }

    [Fact]
    public void Comparison_PreReleaseOrdering()
    {
        Assert.True(AppVersion.Parse("1.0-alpha") < AppVersion.Parse("1.0-beta"));
        Assert.True(AppVersion.Parse("1.0-beta.2") < AppVersion.Parse("1.0-beta.10"));
        Assert.True(AppVersion.Parse("1.0-alpha") < AppVersion.Parse("1.0-alpha.1"));
        Assert.Equal(0, AppVersion.Parse("1.0-Beta").CompareTo(AppVersion.Parse("1.0-beta")));
        Assert.Equal(AppVersion.Parse("1.0-beta"), AppVersion.Parse("1.0-Beta"));
        Assert.Equal(AppVersion.Parse("1.0-beta").GetHashCode(), AppVersion.Parse("1.0-Beta").GetHashCode());
    }

    [Fact]
    public void NormalizedString_DropsTrailingZeroRevision()
    {
        Assert.Equal("0.1.0", AppVersion.Parse("0.1.0.0").ToNormalizedString());
        Assert.Equal("0.1.0.7", AppVersion.Parse("0.1.0.7").ToNormalizedString());
        Assert.Equal("1.2", AppVersion.Parse("1.2").ToNormalizedString().Substring(0, 3));
        Assert.Equal("1.0.0-rc.1", AppVersion.Parse("1.0.0-rc.1").ToNormalizedString());
    }

    [Fact]
    public void CompareTo_ObjectOverload_RejectsWrongType()
    {
        IComparable comparable = AppVersion.Parse("1.0.0");
        Assert.Throws<ArgumentException>(() => comparable.CompareTo("1.0.0"));
        Assert.Equal(1, comparable.CompareTo(null));
    }

    [Fact]
    public void DefaultInstance_IsZeroVersion()
    {
        Assert.Equal("0.0.0", default(AppVersion).ToNormalizedString());
        Assert.True(default(AppVersion).CompareTo(AppVersion.Parse("0.0.1")) < 0);
        Assert.True(default(AppVersion) < AppVersion.Parse("0.0.1"));
    }
}
