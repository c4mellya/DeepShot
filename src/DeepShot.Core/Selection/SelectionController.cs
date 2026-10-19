using DeepShot.Core.Geometry;

namespace DeepShot.Core.Selection;

/// <summary>
/// 纯逻辑的框选状态机：接收鼠标按下/移动/抬起和窗口候选，产出物理像素选区。
/// 不依赖 WPF 或 Win32，所有几何结果都夹取在构造时给定的目标屏幕内。
/// </summary>
/// <remarks>
/// <para>合法状态转移（其余输入一律忽略，不抛异常、不改变状态）：</para>
/// <code>
/// None ──悬停到候选窗口──▶ HoverWindow
/// None / HoverWindow ──按下──▶ Dragging
/// Dragging ──拖动──▶ Dragging（任意方向都得到正矩形）
/// Dragging ──抬起（达到最小尺寸）──▶ Selected
/// Dragging ──抬起（不足最小尺寸）──▶ 采用按下时的候选窗口，否则 None
/// Selected ──按在选区内──▶ Adjusting（手柄决定移动或调整）
/// Selected ──按在选区外──▶ Dragging（重新框选）
/// Adjusting ──拖动──▶ Adjusting（移动或八向调整，夹在屏幕内）
/// Adjusting ──抬起──▶ Selected
/// 任意状态 ──Cancel──▶ None
/// </code>
/// <para>
/// 未选中时 <see cref="HitTest"/> 返回 <see cref="SelectionHandle.None"/>，
/// <see cref="ActiveHandle"/> 也只在 <see cref="SelectionState.Adjusting"/> 期间非空。
/// </para>
/// <para>
/// 调整边缘时的明确规则：<b>不翻转</b>。拖过对面边缘后停在最小尺寸处
/// （例如拖动左边缘越过右边缘，左边缘停在 <c>右边缘 - MinimumSize</c>）；
/// 拖出屏幕时停在屏幕边缘。整体移动保持尺寸，被屏幕夹回。
/// </para>
/// </remarks>
public sealed class SelectionController
{
    private readonly List<PixelRect> _windowCandidates = new();

    private int _lastPointerX;
    private int _lastPointerY;
    private bool _hasPointer;

    private int _dragStartX;
    private int _dragStartY;
    private PixelRect? _pressedHoverCandidate;

    private PixelRect _adjustStartSelection;
    private int _adjustStartX;
    private int _adjustStartY;
    private SelectionHandle _activeHandle;

