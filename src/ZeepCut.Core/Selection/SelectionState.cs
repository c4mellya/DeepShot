namespace ZeepCut.Core.Selection;

/// <summary>
/// 选区交互状态。状态转移规则见 <see cref="SelectionController"/>。
/// </summary>
public enum SelectionState
{
    /// <summary>空闲：没有选区，也没有悬停候选窗口。</summary>
    None = 0,

    /// <summary>鼠标悬停在一个窗口候选上，尚未按下。</summary>
    HoverWindow,

    /// <summary>鼠标已按下，正在拖拽生成新选区。</summary>
    Dragging,

    /// <summary>已有有效选区，鼠标已抬起。</summary>
    Selected,

    /// <summary>鼠标按在已有选区上，正在整体移动或调整边缘/角。</summary>
    Adjusting,
}
