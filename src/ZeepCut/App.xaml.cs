using System.Windows;
using System.Windows.Threading;
using ZeepCut.Capture;
using ZeepCut.Core;
using ZeepCut.Core.Shortcuts;
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

            _trayIcon = new TrayIcon();
            _trayIcon.CommandInvoked += OnTrayCommandInvoked;
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
                PasteFromClipboardPlaceholder("托盘菜单");
                break;

            case TrayMenuCommand.ToggleAllPins:
                ShowNotImplemented("显示/隐藏全部贴图");
                break;

            case TrayMenuCommand.History:
                ShowNotImplemented("历史");
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
        RegisterHotkey(PinShortcutText, () => PasteFromClipboardPlaceholder("全局热键 " + PinShortcutText));

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

    /// <summary>T6 调试入口：捕获鼠标所在屏幕并存 PNG，用于真实桌面验证。</summary>
    private void StartCapture(string trigger)
    {
        AppLog.Info("Capture", $"截图触发：{trigger}");
        try
        {
            var capture = ScreenCapture.CaptureMonitorUnderCursor();
            var path = ScreenCapture.SaveDebugPng(capture);
            AppLog.Info(
                "Capture",
                $"T6 捕获完成：屏幕={capture.DeviceName} 主屏={capture.IsPrimary} " +
                $"物理矩形={capture.Bounds} 图像={capture.PixelWidth}x{capture.PixelHeight} 文件={path}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Capture", "屏幕捕获失败", ex);
            NotifyUser("截图失败", $"屏幕捕获失败：{ex.Message}");
        }
    }

    // TODO(T18–T22): 读取剪贴板图片并创建贴图窗口。本轮仅保留热键与菜单入口。
    private void PasteFromClipboardPlaceholder(string trigger) =>
        AppLog.Info("Pins", $"从剪贴板贴图触发（{trigger}）：占位入口，T21/T22 实现");

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
        DisposeGlobalHotkey();
        DisposeTrayIcon();
        Shutdown();
    }

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
