using System.Windows.Media.Imaging;
using DeepShot.Core.Geometry;
using DeepShot.Core.Pins;
using DeepShot.Platform;

namespace DeepShot.Pins;

/// <summary>
/// 贴图集合管理（T18 创建 / T20 全局显隐 / T21 剪贴板贴图 / T22 历史接缝）：
/// 应用的**唯一**贴图注册表，负责创建、计数、全局显示/隐藏与清理。
/// </summary>
/// <remarks>
/// <para>
/// 贴图窗口不要在别处直接 new：所有创建路径（截图“贴图”按钮、Alt+S 剪贴板、
/// 以后的历史图库）都必须走 <see cref="Create"/> / <see cref="CreateFromClipboard"/>，
/// 这样才有人维护集合、记住创建时的状态、并在需要时统一隐藏/显示。
/// </para>
/// <para>
/// <b>全局隐藏不销毁实例</b>（PLAN）：<see cref="HideAll"/> 只把窗口的
/// <c>Visibility</c> 设为 <c>Collapsed</c> 并记录每个窗口创建时的位置/尺寸/透明度，
/// <see cref="ShowAll"/> 恢复同一份快照。窗口对象、图片、集合条目全程保留，
/// 因此显示回来后缩放比例、位置和透明度与隐藏前**完全一致**。
/// </para>
/// <para>
/// 贴图窗口自己关闭（Esc / × / 右键关闭）时回调 <see cref="NotifyClosed"/> 注销；
/// 应用退出时调用 <see cref="CloseAll"/> 一次性关掉全部（PLAN：退出不恢复贴图）。
/// </para>
/// </remarks>
public static class PinWindows
{
    private static readonly List<PinWindow> Open = new();

    /// <summary>
    /// 历史联动（T22 接缝 / T24 真实实现）。默认是只写日志的
    /// <see cref="LoggingPinHistoryRecorder"/>；App 启动时用
    /// <see cref="SetHistoryRecorder"/> 换成 <see cref="HistoryPinRecorder"/>。
    /// </summary>
    private static IPinHistoryRecorder _historyRecorder = new LoggingPinHistoryRecorder();

    /// <summary>被 <see cref="HideAll"/> 隐藏的窗口及其隐藏前状态快照（顺序与 <see cref="Open"/> 一致）。</summary>
    private static readonly Dictionary<PinWindow, PinWindowSnapshot> HiddenSnapshots = new();

    /// <summary>当前存活的贴图窗口（只读快照）。</summary>
    public static IReadOnlyList<PinWindow> All => Open.ToArray();

    /// <summary>当前存活的贴图数量（含被隐藏的；隐藏不销毁实例）。</summary>
    public static int Count => Open.Count;

