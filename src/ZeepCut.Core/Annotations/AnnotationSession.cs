using ZeepCut.Core.Editing;
using ZeepCut.Core.Geometry;

namespace ZeepCut.Core.Annotations;
/// <summary>
/// 一次截图标注会话：标注文档 + 撤销历史 + 当前工具设置 + 草稿/文字输入状态。
/// </summary>
/// <remarks>
/// <para>
/// UI（<c>CaptureOverlayWindow</c>）只负责把鼠标 DIP 换算成图像物理像素、绘制与键盘，
/// 标注对象怎么创建、什么时候进文档、撤销/重做怎么走，全部由这里决定，因此在无桌面环境
/// 也能单测。
/// </para>
/// <para>
/// 状态机（互斥）：
/// <list type="bullet">
/// <item>空闲：<see cref="Draft"/> 与 <see cref="PendingText"/> 都为空。</item>
/// <item>拖拽草稿：<see cref="BeginDraft"/> → <see cref="UpdateDraft"/> → <see cref="CommitDraft"/>
/// 或 <see cref="CancelDraft"/>；<see cref="AnnotationDraft.Tool"/> 为文字工具时工厂返回空，
/// 文字走 <see cref="BeginText"/>。</item>
/// <item>文字输入：<see cref="BeginText"/> → <see cref="UpdateTextPreview"/> →
/// <see cref="CommitText"/> 或 <see cref="CancelText"/>。</item>
/// </list>
/// 同时开启两种状态会抛 <see cref="InvalidOperationException"/>（UI 必须先提交/取消前一个）。
/// </para>
/// <para>
/// <b>选区锁定</b>（T14 决策）：<see cref="IsSelectionLocked"/> 在文档已有标注，
/// 或正在拖拽草稿/输入文字时为真。锁定后调用方不得再移动/调整选区，
/// 否则已有标注相对选区的图像坐标会错位。全部标注被撤销（文档为空且无草稿）后自动解锁。
/// 注意锁定只看“有标注内容”，与 <see cref="ActiveTool"/> 是否选中无关：只选中工具、还没落笔时
/// 仍允许调整选区（UI 会自己隐藏手柄以免误触）。
/// </para>
/// <para>
/// <see cref="ActiveTool"/> 为 <c>null</c> 表示“选择”模式（不绘制，交给选区状态机）；
/// 非空时会同步到 <see cref="Settings"/>，保证新建标注使用当前工具/颜色/粗细。
/// 只影响**之后**创建的标注，已有标注的属性修改属于 T16。
/// </para>
/// </remarks>
public sealed class AnnotationSession
{
    private AnnotationDraft? _draft;
    private TextAnnotation? _pendingText;

    /// <param name="historyCapacity">撤销步数上限，必须为正数。</param>
    public AnnotationSession(int historyCapacity = EditHistory.DefaultCapacity)
    {
        if (historyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(historyCapacity), historyCapacity, "撤销容量必须为正数。");
        }