    /// <param name="screenBounds">目标屏幕的物理像素矩形，所有选区结果都限制在其中。</param>
    /// <param name="minimumSize">可完成截图的最小宽/高（像素），默认 4。</param>
    /// <param name="handleTolerance">边缘/角手柄的命中容差（像素），默认 4。</param>
    public SelectionController(PixelRect screenBounds, int minimumSize = 4, int handleTolerance = 4)
    {
        if (screenBounds.IsEmpty)
        {
            throw new ArgumentException("目标屏幕不能为空。", nameof(screenBounds));
        }

        if (minimumSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSize), minimumSize, "最小尺寸必须为正数。");
        }

        if (minimumSize > Math.Min(screenBounds.Width, screenBounds.Height))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSize),
                minimumSize,
                "最小尺寸不能大于目标屏幕的宽或高。");
        }

        if (handleTolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handleTolerance), handleTolerance, "手柄容差必须为正数。");
        }

        ScreenBounds = screenBounds;
        MinimumSize = minimumSize;
        HandleTolerance = handleTolerance;
    }

    /// <summary>目标屏幕的物理像素矩形。</summary>
    public PixelRect ScreenBounds { get; }

    /// <summary>可完成截图的最小宽/高（像素）。</summary>
    public int MinimumSize { get; }

    /// <summary>边缘/角手柄的命中容差（像素）。</summary>
    public int HandleTolerance { get; }

    /// <summary>当前状态。</summary>
    public SelectionState State { get; private set; }

    /// <summary>
    /// 当前选区（物理像素）。仅在 <see cref="SelectionState.Dragging"/>、
    /// <see cref="SelectionState.Selected"/>、<see cref="SelectionState.Adjusting"/> 时非空。
    /// </summary>
    public PixelRect Selection { get; private set; } = PixelRect.Empty;

    /// <summary>
    /// 悬停中的窗口候选（已裁剪到目标屏幕）。仅在 <see cref="SelectionState.HoverWindow"/> 时非空。
    /// </summary>
    public PixelRect? HoverCandidate { get; private set; }

    /// <summary>调整中抓住的手柄；非 <see cref="SelectionState.Adjusting"/> 时为 <see cref="SelectionHandle.None"/>。</summary>
    public SelectionHandle ActiveHandle => State == SelectionState.Adjusting ? _activeHandle : SelectionHandle.None;

    /// <summary>当前选区是否已达到可完成截图的最小尺寸（仅已定稿的选区为真）。</summary>
    public bool IsCaptureReady => State == SelectionState.Selected && MeetsMinimumSize(Selection);

    /// <summary>当前保存的窗口候选（已裁剪到屏幕，按传入顺序命中）。</summary>
    public IReadOnlyList<PixelRect> WindowCandidates => _windowCandidates;

    /// <summary>
    /// 更新窗口候选。调用方应按 Z 序从上到下传入，命中时取第一个包含指针的候选；
    /// 候选先与目标屏幕求交集，完全在屏幕外的候选被丢弃。
    /// </summary>
    public void SetWindowCandidates(IEnumerable<PixelRect> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        _windowCandidates.Clear();
        foreach (var candidate in candidates)
        {
            var clipped = candidate.Intersect(ScreenBounds);
            if (!clipped.IsEmpty)
            {
                _windowCandidates.Add(clipped);
            }
        }

        // 候选可能晚于鼠标位置到达；空闲时用最后一次指针位置重新判定悬停。
        if (_hasPointer && State is SelectionState.None or SelectionState.HoverWindow)
        {
            UpdateHover(_lastPointerX, _lastPointerY);
        }
    }

    /// <summary>鼠标按下（左键）。未选中时开始新框选；已选中且落在选区内时进入调整。</summary>
    public void PointerDown(int x, int y)
    {
        RememberPointer(x, y);

        switch (State)
        {
            case SelectionState.Selected:
                var handle = HitTest(x, y);
                if (handle != SelectionHandle.None)
                {
                    _adjustStartSelection = Selection;
                    _adjustStartX = x;
                    _adjustStartY = y;
                    _activeHandle = handle;
                    State = SelectionState.Adjusting;
                }
                else
                {
                    BeginDrag(x, y, pressedCandidate: null);
                }

                break;

            case SelectionState.None:
            case SelectionState.HoverWindow:
                BeginDrag(x, y, HoverCandidate);
                break;

            default:
                // Dragging / Adjusting 期间重复按下忽略，避免覆盖起始锚点。
                break;
        }
    }

    /// <summary>鼠标移动（左键未按下）。仅在空闲/悬停时更新候选，不会清除已定稿的选区。</summary>
    public void PointerMoved(int x, int y)
    {
        RememberPointer(x, y);

        if (State is SelectionState.None or SelectionState.HoverWindow)
        {
            UpdateHover(x, y);
        }
    }

    /// <summary>鼠标拖动（左键按下）。仅在框选/调整期间生效，其它状态下忽略。</summary>
    public void PointerDragged(int x, int y)
    {
        RememberPointer(x, y);

        switch (State)
        {
            case SelectionState.Dragging:
                Selection = BuildDragSelection(_dragStartX, _dragStartY, x, y);
                break;

            case SelectionState.Adjusting:
                Selection = ApplyAdjustment(_activeHandle, x, y);
                break;

            default:
                break;
        }
    }

    /// <summary>鼠标抬起（左键）。定稿选区，或在尺寸不足时回退/采用候选窗口。</summary>
    public void PointerUp(int x, int y)
    {
        RememberPointer(x, y);

        switch (State)
        {
            case SelectionState.Dragging:
                // 以抬起位置定稿，避免丢失最后一次拖动事件。
                Selection = BuildDragSelection(_dragStartX, _dragStartY, x, y);

                if (MeetsMinimumSize(Selection))
                {
                    State = SelectionState.Selected;
                }
                else if (_pressedHoverCandidate is { } candidate && MeetsMinimumSize(candidate))
                {
                    // 视为单击窗口：采用按下时的候选窗口（已裁剪到屏幕）。
                    Selection = candidate;
                    State = SelectionState.Selected;
                }
                else
                {
                    Selection = PixelRect.Empty;
                    State = SelectionState.None;
                    UpdateHover(x, y);
                }

                _pressedHoverCandidate = null;
                break;

            case SelectionState.Adjusting:
                Selection = ApplyAdjustment(_activeHandle, x, y);
                _activeHandle = SelectionHandle.None;
                State = SelectionState.Selected;
                break;

            default:
                break;
        }
    }

    /// <summary>取消当前交互：清空选区并回到 <see cref="SelectionState.None"/>。任何状态下都可调用。</summary>
    public void Cancel()
    {
        _pressedHoverCandidate = null;
        _activeHandle = SelectionHandle.None;
        Selection = PixelRect.Empty;
        HoverCandidate = null;
        State = SelectionState.None;
    }

    /// <summary>
    /// 判断像素点落在选区的哪个手柄上。未选中、点在选区外或点正好在右/下开边界上时返回
    /// <see cref="SelectionHandle.None"/>；角优先于边，边优先于整体移动。
    /// </summary>
    public SelectionHandle HitTest(int x, int y)
    {
        if (State is not (SelectionState.Selected or SelectionState.Adjusting) || Selection.IsEmpty)
        {
            return SelectionHandle.None;
        }

        if (!Selection.Contains(x, y))
        {
            return SelectionHandle.None;
        }

        var rect = Selection;
        var tolerance = Math.Max(1, Math.Min(HandleTolerance, Math.Min(rect.Width, rect.Height) / 3));
        var nearLeft = x < rect.Left + tolerance;
        var nearRight = x >= rect.Right - tolerance;
        var nearTop = y < rect.Top + tolerance;
        var nearBottom = y >= rect.Bottom - tolerance;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => SelectionHandle.TopLeft,
            (_, true, true, _) => SelectionHandle.TopRight,
            (true, _, _, true) => SelectionHandle.BottomLeft,
            (_, true, _, true) => SelectionHandle.BottomRight,
            (true, _, _, _) => SelectionHandle.Left,
            (_, true, _, _) => SelectionHandle.Right,
            (_, _, true, _) => SelectionHandle.Top,
            (_, _, _, true) => SelectionHandle.Bottom,
            _ => SelectionHandle.Move,
        };
    }

    /// <summary>矩形是否达到最小宽高（空矩形一律为假）。</summary>
    public bool MeetsMinimumSize(PixelRect rect) =>
        !rect.IsEmpty && rect.Width >= MinimumSize && rect.Height >= MinimumSize;

    private void RememberPointer(int x, int y)
    {
        _lastPointerX = x;
        _lastPointerY = y;
        _hasPointer = true;
    }

    private void BeginDrag(int x, int y, PixelRect? pressedCandidate)
    {
        _dragStartX = x;
        _dragStartY = y;
        _pressedHoverCandidate = pressedCandidate;
        _activeHandle = SelectionHandle.None;
        HoverCandidate = null;
        Selection = PixelRect.Empty;
        State = SelectionState.Dragging;
    }

    private void UpdateHover(int x, int y)
    {
        PixelRect? match = null;

        foreach (var candidate in _windowCandidates)
        {
            if (candidate.Contains(x, y))
            {
                match = candidate;
                break;
            }
        }

        HoverCandidate = match;
        Selection = PixelRect.Empty;
        State = match.HasValue ? SelectionState.HoverWindow : SelectionState.None;
    }

    /// <summary>
    /// 由按下点和当前点生成正矩形。两个端点先夹到屏幕边界（含右/下开边界，
    /// 这样从屏幕内部拖到边缘能得到完整尺寸），因此结果不会越出屏幕。
    /// </summary>
    private PixelRect BuildDragSelection(int startX, int startY, int x, int y) =>
        PixelRect.FromTwoPoints(
            Math.Clamp(startX, ScreenBounds.Left, ScreenBounds.Right),
            Math.Clamp(startY, ScreenBounds.Top, ScreenBounds.Bottom),
            Math.Clamp(x, ScreenBounds.Left, ScreenBounds.Right),
            Math.Clamp(y, ScreenBounds.Top, ScreenBounds.Bottom));

    private PixelRect ApplyAdjustment(SelectionHandle handle, int x, int y)
    {
        if (handle == SelectionHandle.Move)
        {
            var offsetX = x - _adjustStartX;
            var offsetY = y - _adjustStartY;
            return _adjustStartSelection.Offset(offsetX, offsetY).Clamp(ScreenBounds);
        }

        // 以按下瞬间的选区为锚点，避免逐帧累积误差。
        var left = _adjustStartSelection.Left;
        var top = _adjustStartSelection.Top;
        var right = _adjustStartSelection.Right;
        var bottom = _adjustStartSelection.Bottom;

        var px = Math.Clamp(x, ScreenBounds.Left, ScreenBounds.Right);
        var py = Math.Clamp(y, ScreenBounds.Top, ScreenBounds.Bottom);

        // 拖过对面边缘时钳制在最小尺寸，不翻转。
        if (handle is SelectionHandle.Left or SelectionHandle.TopLeft or SelectionHandle.BottomLeft)
        {
            left = Math.Clamp(px, ScreenBounds.Left, right - MinimumSize);
        }

        if (handle is SelectionHandle.Right or SelectionHandle.TopRight or SelectionHandle.BottomRight)
        {
            right = Math.Clamp(px, left + MinimumSize, ScreenBounds.Right);
        }

        if (handle is SelectionHandle.Top or SelectionHandle.TopLeft or SelectionHandle.TopRight)
        {
            top = Math.Clamp(py, ScreenBounds.Top, bottom - MinimumSize);
        }

        if (handle is SelectionHandle.Bottom or SelectionHandle.BottomLeft or SelectionHandle.BottomRight)
        {
            bottom = Math.Clamp(py, top + MinimumSize, ScreenBounds.Bottom);
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }
}
