using System.Windows;
using ZeepCut.Platform;

namespace ZeepCut.History;

/// <summary>
/// 历史图库窗口的**单实例**宿主（T25）：托盘“历史”与将来其它入口都从这里打开。
/// </summary>
/// <remarks>
/// <para>
/// 应用是托盘常驻、没有主窗口（<c>ShutdownMode=OnExplicitShutdown</c>），所以图库不能
/// 每次点击都 new 一个窗口 —— 那样会同时存在多个图库、各自持有选中项与搜索词。
/// 这里只保留一个实例：已打开时 <see cref="Window.Activate"/> 唤到前台，关闭后再点重新创建。
/// </para>
/// <para>
/// 图库是**普通窗口**（有边框、可缩放、出现在任务栏），关闭只是关窗口，进程与托盘继续运行
/// （PLAN：关闭普通窗口留在托盘）。
/// </para>
/// </remarks>
public static class HistoryGallery
{
    private static HistoryGalleryWindow? _window;

    /// <summary>图库是否已打开。</summary>
    public static bool IsOpen => _window is not null;

    /// <summary>当前打开的图库窗口（未打开时为 null，供验证/诊断读取状态）。</summary>
    internal static HistoryGalleryWindow? Current => _window;

    /// <summary>
    /// 打开图库（已有实例则激活它）。
    /// </summary>
    /// <param name="store">历史写入器（必须是启动时初始化成功的那一个）。</param>
    /// <returns>图库窗口；创建失败返回 null（调用方提示用户）。</returns>
    internal static HistoryGalleryWindow? Show(HistoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (_window is not null)
        {
            AppLog.Info("History", "历史图库已打开，本次只激活已有窗口");
            Activate(_window);
            return _window;
        }

        try
        {
            var window = new HistoryGalleryWindow(store);
            window.Closed += OnWindowClosed;
            _window = window;
            window.Show();
            Activate(window);
            AppLog.Info("History", "历史图库窗口已打开");
            return window;
        }
        catch (Exception ex)
        {
            _window = null;
            AppLog.Error("History", "打开历史图库失败", ex);
            return null;
        }
    }

    /// <summary>关闭图库（退出应用时调用；未打开时什么也不做）。</summary>
    public static void CloseIfOpen()
    {
        if (_window is null)
        {
            return;
        }

        var window = _window;
        _window = null;
        window.Closed -= OnWindowClosed;
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"关闭历史图库失败（忽略）：{ex.Message}");
        }

        AppLog.Info("History", "历史图库窗口已关闭（应用继续在托盘运行）");
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is HistoryGalleryWindow window)
        {
            window.Closed -= OnWindowClosed;
        }

        _window = null;
    }

    private static void Activate(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Focus();
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"激活历史图库窗口失败（忽略）：{ex.Message}");
        }
    }
}
