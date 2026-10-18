using Xunit;
using ZeepCut.Core.Shortcuts;

namespace ZeepCut.Tests.Shortcuts;

public class ShortcutSpecTests
{
    [Theory]
    [InlineData("Alt+A", ShortcutModifiers.Alt, 0x41)]
    [InlineData("Alt+S", ShortcutModifiers.Alt, 0x53)]
    [InlineData("Ctrl+Alt+A", ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x41)]
    [InlineData("Ctrl+Shift+F12", ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x7B)]
    [InlineData("Win+0", ShortcutModifiers.Win, 0x30)]
    [InlineData("Ctrl+Alt+Shift+Win+Z", ShortcutSpec.AllModifiers, 0x5A)]
    public void Parse_ValidText_ReturnsModifiersAndVirtualKey(
        string text,
        ShortcutModifiers expectedModifiers,
        int expectedVirtualKey)
    {
        var shortcut = ShortcutSpec.Parse(text);

        Assert.Equal(expectedModifiers, shortcut.Modifiers);
        Assert.Equal(expectedVirtualKey, shortcut.VirtualKey);
    }

    [Theory]
    [InlineData("alt+a")]
    [InlineData("ALT+A")]
    [InlineData(" alt + a ")]
    [InlineData("a+alt")]
    [InlineData("Alt+Control+A")]
    [InlineData("windows+shift+f1")]
    public void Parse_IsCaseInsensitiveAndOrderIndependent(string text)
    {
        var shortcut = ShortcutSpec.Parse(text);

        Assert.NotEqual(ShortcutModifiers.None, shortcut.Modifiers);
        Assert.True(shortcut.VirtualKey > 0);
    }

    [Fact]
    public void Parse_CtrlAlias_ControlIsEquivalent()
    {
        var ctrl = ShortcutSpec.Parse("Ctrl+A");
        var control = ShortcutSpec.Parse("Control+A");

        Assert.Equal(ctrl, control);
        Assert.Equal("Ctrl+A", ctrl.Format());
    }

    [Fact]
    public void Parse_WinAlias_WindowsIsEquivalent()
    {
        Assert.Equal(
            ShortcutSpec.Parse("Win+A"),
            ShortcutSpec.Parse("Windows+A"));
    }

    public static IEnumerable<object[]> SupportedKeys()
    {
        for (var ch = 'A'; ch <= 'Z'; ch++)
        {
            yield return new object[] { ch.ToString(), (int)ch };
        }

        for (var ch = '0'; ch <= '9'; ch++)
        {
            yield return new object[] { ch.ToString(), (int)ch };
        }

        for (var number = 1; number <= 12; number++)
        {
            yield return new object[] { $"F{number}", 0x70 + number - 1 };
        }
    }

    [Theory]
    [MemberData(nameof(SupportedKeys))]
    public void Parse_RoundTripsEverySupportedKey(string keyName, int virtualKey)
    {
        var shortcut = ShortcutSpec.Parse($"Alt+{keyName}");

        Assert.Equal(keyName, shortcut.KeyName);
        Assert.Equal(virtualKey, shortcut.VirtualKey);
        Assert.Equal($"Alt+{keyName}", shortcut.Format());

        // 格式化后再解析必须得到相等的快捷键（大小写/顺序归一化）。
        Assert.Equal(shortcut, ShortcutSpec.Parse(shortcut.Format()));
    }

    [Fact]
    public void Format_UsesCanonicalModifierOrder()
    {
        var shortcut = ShortcutSpec.Parse("win+shift+alt+ctrl+a");

        Assert.Equal("Ctrl+Alt+Shift+Win+A", shortcut.Format());
        Assert.Equal("Ctrl+Alt+Shift+Win+A", shortcut.ToString());
    }

