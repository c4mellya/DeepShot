using System.Windows;
using System.Windows.Threading;
using ZeepCut.Core;
using ZeepCut.Platform;

namespace ZeepCut;

/// <summary>
/// 应用外壳：单实例、无主窗口的托盘常驻生命周期、全局未处理异常日志。
/// 首版没有主窗口，<c>ShutdownMode=OnExplicitShutdown</c>，只有托盘菜单“退出”才结束进程。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\ZeepCut.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIcon? _trayIcon;

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

            AppLog.Info("App", "启动完成：应用在系统托盘中运行");
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
                StartCapturePlaceholder();
                break;

            case TrayMenuCommand.PasteFromClipboard:
                PasteFromClipboardPlaceholder();
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

    // TODO(T5–T11): 接入全局热键、屏幕捕获与冻结遮罩流程。本轮仅保留菜单入口。
    private void StartCapturePlaceholder() =>
        AppLog.Info("Capture", "截图命令：占位入口，尚未接入截图流程");

    // TODO(T18–T22): 读取剪贴板图片并创建贴图窗口。本轮仅保留菜单入口。
    private void PasteFromClipboardPlaceholder() =>
        AppLog.Info("Pins", "从剪贴板贴图命令：占位入口，尚未实现");

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
        DisposeTrayIcon();
        Shutdown();
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
