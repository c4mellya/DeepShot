using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeepShot.Ui;

namespace DeepShot.Platform;

/// <summary>托盘菜单命令；数值与 Win32 菜单项 ID 一致。</summary>
public enum TrayMenuCommand
{
    Screenshot = 1,
    PasteFromClipboard = 2,
    ToggleAllPins = 3,
    History = 4,
    Settings = 5,
    CheckUpdates = 6,
    Exit = 7,
}

/// <summary>
/// 纯 Win32 系统托盘图标：隐藏窗口接收 <c>WM_TRAYICON</c>，左键打开历史快览、右键弹出 <c>TrackPopupMenu</c> 菜单。
/// 不引用 WinForms（WPF 中启用 UseWindowsForms 会导致 <see cref="System.Windows.Application"/> 类型冲突）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "DeepShot.TrayMessageWindow";
    private const string Tooltip = "DeepShot";
    private const uint TrayIconId = 1;

    /// <summary>
    /// 托盘菜单“显示/隐藏全部贴图”没有贴图时的默认文案（T31：走语言资源）。
    /// </summary>
    /// <remarks>
    /// 保留这个常量名是因为 T20 的验证脚本与 <c>App</c> 都引用它；它现在是**属性**而不是
    /// <c>const</c>，这样切换语言后取到的就是当前语言的文案。
    /// </remarks>
    public static string ToggleAllPinsDefaultText => Ui.StringsUi.Get("S.Tray.TogglePinsDefault");

    private readonly NativeMethods.WndProc _wndProc;
    private readonly uint _taskbarCreatedMessage;
    private readonly Dispatcher _dispatcher;

    private IntPtr _window;
    private IntPtr _icon;
    private IntPtr _largeIcon;
    private bool _ownsIcon;
    private bool _iconAdded;
    private bool _disposed;
    private long _lastLeftClickTick = -1000;

    public TrayIcon()
    {
        // 委托必须由字段保持存活：原生窗口类只保存函数指针。
        _wndProc = WindowProc;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        CreateMessageWindow();
        LoadAppIcon();
    }

    /// <summary>用户在托盘菜单里选择了某个命令；回调在 UI 线程上执行。</summary>
    public event Action<TrayMenuCommand>? CommandInvoked;

    /// <summary>左键/键盘选择托盘图标，打开或收起历史快览。</summary>
    public event Action? QuickViewRequested;

    /// <summary>
    /// T20：“显示/隐藏全部贴图”菜单项的文案（例如“隐藏全部贴图 (2)”）。每次弹菜单时求值，
    /// 因此始终反映当前贴图状态；返回 null/空白时使用默认文案。
    /// </summary>
    public Func<string?>? ToggleAllPinsTextProvider { get; set; }

    /// <summary>
    /// T20：没有贴图时是否禁用“显示/隐藏全部贴图”。返回 true 时该项灰显不可点，
    /// 避免点了没反应（有贴图时始终可用）。
    /// </summary>
    public Func<bool>? ToggleAllPinsDisabledProvider { get; set; }

    /// <summary>把图标加入通知区域。失败时返回 false，由调用方提示用户。</summary>
    public bool Show()
    {
        var data = CreateData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        data.uCallbackMessage = NativeMethods.WM_TRAYICON;
        data.hIcon = _icon;
        data.szTip = Tooltip;

        // Explorer 刚启动/重启时 NIM_ADD 可能短暂失败，重试几次。
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
            {
                _iconAdded = true;
                AppLog.Info("Tray", $"托盘图标已加入通知区域（第 {attempt} 次尝试）");
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            AppLog.Warn("Tray", $"Shell_NotifyIcon(NIM_ADD) 失败，第 {attempt} 次，Win32Error={error}");
            Thread.Sleep(200);
        }

        return false;
    }

    /// <summary>
    /// 通过通知区域弹出气球/Toast。返回 false 表示系统拒绝（例如图标已移除），
    /// 调用方应改用其他方式提示，不能静默失败。
    /// </summary>
    public bool ShowNotification(string title, string message, bool warning = true)
    {
        if (_disposed || !_iconAdded)
        {
            AppLog.Warn("Tray", $"无法显示通知（图标未添加）：{title} - {message}");
            return false;
        }

        // NOTIFYICONDATA 的 szInfoTitle/szInfo 是定长缓冲区：过长会抛 ArgumentException，
        // 而通知是"出错时的兜底提示"，它自己不能成为新的崩溃点。
        title = Clip(title, 63);
        message = Clip(message, 255);

        var data = CreateData();
        data.uFlags = NativeMethods.NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = warning ? NativeMethods.NIIF_WARNING : NativeMethods.NIIF_INFO;
        data.uTimeoutOrVersion = 10000;

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
        {
            AppLog.Warn(
                "Tray",
                $"Shell_NotifyIcon(NIM_MODIFY/NIF_INFO) 失败，Win32Error={Marshal.GetLastWin32Error()}；通知内容：{title} - {message}");
            return false;
        }

        AppLog.Info("Tray", $"已显示通知：{title} - {message}");
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded)
        {
            var data = CreateData();
            if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data))
            {
                AppLog.Info("Tray", "托盘图标已从通知区域移除");
            }
            else
            {
                AppLog.Warn("Tray", $"Shell_NotifyIcon(NIM_DELETE) 失败，Win32Error={Marshal.GetLastWin32Error()}");
            }

            _iconAdded = false;
        }

        if (_window != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        if (_ownsIcon)
        {
            if (_icon != IntPtr.Zero && _icon != _largeIcon)
            {
                NativeMethods.DestroyIcon(_icon);
            }

            if (_largeIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_largeIcon);
            }
        }

        _icon = IntPtr.Zero;
        _largeIcon = IntPtr.Zero;
    }

    private void CreateMessageWindow()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = WindowClassName,
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NativeMethods.ERROR_CLASS_ALREADY_EXISTS)
            {
                throw new Win32Exception(error, "注册托盘消息窗口类失败");
            }
        }

        // 顶层隐藏窗口（不带 WS_VISIBLE）：既能收托盘回调，也能被 SetForegroundWindow 激活，
        // 用于修正 TrackPopupMenu 点击别处后菜单不消失的问题。
        _window = NativeMethods.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: WindowClassName,
            lpWindowName: Tooltip,
            dwStyle: 0,
            x: 0,
            y: 0,
            nWidth: 0,
            nHeight: 0,
            hWndParent: IntPtr.Zero,
            hMenu: IntPtr.Zero,
            hInstance: instance,
            lpParam: IntPtr.Zero);

        if (_window == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建托盘消息窗口失败");
        }

        AppLog.Info("Tray", $"托盘消息窗口已创建（隐藏，hwnd=0x{_window.ToInt64():X}）");
    }

    private void LoadAppIcon()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var count = NativeMethods.ExtractIconEx(exePath, 0, out var large, out var small, 1);
                if (count > 0 && (small != IntPtr.Zero || large != IntPtr.Zero))
                {
                    _largeIcon = large;
                    _icon = small != IntPtr.Zero ? small : large;
                    _ownsIcon = true;
                    AppLog.Info("Tray", $"托盘图标取自可执行文件: {exePath}");
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Tray", "从可执行文件提取图标失败，将回退到系统默认图标", ex);
            }
        }

        // 回退：系统默认应用图标（共享句柄，不能 DestroyIcon）。
        _icon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IDI_APPLICATION);
        _ownsIcon = false;
        AppLog.Warn("Tray", "未能提取应用图标，使用系统默认托盘图标");
    }

    private NativeMethods.NotifyIconData CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        hWnd = _window,
        uID = TrayIconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_TRAYICON)
        {
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMessage)
            {
                case NativeMethods.WM_LBUTTONUP:
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    // Coalesce duplicate Shell notifications without delaying the popup.
                    if (Environment.TickCount64 - _lastLeftClickTick < 150) return IntPtr.Zero;
                    _lastLeftClickTick = Environment.TickCount64;
                    NativeMethods.SetForegroundWindow(_window);
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_disposed) QuickViewRequested?.Invoke();
                    }));
                    return IntPtr.Zero;

                case NativeMethods.WM_LBUTTONDBLCLK:
                    return IntPtr.Zero;

                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_CONTEXTMENU:
                    ShowContextMenu();
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            // Explorer 重启后通知区域被清空，需要重新加入图标。
            AppLog.Info("Tray", "收到 TaskbarCreated，重新添加托盘图标");
            if (_iconAdded)
            {
                var old = CreateData();
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref old);
                _iconAdded = false;
            }

            Show();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_disposed || _window == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            cursor.X = 0;
            cursor.Y = 0;
        }

        AppLog.Info("Tray", $"显示托盘菜单（cursor={cursor.X},{cursor.Y}）");
        var menu = BuildMenu();
        if (menu == IntPtr.Zero)
        {
            AppLog.Error("Tray", "创建托盘菜单失败", new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }
        try
        {
            // 标准修正：先把隐藏窗口设为前台，菜单才会在点击别处时正确消失。
            NativeMethods.SetForegroundWindow(_window);
            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                _window,
                IntPtr.Zero);

            if (command > 0)
            {
                var trayCommand = (TrayMenuCommand)command;
                AppLog.Info("Tray", $"菜单命令: {trayCommand}");
                // 不在窗口过程里直接执行菜单动作（可能弹模态框或退出），交回 Dispatcher。
                _dispatcher.BeginInvoke(new Action(() => CommandInvoked?.Invoke(trayCommand)));
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
            // 菜单关闭后补一条空消息（Raymond Chen 的托盘菜单修正）。
            NativeMethods.PostMessage(_window, NativeMethods.WM_NULL, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private IntPtr BuildMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // T31：全部文案走 Resources\Lang 的语言资源（切换语言后下次弹菜单就是新语言）。
        AppendCommand(menu, TrayMenuCommand.Screenshot, StringsUi.Get("S.Tray.Screenshot"));
        AppendCommand(menu, TrayMenuCommand.PasteFromClipboard, StringsUi.Get("S.Tray.PasteFromClipboard"));
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);

        // T20：文案随贴图状态变化（“隐藏全部贴图 (2)”/“显示全部贴图 (2)”），
        // 没有贴图时灰显，避免点了没反应。
        var pinsText = ToggleAllPinsTextProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(pinsText))
        {
            pinsText = ToggleAllPinsDefaultText;
        }

        var pinsDisabled = ToggleAllPinsDisabledProvider?.Invoke() == true;
        var pinsFlags = NativeMethods.MF_STRING | (pinsDisabled ? NativeMethods.MF_GRAYED : 0u);
        NativeMethods.AppendMenu(menu, pinsFlags, new UIntPtr((uint)TrayMenuCommand.ToggleAllPins), pinsText);
        AppLog.Info(
            "Tray",
            $"托盘菜单“{pinsText}”：禁用={pinsDisabled}");

        AppendCommand(menu, TrayMenuCommand.History, StringsUi.Get("S.Tray.History"));
        AppendCommand(menu, TrayMenuCommand.Settings, StringsUi.Get("S.Tray.Settings"));
        AppendCommand(menu, TrayMenuCommand.CheckUpdates, StringsUi.Get("S.Tray.CheckUpdates"));
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        AppendCommand(menu, TrayMenuCommand.Exit, StringsUi.Get("S.Tray.Exit"));

        AppLog.Info(
            "Tray",
            $"托盘菜单文案：语言={StringsUi.Current} 截图={StringsUi.Get("S.Tray.Screenshot")} " +
            $"设置={StringsUi.Get("S.Tray.Settings")} 检查更新={StringsUi.Get("S.Tray.CheckUpdates")} " +
            $"退出={StringsUi.Get("S.Tray.Exit")}");
        return menu;
    }

    private static void AppendCommand(IntPtr menu, TrayMenuCommand command, string text) =>
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr((uint)command), text);

    /// <summary>把字符串截到定长缓冲区放得下的长度（按字符而不是字节：中文一个字也是一个字符）。</summary>
    private static string Clip(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
