using Xunit;

namespace ZeepCut.Tests;

public class SmokeTests
{
    [Fact]
    public void AppName_IsStable()
    {
        Assert.Equal("Zeep Cut", ZeepCut.Core.AppInfo.Name);
    }
}
