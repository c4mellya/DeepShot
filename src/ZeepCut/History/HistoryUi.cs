using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZeepCut.History;

/// <summary>
/// 图库/预览/对话框共用的朴素样式（T25/T26）。
/// </summary>
/// <remarks>
/// 本轮不做主题（T30 才做浅色/深色切换），所以颜色集中在这里、写死为中性深色文字 +
/// 浅色背景，避免每个窗口各写一套十六进制色值。T30 接入主题时只需改这里。
/// </remarks>
internal static class HistoryUi
{
    /// <summary>窗口背景。</summary>
    internal static readonly Brush WindowBackground = Frozen(Color.FromRgb(0xF7, 0xF7, 0xF9));

    /// <summary>内容卡片背景。</summary>
    internal static readonly Brush CardBackground = Frozen(Colors.White);

    /// <summary>卡片/控件边框。</summary>
    internal static readonly Brush Border = Frozen(Color.FromRgb(0xD8, 0xD8, 0xDE));

    /// <summary>选中卡片边框（蓝色）。</summary>
    internal static readonly Brush SelectedBorder = Frozen(Color.FromRgb(0x1D, 0x74, 0xD8));

    /// <summary>主要文字。</summary>
    internal static readonly Brush Foreground = Frozen(Color.FromRgb(0x1F, 0x1F, 0x23));

    /// <summary>次要文字（时间/尺寸/状态）。</summary>
    internal static readonly Brush SecondaryForeground = Frozen(Color.FromRgb(0x6A, 0x6A, 0x74));

    /// <summary>缩略图占位底色。</summary>
    internal static readonly Brush ThumbnailBackground = Frozen(Color.FromRgb(0xEC, 0xEC, 0xF0));

    /// <summary>收藏星标颜色（金黄色）。</summary>
    internal static readonly Brush StarBrush = Frozen(Color.FromRgb(0xE5, 0xA5, 0x00));

    /// <summary>未收藏星标颜色。</summary>
    internal static readonly Brush StarInactiveBrush = Frozen(Color.FromRgb(0xB4, 0xB4, 0xBE));

    /// <summary>错误提示颜色。</summary>
    internal static readonly Brush ErrorForeground = Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C));

    /// <summary>字体（与 Toast/贴图一致）。</summary>
    internal const string FontFamilyName = "Segoe UI";

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>创建一段文字。</summary>
    internal static TextBlock Text(
        string text,
        double fontSize = 13,
        Brush? foreground = null,
        FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(FontFamilyName),
            FontSize = fontSize,
            Foreground = foreground ?? Foreground,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (weight is { } value)
        {
            block.FontWeight = value;
        }

        return block;
    }

    /// <summary>
    /// 创建按钮：统一外观 + UIA 名字（便于真实桌面自动化按名字点击）。
    /// </summary>
    internal static Button Button(string text, string automationName, Action onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
            FontFamily = new FontFamily(FontFamilyName),
            FontSize = 13,
            MinWidth = 66,
            Background = primary ? SelectedBorder : CardBackground,
            Foreground = primary ? Frozen(Colors.White) : Foreground,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        AutomationProperties.SetName(button, automationName);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>给元素设置 UIA AutomationId（验证脚本按它精确定位某条记录的控件）。</summary>
    internal static T WithAutomationId<T>(T element, string automationId)
        where T : DependencyObject
    {
        AutomationProperties.SetAutomationId(element, automationId);
        return element;
    }
}
