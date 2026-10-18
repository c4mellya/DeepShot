using Xunit;
using ZeepCut.Core.Pins;

namespace ZeepCut.Tests.Pins;

public class PinCollectionStateTests
{
    // --- 空集合 ---

    [Fact]
    public void Empty_HasNoPinsAndNeitherVisibleNorHidden()
    {
        var state = PinCollectionState.FromVisibility(Array.Empty<bool>());

        Assert.Equal(0, state.Count);
        Assert.True(state.IsEmpty);
        Assert.Equal(0, state.VisibleCount);
        Assert.Equal(0, state.HiddenCount);
        Assert.False(state.HasVisible);
        // 空集合既不算"有可见"，也不算"全隐"：没有贴图可藏。
        Assert.False(state.AllHidden);
        Assert.Equal(PinBulkAction.None, state.DecideBulkAction());
    }

    [Fact]
    public void Empty_BulkOperationsAreNoOps()
    {
        var empty = PinCollectionState.Empty;

        Assert.Same(empty, empty.HideAll());
        Assert.Same(empty, empty.ShowAll());
        Assert.Same(empty, empty.ToggleAll());
        Assert.Equal(PinBulkAction.None, empty.ToggleAll().DecideBulkAction());
    }

    // --- 全部可见 ---

    [Fact]
    public void AllVisible_DecidesHideAll()
    {
        var state = PinCollectionState.FromVisibility(new[] { true, true });

        Assert.Equal(2, state.Count);
        Assert.Equal(2, state.VisibleCount);
        Assert.Equal(0, state.HiddenCount);
        Assert.True(state.HasVisible);
        Assert.False(state.AllHidden);
        Assert.Equal(PinBulkAction.HideAll, state.DecideBulkAction());
    }

    [Fact]
    public void HideAll_FromAllVisible_HidesEveryPin()
    {
        var state = PinCollectionState.FromVisibility(new[] { true, true, true });

        var hidden = state.HideAll();

        Assert.Equal(3, hidden.Count);
        Assert.Equal(0, hidden.VisibleCount);
        Assert.False(hidden.HasVisible);
        Assert.True(hidden.AllHidden);
        Assert.False(hidden.IsVisible(0));
        Assert.False(hidden.IsVisible(2));
        // 原状态不变（不可变语义）。
        Assert.Equal(3, state.VisibleCount);
    }

    // --- 全部隐藏 ---

    [Fact]
    public void AllHidden_DecidesShowAll()
    {
        var state = PinCollectionState.FromVisibility(new[] { false, false });

        Assert.True(state.AllHidden);
        Assert.False(state.HasVisible);
        Assert.Equal(PinBulkAction.ShowAll, state.DecideBulkAction());
    }

    [Fact]
    public void ShowAll_RestoresEveryPin()
    {
        var hidden = PinCollectionState.FromVisibility(new[] { false, false, false }).ShowAll();

        Assert.Equal(3, hidden.VisibleCount);
        Assert.True(hidden.HasVisible);
        Assert.False(hidden.AllHidden);
        Assert.True(hidden.IsVisible(1));
    }

    // --- 混合状态：有可见就全隐 ---

    [Fact]
    public void MixedState_DecidesHideAllNotShowAll()
    {
        var state = PinCollectionState.FromVisibility(new[] { false, true, false });

        Assert.Equal(1, state.VisibleCount);
        Assert.Equal(2, state.HiddenCount);
        Assert.True(state.HasVisible);
        Assert.False(state.AllHidden);
        Assert.Equal(PinBulkAction.HideAll, state.DecideBulkAction());
    }

    [Fact]
    public void ToggleAll_FromMixedState_HidesEverythingFirst()
    {
        var state = PinCollectionState.FromVisibility(new[] { false, true, false });

        var toggled = state.ToggleAll();

        Assert.True(toggled.AllHidden);
        Assert.Equal(0, toggled.VisibleCount);
    }