        History = new EditHistory(historyCapacity);
    }

    /// <summary>
    /// 带图像范围的构造（T16）：提供 <see cref="Editing"/> 用来选择/移动/缩放/删除已有标注。
    /// </summary>
    /// <param name="imageBounds">截图图像范围，通常是 <c>(0,0,width,height)</c>（物理像素）。</param>
    /// <param name="historyCapacity">撤销步数上限，必须为正数。</param>
    public AnnotationSession(PixelRect imageBounds, int historyCapacity = EditHistory.DefaultCapacity)
        : this(historyCapacity)
    {
        Editing = new AnnotationEditController(Document, History, imageBounds);
    }

    /// <summary>
    /// 标注编辑状态机（选中 / 拖动 / 缩放 / 删除）。用 <see cref="AnnotationSession(int)"/>
    /// 构造时为 <c>null</c>（纯绘制场景不需要），用它构造时非空。
    /// </summary>
    public AnnotationEditController? Editing { get; }

    /// <summary>是否有选中的标注（选中态优先于选区操作与 Esc 取消）。</summary>
    public bool HasSelection => Editing?.HasSelection == true;

    /// <summary>已提交的标注（Z 序底 → 顶）。</summary>
    public AnnotationDocument Document { get; } = new();

    /// <summary>撤销/重做历史。</summary>
    public EditHistory History { get; }

    /// <summary>新标注使用的工具、颜色、粗细、字号。</summary>
    public AnnotationToolSettings Settings { get; } = new();

    /// <summary>
    /// 当前绘制工具；<c>null</c> 表示“选择”模式（不绘制标注，允许调整选区）。
    /// </summary>
    public AnnotationTool? ActiveTool { get; private set; }

    /// <summary>进行中的拖拽草稿；没有时为 <c>null</c>。</summary>
    public AnnotationDraft? Draft => _draft;

    /// <summary>正在输入但尚未提交的文字标注；没有时为 <c>null</c>。</summary>
    public TextAnnotation? PendingText => _pendingText;

    /// <summary>
    /// 实时预览用的标注对象：优先返回拖拽草稿的标注（草稿即最终对象），
    /// 其次是正在输入的文字。没有时返回 <c>null</c>。
    /// </summary>
    public Annotation? Preview => (Annotation?)_draft?.Annotation ?? _pendingText;

    /// <summary>是否正在拖拽绘制。</summary>
    public bool IsDrafting => _draft is not null;

    /// <summary>是否正在输入文字。</summary>
    public bool IsTextEditing => _pendingText is not null;

    /// <summary>文档中的标注数量。</summary>
    public int Count => Document.Count;

    /// <summary>
    /// 选区是否已锁定（开始标注后为真）：文档已有标注，或正在草稿/文字输入。
    /// 锁定时调用方必须禁止移动/调整选区。
    /// </summary>
    public bool IsSelectionLocked =>
        Document.Count > 0 || _draft is not null || _pendingText is not null;

    /// <summary>是否有可撤销的命令。</summary>
    public bool CanUndo => History.CanUndo;

    /// <summary>是否有可重做的命令。</summary>
    public bool CanRedo => History.CanRedo;

    /// <summary>
    /// 切换绘制工具；<c>null</c> 表示回到“选择”模式。非空时同步写入
    /// <see cref="Settings"/>，新建标注即使用该工具。
    /// </summary>
    /// <exception cref="InvalidOperationException">正在草稿或文字输入时不允许切换工具。</exception>
    public void SetActiveTool(AnnotationTool? tool)
    {
        if (_draft is not null || _pendingText is not null)
        {
            throw new InvalidOperationException("正在绘制/输入文字时不能切换工具，请先提交或取消。");
        }

        if (tool.HasValue)
        {
            Settings.Tool = tool.Value;
        }

        ActiveTool = tool;

        // 选了绘制工具就退出选中态（否则点选区内会变成"编辑已有标注"而不是画新的）。
        if (tool.HasValue)
        {
            Editing?.Select(null);
        }
    }

    /// <summary>
    /// 在图像物理像素处开始一次拖拽草稿，使用当前 <see cref="ActiveTool"/>、颜色与粗细。
    /// </summary>
    /// <returns>草稿；文字工具（没有拖拽语义）返回 <c>null</c>，调用方应改用 <see cref="BeginText"/>。</returns>
    /// <exception cref="InvalidOperationException">已有草稿或正在输入文字。</exception>
    public AnnotationDraft? BeginDraft(PixelPoint start)
    {
        EnsureIdle("开始新的标注草稿");

        var tool = ActiveTool ?? throw new InvalidOperationException("没有选中绘制工具（当前是选择模式）。");
        _draft = AnnotationDraftFactory.Begin(tool, start, Settings.Color, Settings.Thickness);
        return _draft;
    }

    /// <summary>把当前草稿拖到新的图像物理像素；没有草稿时返回 false。</summary>
    public bool UpdateDraft(PixelPoint point)
    {
        if (_draft is null)
        {
            return false;
        }

        _draft.DragTo(point);
        return true;
    }

    /// <summary>
    /// 提交当前草稿：有意义的草稿经 <see cref="AddAnnotationCommand"/> 进入文档并可撤销；
    /// 退化操作（单击、零长度线段）直接丢弃。
    /// </summary>
    /// <returns>是否真的加入了文档。</returns>
    public bool CommitDraft()
    {
        var draft = _draft;
        _draft = null;

        if (draft is null || !draft.IsMeaningful)
        {
            return false;
        }

        History.Execute(new AddAnnotationCommand(Document, draft.Annotation));
        return true;
    }

    /// <summary>丢弃当前草稿；返回是否真的丢弃了草稿。</summary>
    public bool CancelDraft()
    {
        if (_draft is null)
        {
            return false;
        }

        _draft = null;
        return true;
    }

    /// <summary>
    /// 在图像物理像素处开始文字输入：创建空的 <see cref="TextAnnotation"/> 作为预览对象，
    /// 字号取 <see cref="AnnotationToolSettings.FontSize"/>、颜色取当前颜色。
    /// </summary>
    /// <exception cref="InvalidOperationException">已有草稿或正在输入文字。</exception>
    public TextAnnotation BeginText(PixelPoint position)
    {
        EnsureIdle("开始文字输入");

        _pendingText = new TextAnnotation(position, string.Empty, Settings.FontSize, Settings.Color);
        return _pendingText;
    }

    /// <summary>更新正在输入的文字（实时预览）；没有输入时返回 false。</summary>
    public bool UpdateTextPreview(string? text)
    {
        if (_pendingText is null)
        {
            return false;
        }

        _pendingText.Text = text ?? string.Empty;
        return true;
    }

    /// <summary>
    /// 提交文字：内容为空或全空白时视为取消（不产生空标注）；否则加入文档并可撤销。
    /// </summary>
    /// <returns>是否真的加入了文档。</returns>
    public bool CommitText(string? text)
    {
        var pending = _pendingText;
        _pendingText = null;

        if (pending is null)
        {
            return false;
        }

        pending.Text = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pending.Text))
        {
            return false;
        }

        History.Execute(new AddAnnotationCommand(Document, pending));
        return true;
    }

    /// <summary>取消文字输入；返回是否真的取消了。</summary>
    public bool CancelText()
    {
        if (_pendingText is null)
        {
            return false;
        }

        _pendingText = null;
        return true;
    }

    /// <summary>撤销一步；正在草稿/输入文字时返回 false（不打断当前操作）。</summary>
    public bool Undo()
    {
        if (_draft is not null || _pendingText is not null || Editing is { IsDragging: true })
        {
            return false;
        }

        var done = History.Undo();

        // 撤销可能移除了当前选中的标注（例如撤销"添加矩形"），此时取消选中。
        Editing?.ValidateSelection();
        return done;
    }

    /// <summary>重做一步；正在草稿/输入文字时返回 false。</summary>
    public bool Redo()
    {
        if (_draft is not null || _pendingText is not null || Editing is { IsDragging: true })
        {
            return false;
        }

        var done = History.Redo();
        Editing?.ValidateSelection();
        return done;
    }

    /// <summary>
    /// 删除当前选中的标注（一条可撤销记录）；没有选中时返回 <c>null</c>。
    /// </summary>
    public string? DeleteSelected() =>
        _draft is not null || _pendingText is not null ? null : Editing?.DeleteSelected();

    /// <summary>取消选中；没有选中或正在拖拽时返回 false。</summary>
    public bool ClearSelection() => Editing?.ClearSelection() == true;

    private void EnsureIdle(string action)
    {
        if (_draft is not null)
        {
            throw new InvalidOperationException($"已有进行中的草稿，不能{action}。");
        }

        if (_pendingText is not null)
        {
            throw new InvalidOperationException($"正在输入文字，不能{action}。");
        }
    }
}