    [Fact]
    public void Format_AltOnly_MatchesDocumentedDefault()
    {
        Assert.Equal("Alt+A", ShortcutSpec.Parse("Alt+A").Format());
        Assert.Equal("Alt+S", ShortcutSpec.Parse("alt+s").Format());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("F1")]
    [InlineData("Alt")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Alt+")]
    [InlineData("+Alt+A")]
    [InlineData("Alt++A")]
    [InlineData("Alt+A+B")]
    [InlineData("Alt+A+a")]
    [InlineData("Alt+Alt+A")]
    [InlineData("Alt+Ctrl+Control+A")]
    [InlineData("Hyper+A")]
    [InlineData("Alt+Enter")]
    [InlineData("Alt+Space")]
    [InlineData("Alt+F13")]
    [InlineData("Alt+F0")]
    [InlineData("Alt+F01")]
    [InlineData("Alt+AA")]
    [InlineData("Alt+1A")]
    public void Parse_InvalidText_ThrowsFormatExceptionWithMessage(string? text)
    {
        var exception = Assert.Throws<FormatException>(() => ShortcutSpec.Parse(text!));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Alt")]
    [InlineData("Alt+Alt+A")]
    [InlineData("Alt+Enter")]
    public void TryParse_InvalidText_ReturnsFalseWithError(string? text)
    {
        var result = ShortcutSpec.TryParse(text, out var shortcut, out var error);

        Assert.False(result);
        Assert.Null(shortcut);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_ValidText_ReturnsShortcutAndNullError()
    {
        var result = ShortcutSpec.TryParse("Alt+A", out var shortcut, out var error);

        Assert.True(result);
        Assert.NotNull(shortcut);
        Assert.Null(error);
        Assert.Equal(ShortcutModifiers.Alt, shortcut.Modifiers);
        Assert.Equal(0x41, shortcut.VirtualKey);
    }

    [Fact]
    public void Equals_SameBindingDifferentText_IsEqual()
    {
        var first = ShortcutSpec.Parse("Alt+A");
        var second = ShortcutSpec.Parse("a + alt");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(first.ConflictsWith(second));
        Assert.True(second.ConflictsWith(first));
    }

    [Fact]
    public void Equals_DifferentModifiers_DoesNotConflict()
    {
        var altA = ShortcutSpec.Parse("Alt+A");
        var ctrlA = ShortcutSpec.Parse("Ctrl+A");
        var ctrlAltA = ShortcutSpec.Parse("Ctrl+Alt+A");

        Assert.NotEqual(altA, ctrlA);
        Assert.NotEqual(altA, ctrlAltA);
        Assert.False(altA.ConflictsWith(ctrlA));
    }

    [Fact]
    public void Equals_DifferentKeys_DoesNotConflict()
    {
        var altA = ShortcutSpec.Parse("Alt+A");
        var altF1 = ShortcutSpec.Parse("Alt+F1");

        Assert.NotEqual(altA, altF1);
        Assert.False(altA.ConflictsWith(altF1));
    }

    [Fact]
    public void ConflictsWith_Null_ReturnsFalse()
    {
        Assert.False(ShortcutSpec.Parse("Alt+A").ConflictsWith(null));
    }

    [Fact]
    public void HashSet_DetectsDuplicateBindingsIgnoringTextForm()
    {
        var set = new HashSet<ShortcutSpec>
        {
            ShortcutSpec.Parse("Alt+A"),
            ShortcutSpec.Parse("alt + a"),
            ShortcutSpec.Parse("Alt+S"),
        };

        Assert.Equal(2, set.Count);
        Assert.Contains(ShortcutSpec.Parse("ALT+A"), set);
    }

    [Fact]
    public void GetKeyName_ReturnsCanonicalNames()
    {
        Assert.Equal("A", ShortcutSpec.GetKeyName(0x41));
        Assert.Equal("Z", ShortcutSpec.GetKeyName(0x5A));
        Assert.Equal("0", ShortcutSpec.GetKeyName(0x30));
        Assert.Equal("9", ShortcutSpec.GetKeyName(0x39));
        Assert.Equal("F1", ShortcutSpec.GetKeyName(0x70));
        Assert.Equal("F12", ShortcutSpec.GetKeyName(0x7B));
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x20)]
    [InlineData(0x40)]
    [InlineData(0x5B)]
    [InlineData(0x6F)]
    [InlineData(0x7C)]
    public void GetKeyName_UnsupportedVirtualKey_Throws(int virtualKey)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutSpec.GetKeyName(virtualKey));
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x30)]
    [InlineData(0x70)]
    [InlineData(0x7B)]
    public void IsSupportedVirtualKey_SupportedRange_ReturnsTrue(int virtualKey)
    {
        Assert.True(ShortcutSpec.IsSupportedVirtualKey(virtualKey));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x20)]
    [InlineData(0x5B)]
    [InlineData(0x7C)]
    public void IsSupportedVirtualKey_OutsideRange_ReturnsFalse(int virtualKey)
    {
        Assert.False(ShortcutSpec.IsSupportedVirtualKey(virtualKey));
    }
}
