using Xunit;
using ZeepCut.Core.Pins;

namespace ZeepCut.Tests.Pins;

/// <summary>
/// T22 历史联动规则的测试：只有外部剪贴板来源才新增一条历史，
/// 内部截图（已由 ICaptureSink 记录）与历史贴图都不重复记录。
/// </summary>
public class PinSourceTests
{
    [Fact]
    public void Clipboard_ShouldRecord()
    {
        Assert.True(PinSourceText.ShouldRecordHistory(PinSource.Clipboard));
    }

    [Fact]
    public void Screenshot_ShouldNotRecord_BecauseCaptureSinkAlreadyDid()
    {
        Assert.False(PinSourceText.ShouldRecordHistory(PinSource.Screenshot));
    }

    [Fact]
    public void History_ShouldNotRecord_BecauseRecordAlreadyExists()
    {
        Assert.False(PinSourceText.ShouldRecordHistory(PinSource.History));
    }

    [Theory]
    [InlineData(PinSource.Clipboard, "外部剪贴板")]
    [InlineData(PinSource.History, "历史记录")]
    [InlineData(PinSource.Screenshot, "内部截图")]
    public void Describe_ReturnsChineseLabel(PinSource source, string expected)
    {
        Assert.Equal(expected, PinSourceText.Describe(source));
    }

    [Fact]
    public void Sources_HaveDistinctValues()
    {
        var values = Enum.GetValues<PinSource>();

        Assert.Equal(3, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
