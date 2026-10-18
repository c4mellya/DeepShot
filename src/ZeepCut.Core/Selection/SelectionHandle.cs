namespace ZeepCut.Core.Selection;

/// <summary>
/// 已选区域上被抓住的部位：八个调整方向，以及整个选区。
/// </summary>
public enum SelectionHandle
{
    /// <summary>没有抓住任何部位（未选中或点在选区外）。</summary>
    None = 0,

    /// <summary>抓住选区内部，整体移动。</summary>
    Move,

    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}
