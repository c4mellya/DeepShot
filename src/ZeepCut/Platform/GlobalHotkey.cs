using System.Runtime.InteropServices;
using System.Windows.Interop;
using ZeepCut.Core.Shortcuts;

namespace ZeepCut.Platform;

/// <summary>
/// 单个快捷键的注册结果。<see cref="Success"/> 为 false 时 <see cref="ErrorCode"/> 保留 Win32 错误码，
/// 可据此区分“被占用”（1409）与其他失败原因。
/// </summary>
public readonly record struct HotkeyRegistrationResult(
    bool Success,
    ShortcutSpec Shortcut,
    int HotkeyId,
    int ErrorCode)
{
    /// <summary>是否因为组合已被其他程序（或本程序）注册而失败。</summary>
    public bool IsAlreadyRegistered => !Success && ErrorCode == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED;

    /// <summary>面向用户的失败原因；成功时为空字符串。</summary>
    public string DescribeFailure()
    {
        if (Success)
        {
            return string.Empty;
        }

        return IsAlreadyRegistered
            ? $"快捷键 {Shortcut.Format()} 被占用，请修改。"
            : $"快捷键 {Shortcut.Format()} 注册失败（Win32 错误 {ErrorCode}），请修改。";
    }
}

/// <summary>
/// 全局热键注册器：专用 message-only <see cref="HwndSource"/> 接收 <c>WM_HOTKEY</c>，
/// 一个实例可同时注册多个组合，退出时统一注销。
/// </summary>
/// <remarks>
/// 注册使用 <c>MOD_NOREPEAT</c>，长按不会连续触发。窗口只存在于本进程，
/// 不显示、不参与 Z 序，也不影响托盘窗口的生命周期。
/// </remarks>
public sealed class GlobalHotkey : IDisposable
{
    /// <summary>HWND_MESSAGE：消息专用（message-only）父窗口。</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly Dictionary<int, ShortcutSpec> _registrations = new();
    private readonly HwndSource _source;
    private readonly HwndSourceHook _hook;
    private int _nextId = 1;
    private bool _disposed;

    public GlobalHotkey()
    {
        // 委托必须由字段保持存活，原生窗口过程只保存函数指针。
        _hook = WndProc;

        var parameters = new HwndSourceParameters("ZeepCut.HotkeyMessageWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = HwndMessage,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(_hook);
        AppLog.Info("Hotkey", $"全局热键消息窗口已创建（message-only，hwnd=0x{_source.Handle.ToInt64():X}）");
    }

    /// <summary>热键被按下。回调在 UI 线程的 Dispatcher 上执行，不在窗口过程里。</summary>
    public event Action<ShortcutSpec>? Pressed;

    /// <summary>Cursor captured when WM_HOTKEY arrived, before queued UI work.</summary>
    internal NativeMethods.Point? TriggerCursor { get; private set; }

    /// <summary>已成功注册的快捷键。</summary>
    public IReadOnlyCollection<ShortcutSpec> RegisteredShortcuts => _registrations.Values;

    /// <summary>
    /// 注册一个快捷键。失败不抛异常，返回带 Win32 错误码的结果，由调用方提示用户；
    /// 一个失败不影响其他组合继续注册。
    /// </summary>
    public HotkeyRegistrationResult Register(ShortcutSpec shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = _nextId++;
        var modifiers = (uint)shortcut.Modifiers | NativeMethods.MOD_NOREPEAT;

        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, (uint)shortcut.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            var failure = new HotkeyRegistrationResult(false, shortcut, id, error);
            AppLog.Warn(
                "Hotkey",
                $"注册 {shortcut.Format()} 失败（id={id}, Win32Error={error}）: {failure.DescribeFailure()}");
            return failure;
        }

        _registrations[id] = shortcut;
        AppLog.Info(
            "Hotkey",
            $"已注册全局热键 {shortcut.Format()}（id={id}, modifiers=0x{modifiers:X}, vk=0x{shortcut.VirtualKey:X2}）");
        return new HotkeyRegistrationResult(true, shortcut, id, 0);
    }

    /// <summary>注销全部快捷键；没有注册项时为空操作。</summary>
    public void UnregisterAll()
    {
        if (_registrations.Count == 0)
        {
            return;
        }

        if (_source.Handle == IntPtr.Zero)
        {
            _registrations.Clear();
            return;
        }

        foreach (var (id, shortcut) in _registrations.ToArray())
        {
            if (NativeMethods.UnregisterHotKey(_source.Handle, id))
            {
                AppLog.Info("Hotkey", $"已注销全局热键 {shortcut.Format()}（id={id}）");
            }
            else
            {
                AppLog.Warn(
                    "Hotkey",
                    $"注销全局热键 {shortcut.Format()} 失败（id={id}, Win32Error={Marshal.GetLastWin32Error()}）");
            }
        }

        _registrations.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        Pressed = null;

        _source.RemoveHook(_hook);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return IntPtr.Zero;
        }

        handled = true;
        var id = wParam.ToInt32();

        if (!_registrations.TryGetValue(id, out var shortcut))
        {
            AppLog.Warn("Hotkey", $"收到未知热键 id={id} 的 WM_HOTKEY，已忽略");
            return IntPtr.Zero;
        }

        AppLog.Info("Hotkey", $"热键触发：{shortcut.Format()}（id={id}）");
        // 捕获/弹窗等动作不在窗口过程里同步执行，交回 Dispatcher。
        NativeMethods.GetCursorPos(out var cursor);
        _source.Dispatcher.BeginInvoke(new Action(() =>
        {
            TriggerCursor = cursor;
            try { Pressed?.Invoke(shortcut); }
            finally { TriggerCursor = null; }
        }));
        return IntPtr.Zero;
    }
}
