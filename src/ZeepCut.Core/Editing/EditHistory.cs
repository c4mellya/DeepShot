namespace ZeepCut.Core.Editing;

/// <summary>
/// 撤销/重做历史：两条命令栈（撤销栈 + 重做栈），容量有上限，超出时丢弃最旧的一步。
/// </summary>
/// <remarks>
/// <para>
/// 语义与常见编辑器一致：
/// <list type="bullet">
/// <item><see cref="Execute"/> 执行命令、压入撤销栈，并**清空重做栈**（产生了新分支）。</item>
/// <item><see cref="Undo"/> 撤销栈顶命令并把它移到重做栈。</item>
/// <item><see cref="Redo"/> 重做栈顶命令并把它移回撤销栈。</item>
/// <item>空栈时 <see cref="Undo"/>/<see cref="Redo"/> 返回 false，不抛异常。</item>
/// <item>撤销栈超过 <see cref="Capacity"/> 时丢弃最旧（栈底）的命令；被丢弃的历史无法再撤销。</item>
/// </list>
/// </para>
/// <para>
/// 本类不持有文档，具体修改由 <see cref="IEditCommand"/> 完成；这样同一套历史可以
/// 同时用于截图标注编辑窗口与历史再编辑窗口。
/// </para>
/// </remarks>
public sealed class EditHistory
{
    /// <summary>默认容量（步数）。</summary>
    public const int DefaultCapacity = 100;

    private readonly List<IEditCommand> _undoStack = new();
    private readonly List<IEditCommand> _redoStack = new();

    /// <param name="capacity">最多保留多少步可撤销历史，必须为正数。</param>
    public EditHistory(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "撤销容量必须为正数。");
        }

        Capacity = capacity;
    }

    /// <summary>最多保留的可撤销步数。</summary>
    public int Capacity { get; }

    /// <summary>是否可撤销。</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>是否可重做。</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>撤销栈当前步数。</summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>重做栈当前步数。</summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>下一次撤销对应的说明；没有可撤销命令时为 null。</summary>
    public string? NextUndoDescription => CanUndo ? _undoStack[^1].Description : null;

    /// <summary>下一次重做对应的说明；没有可重做命令时为 null。</summary>
    public string? NextRedoDescription => CanRedo ? _redoStack[^1].Description : null;

    /// <summary>
    /// 执行一条新命令并记录到撤销栈；重做栈被清空。命令抛异常时历史不变。
    /// </summary>
    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Execute();
        _undoStack.Add(command);
        TrimUndoStack();
        _redoStack.Clear();
    }

    /// <summary>撤销一步；没有可撤销命令时返回 false。</summary>
    public bool Undo()
    {
        if (_undoStack.Count == 0)
        {
            return false;
        }

        var command = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        command.Undo();
        _redoStack.Add(command);
        return true;
    }

    /// <summary>重做一步；没有可重做命令时返回 false。</summary>
    public bool Redo()
    {
        if (_redoStack.Count == 0)
        {
            return false;
        }

        var command = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        command.Execute();
        _undoStack.Add(command);
        TrimUndoStack();
        return true;
    }

    /// <summary>清空撤销与重做历史（不影响文档内容）。</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void TrimUndoStack()
    {
        while (_undoStack.Count > Capacity)
        {
            _undoStack.RemoveAt(0);
        }
    }
}
