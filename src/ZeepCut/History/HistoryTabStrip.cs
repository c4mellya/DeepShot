using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace ZeepCut.History;

/// <summary>
/// 图库顶部的“全部 / 只看收藏”两段式开关（T26）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用普通 CheckBox</b>：默认样式会画出一个与图库配色不搭的勾选框，
/// 而且要额外解释“勾上=只看收藏”这种双重否定（T30 才做统一主题）。
/// 这里用两个 <see cref="RadioButton"/> + 自定义模板画成“选项卡”，
/// 同时保留 UIA 的可点击语义（验证脚本按名字找“只看收藏”）。
/// </para>
/// <para>
/// 它是**无状态的视觉件**：只有 <see cref="All"/> / <see cref="Favorites"/> 两个回调，
/// 当前选中哪一项由 <see cref="Apply"/> 按 <c>HistoryView.FavoritesOnly</c> 刷新，
/// 避免控件自己记状态、与数据库里的事实分叉。
/// </para>
/// </remarks>
internal sealed class HistoryTabStrip : Border
{
    /// <summary>"全部"选项卡的 UIA 名字。</summary>
    internal const string AllName = "全部";

    /// <summary>"只看收藏"选项卡的 UIA 名字。</summary>
    internal const string FavoritesName = "只看收藏";

    private readonly RadioButton _all;
    private readonly RadioButton _favorites;

    internal HistoryTabStrip(Action showAll, Action showFavorites)
    {
        _all = CreateTab(AllName, "history-filter-all", showAll);
        _favorites = CreateTab(FavoritesName, "history-filter-favorites", showFavorites);

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(_all);
        stack.Children.Add(_favorites);

        Padding = new Thickness(3);
        CornerRadius = new CornerRadius(6);
        Background = HistoryUi.ThumbnailBackground;
        BorderBrush = HistoryUi.Border;
        BorderThickness = new Thickness(1);
        Child = stack;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>刷新选中项（favoritesOnly = true 时高亮“只看收藏”）。</summary>
    internal void Apply(bool favoritesOnly)
    {
        _all.IsChecked = !favoritesOnly;
        _favorites.IsChecked = favoritesOnly;
    }

    private static RadioButton CreateTab(string text, string automationId, Action onClick)
    {
        var tab = new RadioButton
        {
            Content = text,
            GroupName = "history-favorite-filter",
            FontFamily = new FontFamily(HistoryUi.FontFamilyName),
            FontSize = 12.5,
            Cursor = Cursors.Hand,
            Template = BuildTemplate(),
        };

        AutomationProperties.SetName(tab, text);
        AutomationProperties.SetAutomationId(tab, automationId);
        tab.Click += (_, _) => onClick();
        return tab;
    }

    /// <summary>最小模板：圆角选中底色 + 白字；未选中透明底 + 次要文字色。</summary>
    private static ControlTemplate BuildTemplate()
    {
        var background = new FrameworkElementFactory(typeof(Border));
        background.Name = "Chrome";
        background.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        background.SetValue(Border.BackgroundProperty, Brushes.Transparent);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 4, 12, 4));

        var host = new FrameworkElementFactory(typeof(Grid));
        host.AppendChild(background);
        host.AppendChild(content);

        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = host };

        // 选中：深蓝底 + 白字；未选中：透明底 + 次要文字色。
        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, HistoryUi.SelectedBorder, "Chrome"));
        checkedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, HistoryUi.Frozen(Colors.White)));

        var uncheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
        uncheckedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, HistoryUi.SecondaryForeground));

        template.Triggers.Add(checkedTrigger);
        template.Triggers.Add(uncheckedTrigger);
        return template;
    }
}
