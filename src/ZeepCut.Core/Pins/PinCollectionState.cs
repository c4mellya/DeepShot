namespace ZeepCut.Core.Pins;

/// <summary>
/// 一批贴图的批量操作（T20）：隐藏全部 / 显示全部 / 什么都不做。
/// </summary>
public enum PinBulkAction
{
    /// <summary>没有可操作的贴图（空集合），调用方应给出提示或不执行任何动作。</summary>
    None = 0,

    /// <summary>隐藏全部贴图（不销毁窗口实例）。</summary>
    HideAll = 1,

    /// <summary>显示全部贴图（恢复原来的位置/尺寸/透明度）。</summary>
    ShowAll = 2,
}

/// <summary>
/// 贴图集合的可见性状态（T20，纯逻辑、无 WPF/Win32 依赖、不可变）。
/// </summary>
/// <remarks>
/// <para>
/// 只表达"每张贴图当前是否显示"，不持有窗口实例：UI 层（<c>PinWindows</c>）把
/// 每个 <c>PinWindow</c> 的可见性投影成本类型，先在这里算出该做哪个批量动作，
/// 再去逐个窗口执行。这样"有可见就全隐、全隐就全显"的语义可以单测，
/// 不必靠桌面操作验证。
/// </para>
/// <para>
/// <b>空集合</b>：<see cref="Count"/> 为 0 时 <see cref="HasVisible"/>、<see cref="AllHidden"/>
/// 都是 false（没有贴图既不算"有可见"也不算"全隐"），
/// <see cref="DecideBulkAction"/> 返回 <see cref="PinBulkAction.None"/>，
/// 三个批量方法都是原样返回的空操作。
/// </para>
/// <para>
/// <b>混合状态</b>：只要还有一张可见，<see cref="DecideBulkAction"/> 就是
/// <see cref="PinBulkAction.HideAll"/>（先全部藏起来，符合"一键清屏"的使用预期）；
/// 全部隐藏时才是 <see cref="PinBulkAction.ShowAll"/>。
/// </para>
/// </remarks>
public sealed class PinCollectionState : IEquatable<PinCollectionState>
{
    private readonly bool[] _visible;

    private PinCollectionState(bool[] visible) => _visible = visible;

    /// <summary>没有任何贴图的状态。</summary>
    public static PinCollectionState Empty => new(Array.Empty<bool>());

    /// <summary>由每张贴图的可见性构造状态（顺序即贴图顺序）。</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visibility"/> 为 null。</exception>
    public static PinCollectionState FromVisibility(IEnumerable<bool> visibility)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        return new PinCollectionState(visibility.ToArray());
    }

    /// <summary>贴图数量。</summary>
    public int Count => _visible.Length;

    /// <summary>是否没有贴图。</summary>
    public bool IsEmpty => _visible.Length == 0;

    /// <summary>当前可见的贴图数量。</summary>
    public int VisibleCount
    {
        get
        {
            var count = 0;
            foreach (var visible in _visible)
            {
                if (visible)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>当前隐藏的贴图数量。</summary>
    public int HiddenCount => Count - VisibleCount;

    /// <summary>是否至少有一张贴图可见。</summary>
    public bool HasVisible => VisibleCount > 0;

    /// <summary>是否所有贴图都被隐藏（空集合为 false，因为没有"贴图"可藏）。</summary>
    public bool AllHidden => Count > 0 && VisibleCount == 0;

    /// <summary>第 <paramref name="index"/> 张贴图是否可见。</summary>
    /// <exception cref="ArgumentOutOfRangeException">索引越界。</exception>
    public bool IsVisible(int index)
    {
        if ((uint)index >= (uint)_visible.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"索引必须落在 [0,{_visible.Length}) 内。");
        }

        return _visible[index];
    }

    /// <summary>改变单张贴图的可见性，返回新状态（原状态不变）。</summary>
    public PinCollectionState WithVisible(int index, bool isVisible)
    {
        _ = IsVisible(index); // 复用越界检查
        if (_visible[index] == isVisible)
        {
            return this;
        }

        var copy = (bool[])_visible.Clone();
        copy[index] = isVisible;
        return new PinCollectionState(copy);
    }

    /// <summary>按当前状态决定批量操作：空集合 → None；有可见 → HideAll；全隐 → ShowAll。</summary>
    public PinBulkAction DecideBulkAction() => IsEmpty
        ? PinBulkAction.None
        : HasVisible
            ? PinBulkAction.HideAll
            : PinBulkAction.ShowAll;

    /// <summary>全部隐藏（已经是全部隐藏时原样返回）。</summary>
    public PinCollectionState HideAll() => Resolve(PinBulkAction.HideAll);

    /// <summary>全部显示（已经全部可见时原样返回）。</summary>
    public PinCollectionState ShowAll() => Resolve(PinBulkAction.ShowAll);

    /// <summary>切换：有可见就全部隐藏，全部隐藏就全部显示（空集合无操作）。</summary>
    public PinCollectionState ToggleAll() => Resolve(DecideBulkAction());

    /// <summary>执行指定批量操作，返回新状态（原状态不变；<see cref="PinBulkAction.None"/> 为无操作）。</summary>
    public PinCollectionState Resolve(PinBulkAction action)
    {
        switch (action)
        {
            case PinBulkAction.HideAll:
                return IsEmpty || AllHidden ? this : new PinCollectionState(new bool[Count]);

            case PinBulkAction.ShowAll:
                if (Count == 0 || HiddenCount == 0)
                {
                    return this;
                }

                var allVisible = new bool[Count];
                Array.Fill(allVisible, true);
                return new PinCollectionState(allVisible);

            default:
                return this;
        }
    }

    /// <inheritdoc />
    public bool Equals(PinCollectionState? other)
    {
        if (other is null || other.Count != Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (_visible[i] != other._visible[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PinCollectionState);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var visible in _visible)
        {
            hash.Add(visible);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        IsEmpty ? "贴图集合=空" : $"贴图集合={Count} 张（可见 {VisibleCount} 张，隐藏 {HiddenCount} 张）";
}
