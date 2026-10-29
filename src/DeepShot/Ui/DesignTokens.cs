using System.Windows;
using System.Windows.Media.Animation;

namespace DeepShot.Ui;

/// <summary>
/// 设计令牌：字号阶梯、间距、圆角、控件高度与动效时长。所有窗口从这里取值，
/// 不再各自写 12.5 / 13.5 / 14 这种"差不多"的数字。
/// </summary>
internal static class Tokens
{
    /// <summary>字号阶梯（组件库字体栈，正文 13 在 100% 缩放下最清晰）。</summary>
    internal static class Text
    {
        /// <summary>组件库 eyebrow：分组标签、时间索引年份。</summary>
        internal const double Eyebrow = 11;

        /// <summary>辅助说明、徽标、时间戳。</summary>
        internal const double Caption = 12;

        /// <summary>正文、按钮、输入框。</summary>
        internal const double Body = 13;

        /// <summary>设置项标题、对话框正文。</summary>
        internal const double BodyLarge = 14;

        /// <summary>卡片/分组标题。</summary>
        internal const double Subtitle = 15;

        /// <summary>对话框/窗口标题。</summary>
        internal const double Title = 18;

        /// <summary>欢迎页、关于页的大标题。</summary>
        internal const double Display = 24;
    }

    /// <summary>4 的倍数间距。</summary>
    internal static class Space
    {
        internal const double XS = 4;
        internal const double S = 8;
        internal const double M = 12;
        internal const double L = 16;
        internal const double XL = 24;
        internal const double XXL = 32;
        internal const double XXXL = 48;
    }

    /// <summary>圆角。</summary>
    internal static class Radius
    {
        /// <summary>行内代码 / 小色块（组件库 xs）。</summary>
        internal const double XS = 2;

        /// <summary>徽标、色块。</summary>
        internal const double Small = 4;

        /// <summary>按钮、输入框、下拉框、菜单项。</summary>
        internal const double Control = 4;

        /// <summary>弹出层（下拉列表、右键菜单）。</summary>
        internal const double Popup = 8;

        /// <summary>内容卡片、设置分组。</summary>
        internal const double Card = 8;

        /// <summary>浮起面板（组件库 panel）。</summary>
        internal const double Panel = 8;
    }

    /// <summary>控件高度。</summary>
    internal static class Height
    {
        internal const double Compact = 28;
        internal const double Control = 32;
        internal const double Large = 36;

        /// <summary>组件库页面控件基准（按钮 / 输入 / 分段）。</summary>
        internal const double PageCompact = 28;
        internal const double PageControl = 32;
        internal const double PageLarge = 36;
    }

    /// <summary>卡片内容的标准内边距（设置行、欢迎页行）。</summary>
    internal static readonly Thickness CardRowPadding = new(16, 12, 16, 12);

    /// <summary>对话框内容区的标准外边距。</summary>
    internal static readonly Thickness DialogPadding = new(24, 16, 24, 24);

    /// <summary>
    /// 深色玻璃（截图浮层、贴图、标注工具条，永远是深底、不随主题变）上的危险色。
    /// 与深色主题的 <c>Theme.Error</c>（#FF8A7A）同一色相；窗口标题栏的关闭按钮另用 Windows 系统红 #E81123（见 UiStyles）。
    /// </summary>
    internal static class Danger
    {
        /// <summary>实心/泛红底（悬停的关闭贴图、取消按钮）。</summary>
        internal const string GlassFill = "#FFC42B1C";

        /// <summary>深底上的危险文字与图标（错误提示、取消按钮悬停）。</summary>
        internal const string GlassText = "#FFFF99A4";
    }

    /// <summary>动效。</summary>
    internal static class Motion
    {
        /// <summary>悬停/按下等即时反馈。</summary>
        internal static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(83);

        /// <summary>出现/选中切换。</summary>
        internal static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(167);

        /// <summary>窗口/面板进入。</summary>
        internal static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(250);

        /// <summary>统一缓动（快出慢停）。</summary>
        internal static IEasingFunction Ease { get; } = CreateEase();

        private static IEasingFunction CreateEase()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ease.Freeze();
            return ease;
        }
    }
}
