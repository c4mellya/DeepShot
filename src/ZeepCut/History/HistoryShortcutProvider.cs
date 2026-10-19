namespace ZeepCut.History;

/// <summary>
/// 图库/编辑窗口需要的"当前快捷键"（T31）：空状态提示与提示要用设置里的组合，
/// 而不是写死 Alt+A。
/// </summary>
/// <remarks>
/// 用静态注入而不是让每个窗口都持有 <c>SettingsStore</c>：图库是由
/// <see cref="HistoryGallery"/> 单例打开的，它拿不到 App 的设置对象；
/// 这里由 App 在启动时填一次（默认值就是设置默认值，App 不可用时也说得对）。
/// </remarks>
internal static class HistoryShortcutProvider
{
    /// <summary>当前截图快捷键（规范文本，例如 <c>Alt+A</c>）。</summary>
    internal static string ScreenshotShortcut { get; set; } = Core.Settings.AppSettings.DefaultScreenshotShortcut;

    /// <summary>当前剪贴板贴图快捷键。</summary>
    internal static string PinShortcut { get; set; } = Core.Settings.AppSettings.DefaultPinShortcut;
}