    [Fact]
    public void ToggleAll_TwiceFromMixedState_EndsAllVisible()
    {
        // 混合状态第一次 toggle 是"全隐"，第二次是"全显"：所以全显（不是回到混合）。
        var state = PinCollectionState.FromVisibility(new[] { true, false });

        var hidden = state.ToggleAll();
        var shown = hidden.ToggleAll();

        Assert.True(hidden.AllHidden);
        Assert.Equal(2, shown.VisibleCount);
        Assert.NotEqual(state, shown);
    }

    [Fact]
    public void ToggleAll_SingleVisiblePin_Hides()
    {
        var state = PinCollectionState.FromVisibility(new[] { true });

        Assert.True(state.ToggleAll().AllHidden);
    }

    [Fact]
    public void ToggleAll_SingleHiddenPin_Shows()
    {
        var state = PinCollectionState.FromVisibility(new[] { false });

        Assert.True(state.ToggleAll().HasVisible);
    }

    // --- 幂等 ---

    [Fact]
    public void HideAll_WhenAllHidden_ReturnsSameInstance()
    {
        var hidden = PinCollectionState.FromVisibility(new[] { false, false });

        Assert.Same(hidden, hidden.HideAll());
    }

    [Fact]
    public void ShowAll_WhenAllVisible_ReturnsSameInstance()
    {
        var visible = PinCollectionState.FromVisibility(new[] { true, true });

        Assert.Same(visible, visible.ShowAll());
    }

    [Fact]
    public void ShowAll_RestoresMixedStateToAllVisible()
    {
        var mixed = PinCollectionState.FromVisibility(new[] { false, true });

        var shown = mixed.ShowAll();

        Assert.Equal(2, shown.VisibleCount);
        Assert.False(shown.AllHidden);
    }

    // --- 单张可见性 ---

    [Fact]
    public void WithVisible_ChangesOnlyThatPin()
    {
        var state = PinCollectionState.FromVisibility(new[] { true, true });

        var updated = state.WithVisible(1, false);

        Assert.True(updated.IsVisible(0));
        Assert.False(updated.IsVisible(1));
        Assert.True(state.IsVisible(1));
        Assert.Equal(1, updated.VisibleCount);
    }

    [Fact]
    public void WithVisible_SameValue_ReturnsSameInstance()
    {
        var state = PinCollectionState.FromVisibility(new[] { true, false });

        Assert.Same(state, state.WithVisible(0, true));
        Assert.Same(state, state.WithVisible(1, false));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void IsVisible_OutOfRange_Throws(int index)
    {
        var state = PinCollectionState.FromVisibility(new[] { true, false });

        Assert.Throws<ArgumentOutOfRangeException>(() => state.IsVisible(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.WithVisible(index, true));
    }

    [Fact]
    public void FromVisibility_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PinCollectionState.FromVisibility(null!));
    }

    // --- 相等性与显示 ---

    [Fact]
    public void Equality_ComparesVisibilitySequence()
    {
        var a = PinCollectionState.FromVisibility(new[] { true, false });
        var b = PinCollectionState.FromVisibility(new[] { true, false });
        var c = PinCollectionState.FromVisibility(new[] { false, true });

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_ReportsCounts()
    {
        var empty = PinCollectionState.Empty.ToString();
        var state = PinCollectionState.FromVisibility(new[] { true, false, false }).ToString();

        Assert.Contains("空", empty);
        Assert.Contains("3", state);
        Assert.Contains("可见 1", state);
        Assert.Contains("隐藏 2", state);
    }

    // --- 多张（T20 桌面验证用的规模）---

    [Fact]
    public void TwoPins_HideThenShow_KeepsBothEntries()
    {
        var state = PinCollectionState.FromVisibility(new[] { true, true });

        var hidden = state.HideAll();
        var shown = hidden.ShowAll();

        Assert.Equal(2, hidden.Count);
        Assert.Equal(2, shown.Count);
        Assert.Equal(2, shown.VisibleCount);
        Assert.Equal(state, shown);
    }
}
