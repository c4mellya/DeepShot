using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ZeepCut.Capture;
using ZeepCut.Core;
using ZeepCut.Core.Capture;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.History;
using ZeepCut.Core.Pins;
using ZeepCut.Core.Shortcuts;
using ZeepCut.History;
using ZeepCut.Output;
using ZeepCut.Pins;
using ZeepCut.Platform;

namespace ZeepCut;

/// <summary>
/// 应用外壳：单实例、无主窗口的托盘常驻生命周期、全局未处理异常日志。
/// 首版没有主窗口，<c>ShutdownMode=OnExplicitShutdown</c>，只有托盘菜单“退出”才结束进程。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\ZeepCut.SingleInstance";

    // 首版默认快捷键；T29 设置窗口接入后改为从设置读取。
    private const string ScreenshotShortcutText = "Alt+A";
    private const string PinShortcutText = "Alt+S";

    private readonly Dictionary<ShortcutSpec, Action> _hotkeyActions = new();
    private readonly List<string> _hotkeyRegistrationFailures = new();

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIcon? _trayIcon;
    private GlobalHotkey? _globalHotkey;
    private CaptureOverlayWindow? _captureOverlay;

    /// <summary>
    /// 历史写入器（T23/T24）：启动时初始化数据库，之后由 <see cref="HistoryCaptureSink"/>
    /// 与 <see cref="HistoryPinRecorder"/> 共用。初始化失败时为 null，历史功能整体降级
    /// （截图/贴图照常工作，只是不记录）。
    /// </summary>
    private HistoryStore? _historyStore;

    /// <summary>
    /// 截图成功输出的接收端（T24）：写一条历史。历史不可用时退回只写日志的
    /// <see cref="LoggingCaptureSink"/>（复制/保存/贴图本身不受影响）。
    /// </summary>
    private ICaptureSink _captureSink = new LoggingCaptureSink();

    /// <summary>T24：截图历史的 sink 实例（用于读取 <c>LastFailure</c> 并提示用户）。</summary>
    private HistoryCaptureSink? _historyCaptureSink;

    /// <summary>
    /// T27：30 天清理调度器（启动清一次 + 跨日再清）。历史不可用时为 null。
    /// </summary>
    private HistoryRetentionScheduler? _retentionScheduler;

    public App()
    {
        // 全局异常钩子尽早安装，启动阶段（含托盘创建）的异常也能落盘。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!EnsureSingleInstance())
        {
            Shutdown();
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            AppLog.Initialize();
            AppLog.Info("App", "生命周期：无主窗口，托盘常驻（ShutdownMode=OnExplicitShutdown）");

            InitializeHistory();

            _trayIcon = new TrayIcon();
            _trayIcon.CommandInvoked += OnTrayCommandInvoked;
            _trayIcon.ToggleAllPinsTextProvider = DescribeToggleAllPinsText;
            _trayIcon.ToggleAllPinsDisabledProvider = () => PinWindows.Count == 0;
            if (!_trayIcon.Show())
            {
                AppLog.Error("App", "托盘图标加入通知区域失败，程序无法驻留");
                MessageBox.Show(
                    "Zeep Cut 无法创建托盘图标，程序将退出。",
                    AppInfo.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            StartGlobalHotkeys();
            StartHistoryRetention();
            AppLog.Info("App", "启动完成：应用在系统托盘中运行，全局热键已处理");
        }
        catch (Exception ex)
        {
            AppLog.Error("App", "启动失败", ex);
            try
            {
                MessageBox.Show(
                    $"Zeep Cut 启动失败，已记录日志：\n{AppLog.CurrentLogPath}\n\n{ex.Message}",
                    AppInfo.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception dialogFailure)
            {
                AppLog.Error("App", "显示启动失败提示时再次出错", dialogFailure);
            }

            Shutdown(1);
        }
    }

    /// <summary>
    /// T23：启动时初始化历史数据库（首次运行建库建表，已有库做版本检查），
    /// 并把 T24 的写入实现接到截图与贴图两条路径上。
    /// </summary>
    /// <remarks>
    /// 历史初始化失败**不阻塞启动**（PLAN：历史缺失不阻塞本地功能）：只写 ERROR 日志、
    /// 弹一次提示，然后截图 sink 退回 T11 的 <see cref="LoggingCaptureSink"/>，
    /// 贴图 recorder 退回 <see cref="LoggingPinHistoryRecorder"/>。
    /// </remarks>
    private void InitializeHistory()
    {
        try
        {
            // 数据库固定放在数据根目录：%LOCALAPPDATA%\ZeepCut\history.db（ARCHITECTURE §7）。
            var repository = new HistoryRepository(
                AppPaths.DataRoot,
                Path.Combine(AppPaths.DataRoot, HistoryRepository.DatabaseFileName));
            var schema = repository.Initialize();

            _historyStore = new HistoryStore(repository);
            _historyCaptureSink = new HistoryCaptureSink(_historyStore);
            _captureSink = _historyCaptureSink;
            PinWindows.SetHistoryRecorder(new HistoryPinRecorder(_historyStore));

            AppLog.Info(
                "History",
                $"历史库已就绪：{repository.DatabasePath} " +
                $"版本 {schema.VersionBefore} → {schema.VersionAfter} " +
                $"新建={schema.Created} 迁移步数={schema.MigratedSteps} " +
                $"记录数={_historyStore.Count()} 图片目录={repository.ImagesDirectory}");

            // T28：覆盖说明只在跨启动的第一次保存时出现（PLAN：首次使用时解释）。
            HistoryUiState.Load();
            HistoryEditWindow.ApplyPersistedExplanation(HistoryUiState.OverwriteExplained);
            HistoryEditWindow.OnExplanationShown = HistoryUiState.MarkOverwriteExplained;

            if (schema.IsNewerThanSupported)
            {
                AppLog.Warn(
                    "History",
                    $"历史库版本 {schema.VersionBefore} 高于本程序支持的 {schema.VersionAfter}，" +
                    "本次只读不降级（可能缺少新版本才有的列）");
            }
        }
        catch (Exception ex)
        {
            _historyStore = null;
            _historyCaptureSink = null;
            _captureSink = new LoggingCaptureSink();
            PinWindows.SetHistoryRecorder(new LoggingPinHistoryRecorder());

            AppLog.Error("History", "历史库初始化失败，本次运行不记录历史（截图/贴图不受影响）", ex);
            NotifyUser(
                "历史功能不可用",
                $"无法初始化历史数据库，本次运行的截图与贴图不会被记录：\n{ex.Message}");
        }
    }

    /// <summary>
    /// T27：启动 30 天清理并开启跨日检查。历史不可用时什么也不做。
    /// </summary>
    /// <remarks>
    /// 清理规则在 Core（<see cref="HistoryRetentionPolicy"/>）：按**创建时间**算 30 天、
    /// 编辑与重命名不延期、**收藏豁免**；删除走 <see cref="HistoryRepository.Delete(Guid)"/>
    /// 同一条路径（行 + images/ + thumbs/ 内部文件），用户另存为到别处的文件不在库内、
    /// 不会被清理碰到。清理失败只写日志，不影响截图/贴图/图库。
    /// </remarks>
    private void StartHistoryRetention()
    {
        if (_historyStore is null)
        {
            AppLog.Info("History", "30 天清理未启动：历史库本次不可用");
            return;
        }

        try
        {
            _retentionScheduler = new HistoryRetentionScheduler(_historyStore, OnHistoryPurged);
            _retentionScheduler.Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "30 天清理调度器启动失败（历史功能仍可用，只是不再自动清理）", ex);
            _retentionScheduler = null;
        }
    }

    /// <summary>清理删除了记录后的收尾：刷新已打开的图库（否则用户看到的还是被删掉的卡片）。</summary>
    private void OnHistoryPurged(int deletedCount, HistoryRetentionResult result)
    {
        AppLog.Info("History", $"30 天清理已删除 {deletedCount} 条记录：{result}");

        // 图库没打开时不需要任何提示（启动时清理是"静默的家务活"）。
        if (!HistoryGallery.IsOpen)
        {
            return;
        }

        HistoryGallery.ReloadIfOpen($"30 天清理删除了 {deletedCount} 条过期记录");
    }

    /// <summary>截图/贴图历史写入失败时提示一次（主操作已经成功，只提"历史没记上"）。</summary>
    private void NotifyHistoryFailureIfAny(string action)
    {
        var failure = _historyCaptureSink?.LastFailure;
        if (string.IsNullOrEmpty(failure))
        {
            return;
        }

        AppLog.Warn("History", $"{action}已成功，但历史保存失败：{failure}");
        ToastWindow.Show($"{action}已成功，但历史保存失败（详见日志）。");
    }

    /// <summary>
    /// 命名互斥体实现单实例。已有实例时只做简短提示后退出（首版不实现 IPC 激活）。
    /// </summary>
    private bool EnsureSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (createdNew)
            {
                _ownsSingleInstanceMutex = true;
                AppLog.Info("App", $"已获得单实例互斥体 {SingleInstanceMutexName}");
                return true;
            }
        }
        catch (Exception ex)
        {
            // 互斥体异常不阻塞启动（例如极端权限问题），但必须留下记录。
            AppLog.Error("App", "单实例互斥体创建失败，继续启动", ex);
            return true;
        }

        AppLog.Info("App", "检测到已有实例，提示后退出");
        MessageBox.Show(
            "Zeep Cut 已在运行。\n\n请使用任务栏通知区域（如未显示，请点击“显示隐藏的图标”）中的托盘图标。",
            AppInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void OnTrayCommandInvoked(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.Screenshot:
                StartCapture("托盘菜单");
                break;

            case TrayMenuCommand.PasteFromClipboard:
                PasteFromClipboard("托盘菜单");
                break;

            case TrayMenuCommand.ToggleAllPins:
                ToggleAllPins("托盘菜单");
                break;

            case TrayMenuCommand.History:
                ShowHistoryGallery("托盘菜单");
                break;

            case TrayMenuCommand.Settings:
                ShowNotImplemented("设置");
                break;

            case TrayMenuCommand.CheckUpdates:
                ShowNotImplemented("检查更新");
                break;

            case TrayMenuCommand.Exit:
                ExitFromTray();
                break;
        }
    }

    /// <summary>
    /// 创建全局热键接收窗口并注册默认快捷键。单个注册失败只提示该快捷键，
    /// 不影响另一个继续注册（ALT+S 仍可用）。
    /// </summary>
    private void StartGlobalHotkeys()
    {
        _hotkeyRegistrationFailures.Clear();

        try
        {
            _globalHotkey = new GlobalHotkey();
            _globalHotkey.Pressed += OnGlobalHotkeyPressed;
        }
        catch (Exception ex)
        {
            AppLog.Error("Hotkey", "全局热键消息窗口创建失败，快捷键不可用", ex);
            NotifyUser("全局热键不可用", $"无法创建热键消息窗口：{ex.Message}");
            return;
        }

        RegisterHotkey(ScreenshotShortcutText, () => StartCapture("全局热键 " + ScreenshotShortcutText));
        RegisterHotkey(PinShortcutText, () => PasteFromClipboard("全局热键 " + PinShortcutText));

        if (_hotkeyRegistrationFailures.Count > 0)
        {
            // 托盘气球可能被系统通知设置抑制；再弹一次启动提示，确保用户知道快捷键不可用。
            var details = string.Join(Environment.NewLine, _hotkeyRegistrationFailures);
            AppLog.Warn("Hotkey", $"启动时共有 {_hotkeyRegistrationFailures.Count} 个热键注册失败，已提示用户");
            MessageBox.Show(
                details,
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RegisterHotkey(string text, Action action)
    {
        if (!ShortcutSpec.TryParse(text, out var shortcut, out var parseError) || shortcut is null)
        {
            var message = $"快捷键配置“{text}”无效：{parseError}";
            AppLog.Error("Hotkey", message);
            NotifyUser("快捷键配置无效", message);
            _hotkeyRegistrationFailures.Add(message);
            return;
        }

        var result = _globalHotkey!.Register(shortcut);
        if (!result.Success)
        {
            var message = result.DescribeFailure();
            AppLog.Warn("Hotkey", message);
            NotifyUser("全局热键注册失败", message);
            _hotkeyRegistrationFailures.Add(message);
            return;
        }

        _hotkeyActions[shortcut] = action;
    }

    private void OnGlobalHotkeyPressed(ShortcutSpec shortcut)
    {
        if (_hotkeyActions.TryGetValue(shortcut, out var action))
        {
            action();
            return;
        }

        AppLog.Warn("Hotkey", $"热键 {shortcut.Format()} 没有绑定动作");
    }

    /// <summary>
    /// 截图入口：先捕获鼠标所在屏幕、枚举窗口候选，再显示冻结遮罩。
    /// 捕获与枚举都发生在遮罩显示之前，因此遮罩不会进入画面，也不会成为候选。
    /// </summary>
    private void StartCapture(string trigger)
    {
        if (_captureOverlay is not null)
        {
            AppLog.Info("Capture", $"已有截图遮罩显示中，忽略本次触发：{trigger}");
            return;
        }

        AppLog.Info("Capture", $"截图触发：{trigger}");
        try
        {
            var capture = ScreenCapture.CaptureMonitorUnderCursor();
            var dip = new DpiTransform(capture.Dpi).ToDip(capture.Bounds);

            IReadOnlyList<WindowCandidate> candidates;
            try
            {
                candidates = WindowEnumerator.Enumerate(capture.Bounds);
            }
            catch (Exception ex)
            {
                // 窗口识别失败不应阻断冻结遮罩：仍可显示画面、按 Esc 取消。
                AppLog.Error("Capture", "窗口候选枚举失败，本次仅显示冻结图", ex);
                candidates = Array.Empty<WindowCandidate>();
            }

            AppLog.Info(
                "Capture",
                $"冻结遮罩：屏幕={capture.DeviceName} 主屏={capture.IsPrimary} 物理={capture.Bounds} " +
                $"图像={capture.PixelWidth}x{capture.PixelHeight} DPI={capture.Dpi} " +
                $"→ DIP=({dip.Left:0.##},{dip.Top:0.##}) {dip.Width:0.##}x{dip.Height:0.##}，" +
                $"窗口候选={candidates.Count}");

            var overlay = new CaptureOverlayWindow(capture, candidates, _captureSink);
            overlay.Closed += OnCaptureOverlayClosed;
            _captureOverlay = overlay;

            try
            {
                overlay.Show();
            }
            catch
            {
                // 显示失败时清掉引用，避免后续触发被误判为“已有遮罩显示中”。
                overlay.Closed -= OnCaptureOverlayClosed;
                _captureOverlay = null;
                throw;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture", "屏幕捕获或冻结遮罩显示失败", ex);
            NotifyUser("截图失败", $"截图失败：{ex.Message}");
        }
    }

    private void OnCaptureOverlayClosed(object? sender, EventArgs e)
    {
        if (sender is CaptureOverlayWindow overlay)
        {
            overlay.Closed -= OnCaptureOverlayClosed;
            AppLog.Info(
                "Capture",
                $"截图结束：屏幕={overlay.ScreenBounds} DPI={overlay.ScreenDpi} " +
                $"选区={overlay.Selection} 已定稿={overlay.IsSelectionReady} " +
                $"已取消={overlay.WasCancelled} 完成动作={(overlay.CompletedAction?.ToString() ?? "无")}");

            // T24：历史写入失败不影响已经成功的复制/保存/贴图，只单独提示一次。
            if (overlay.CompletedAction is { } completed)
            {
                NotifyHistoryFailureIfAny(LoggingCaptureSink.Describe(completed));
            }
        }

        _captureOverlay = null;
    }

    // --- T21 剪贴板贴图 ---

    /// <summary>
    /// Alt+S / 托盘“从剪贴板贴图”：读取剪贴板图片，在光标附近创建一张贴图。
    /// 无图 / 非图片格式 / 剪贴板被占用都只提示，不抛异常、不影响已有贴图。
    /// </summary>
    private void PasteFromClipboard(string trigger)
    {
        AppLog.Info("Pins", $"从剪贴板贴图触发：{trigger}");

        if (!NativeMethods.GetCursorPos(out var cursorPoint))
        {
            var error = Marshal.GetLastWin32Error();
            AppLog.Warn("Pins", $"GetCursorPos 失败（Win32Error={error}），改按主屏中心摆放贴图");
        }

        var cursor = new PixelPoint(cursorPoint.X, cursorPoint.Y);
        DisplaySpace display;
        try
        {
            display = MonitorLayout.ReadDisplaySpace();
        }
        catch (Exception ex)
        {
            AppLog.Error("Pins", "读取屏幕布局失败，无法定位剪贴板贴图", ex);
            NotifyPinFailure("无法定位屏幕位置，贴图失败。");
            return;
        }

        var result = PinWindows.CreateFromClipboard(cursor, display);
        if (result.Success)
        {
            AppLog.Info(
                "Pins",
                $"剪贴板贴图成功：{result.Message} 格式={result.Format} 位置={result.Pin?.Bounds} " +
                $"当前共 {PinWindows.Count} 张");
            return;
        }

        AppLog.Warn("Pins", $"剪贴板贴图失败：状态={result.Status} 原因={result.Message}");
        NotifyPinFailure(result.Message);
    }

    /// <summary>剪贴板贴图失败的提示：轻量提示窗口（可见性可靠）+ 托盘通知 + WARN 日志。</summary>
    private void NotifyPinFailure(string message)
    {
        ToastWindow.Show(message);
        _trayIcon?.ShowNotification("从剪贴板贴图失败", message, warning: true);
    }

    // --- T20 贴图全局显隐 ---

    /// <summary>托盘“显示/隐藏全部贴图”的文案：随当前状态变化（T20）。</summary>
    private static string? DescribeToggleAllPinsText()
    {
        if (PinWindows.Count == 0)
        {
            return TrayIcon.ToggleAllPinsDefaultText;
        }

        var state = PinWindows.Snapshot();
        return state.HasVisible
            ? $"隐藏全部贴图 ({state.VisibleCount})"
            : $"显示全部贴图 ({state.Count})";
    }

    /// <summary>
    /// 托盘“显示/隐藏全部贴图”：有可见就全部隐藏，全部隐藏就全部显示。
    /// 隐藏不销毁实例；没有贴图时给出提示（菜单项同时是灰显的）。
    /// </summary>
    private void ToggleAllPins(string trigger)
    {
        var state = PinWindows.Snapshot();
        AppLog.Info("Pins", $"全局显隐触发：{trigger}，当前 {state}");

        if (state.IsEmpty)
        {
            AppLog.Info("Pins", "全局显隐：当前没有贴图，提示用户");
            ToastWindow.Show("当前没有贴图，先按 Alt+A 截图或 Alt+S 从剪贴板贴图。");
            return;
        }

        var action = PinWindows.ToggleAll();
        switch (action)
        {
            case PinBulkAction.HideAll:
                AppLog.Info("Pins", $"全局显隐结果：已隐藏全部贴图，仍保留 {PinWindows.Count} 张实例");
                ToastWindow.Show($"已隐藏 {PinWindows.Count} 张贴图（再次点击托盘菜单可显示）");
                break;

            case PinBulkAction.ShowAll:
                AppLog.Info("Pins", $"全局显隐结果：已显示全部贴图，当前可见 {PinWindows.VisibleCount} 张");
                ToastWindow.Show($"已显示 {PinWindows.VisibleCount} 张贴图");
                break;

            default:
                AppLog.Info("Pins", "全局显隐结果：无操作");
                break;
        }
    }

    /// <summary>托盘通知优先；系统拒绝通知时退回 MessageBox，保证失败不会静默。</summary>
    private void NotifyUser(string title, string message)
    {
        if (_trayIcon?.ShowNotification(title, message, warning: true) == true)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                message,
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Error("App", "显示失败提示时出错", ex);
        }
    }

    // --- T25 历史图库 ---

    /// <summary>
    /// 托盘“历史”：打开历史图库（单实例）。历史库初始化失败时说明原因，不弹“尚未实现”。
    /// </summary>
    private void ShowHistoryGallery(string trigger)
    {
        AppLog.Info("History", $"历史图库触发：{trigger}");

        if (_historyStore is null)
        {
            AppLog.Warn("History", "历史图库不可用：本次启动历史库初始化失败");
            MessageBox.Show(
                "历史库本次启动初始化失败，图库不可用。\n\n截图与贴图仍可正常使用，" +
                $"但不会记录历史。详情见日志：\n{AppLog.CurrentLogPath}",
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = HistoryGallery.Show(_historyStore);
        if (window is null)
        {
            MessageBox.Show(
                "打开历史图库失败，详情见日志：\n" + AppLog.CurrentLogPath,
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        AppLog.Info(
            "History",
            $"历史图库已显示：显示中={window.IsVisible} 库内记录={_historyStore.Count()} " +
            $"图片目录={_historyStore.Repository.ImagesDirectory}");
    }

    private void ShowNotImplemented(string feature)
    {
        AppLog.Info("App", $"菜单命令“{feature}”尚未实现");
        MessageBox.Show(
            $"“{feature}”尚未实现，将在后续版本提供。",
            AppInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitFromTray()
    {
        AppLog.Info("App", "退出：用户选择托盘菜单“退出”");
        DisposeCaptureOverlay();
        DisposeHistoryRetention();
        DisposeHistoryGallery();
        DisposePins();
        DisposeGlobalHotkey();
        DisposeTrayIcon();
        Shutdown();
    }

    /// <summary>退出时关闭仍在显示的遮罩（视作取消，不产生历史）。</summary>
    private void DisposeCaptureOverlay()
    {
        if (_captureOverlay is null)
        {
            return;
        }

        var overlay = _captureOverlay;
        _captureOverlay = null;
        overlay.Closed -= OnCaptureOverlayClosed;
        overlay.Cancel();
    }

    /// <summary>退出时停止 30 天清理调度器（不删任何东西，下次启动会再清一次）。</summary>
    private void DisposeHistoryRetention()
    {
        _retentionScheduler?.Dispose();
        _retentionScheduler = null;
    }

    /// <summary>退出时关闭历史图库窗口（历史数据留在磁盘上，关闭窗口不动数据库）。</summary>
    private void DisposeHistoryGallery() => HistoryGallery.CloseIfOpen();

    /// <summary>退出时关闭全部贴图（PLAN：退出不恢复贴图，也不销毁历史）。</summary>
    private void DisposePins() => PinWindows.CloseAll();

    private void DisposeGlobalHotkey()
    {
        if (_globalHotkey is null)
        {
            _hotkeyActions.Clear();
            return;
        }

        _globalHotkey.Pressed -= OnGlobalHotkeyPressed;
        _globalHotkey.Dispose();
        _globalHotkey = null;
        _hotkeyActions.Clear();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.CommandInvoked -= OnTrayCommandInvoked;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeCaptureOverlay();
        DisposeHistoryRetention();
        DisposeHistoryGallery();
        DisposePins();
        DisposeGlobalHotkey();
        DisposeTrayIcon();

        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 未持有互斥体（例如启动失败路径）时忽略。
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        AppLog.Info("App", $"退出完成 (exit code={e.ApplicationExitCode})");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("DispatcherUnhandledException", "UI 线程未处理异常", e.Exception);
        e.Handled = true;
        try
        {
            MessageBox.Show(
                $"Zeep Cut 遇到未处理的错误，已记录日志：\n{AppLog.CurrentLogPath}\n\n{e.Exception.Message}",
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception dialogFailure)
        {
            AppLog.Error("App", "显示未处理异常提示失败", dialogFailure);
        }
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error(
            "AppDomain.UnhandledException",
            $"进程级未处理异常（IsTerminating={e.IsTerminating}）",
            e.ExceptionObject as Exception);
    }

    private static void OnTaskSchedulerUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("TaskScheduler.UnobservedTaskException", "后台任务未观测异常", e.Exception);
        e.SetObserved();
    }
}