    /// <summary>当前可见的贴图数量。</summary>
    public static int VisibleCount
    {
        get
        {
            var count = 0;
            foreach (var pin in Open)
            {
                if (pin.IsShown)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>是否至少有一张贴图可见（托盘菜单文案与“没有贴图”判断用）。</summary>
    public static bool HasVisible => VisibleCount > 0;

    /// <summary>是否所有贴图都被隐藏（空集合为 false）。</summary>
    public static bool AllHidden => Open.Count > 0 && VisibleCount == 0;

    /// <summary>把当前集合投影成 Core 的可见性状态（T20 的“有可见就全隐、全隐就全显”在 Core 判定）。</summary>
    public static PinCollectionState Snapshot() =>
        PinCollectionState.FromVisibility(Open.Select(pin => pin.IsShown));

    /// <summary>替换历史记录实现（App 启动用 <see cref="HistoryPinRecorder"/>；测试或调试可换成其它实现）。</summary>
    public static void SetHistoryRecorder(IPinHistoryRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        _historyRecorder = recorder;
    }

    /// <summary>
    /// 用一张已合成的图片创建贴图窗口，并摆到 <paramref name="selection"/>（屏幕物理像素）。
    /// </summary>
    /// <param name="image">贴图内容（通常是 T17 的合成图），会以 1:1 物理像素显示。</param>
    /// <param name="selection">贴图在屏幕上的初始物理矩形（来自截图选区）。</param>
    /// <param name="screenBounds">截图所在屏幕的物理矩形，仅用于日志。</param>
    /// <param name="dpi">截图所在屏幕的 DPI，用于物理像素→DIP 换算。</param>
    /// <param name="source">贴图来源；默认按内部截图处理（历史已由 ICaptureSink 记录，不重复记）。</param>
    /// <returns>已显示并登记的贴图窗口。</returns>
    public static PinWindow Create(
        BitmapSource image,
        PixelRect selection,
        PixelRect screenBounds,
        int dpi,
        PinSource source = PinSource.Screenshot)
    {
        ArgumentNullException.ThrowIfNull(image);

        var pin = new PinWindow(image, selection, screenBounds, dpi);
        pin.Show();
        Open.Add(pin);

        AppLog.Info(
            "Pins",
            $"贴图已加入集合：当前 {Open.Count} 张，来源={PinSourceText.Describe(source)} " +
            $"物理={pin.Bounds} DPI={pin.ScreenDpi} 图像={image.PixelWidth}x{image.PixelHeight}");

        RecordHistory(pin, source);
        return pin;
    }

    /// <summary>
    /// T21：从剪贴板取图并创建贴图（Alt+S / 托盘“从剪贴板贴图”）。
    /// 贴图放在光标附近，并夹在光标所在屏幕内。
    /// </summary>
    /// <param name="cursor">光标屏幕物理坐标（GetCursorPos）。</param>
    /// <param name="display">当前屏幕集合（含负坐标副屏），用于把贴图夹进光标所在屏幕。</param>
    /// <returns>创建结果：成功时带窗口与日志描述，失败时带面向用户的中文原因。</returns>
    public static ClipboardPinResult CreateFromClipboard(PixelPoint cursor, DisplaySpace display)
    {
        ArgumentNullException.ThrowIfNull(display);

        var clipboard = ClipboardImage.TryRead();
        if (!clipboard.Success)
        {
            AppLog.Warn("Pins", $"从剪贴板贴图失败：状态={clipboard.Status} 格式={clipboard.Format} 原因={clipboard.Message}");
            return ClipboardPinResult.Failed(clipboard.Status, clipboard.Message);
        }

        var image = clipboard.Image!;
        var monitor = display.FindByPoint(cursor.X, cursor.Y);
        var bounds = PinPlacement.NearCursor(cursor, image.PixelWidth, image.PixelHeight, monitor.Bounds);

        try
        {
            var pin = Create(image, bounds, monitor.Bounds, monitor.Dpi, PinSource.Clipboard);
            AppLog.Info(
                "Pins",
                $"剪贴板贴图完成：光标={cursor} 屏幕={monitor.DeviceName} DPI={monitor.Dpi} " +
                $"摆放={bounds} 图像={image.PixelWidth}x{image.PixelHeight} 格式={clipboard.Format}");

            // 贴图是 Topmost 新窗口：激活它，Esc/滚轮/拖动可以立刻用。
            pin.Activate();
            return ClipboardPinResult.Succeeded(pin, clipboard.Format, image.PixelWidth, image.PixelHeight);
        }
        catch (Exception ex)
        {
            AppLog.Error("Pins", "从剪贴板创建贴图窗口失败", ex);
            return ClipboardPinResult.Failed(ClipboardImageStatus.Failed, $"创建贴图失败：{ex.Message}");
        }
    }

    /// <summary>
    /// T22 接缝：从历史记录创建贴图（本轮**还没有历史图库**，方法先留在这里给 T25/T28 用）。
    /// 语义按要求固定：<b>不新增历史记录</b>，因为原记录已经存在于历史里。
    /// </summary>
    /// <param name="image">历史记录的图片（调用方已按历史尺寸读入）。</param>
    /// <param name="bounds">贴图的屏幕物理矩形。</param>
    /// <param name="screenBounds">目标屏幕物理矩形。</param>
    /// <param name="dpi">目标屏幕 DPI。</param>
    /// <returns>已显示并登记的贴图窗口。</returns>
    public static PinWindow CreateFromHistory(
        BitmapSource image,
        PixelRect bounds,
        PixelRect screenBounds,
        int dpi)
    {
        AppLog.Info(
            "Pins",
            "从历史创建贴图：历史图库（T25）尚未实现，本次调用只创建贴图、不新增历史记录");
        return Create(image, bounds, screenBounds, dpi, PinSource.History);
    }

    /// <summary>隐藏全部贴图（不销毁实例）：记录窗口当前的位置/尺寸/透明度后折叠。</summary>
    /// <returns>实际被隐藏的贴图数量（已经全隐时为 0）。</returns>
    public static int HideAll()
    {
        if (Open.Count == 0)
        {
            AppLog.Info("Pins", "隐藏全部贴图：当前没有贴图，忽略");
            return 0;
        }

        var action = Snapshot().DecideBulkAction();
        if (action != PinBulkAction.HideAll)
        {
            AppLog.Info("Pins", $"隐藏全部贴图：已经全部隐藏（{Open.Count} 张），忽略");
            return 0;
        }

        var hidden = 0;
        foreach (var pin in Open)
        {
            if (!pin.IsShown)
            {
                continue;
            }

            var snapshot = pin.CaptureSnapshot();
            HiddenSnapshots[pin] = snapshot;
            pin.HideForBulk();
            hidden++;
            AppLog.Info(
                "Pins",
                $"已隐藏贴图：{snapshot.State.Bounds} 缩放={snapshot.State.Scale:0.###}x " +
                $"透明度={snapshot.State.Opacity:0.##} 锁定={snapshot.State.IsLocked}");
        }

        AppLog.Info("Pins", $"隐藏全部贴图完成：隐藏 {hidden} 张，集合仍有 {Open.Count} 张（实例未销毁）");
        return hidden;
    }

    /// <summary>显示全部贴图：恢复隐藏前记录的位置/尺寸/透明度。</summary>
    /// <returns>实际被显示的贴图数量（本来就没有隐藏项时为 0）。</returns>
    public static int ShowAll()
    {
        if (Open.Count == 0)
        {
            AppLog.Info("Pins", "显示全部贴图：当前没有贴图，忽略");
            return 0;
        }

        var shown = 0;
        foreach (var pin in Open)
        {
            if (pin.IsShown)
            {
                continue;
            }

            var restored = HiddenSnapshots.Remove(pin, out var snapshot)
                ? pin.ShowFromBulk(snapshot)
                : pin.ShowFromBulk(null);

            shown++;
            AppLog.Info(
                "Pins",
                $"已显示贴图：{pin.Bounds} 缩放={pin.State.Scale:0.###}x " +
                $"透明度={pin.State.Opacity:0.##} 状态已恢复={restored}");
        }

        HiddenSnapshots.Clear();
        AppLog.Info("Pins", $"显示全部贴图完成：显示 {shown} 张，当前可见 {VisibleCount} 张");
        return shown;
    }

    /// <summary>
    /// 切换全部显隐：有可见就全部隐藏，全部隐藏就全部显示（空集合无操作）。
    /// </summary>
    /// <returns>本次执行的批量动作，供调用方提示用户。</returns>
    public static PinBulkAction ToggleAll()
    {
        var state = Snapshot();
        var action = state.DecideBulkAction();
        switch (action)
        {
            case PinBulkAction.HideAll:
                HideAll();
                break;

            case PinBulkAction.ShowAll:
                ShowAll();
                break;

            default:
                AppLog.Info("Pins", "切换全部贴图显隐：当前没有贴图，忽略");
                break;
        }

        return action;
    }

    /// <summary>贴图窗口关闭时从集合移除（由 <see cref="PinWindow"/> 调用，不要手动重复移除）。</summary>
    internal static void NotifyClosed(PinWindow pin)
    {
        HiddenSnapshots.Remove(pin);
        if (!Open.Remove(pin))
        {
            return;
        }

        AppLog.Info("Pins", $"贴图已从集合移除：剩余 {Open.Count} 张");
    }

    /// <summary>关闭全部贴图（应用退出时调用；PLAN：退出不恢复贴图）。</summary>
    public static void CloseAll()
    {
        if (Open.Count == 0)
        {
            return;
        }

        var closing = Open.ToArray();
        Open.Clear();
        HiddenSnapshots.Clear();
        AppLog.Info("Pins", $"退出：关闭全部贴图 {closing.Length} 张");

        foreach (var pin in closing)
        {
            try
            {
                pin.Close();
            }
            catch (Exception ex)
            {
                AppLog.Warn("Pins", $"关闭贴图失败（继续关闭其它贴图）：{ex.Message}");
            }
        }
    }

    /// <summary>贴图创建后的历史接缝（T22）：只对“外部剪贴板”来源记录一次。</summary>
    private static void RecordHistory(PinWindow pin, PinSource source)
    {
        try
        {
            _historyRecorder.OnPinCreated(
                new PinHistoryEntry(source, pin.Image, pin.Bounds, DateTime.Now));
        }
        catch (Exception ex)
        {
            // 历史写失败绝不能影响贴图本身（PLAN：历史缺失不阻塞本地功能）。
            AppLog.Error("Pins", $"贴图历史记录失败（忽略，不影响贴图）：来源={PinSourceText.Describe(source)}", ex);
        }
    }
}

/// <summary>
/// <see cref="PinWindows.CreateFromClipboard"/> 的结果：成功带窗口，失败带原因（供 UI 提示）。
/// </summary>
public sealed record ClipboardPinResult(
    bool Success,
    ClipboardImageStatus Status,
    string Message,
    PinWindow? Pin,
    string Format,
    int PixelWidth,
    int PixelHeight)
{
    public static ClipboardPinResult Succeeded(PinWindow pin, string format, int width, int height) =>
        new(true, ClipboardImageStatus.Success, $"已贴图 {width}×{height}", pin, format, width, height);

    public static ClipboardPinResult Failed(ClipboardImageStatus status, string message) =>
        new(false, status, message, null, "-", 0, 0);
}
