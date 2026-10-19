using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>筛选项：全部 / 只看收藏。</summary>
internal enum HistoryFilter
{
    All,
    Favorites,
}

/// <summary>
/// 图库顶部的“全部 / 收藏”分段胶囊（T26）。
/// </summary>
/// <remarks>
/// <para>
/// 外观由应用统一的 <see cref="SegmentedControl{T}"/> 提供（强调色选中胶囊，颜色全部走主题资源，
/// 换主题即时生效）；这里只负责把它接到"全部/只看收藏"两个回调，并保持原来的公开 API 与 UIA id
/// （<c>history-filter-all</c> / <c>history-filter-favorites</c>）。
/// </para>
/// <para>
/// 它是<b>无状态的视觉件</b>：当前选中哪一项由 <see cref="Apply"/> 按 <c>HistoryView.FavoritesOnly</c> 刷新，
/// 避免控件自己记状态、与数据库里的事实分叉。
/// </para>
/// </remarks>
internal sealed class HistoryTabStrip : Border
{
    /// <summary>"全部"选项卡的 UIA 名字（来自语言资源，见 <see cref="StringsUi"/>）。</summary>
    internal static string AllName => StringsUi.Get("S.History.FilterAll");

    /// <summary>"只看收藏"选项卡的 UIA 名字。</summary>
    internal static string FavoritesName => StringsUi.Get("S.History.FilterFavorites");

    private readonly SegmentedControl<HistoryFilter> _segments = new();

    internal HistoryTabStrip(Action showAll, Action showFavorites, string? allText = null, string? favoritesText = null)
    {
        _segments.Add(HistoryFilter.All, "S.History.FilterAll", "history-filter-all");
        _segments.Add(HistoryFilter.Favorites, "S.History.FilterFavorites", "history-filter-favorites");
        _segments.UserSelected += filter =>
        {
            if (filter == HistoryFilter.Favorites)
            {
                showFavorites();
            }
            else
            {
                showAll();
            }
        };

        _segments.Select(HistoryFilter.All);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _segments;
    }

    /// <summary>刷新选中项（favoritesOnly = true 时高亮“只看收藏”）。</summary>
    internal void Apply(bool favoritesOnly) =>
        _segments.Select(favoritesOnly ? HistoryFilter.Favorites : HistoryFilter.All);

    /// <summary>语言切换后换两项的文案（保留当前选中项）。</summary>
    internal void Recreate(string allText, string favoritesText, bool favoritesOnly)
    {
        _segments.RefreshTexts();
        Apply(favoritesOnly);
    }
}
