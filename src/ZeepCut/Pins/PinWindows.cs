using System.Windows.Media.Imaging;
using ZeepCut.Core.Geometry;
using ZeepCut.Platform;

namespace ZeepCut.Pins;

/// <summary>
/// 贴图集合管理（T18）：应用的**唯一**贴图注册表，负责创建、计数与清理。
/// </summary>
/// <remarks>
/// <para>
/// T18 只做“多张贴图并存、各自独立、退出时全部关闭”。全局显示/隐藏（T20）、
/// 剪贴板贴图（T21）与历史联动（T22）都从这里取实例，所以贴图窗口不要在别处直接 new。
/// </para>
/// <para>
/// 贴图窗口自己关闭（Esc / × / 右键关闭）时回调 <see cref="NotifyClosed"/> 注销；
/// 应用退出时调用 <see cref="CloseAll"/> 一次性关掉全部（PLAN：退出不恢复贴图）。
/// </para>
/// </remarks>
public static class PinWindows
{
    private static readonly List<PinWindow> Open = new();

    /// <summary>当前存活的贴图窗口（只读快照）。</summary>
    public static IReadOnlyList<PinWindow> All => Open.ToArray();

    /// <summary>当前存活的贴图数量。</summary>
    public static int Count => Open.Count;

    /// <summary>
    /// 用一张已合成的图片创建贴图窗口，并摆到 <paramref name="selection"/>（屏幕物理像素）。
    /// </summary>
    /// <param name="image">贴图内容（通常是 T17 的合成图），会以 1:1 物理像素显示。</param>
    /// <param name="selection">贴图在屏幕上的初始物理矩形（来自截图选区）。</param>
    /// <param name="screenBounds">截图所在屏幕的物理矩形，仅用于日志。</param>
    /// <param name="dpi">截图所在屏幕的 DPI，用于物理像素→DIP 换算。</param>
    /// <returns>已显示并登记的贴图窗口。</returns>
    public static PinWindow Create(
        BitmapSource image,
        PixelRect selection,
        PixelRect screenBounds,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(image);

        var pin = new PinWindow(image, selection, screenBounds, dpi);
        pin.Show();
        Open.Add(pin);

        AppLog.Info(
            "Pins",
            $"贴图已加入集合：当前 {Open.Count} 张，物理={pin.Bounds} DPI={pin.ScreenDpi} " +
            $"图像={image.PixelWidth}x{image.PixelHeight}");
        return pin;
    }

    /// <summary>贴图窗口关闭时从集合移除（由 <see cref="PinWindow"/> 调用，不要手动重复移除）。</summary>
    internal static void NotifyClosed(PinWindow pin)
    {
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
}
