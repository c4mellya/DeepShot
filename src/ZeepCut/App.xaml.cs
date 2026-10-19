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
using ZeepCut.Core.Settings;
using ZeepCut.Core.Shortcuts;
using ZeepCut.Core.Updates;
using ZeepCut.History;
using ZeepCut.Output;
using ZeepCut.Pins;
using ZeepCut.Platform;
using ZeepCut.Settings;
using ZeepCut.Ui;
using ZeepCut.Updates;

namespace ZeepCut;

/// <summary>
/// 应用外壳：单实例、无主窗口的托盘常驻生命周期、全局未处理异常日志。
/// 首版没有主窗口，<c>ShutdownMode=OnExplicitShutdown</c>，只有托盘菜单“退出”才结束进程。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\ZeepCut.SingleInstance";

    private readonly Dictionary<ShortcutSpec, Action> _hotkeyActions = new();
    private readonly List<string> _hotkeyRegistrationFailures = new();

    /// <summary>
    /// T29：设置存储器（<c>%LOCALAPPDATA%\ZeepCut\settings.json</c>）。启动最早读它，
    /// 之后主题/语言/热键/开机启动全部以它为准。读取失败时是默认值（<see cref="AppSettings.Default"/>）。
    /// </summary>
    private SettingsStore? _settingsStore;

    /// <summary>当前生效的设置（<see cref="_settingsStore"/>.Current 的便取）。</summary>
    private AppSettings Settings => _settingsStore?.Current ?? AppSettings.Default;

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIcon? _trayIcon;
    private GlobalHotkey? _globalHotkey;
    private bool _capturingShortcut;
    private CaptureOverlayWindow? _captureOverlay;
    private HistoryQuickViewWindow? _historyQuickView;
    private long _quickViewClosedAt = -1000;

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

            // T29/T30/T31：设置 → 主题 → 语言，必须**先于任何窗口**（托盘菜单第一次弹出就用当前语言）。
            InitializeSettings();
            PublishShortcutHints();
            InitializeTheme();
            InitializeLanguage();

            InitializeHistory();

            _trayIcon = new TrayIcon();
            _trayIcon.CommandInvoked += OnTrayCommandInvoked;
            _trayIcon.QuickViewRequested += OnTrayQuickViewRequested;
            _trayIcon.ToggleAllPinsTextProvider = DescribeToggleAllPinsText;
            _trayIcon.ToggleAllPinsDisabledProvider = () => PinWindows.Count == 0;
            if (!_trayIcon.Show())
            {
                AppLog.Error("App", "托盘图标加入通知区域失败，程序无法驻留");
                MessageBox.Show(
                    StringsUi.Get("S.Message.TrayFailed"),
                    AppInfo.Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // T29：开机启动以设置为准同步注册表（用户手工删了启动项也会补回来）。
            SyncStartupRegistration("启动");

            StartGlobalHotkeys();
            StartHistoryRetention();
            WelcomeGuide.ShowIfNeeded(Settings.ScreenshotShortcut, Settings.PinShortcut);
            AppLog.Info("App", "启动完成：应用在系统托盘中运行，全局热键已处理");
        }
        catch (Exception ex)
        {
            AppLog.Error("App", "启动失败", ex);
            try
            {
                MessageBox.Show(
                    StringsUi.Format("S.Message.StartupFailed", AppLog.CurrentLogPath, ex.Message),
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

    // --- T29/T30/T31 设置 / 主题 / 语言 ---

    /// <summary>
    /// T29：读取 <c>settings.json</c>（缺失/损坏/非法值都会回退到默认值并写日志，绝不阻塞启动）。
    /// </summary>
    private void InitializeSettings()
    {
        try
        {
            _settingsStore = new SettingsStore();
            var result = _settingsStore.Load();
            AppLog.Info(
                "Settings",
                $"启动设置就绪：{_settingsStore.Current}（读取={result.Status} " +
                $"回退={result.Warnings.Count} 项 文件={_settingsStore.FilePath}）");
        }
        catch (Exception ex)
        {
            // 连默认路径都不可用时（极端的权限问题）仍按默认值运行：设置缺失不阻塞本地功能。
            _settingsStore = null;
            AppLog.Error("Settings", "设置初始化失败，本次按默认设置运行（不会写回设置文件）", ex);
        }
    }

    /// <summary>
    /// T30：把主题资源键写进应用资源字典并应用设置里的主题。
    /// </summary>
    /// <remarks>
    /// 必须在创建任何窗口之前调用：窗口构造时会取 <see cref="ThemeManager.Palette"/> 的语义色，
    /// 晚于窗口就会"先浅色再闪一下变深色"。
    /// </remarks>
    private void InitializeTheme()
    {
        try
        {
            var theme = Settings.Theme;
            var changed = ThemeManager.Initialize(this, theme);
            AppLog.Info(
                "Theme",
                $"主题就绪：选择={theme} 生效={ThemeManager.EffectiveTheme} 变化={changed} " +
                $"系统浅色={ThemeManager.IsSystemUsingLightTheme()} 调色板={ThemeManager.Palette}");
        }
        catch (Exception ex)
        {
            // 主题失败不能阻止程序可用：界面会退回浅色（资源键缺失时 WPF 用默认色）。
            AppLog.Error("Theme", "主题初始化失败，本次使用默认浅色外观", ex);
        }
    }

    /// <summary>
    /// T31：按设置加载中英文资源字典（首次跟随系统语言），并让历史日期分组跟着语言走。
    /// </summary>
    private void InitializeLanguage()
    {
        try
        {
            StringsUi.Initialize(this, Settings.Language);
            AppLog.Info(
                "Lang",
                $"界面语言就绪：选择={Settings.Language} 生效={StringsUi.Current} 文案={StringsUi.AllKeys.Count} 条");
        }
        catch (Exception ex)
        {
            AppLog.Error("Lang", "语言初始化失败，界面显示 [键名]（功能不受影响）", ex);
        }
    }

    /// <summary>让注册表启动项与设置一致（启动时与设置里切换时都调）。</summary>
    private void SyncStartupRegistration(string trigger)
    {
        try
        {
            var enabled = Settings.LaunchAtStartup;
            var ok = StartupRegistration.Sync(enabled);
            AppLog.Info(
                "Startup",
                $"开机启动同步（{trigger}）：期望={(enabled ? "开启" : "关闭")} 结果={(ok ? "成功" : "失败")} " +
                $"注册表当前={(StartupRegistration.Read() ?? "(无)")}");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Startup", $"开机启动同步失败（{trigger}，忽略）：{ex.Message}");
        }
    }

    /// <summary>
    /// T29：把设置里的快捷键注册成全局热键。返回 null 表示全部成功，否则返回面向用户的原因。
    /// </summary>
    /// <remarks>
    /// 先 <see cref="GlobalHotkey.UnregisterAll"/> 再按新设置注册：修改快捷键时不能出现
    /// "旧组合还占着"（否则改回原值时注册会失败）。设置窗口在保存后立刻调用它，
    /// 因此新组合**立即生效**、旧组合**立即失效**，不需要重启。
    /// </remarks>
    internal string? ApplyHotkeys()
    {
        if (_globalHotkey is null)
        {
            return StringsUi.Get("S.Settings.Shortcut.RegistrationFailed");
        }

        _hotkeyActions.Clear();
        _hotkeyRegistrationFailures.Clear();
        _globalHotkey.UnregisterAll();

        var screenshotText = Settings.ScreenshotShortcut;
        var pinText = Settings.PinShortcut;
        var errors = new List<string>();

        var screenshotOk = RegisterHotkeyFor(
            screenshotText,
            () => StartCapture($"全局热键 {Settings.ScreenshotShortcut}"),
            errors);
        var pinOk = RegisterHotkeyFor(
            pinText,
            () => PasteFromClipboard($"全局热键 {Settings.PinShortcut}"),
            errors);

        if (!string.IsNullOrWhiteSpace(Settings.QuickViewShortcut))
            RegisterHotkeyFor(Settings.QuickViewShortcut, () => ToggleHistoryQuickView(anchor: _globalHotkey?.TriggerCursor), errors);

        AppLog.Info(
            "Hotkey",
            $"快捷键应用：截图={screenshotText}（{(screenshotOk ? "成功" : "失败")}） " +
            $"贴图={pinText}（{(pinOk ? "成功" : "失败")}） 已注册={_hotkeyActions.Count} 个");

        PublishShortcutHints();
        if (_capturingShortcut)
        {
            _globalHotkey.UnregisterAll();
            _hotkeyActions.Clear();
        }

        return errors.Count == 0
            ? null
            : string.Join(Environment.NewLine, errors);
    }

    internal void BeginShortcutCapture()
    {
        if (_capturingShortcut) return;
        _capturingShortcut = true;
        _globalHotkey?.UnregisterAll();
        _hotkeyActions.Clear();
    }

    internal void EndShortcutCapture()
    {
        if (!_capturingShortcut) return;
        _capturingShortcut = false;
        if (_globalHotkey is null) return;
        var failure = ApplyHotkeys();
        if (!string.IsNullOrEmpty(failure))
        {
            AppLog.Warn("Hotkey", $"结束快捷键录入后恢复失败：{failure}");
            ToastWindow.Show(failure);
        }
    }

    /// <summary>注册一个热键并绑定动作，失败时写日志 + 记录原因（不抛异常）。</summary>
    private bool RegisterHotkeyFor(string text, Action action, List<string> errors)
    {
        if (!ShortcutSpec.TryParse(text, out var shortcut, out _) || shortcut is null)
        {
            var message = StringsUi.Format("S.Message.HotkeyInvalid", text, string.Empty);
            AppLog.Warn("Hotkey", message);
            errors.Add(message);
            return false;
        }

        var result = _globalHotkey!.Register(shortcut);
        if (!result.Success)
        {
            var message = DescribeRegistrationFailure(shortcut, result);
            AppLog.Warn("Hotkey", message);
            errors.Add(message);
            return false;
        }

        _hotkeyActions[shortcut] = action;
        return true;
    }

    /// <summary>把当前快捷键告诉图库（空状态提示里写的是"按 X 截个图"）。</summary>
    private void PublishShortcutHints()
    {
        HistoryShortcutProvider.ScreenshotShortcut = Settings.ScreenshotShortcut;
        HistoryShortcutProvider.PinShortcut = Settings.PinShortcut;
    }

    /// <summary>注册失败原因（面向用户、随语言变化）；<see cref="HotkeyRegistrationResult"/> 的细节进日志。</summary>
    private static string DescribeRegistrationFailure(ShortcutSpec shortcut, HotkeyRegistrationResult result) =>
        result.IsAlreadyRegistered
            ? StringsUi.Format("S.Message.HotkeyBusy", shortcut.Format())
            : StringsUi.Format("S.Message.HotkeyRegisterError", shortcut.Format(), result.ErrorCode);

    /// <summary>设置窗口回报的外部状态（"已是最新版"/"尚未配置更新渠道"等写进日志，便于验证脚本 grep）。</summary>
    internal SettingsStore? SettingsStore => _settingsStore;

    /// <summary>
    /// 设置窗口提交了一份新设置（已经写盘成功）：按差分逐项应用。
    /// </summary>
    /// <remarks>
    /// 差分而不是"全量重放"：改快捷键不该重载语言字典，改主题不该重注册热键
    /// （重注册在组合被别的程序抢走时会失败，且会让另一个功能的注册瞬间消失）。
    /// </remarks>
    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        AppLog.Info("Settings", $"设置已变更：{previous} → {current}");
        AppLog.Info(
            "Settings",
            $"SETTINGS_CHANGED shortcuts={(previous.ScreenshotShortcut != current.ScreenshotShortcut || previous.PinShortcut != current.PinShortcut || previous.QuickViewShortcut != current.QuickViewShortcut)} " +
            $"language={previous.Language != current.Language} theme={previous.Theme != current.Theme} " +
            $"startup={previous.LaunchAtStartup != current.LaunchAtStartup}");

        if (previous.Language != current.Language)
        {
            ApplyLanguage(current.Language);
        }

        if (previous.Theme != current.Theme)
        {
            ApplyTheme(current.Theme);
        }

        if (previous.LaunchAtStartup != current.LaunchAtStartup)
        {
            SyncStartupRegistration("设置窗口");
        }

        // 快捷键由设置窗口自己调 ApplyHotkeys（它需要拿到失败原因来回滚），这里不再重复注册。
    }

    /// <summary>T30：切换主题（立即生效，不重启）。</summary>
    private void ApplyTheme(AppTheme theme)
    {
        try
        {
            var changed = ThemeManager.Apply(theme);
            AppLog.Info(
                "Theme",
                $"主题已切换：选择={theme} 生效={ThemeManager.EffectiveTheme} 颜色变化={changed}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Theme", $"切换主题失败（保持当前外观）：{theme}", ex);
        }
    }

    /// <summary>T31：切换界面语言（立即生效；已打开的窗口监听 <see cref="StringsUi.Changed"/> 自行重建文案）。</summary>
    private void ApplyLanguage(AppLanguage language)
    {
        try
        {
            var effective = StringsUi.Apply(this, language);
            AppLog.Info("Lang", $"界面语言已切换：选择={language} 生效={effective}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Lang", $"切换语言失败（界面保持原语言）：{language}", ex);
        }
    }

    /// <summary>托盘“设置”：打开（或激活）设置窗口（T29）。</summary>
    private void ShowSettings(string trigger)
    {
        AppLog.Info("Settings", $"设置窗口触发：{trigger}");

        var store = _settingsStore;
        if (store is null)
        {
            // 极端情况：设置文件完全不可用。仍让用户能改（只在内存里生效），只是写不进磁盘。
            AppLog.Warn("Settings", "设置存储器不可用，本次打开的设置窗口不会写盘");
            store = new SettingsStore();
        }

        // 历史记录数必须在窗口**构造之前**接好：窗口构造时就会统计并显示"共 N 条历史记录"，
        // 晚一步接线会让第一眼看到的数字永远是 0（桌面实测撞出来的）。
        // 历史不可用时显示 0（不阻塞设置窗口）。
        HistoryWindowCounts.SetProvider(_historyStore is null ? null : () => _historyStore.Count());

        var window = SettingsWindowHost.Show(store);
        if (window is null)
        {
            NotifyUser(AppInfo.Name, StringsUi.Get("S.Message.SettingsOpenFailed"));
            return;
        }

        window.ApplyShortcuts = _ => ApplyHotkeys();
        window.ChangeRequested = OnSettingsChanged;
        window.CheckUpdates = () => CheckForUpdates("设置窗口");
    }

    /// <summary>
    /// 托盘“检查更新”（T32）：手动查询一次更新清单并弹结果。
    /// </summary>
    /// <remarks>
    /// **未配置渠道 / 网络失败都不能显示成"已是最新版"**：这条规则在 Core
    /// （<see cref="UpdateCheck"/>）里判定并单测，这里只负责把结论如实显示出来。
    /// 渠道缺失不阻塞本地功能（只弹一个说明框）。
    /// </remarks>
    private async void CheckForUpdates(string trigger)
    {
        AppLog.Info("Update", $"检查更新触发：{trigger}（渠道={AppInfo.UpdateManifestUrl}）");

        UpdateCheckResult result;
        try
        {
            result = await UpdateChecker.CheckAsync(AppInfo.UpdateManifestUrl).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // CheckAsync 自己不会抛；这里只是"绝不让未处理的异常打断托盘"的兜底。
            AppLog.Error("Update", "检查更新时出现未处理的错误", ex);
            result = UpdateCheck.Failed(
                UpdateChecker.GetCurrentVersion(),
                AppInfo.UpdateManifestUrl,
                ex.Message);
        }

        UpdateChecker.LogResult(result, trigger);

        var window = UpdateCheckWindow.ShowResult(result);
        AppLog.Info(
            "Update",
            $"检查更新对话框：status={result.Status} 标题={result.DescribeTitle()} " +
            $"显示下载按钮={result.DownloadUrl is not null} 窗口={window?.Title ?? "(未显示)"}");

        // 未配置渠道时再补一条 Toast：PLAN 要求"明确提示"，托盘通知在本机常被系统抑制。
        if (result.Status == UpdateStatus.NotConfigured)
        {
            ToastWindow.Show(result.DescribeTitle());
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
                StringsUi.Get("S.Message.HistoryUnavailableTitle"),
                StringsUi.Format("S.Message.HistoryUnavailable", ex.Message));
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

        _historyQuickView?.Reload();

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
        ToastWindow.Show(StringsUi.Format("S.Toast.HistoryFailed", action));
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
                ShowSettings("托盘菜单");
                break;

            case TrayMenuCommand.CheckUpdates:
                CheckForUpdates("托盘菜单");
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
            NotifyUser(
                StringsUi.Get("S.Message.HotkeyUnavailableTitle"),
                StringsUi.Format("S.Message.HotkeyUnavailable", ex.Message));
            return;
        }

        // T29：快捷键来自设置（默认 Alt+A / Alt+S），改完立即重注册，不需要重启。
        var failure = ApplyHotkeys();
        if (string.IsNullOrEmpty(failure))
        {
            return;
        }

        // 托盘气球可能被系统通知设置抑制；再弹一次启动提示，确保用户知道快捷键不可用。
        AppLog.Warn("Hotkey", $"启动时热键注册失败：{failure}");
        MessageBox.Show(
            failure,
            AppInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnGlobalHotkeyPressed(ShortcutSpec shortcut)
    {
        if (_capturingShortcut) return;
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

        CloseHistoryQuickView();
        AppLog.Info("Capture", $"截图触发：{trigger}");
        try
        {
            var capture = ScreenCapture.CaptureMonitorUnderCursor();
            var dip = new DpiTransform(capture.Dpi).ToDip(capture.Bounds);

            IReadOnlyList<WindowCandidate> candidates;
            try
            {
                candidates = Settings.CaptureMode == CaptureSelectionMode.Window
                    ? WindowEnumerator.Enumerate(capture.Bounds)
                    : Array.Empty<WindowCandidate>();
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
                $"截图方式={Settings.CaptureMode} 窗口候选={candidates.Count}");

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
            NotifyUser(
                StringsUi.Get("S.Message.CaptureFailedTitle"),
                StringsUi.Format("S.Message.CaptureFailed", ex.Message));
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
            NotifyPinFailure(StringsUi.Get("S.Message.PinLocationFailed"));
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
        _trayIcon?.ShowNotification(StringsUi.Get("S.Tray.Notification.FailedTitle"), message, warning: true);
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
            ? StringsUi.Format("S.Tray.HideAllPins", state.VisibleCount)
            : StringsUi.Format("S.Tray.ShowAllPins", state.Count);
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
            ToastWindow.Show(StringsUi.Format(
                "S.Toast.NoPins",
                Settings.ScreenshotShortcut,
                Settings.PinShortcut));
            return;
        }

        var action = PinWindows.ToggleAll();
        switch (action)
        {
            case PinBulkAction.HideAll:
                AppLog.Info("Pins", $"全局显隐结果：已隐藏全部贴图，仍保留 {PinWindows.Count} 张实例");
                ToastWindow.Show(StringsUi.Format("S.Toast.PinsHidden", PinWindows.Count));
                break;

            case PinBulkAction.ShowAll:
                AppLog.Info("Pins", $"全局显隐结果：已显示全部贴图，当前可见 {PinWindows.VisibleCount} 张");
                ToastWindow.Show(StringsUi.Format("S.Toast.PinsShown", PinWindows.VisibleCount));
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

    private void OnTrayQuickViewRequested() => ToggleHistoryQuickView(fromTray: true);

    private void ToggleHistoryQuickView(bool fromTray = false, NativeMethods.Point? anchor = null)
    {
        if (_captureOverlay is not null) return;
        if (_historyQuickView is not null)
        {
            CloseHistoryQuickView();
            return;
        }
        // Clicking the tray first deactivates the popup. Do not immediately reopen it.
        if (fromTray && Environment.TickCount64 - _quickViewClosedAt < 300) return;
        if (_historyStore is null)
        {
            ShowHistoryGallery("快览");
            return;
        }
        try
        {
            if (anchor is null)
            {
                NativeMethods.GetCursorPos(out var cursor);
                anchor = cursor;
            }
            var window = new HistoryQuickViewWindow(
                _historyStore,
                (record, edit) =>
                {
                    ShowHistoryGallery("快览");
                    if (record is not null) HistoryGallery.Current?.RevealRecord(record, edit);
                },
                () => ShowSettings("快览"), anchor.Value, anchorAtTopLeft: !fromTray);
            _historyQuickView = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_historyQuickView, window)) _historyQuickView = null;
                _quickViewClosedAt = Environment.TickCount64;
            };
            window.Show();
        }
        catch (Exception ex)
        {
            CloseHistoryQuickView();
            AppLog.Error("History", "打开历史快览失败", ex);
            ToastWindow.Show(StringsUi.Get("S.QuickView.LoadFailed"));
        }
    }

    private void CloseHistoryQuickView()
    {
        var window = _historyQuickView;
        _historyQuickView = null;
        window?.Close();
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
                StringsUi.Format("S.Message.HistoryGalleryUnavailable", AppLog.CurrentLogPath),
                AppInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = HistoryGallery.Show(_historyStore);
        if (window is null)
        {
            MessageBox.Show(
                StringsUi.Format("S.Message.HistoryGalleryOpenFailed", AppLog.CurrentLogPath),
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

    private void ExitFromTray()
    {
        AppLog.Info("App", "退出：用户选择托盘菜单“退出”");
        DisposeCaptureOverlay();
        DisposeHistoryRetention();
        DisposeHistoryGallery();
        DisposeSettings();
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
    private void DisposeHistoryGallery()
    {
        CloseHistoryQuickView();
        HistoryGallery.CloseIfOpen();
    }

    /// <summary>退出时关闭设置窗口（设置已经即时写盘，关闭窗口不需要额外保存）。</summary>
    private void DisposeSettings() => SettingsWindowHost.CloseIfOpen();

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
        _trayIcon.QuickViewRequested -= OnTrayQuickViewRequested;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeCaptureOverlay();
        DisposeHistoryRetention();
        DisposeHistoryGallery();
        DisposeSettings();
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
                StringsUi.Format("S.Message.Unhandled", AppLog.CurrentLogPath, e.Exception.Message),
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
