namespace DeepShot.Core.Editing;

/// <summary>
/// 一条可撤销的编辑命令。约定 <see cref="Execute"/> 可以被调用多次
///（首次执行与之后的重做），<see cref="Undo"/> 必须把它恢复到执行前的状态。
/// </summary>
/// <remarks>
/// 命令应保存执行所需的旧值（快照），不要在 <see cref="Undo"/> 时再去推断；
/// 这样无论中间发生什么，撤销结果都是确定的。命令本身不判断当前状态是否合法，
/// 由 <see cref="EditHistory"/> 保证 Undo/Redo 的调用顺序正确。
/// </remarks>
public interface IEditCommand
{
    /// <summary>给日志/UI 用的简短说明，例如"添加矩形"。</summary>
    string Description { get; }

    /// <summary>执行（也用于重做）。</summary>
    void Execute();

    /// <summary>撤销，恢复到执行前的状态。</summary>
    void Undo();
}
