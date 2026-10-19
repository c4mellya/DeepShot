using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ZeepCut.Core.History;
using ZeepCut.Core.Settings;
using ZeepCut.Platform;
using ZeepCut.Ui;

namespace ZeepCut.History;

/// <summary>
/// 历史图库主窗口（T25 图库 + T26 历史管理）。
/// </summary>
/// <remarks>
/// <para>
/// <b>唯一实例由 <see cref="HistoryGallery"/> 管</b>（应用无主窗口、托盘常驻：
/// 关闭图库只是关窗口，进程与托盘继续运行）。
/// </para>
/// <para>
/// <b>分层</b>：窗口只管"视图与交互"——把 <see cref="HistoryView"/> 翻成查询、把查到的
/// <see cref="HistoryDayGroup"/> 铺成卡片、把卡片回调转给 <see cref="HistoryActions"/>
/// 并按结果刷新。<b>日期分组、搜索/收藏过滤、重命名校验、空状态文案全部在 Core</b>
/// （<see cref="HistoryGrouping"/> / <see cref="HistoryView"/> / <see cref="HistoryRename"/>），
/// 这里的代码不重复任何业务规则。
/// </para>
/// <para>
/// <b>UIA</b>：搜索框、收藏开关、每张卡片及其星标/名称/操作按钮都带稳定的 AutomationId
/// （<c>history-search</c> / <c>history-filter-favorites</c> / <c>history-card-N</c> /
/// <c>history-action-复制-N</c> …），真实桌面验证脚本按名字与序号定位，不依赖屏幕坐标。
/// </para>
/// <para>
/// <b>刷新策略</b>：任何会改变列表的操作（收藏、重命名、删除、搜索）都走
/// <see cref="Reload"/> 重新查库重建卡片——图库每页最多
/// <see cref="HistoryQuery.MaxLimit"/> 条，重建比增量维护更不容易出错。
/// </para>
/// </remarks>
public sealed class HistoryGalleryWindow : Window
{
    /// <summary>每页条数（图库一页显示多少条；滚到底部自动"加载更多"）。</summary>
    private const int PageSize = 60;

    /// <summary>搜索框输入后的合并刷新延迟（毫秒）：避免每敲一个字符就查一次库。</summary>
    private const int SearchDebounceMs = 250;

    private readonly HistoryStore _store;
    private readonly TextBox _search;
    private readonly HistoryTabStrip _filter;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _cards;
    private readonly Border _searchPill;
    private readonly Button _searchClear;
    private readonly TextBlock _searchHint;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyBody;
    private readonly Button _clearFilters;
    private readonly DispatcherTimer _searchTimer;
    private readonly TextBlock _title;
    private readonly Button _refreshButton;

    // --- 多选 ---
    private readonly HashSet<Guid> _checkedIds = new();
    private readonly List<GroupSection> _sections = new();
    private GroupSection? _currentSection;
    private readonly Border _selectionBar;
    private readonly TranslateTransform _barShift = new(0, 18);
    private readonly TextBlock _selectionCount;
    private readonly Button _selectAllButton;
    private readonly Button _deleteSelectedButton;
    private readonly Button _clearSelectionButton;
    private bool _selectionMode;

    /// <summary>一个日期分区：标题行的复选框 + 该分区里的卡片。</summary>
    private sealed class GroupSection
    {
        internal GroupSection(DateOnly date, CheckBox box)
        {
            Date = date;
            Box = box;
        }

        internal DateOnly Date { get; }

        internal CheckBox Box { get; }

        internal List<HistoryCard> Cards { get; } = new();

        internal bool Hovered { get; set; }
    }

    /// <summary>当前页的卡片（顺序 = 查询顺序 = 日期分组顺序）。</summary>
    private readonly List<HistoryCard> _loaded = new();

    /// <summary>当前视图条件（不可变实例，改变时整体替换）。</summary>
    private HistoryView _view = HistoryView.Default with { PageSize = PageSize };

    private IReadOnlyList<HistoryDayGroup> _groups = Array.Empty<HistoryDayGroup>();
    private int _totalCount;
    private int _filteredCount;
    private bool _lastPage;
    private bool _busy;
    private HistoryCard? _selected;

    /// <summary>当前最后一个日期分区的卡片容器（加载更多时接着往里放）。</summary>
    private WrapPanel? _currentWrap;

    private DateOnly? _currentDate;

    /// <summary>T27：自动清理后写进状态栏的说明（下次有其它操作时会被覆盖）。</summary>

    /// <summary>
    /// 创建图库窗口。外部必须走 <see cref="HistoryGallery.Show"/>（单实例），
    /// 直接 new 会得到两个同时改同一批记录的图库。
    /// </summary>
    /// <param name="store">历史写入器（必须是启动时初始化成功的那一个）。</param>
    internal HistoryGalleryWindow(HistoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Title = StringsUi.Get("S.History.WindowTitle");
        Width = 1180;
        Height = 820;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, HistoryUi.Keys.Window);
        FontFamily = new FontFamily(HistoryUi.FontFamilyName);
        AutomationProperties.SetAutomationId(this, "history-gallery-window");
        AutomationProperties.SetName(this, StringsUi.Get("S.History.WindowName"));

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- 顶栏：标题 | 搜索胶囊 | 全部/收藏分段 | 刷新 ---
        var header = new Grid { Margin = new Thickness(24, 8, 24, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _title = HistoryUi.Text(StringsUi.Get("S.History.Title"), 21, weight: FontWeights.SemiBold);
        _title.HorizontalAlignment = HorizontalAlignment.Left;
        _title.Margin = new Thickness(0, 0, 0, 2);
        var appIcon = AppChrome.IconImage(30);
        appIcon.Margin = new Thickness(0, 0, 12, 0);
        appIcon.VerticalAlignment = VerticalAlignment.Center;
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0),
        };
        titleRow.Children.Add(appIcon);
        titleRow.Children.Add(_title);
        Grid.SetColumn(titleRow, 0);
        header.Children.Add(titleRow);

        _search = new TextBox
        {
            FontSize = 13.5,
            Padding = new Thickness(0, 0, 0, 1),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = StringsUi.Get("S.History.SearchPlaceholder"),
        };
        AutomationProperties.SetName(_search, StringsUi.Get("S.History.SearchName"));
        AutomationProperties.SetAutomationId(_search, "history-search");
        _search.TextChanged += (_, _) => OnSearchTextChanged();
        _search.PreviewKeyDown += OnSearchKeyDown;

        _searchHint = HistoryUi.Text(StringsUi.Get("S.History.SearchName"), 13.5, brushKey: HistoryUi.Keys.Secondary);
        _searchHint.HorizontalAlignment = HorizontalAlignment.Left;
        _searchHint.IsHitTestVisible = false;
        _searchHint.Opacity = 0.8;

        _searchClear = HistoryUi.IconButton(
            HistoryIcons.Close,
            StringsUi.Get("S.History.SearchClear"),
            StringsUi.Get("S.History.SearchClear"),
            () =>
            {
                _search.Text = string.Empty;
                _search.Focus();
            },
            size: 26,
            iconKey: HistoryUi.Keys.Secondary,
            iconSize: 12);
        _searchClear.Margin = new Thickness(0, 0, 6, 0);
        _searchClear.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_searchClear, "history-search-clear");

        var searchIcon = HistoryIcons.Create(HistoryIcons.Search, 16, HistoryUi.Keys.Secondary);
        searchIcon.Margin = new Thickness(15, 0, 9, 0);

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(searchIcon, 0);
        Grid.SetColumn(_search, 1);
        Grid.SetColumn(_searchHint, 1);
        Grid.SetColumn(_searchClear, 2);
        searchGrid.Children.Add(searchIcon);
        searchGrid.Children.Add(_searchHint);
        searchGrid.Children.Add(_search);
        searchGrid.Children.Add(_searchClear);

        _searchPill = new Border
        {
            Height = 38,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(19),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            Child = searchGrid,
        };
        _searchPill.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.Surface);
        _searchPill.SetResourceReference(Border.BorderBrushProperty, HistoryUi.Keys.Border);
        _searchPill.IsKeyboardFocusWithinChanged += (_, _) =>
            _searchPill.SetResourceReference(
                Border.BorderBrushProperty,
                _searchPill.IsKeyboardFocusWithin ? HistoryUi.Keys.Accent : HistoryUi.Keys.Border);
        _searchPill.MouseLeftButtonDown += (_, _) => _search.Focus();
        Grid.SetColumn(_searchPill, 1);
        header.Children.Add(_searchPill);

        _filter = new HistoryTabStrip(
            showAll: () => SetFavoritesOnly(false),
            showFavorites: () => SetFavoritesOnly(true),
            StringsUi.Get("S.History.FilterAll"),
            StringsUi.Get("S.History.FilterFavorites"));
        _filter.Margin = new Thickness(0, 0, 12, 0);
        _filter.Apply(false);
        Grid.SetColumn(_filter, 2);
        header.Children.Add(_filter);

        _refreshButton = HistoryUi.IconButton(
            HistoryIcons.Refresh,
            StringsUi.Get("S.History.Refresh"),
            StringsUi.Get("S.History.Refresh") + " (F5)",
            Reload,
            size: 38,
            iconSize: 16);
        HistoryUi.StyleButton(_refreshButton, primary: false);
        _refreshButton.BorderThickness = new Thickness(1);
        AutomationProperties.SetAutomationId(_refreshButton, "history-refresh");
        Grid.SetColumn(_refreshButton, 3);
        header.Children.Add(_refreshButton);

        Grid.SetRow(header, 0);
        content.Children.Add(header);

        // --- 中间：日期分区 + 卡片（与空状态互斥显示） ---
        _cards = new StackPanel { Orientation = Orientation.Vertical };
        _scroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 2, 12, 16),
        };
        _scroll.ScrollChanged += OnScrollChanged;

        var emptyIcon = new Border
        {
            Width = 104,
            Height = 104,
            CornerRadius = new CornerRadius(52),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = HistoryIcons.Create(HistoryIcons.Image, 46, HistoryUi.Keys.Secondary, 1.3),
        };
        emptyIcon.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.SurfaceAlt);

        _emptyTitle = HistoryUi.Text(StringsUi.Get("S.History.EmptyTitle"), 17, weight: FontWeights.SemiBold);
        _emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyTitle.Margin = new Thickness(0, 22, 0, 0);
        AutomationProperties.SetAutomationId(_emptyTitle, "history-empty-title");

        _emptyBody = HistoryUi.Text(string.Empty, 13, brushKey: HistoryUi.Keys.Secondary);
        _emptyBody.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyBody.TextAlignment = TextAlignment.Center;
        _emptyBody.TextWrapping = TextWrapping.Wrap;
        _emptyBody.MaxWidth = 460;
        _emptyBody.Margin = new Thickness(0, 8, 0, 18);
        AutomationProperties.SetAutomationId(_emptyBody, "history-empty-text");

        _clearFilters = HistoryUi.Button(
            StringsUi.Get("S.History.ClearFilters"),
            StringsUi.Get("S.History.ClearFilters"),
            ClearFilters);
        _clearFilters.HorizontalAlignment = HorizontalAlignment.Center;
        _clearFilters.Margin = new Thickness(0);
        AutomationProperties.SetAutomationId(_clearFilters, "history-clear-filters");

        _empty = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(24, 0, 24, 40),
        };
        _empty.Children.Add(emptyIcon);
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyBody);
        _empty.Children.Add(_clearFilters);
        AutomationProperties.SetAutomationId(_empty, "history-empty-state");

        // --- 多选操作条：勾选 ≥1 张时从底部滑入的悬浮胶囊 ---
        _selectionCount = HistoryUi.Text(string.Empty, 13, weight: FontWeights.SemiBold, measureNaturalWidth: true);
        _selectionCount.Margin = new Thickness(8, 0, 4, 0);
        AutomationProperties.SetAutomationId(_selectionCount, "history-selection-count");

        _selectAllButton = HistoryUi.GhostButton(
            StringsUi.Get("S.History.SelectAll"),
            null,
            StringsUi.Get("S.History.SelectAll"),
            SelectAllOrClear);
        AutomationProperties.SetAutomationId(_selectAllButton, "history-select-all");

        _deleteSelectedButton = BuildDeleteSelectedButton();

        _clearSelectionButton = HistoryUi.IconButton(
            HistoryIcons.Close,
            StringsUi.Get("S.History.ClearSelection"),
            StringsUi.Get("S.History.ClearSelection") + " (Esc)",
            ClearSelection,
            size: 28,
            iconKey: HistoryUi.Keys.Secondary,
            iconSize: 13);
        AutomationProperties.SetAutomationId(_clearSelectionButton, "history-clear-selection");

        var barRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        barRow.Children.Add(_selectionCount);
        barRow.Children.Add(HistoryUi.VerticalSeparator(16));
        barRow.Children.Add(_selectAllButton);
        barRow.Children.Add(_deleteSelectedButton);
        _clearSelectionButton.Margin = new Thickness(6, 0, 0, 0);
        barRow.Children.Add(_clearSelectionButton);

        _selectionBar = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 8, 7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(24, 0, 24, 22),
            Child = barRow,
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            RenderTransform = _barShift,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = 0.22,
            },
        };
        _selectionBar.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.Surface);
        _selectionBar.SetResourceReference(Border.BorderBrushProperty, HistoryUi.Keys.Divider);
        AutomationProperties.SetAutomationId(_selectionBar, "history-selection-bar");

        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_empty);
        body.Children.Add(_selectionBar);
        Grid.SetRow(body, 1);
        content.Children.Add(body);

        Content = content;
        AppChrome.Apply(this);

        SourceInitialized += (_, _) => DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch(_search.Text);
        };

        Loaded += (_, _) =>
        {
            ClampToPrimaryWorkArea();
            Reload();
            _search.Focus();
        };

        Closed += OnClosedInternal;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        // T30/T31：主题或语言变化时就地刷新（不重建窗口，用户的搜索/滚动位置都还在）。
        ThemeManager.Changed += OnThemeChanged;
        StringsUi.Changed += OnLanguageChanged;

        AppLog.Info(
            "History",
            $"历史图库窗口已创建（T25 图库 + T26 收藏/重命名/搜索）：语言={StringsUi.Current} " +
            $"主题={ThemeManager.EffectiveTheme} 标题={Title}");
    }

    // --- 供验证/诊断读取的只读状态 ---

    /// <summary>当前视图条件（搜索 + 只看收藏）。</summary>
    internal HistoryView CurrentView => _view;

    /// <summary>当前显示（已加载）的卡片数量。</summary>
    internal int LoadedCount => _loaded.Count;

    /// <summary>最近一次刷新命中的记录数（过滤后）。</summary>
    internal int FilteredCount => _filteredCount;

    /// <summary>库里总记录数（不过滤）。</summary>
    internal int TotalCount => _totalCount;

    /// <summary>最近一次刷新的日期分组（供真实桌面验证核对分组顺序与标题）。</summary>
    internal IReadOnlyList<HistoryDayGroup> Groups => _groups;

    /// <summary>最近打开的大图预览窗口（未打开为 null）。</summary>
    internal HistoryPreviewWindow? Preview { get; private set; }

    /// <summary>当前的追加标注编辑窗口（T28，未打开为 null）。</summary>
    internal HistoryEditWindow? Edit { get; private set; }


    // --- 刷新 ---

    /// <summary>
    /// 按当前视图重新查库、重建卡片。所有会改变列表的操作最后都走这里。
    /// </summary>
    internal void Reload()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _totalCount = _store.Count();
            var page = _store.GetPage(_view.ToQuery(0));
            _filteredCount = page.TotalCount;
            _lastPage = !page.HasMore;

            // 分组在 Core：今天/昨天/具体日期的标题、组内倒序、未来时间归今天都在那边测过。
            _groups = HistoryGrouping.GroupByDay(page.Items, DateTime.Now);
            RebuildCards(HistoryGrouping.Flatten(_groups));
            UpdateEmptyState();

            LogResult(_view.HasFilter
                ? StringsUi.Format("S.History.CountFiltered", page.TotalCount, _totalCount)
                : StringsUi.Format("S.History.Count", page.TotalCount));

            AppLog.Info(
                "History",
                $"历史图库刷新：{_view} → 命中={page.TotalCount} 显示={_loaded.Count} " +
                $"分组={DescribeGroups(_groups)} 总记录={_totalCount} " +
                $"用时={stopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "历史图库刷新失败", ex);
            _groups = Array.Empty<HistoryDayGroup>();
            _filteredCount = 0;
            _loaded.Clear();
            _cards.Children.Clear();
            ShowEmpty(
                StringsUi.Get("S.History.ReadFailedTitle"),
                StringsUi.Format("S.History.ReadFailedBody", ex.Message),
                showClearFilters: false);
            LogResult(StringsUi.Get("S.History.ReadFailedStatus"));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>重建卡片：按查询顺序编号（AutomationId 与"第 N 条"日志都用它），每个日期一个带标题的分区。</summary>
    private void RebuildCards(IReadOnlyList<HistoryRecord> records)
    {
        _cards.Children.Clear();
        _loaded.Clear();
        _selected = null;
        _currentWrap = null;
        _currentDate = null;
        _sections.Clear();
        _currentSection = null;

        AppendGroups(_groups);

        // 勾选集合按 Id 保存：仍在列表里的保持勾选，不在了（被删/被筛掉）的丢弃。
        _checkedIds.IntersectWith(_loaded.Select(card => card.Record.Id));
        RefreshSelectionUi();
    }

    /// <summary>把若干日期分组追加到列表末尾；与当前最后一个分区同一天的接着放进去（加载更多时用）。</summary>
    private void AppendGroups(IReadOnlyList<HistoryDayGroup> groups)
    {
        foreach (var group in groups)
        {
            if (_currentWrap is null || _currentDate != group.Date)
            {
                var section = new StackPanel();
                var info = BuildGroupHeader(group, first: _cards.Children.Count == 0, out var headerElement);
                _sections.Add(info);
                _currentSection = info;
                section.Children.Add(headerElement);
                _currentWrap = new WrapPanel { Orientation = Orientation.Horizontal };
                section.Children.Add(_currentWrap);
                _cards.Children.Add(section);
                _currentDate = group.Date;
            }

            foreach (var record in group.Items)
            {
                var card = CreateCard(record, _loaded.Count, _store.TryLoadPreview(record));
                card.IsChecked = _checkedIds.Contains(record.Id);
                card.SetSelectionMode(_selectionMode);
                _currentWrap.Children.Add(card);
                _loaded.Add(card);
                _currentSection?.Cards.Add(card);
            }
        }
    }

    private HistoryCard CreateCard(HistoryRecord record, int index, System.Windows.Media.Imaging.BitmapSource? preview)
    {
        var card = new HistoryCard(record, index, preview, BuildCallbacks());
        card.SetDoubleClickPreview(OpenPreview);
        card.SetClickSelect(SelectCard);
        card.SetCheckedChanged(OnCardCheckedChanged);
        card.SetDragScroll(_scroll);
        return card;
    }

    /// <summary>日期分区标题：三态复选框 + 小号次要色标题 + 数量徽标 + 一条延伸到行尾的分隔线。</summary>
    private GroupSection BuildGroupHeader(HistoryDayGroup group, bool first, out UIElement header)
    {
        var dateKey = group.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var text = HistoryUi.Text(group.Title, 12.5, weight: FontWeights.SemiBold, measureNaturalWidth: true, brushKey: HistoryUi.Keys.Secondary);
        text.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(text, $"history-group-{dateKey}");
        AutomationProperties.SetName(text, StringsUi.Format("S.History.GroupTitle", group.Title));

        var box = HistoryUi.CheckBox($"history-group-select-{dateKey}", StringsUi.Format("S.History.SelectGroup", group.Title));
        box.IsThreeState = true;
        box.Margin = new Thickness(0, 0, 10, 0);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Opacity = 0;
        var section = new GroupSection(group.Date, box);
        box.Click += (_, e) =>
        {
            e.Handled = true;
            // 全部已勾 → 取消；否则（含半选）→ 全部勾上。
            var check = !section.Cards.All(card => card.IsChecked);
            foreach (var card in section.Cards)
            {
                card.IsChecked = check;
                if (check)
                {
                    _checkedIds.Add(card.Record.Id);
                }
                else
                {
                    _checkedIds.Remove(card.Record.Id);
                }
            }

            RefreshSelectionUi();
        };

        var count = HistoryUi.Text(group.Count.ToString(CultureInfo.InvariantCulture), 11, brushKey: HistoryUi.Keys.Secondary, measureNaturalWidth: true);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = count,
        };
        badge.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.SurfaceAlt);

        var line = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        line.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.Divider);

        var grid = new Grid { Margin = new Thickness(2, first ? 6 : 14, 0, 10), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(badge, 2);
        Grid.SetColumn(line, 3);
        grid.Children.Add(box);
        grid.Children.Add(text);
        grid.Children.Add(badge);
        grid.Children.Add(line);

        // 复选框：悬停标题行 / 选择模式 / 本组有勾选时才显示。
        grid.MouseEnter += (_, _) =>
        {
            section.Hovered = true;
            UpdateGroupBoxVisibility(section);
        };
        grid.MouseLeave += (_, _) =>
        {
            section.Hovered = false;
            UpdateGroupBoxVisibility(section);
        };

        header = grid;
        return section;
    }

    private void UpdateGroupBoxVisibility(GroupSection section)
    {
        var visible = section.Hovered || _selectionMode || section.Box.IsChecked != false || section.Box.IsKeyboardFocusWithin;
        HistoryUi.Fade(section.Box, UIElement.OpacityProperty, visible ? 1 : 0);
    }

    // --- 多选 ---

    private void OnCardCheckedChanged(HistoryCard card)
    {
        if (card.IsChecked)
        {
            _checkedIds.Add(card.Record.Id);
        }
        else
        {
            _checkedIds.Remove(card.Record.Id);
        }

        RefreshSelectionUi();
    }

    /// <summary>勾选集合变化后：同步日期复选框三态、各卡片的选择模式、操作条显隐与文案。</summary>
    private void RefreshSelectionUi()
    {
        var count = _checkedIds.Count;
        var mode = count > 0;

        if (mode != _selectionMode)
        {
            _selectionMode = mode;
            foreach (var card in _loaded)
            {
                card.SetSelectionMode(mode);
            }

            AnimateSelectionBar(mode);
        }

        foreach (var section in _sections)
        {
            var checkedCount = section.Cards.Count(card => card.IsChecked);
            section.Box.IsChecked = checkedCount == 0 ? false : checkedCount == section.Cards.Count ? true : null;
            UpdateGroupBoxVisibility(section);
        }

        UpdateSelectionTexts();
    }

    private void UpdateSelectionTexts()
    {
        var count = _checkedIds.Count;
        _selectionCount.Text = StringsUi.Format("S.History.SelectedCount", count);
        var all = count > 0 && count >= _loaded.Count;
        var allText = StringsUi.Get(all ? "S.History.ClearSelection" : "S.History.SelectAll");
        HistoryUi.SetButtonText(_selectAllButton, allText);
        AutomationProperties.SetName(_selectAllButton, allText);
        var delete = StringsUi.Get("S.History.DeleteSelected");
        AutomationProperties.SetName(_deleteSelectedButton, delete);
        AutomationProperties.SetName(_clearSelectionButton, StringsUi.Get("S.History.ClearSelection"));
        _clearSelectionButton.ToolTip = StringsUi.Get("S.History.ClearSelection") + " (Esc)";
    }

    private void AnimateSelectionBar(bool show)
    {
        _scroll.Padding = new Thickness(24, 2, 12, show ? 92 : 16);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(show ? 190 : 140);

        if (show)
        {
            _selectionBar.Visibility = Visibility.Visible;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(show ? 0 : 18, duration) { EasingFunction = ease };
        if (!show)
        {
            fade.Completed += (_, _) =>
            {
                if (!_selectionMode)
                {
                    _selectionBar.Visibility = Visibility.Collapsed;
                }
            };
        }

        _selectionBar.BeginAnimation(UIElement.OpacityProperty, fade);
        _barShift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void SelectAllOrClear()
    {
        if (_checkedIds.Count > 0 && _checkedIds.Count >= _loaded.Count)
        {
            ClearSelection();
            return;
        }

        foreach (var card in _loaded)
        {
            card.IsChecked = true;
            _checkedIds.Add(card.Record.Id);
        }

        RefreshSelectionUi();
    }

    private void ClearSelection()
    {
        foreach (var card in _loaded)
        {
            card.IsChecked = false;
        }

        _checkedIds.Clear();
        RefreshSelectionUi();
    }

    private Button BuildDeleteSelectedButton()
    {
        var glyph = HistoryIcons.Create(HistoryIcons.Delete, 15, HistoryUi.Keys.OnAccent);
        glyph.Margin = new Thickness(0, 0, 7, 0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var label = HistoryUi.Text(StringsUi.Get("S.History.DeleteSelected"), 13, weight: FontWeights.SemiBold, measureNaturalWidth: true);
        label.ClearValue(TextBlock.ForegroundProperty);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyph);
        content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            Height = 32,
            Padding = new Thickness(13, 0, 15, 0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(0),
            FontSize = 13,
        };
        button.SetResourceReference(Control.BackgroundProperty, HistoryUi.Keys.Error);
        button.SetResourceReference(Control.ForegroundProperty, HistoryUi.Keys.OnAccent);
        AutomationProperties.SetName(button, StringsUi.Get("S.History.DeleteSelected"));
        AutomationProperties.SetAutomationId(button, "history-delete-selected");
        button.Click += (_, _) => DeleteSelected();
        return button;
    }

    /// <summary>批量删除：整批只问一次；失败的保留勾选，其余删除并刷新。</summary>
    private void DeleteSelected()
    {
        var records = _loaded.Where(card => card.IsChecked).Select(card => card.Record).ToList();
        if (records.Count == 0)
        {
            return;
        }

        var favorites = records.Count(record => record.IsFavorite);
        var message = favorites > 0
            ? StringsUi.Format("S.History.DeleteSelectedConfirmFav", records.Count, favorites)
            : StringsUi.Format("S.History.DeleteSelectedConfirm", records.Count);

        AppLog.Info("History", $"批量删除确认框已打开：数量={records.Count} 其中收藏={favorites}");
        var answer = MessageBox.Show(
            this,
            message,
            StringsUi.Get("S.History.DeleteDialogTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            AppLog.Info("History", "批量删除已取消");
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var record in records)
        {
            if (HistoryActions.TryDelete(_store, record, out _))
            {
                deleted++;
                _checkedIds.Remove(record.Id);
                if (Preview is { } preview && preview.Record.Id == record.Id)
                {
                    preview.Close();
                }

                if (Edit is { } edit && edit.Record.Id == record.Id)
                {
                    edit.Close();
                }
            }
            else
            {
                failed++;
            }
        }

        AppLog.Info("History", $"批量删除完成：成功={deleted} 失败={failed}");
        Reload();
        LogResult(failed == 0
            ? StringsUi.Format("S.History.DeletedMany", deleted)
            : StringsUi.Format("S.History.DeletedManyFailed", deleted, failed));
    }

    private HistoryCardCallbacks BuildCallbacks() => new()
    {
        Copy = record => RunAction(record, StringsUi.Get("S.Common.Copy"), () => HistoryActions.Copy(_store, record)),
        Drag = DragRecord,
        SaveAs = record => RunAction(record, StringsUi.Get("S.Common.SaveAs"), () => HistoryActions.SaveAs(this, _store, record)),
        Pin = record => RunAction(record, StringsUi.Get("S.Common.Pin"), () => HistoryActions.Pin(_store, record)),
        Delete = DeleteRecord,
        Rename = RenameRecord,
        Preview = OpenPreview,
        ToggleFavorite = ToggleFavorite,
        Edit = EditRecord,
    };

    private void DragRecord(HistoryRecord record, DependencyObject source) =>
        RunAction(record, StringsUi.Get("S.History.DragAction"), () => HistoryActions.DragFile(_store, record, source));

    /// <summary>执行一个 <see cref="HistoryActions"/> 动作并在状态栏显示结果。</summary>
    private void RunAction(HistoryRecord record, string action, Func<string> run)
    {
        string message;
        try
        {
            message = run();
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"历史“{action}”失败：{record.Name}", ex);
            message = StringsUi.Format("S.History.ActionFailed", action, ex.Message);
        }

        LogResult(message);
        AppLog.Info("History", $"历史图库“{action}”结果：名称={record.Name} {message}");
    }

    // --- T26 收藏 ---

    /// <summary>
    /// 切换收藏。T27：**取消一条已过期收藏**时先确认——取消收藏后它会在下一次清理
    /// （启动或跨日）被删掉，用户必须有机会反悔（PLAN：告知即将清理并允许取消）。
    /// 文案由 Core 的 <see cref="HistoryRetentionPolicy.DescribeFavoriteRemovalWarning"/> 给出。
    /// </summary>
    private void ToggleFavorite(HistoryRecord record)
    {
        if (record.IsFavorite)
        {
            var warning = HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(record, DateTime.UtcNow);
            if (warning is not null)
            {
                AppLog.Info(
                    "History",
                    $"取消过期收藏确认框已打开：名称={record.Name} Id={record.Id} " +
                    $"创建={HistoryRecord.FormatUtc(record.CreatedAtUtc)} " +
                    $"已过期=True 剩余天数=0");
                var answer = MessageBox.Show(
                    this,
                    warning,
                    StringsUi.Get("S.History.UnfavoriteDialogTitle"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                if (answer != MessageBoxResult.OK)
                {
                    AppLog.Info("History", $"取消过期收藏已放弃：用户选择保留收藏 名称={record.Name}");
                    LogResult(StringsUi.Format("S.History.KeptFavorite", record.Name));
                    return;
                }

                AppLog.Info("History", $"取消过期收藏已确认：名称={record.Name}，它会在下次清理时被删除");
            }
        }

        var updated = HistoryActions.ToggleFavorite(_store, record);
        if (updated is null)
        {
            LogResult(StringsUi.Get("S.History.FavoriteFailed"));
            return;
        }

        // 先就地更新星标（数据库已经写成功），再按新条件刷新列表：
        // "只看收藏"开着时取消收藏会让这条从列表里消失，这是正确的过滤行为。
        var card = _loaded.FirstOrDefault(c => c.Record.Id == updated.Id);
        card?.ApplyFavorite(updated.IsFavorite);
        Reload();

        if (updated.IsFavorite)
        {
            LogResult(StringsUi.Format("S.History.Favorited", updated.Name));
        }
        else if (HistoryRetentionPolicy.IsExpired(updated, DateTime.UtcNow))
        {
            LogResult(StringsUi.Format("S.History.UnfavoritedExpired", updated.Name));
        }
        else
        {
            LogResult(StringsUi.Format("S.History.Unfavorited", updated.Name));
        }
    }

    private void SetFavoritesOnly(bool favoritesOnly)
    {
        if (_view.FavoritesOnly == favoritesOnly)
        {
            _filter.Apply(favoritesOnly);
            return;
        }

        _view = _view.WithFavoritesOnly(favoritesOnly);
        _filter.Apply(favoritesOnly);
        AppLog.Info("History", $"历史图库收藏筛选：只看收藏={favoritesOnly}");
        Reload();
    }

    // --- T26 搜索 ---

    private void ApplySearch(string? text)
    {
        var updated = _view.WithSearch(text);
        if (string.Equals(updated.NormalizedSearchText, _view.NormalizedSearchText, StringComparison.Ordinal))
        {
            return;
        }

        _view = updated;
        AppLog.Info(
            "History",
            $"历史图库搜索：关键字=\"{_view.NormalizedSearchText ?? string.Empty}\" " +
            $"只看收藏={_view.FavoritesOnly}");
        Reload();
    }

    private void OnSearchTextChanged()
    {
        var hasText = _search.Text.Length > 0;
        _searchHint.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        _searchClear.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        // 防抖：每敲一个字符就查库会让输入卡顿，250ms 合并后再刷（Enter 立即刷）。
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchTimer.Stop();
            ApplySearch(_search.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _search.Text.Length > 0)
        {
            _search.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void ClearFilters()
    {
        _searchTimer.Stop();
        _search.Text = string.Empty;
        _view = _view.ClearFilters();
        _filter.Apply(false);
        AppLog.Info("History", "历史图库已清除筛选（搜索与只看收藏）");
        Reload();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _checkedIds.Count > 0 &&
                 !(_search.IsKeyboardFocusWithin && _search.Text.Length > 0))
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    // --- T26 重命名 ---

    private void RenameRecord(HistoryRecord record)
    {
        AppLog.Info("History", $"历史重命名对话框已打开：Id={record.Id} 当前名称={record.Name}");
        var dialog = new HistoryRenameDialog(this, record.Name);
        if (dialog.ShowDialog() != true)
        {
            AppLog.Info("History", $"历史重命名已取消：Id={record.Id} 名称={record.Name}");
            return;
        }

        var updated = HistoryActions.Rename(_store, record, dialog.Result, out var error);
        if (updated is null)
        {
            LogResult(StringsUi.Format("S.History.RenameFailed", error ?? "?"));
            return;
        }

        if (ReferenceEquals(updated, record))
        {
            LogResult(StringsUi.Get("S.History.RenameUnchanged"));
            return;
        }

        Reload();
        _loaded.FirstOrDefault(card => card.Record.Id == updated.Id)?.ApplyName(updated.Name);
        LogResult(StringsUi.Format("S.History.Renamed", updated.Name));
    }

    // --- T25 删除 ---

    private void DeleteRecord(HistoryRecord record)
    {
        var message = record.IsFavorite
            ? StringsUi.Format("S.History.DeleteConfirmFavorite", record.Name)
            : StringsUi.Format("S.History.DeleteConfirm", record.Name);

        AppLog.Info(
            "History",
            $"历史删除确认框已打开：名称={record.Name} Id={record.Id} 收藏={record.IsFavorite}");

        var answer = MessageBox.Show(
            this,
            message,
            StringsUi.Get("S.History.DeleteDialogTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            AppLog.Info("History", $"历史删除已取消：名称={record.Name} Id={record.Id}");
            return;
        }

        var result = HistoryActions.Delete(_store, record);
        if (Preview is { } preview && preview.Record.Id == record.Id)
        {
            preview.Close();
        }

        Reload();
        LogResult(result);
    }

    // --- T28 追加标注覆盖 ---

    /// <summary>
    /// 打开这条记录的编辑窗口（T28）：复用标注组件，在现有图片上追加标注，
    /// “保存并覆盖”写回**同一条记录**（保留 Id/名称/创建时间/收藏，不新增记录）。
    /// 每条记录同时只开一个编辑窗口（再点就激活已有那个）。
    /// </summary>
    private void EditRecord(HistoryRecord record)
    {
        if (Edit is { } existing)
        {
            if (existing.Record.Id == record.Id)
            {
                AppLog.Info("History", $"编辑窗口已打开，本次只激活：名称={record.Name} Id={record.Id}");
                existing.Activate();
                LogResult(StringsUi.Get("S.History.EditAlreadyOpen"));
                return;
            }

            AppLog.Info(
                "History",
                $"已有另一条记录的编辑窗口（{existing.Record.Name}），先关闭它再打开 {record.Name}");
            existing.Close();
        }

        var image = HistoryActions.LoadImage(_store, record);
        if (image is null)
        {
            LogResult(StringsUi.Get("S.History.ImageMissingEdit"));
            return;
        }

        try
        {
            var before = _store.Count();
            var window = new HistoryEditWindow(this, _store, record, image);
            window.Closed += (_, _) => OnEditWindowClosed(window, before);
            Edit = window;
            window.Show();
            window.Activate();
            AppLog.Info(
                "History",
                $"历史编辑窗口已打开：名称={record.Name} Id={record.Id} 图片={image.PixelWidth}x{image.PixelHeight} " +
                $"历史条数={before} 收藏={record.IsFavorite}");
            LogResult(StringsUi.Format("S.History.EditOpened", record.Name));
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"打开历史编辑窗口失败：{record.Name}", ex);
            LogResult(StringsUi.Format("S.History.EditOpenFailed", ex.Message));
        }
    }

    private void OnEditWindowClosed(HistoryEditWindow window, int countBefore)
    {
        if (ReferenceEquals(Edit, window))
        {
            Edit = null;
        }

        if (!window.WasSaved || window.SavedRecord is not { } saved)
        {
            AppLog.Info(
                "History",
                $"历史编辑窗口已关闭（未保存）：名称={window.Record.Name} Id={window.Record.Id} " +
                "原图未修改");
            return;
        }

        var countAfter = _store.Count();
        AppLog.Info(
            "History",
            $"历史编辑覆盖完成：名称={saved.Name} Id={saved.Id} 尺寸={saved.Width}x{saved.Height} " +
            $"追加标注={window.AddedAnnotationCount} 历史条数 {countBefore} → {countAfter}：" +
            $"创建时间={HistoryRecord.FormatUtc(saved.CreatedAtUtc)} 收藏={saved.IsFavorite} " +
            $"修改时间={HistoryRecord.FormatUtc(saved.UpdatedAtUtc)}");

        Reload();
        LogResult(StringsUi.Format(
            "S.History.EditSaveOverwritten",
            saved.Name,
            saved.Width,
            saved.Height,
            countAfter));
    }

    /// <summary>Open the quick-view image in the existing gallery and optionally edit it.</summary>
    internal void RevealRecord(HistoryRecord record, bool edit)
    {
        ClearFilters();
        _searchTimer.Stop(); // Clearing the search box must not schedule a second reload.
        var card = _loaded.FirstOrDefault(item => item.Record.Id == record.Id);
        if (card is null)
        {
            LogResult(StringsUi.Get("S.QuickView.RecordMissing"));
            return;
        }
        SelectCard(card);
        Dispatcher.BeginInvoke(new Action(() => card.BringIntoView()));
        if (edit) EditRecord(card.Record);
    }

    // --- T25 预览 ---

    private void OpenPreview(HistoryRecord record)
    {
        var image = HistoryActions.LoadImage(_store, record);
        if (image is null)
        {
            LogResult(StringsUi.Get("S.History.ImageMissingPreview"));
            return;
        }

        try
        {
            if (Preview is { } existing)
            {
                existing.Close();
                Preview = null;
            }

            var window = new HistoryPreviewWindow(
                this,
                record,
                image,
                r => RunAction(r, StringsUi.Get("S.Common.Copy"), () => HistoryActions.Copy(_store, r)),
                r => RunAction(r, StringsUi.Get("S.Common.Pin"), () => HistoryActions.Pin(_store, r)),
                DragRecord);
            window.ClampToWorkArea();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(Preview, window))
                {
                    Preview = null;
                }
            };

            Preview = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"打开历史预览失败：{record.Name}", ex);
            LogResult(StringsUi.Format("S.History.PreviewOpenFailed", ex.Message));
        }
    }

    // --- 加载更多 ---

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0 || _lastPage || _busy)
        {
            return;
        }

        // 离底部还远就不管（每页 60 条，正常使用很少触发）。
        if (e.ExtentHeight - e.VerticalOffset - e.ViewportHeight > 160)
        {
            return;
        }

        LoadMore();
    }

    private void LoadMore()
    {
        try
        {
            var page = _store.GetPage(_view.ToQuery(_loaded.Count));
            _lastPage = !page.HasMore;
            if (page.Items.Count == 0)
            {
                return;
            }

            var existing = _loaded.Select(card => card.Record.Id).ToHashSet();
            var fresh = page.Items.Where(record => !existing.Contains(record.Id)).ToList();
            var added = fresh.Count;
            AppendGroups(HistoryGrouping.GroupByDay(fresh, DateTime.Now));
            RefreshSelectionUi();

            _filteredCount = page.TotalCount;
            AppLog.Info(
                "History",
                $"历史图库加载更多：本页={page.Items.Count} 新增={added} 已显示={_loaded.Count} " +
                $"还有更多={page.HasMore}");
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "历史图库加载更多失败", ex);
            LogResult(StringsUi.Get("S.History.LoadMoreFailed"));
        }
    }

    // --- 空状态 / 状态栏 / 选中 ---

    private void UpdateEmptyState()
    {
        if (_filteredCount > 0)
        {
            _empty.Visibility = Visibility.Collapsed;
            _scroll.Visibility = Visibility.Visible;
            return;
        }

        // 文案全部来自 Core（区分"一条都没有"与"被筛掉了"），窗口不自己拼。
        var body = DescribeEmptyState(_view, _totalCount);
        var title = _totalCount <= 0
            ? StringsUi.Get("S.History.EmptyTitle")
            : _view.HasFilter
                ? StringsUi.Get("S.History.NoMatchTitle")
                : StringsUi.Get("S.History.NothingToShowTitle");

        ShowEmpty(title, body, showClearFilters: _view.HasFilter);
    }

    private void ShowEmpty(string title, string body, bool showClearFilters)
    {
        _emptyTitle.Text = title;
        _emptyBody.Text = body;
        _clearFilters.Visibility = showClearFilters ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        AppLog.Info(
            "History",
            $"历史图库空状态：标题={title} 说明={body} 库内总计={_totalCount} " +
            $"有筛选={_view.HasFilter}");
    }

    private static void LogResult(string message)
    {
        if (!string.IsNullOrWhiteSpace(message)) AppLog.Info("History", message);
    }

    /// <summary>保留清理日志；图库不再显示底部提示栏。</summary>
    internal void ShowRetentionNotice(string reason) => LogResult(reason);

    private void SelectCard(HistoryCard card)
    {
        if (ReferenceEquals(_selected, card))
        {
            return;
        }

        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = card;
        card.IsSelected = true;
        LogResult(StringsUi.Format(
            "S.History.SelectedStatus",
            card.Record.Name,
            card.Record.Width,
            card.Record.Height));
    }

    // --- 生命周期 ---

    /// <summary>让窗口初始尺寸不超过工作区（小屏/副屏上开窗不至于溢出）。</summary>
    private void ClampToPrimaryWorkArea()
    {
        try
        {
            var work = SystemParameters.WorkArea;
            Width = Math.Min(Width, Math.Max(MinWidth, work.Width - 80));
            Height = Math.Min(Height, Math.Max(MinHeight, work.Height - 80));
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"按工作区限制图库尺寸失败（忽略）：{ex.Message}");
        }
    }

    // --- T30 主题 / T31 语言 ---

    /// <summary>主题切换：所有颜色都是主题资源引用、自动更新；这里只需要同步系统标题栏的深浅。</summary>
    private void OnThemeChanged(AppTheme effective)
    {
        var palette = ThemeManager.Palette;
        DwmApi.TrySetDarkTitleBar(this, palette.IsDark);
        AppLog.Info("History", $"历史图库已按新主题刷新：{effective}（{palette}）");
    }

    /// <summary>语言切换后就地换文案（重建卡片与空状态，保留当前筛选）。</summary>
    private void OnLanguageChanged(AppLanguage language)
    {
        Title = StringsUi.Get("S.History.WindowTitle");
        AutomationProperties.SetName(this, StringsUi.Get("S.History.WindowName"));
        _title.Text = StringsUi.Get("S.History.Title");
        _search.ToolTip = StringsUi.Get("S.History.SearchPlaceholder");
        AutomationProperties.SetName(_search, StringsUi.Get("S.History.SearchName"));
        AutomationProperties.SetName(_refreshButton, StringsUi.Get("S.History.Refresh"));
        _refreshButton.ToolTip = StringsUi.Get("S.History.Refresh") + " (F5)";
        AutomationProperties.SetName(_searchClear, StringsUi.Get("S.History.SearchClear"));
        _searchClear.ToolTip = StringsUi.Get("S.History.SearchClear");
        _searchHint.Text = StringsUi.Get("S.History.SearchName");
        HistoryUi.SetButtonText(_clearFilters, StringsUi.Get("S.History.ClearFilters"));
        UpdateSelectionTexts();
        _filter.Recreate(
            StringsUi.Get("S.History.FilterAll"),
            StringsUi.Get("S.History.FilterFavorites"),
            _view.FavoritesOnly);

        // 卡片里的名称/时间/来源/按钮全部重新生成一遍（重建卡片比逐个改文字更不容易漏）。
        Reload();
        AppLog.Info("History", $"历史图库已按新语言刷新：{language}");
    }

    /// <summary>
    /// 空状态说明（T31）：与 Core <see cref="HistoryView.DescribeEmptyState"/> 同一套分支，
    /// 但文案走语言资源，并且把快捷键换成当前设置里的组合。
    /// </summary>
    private static string DescribeEmptyState(HistoryView view, int totalCount)
    {
        if (totalCount <= 0)
        {
            return StringsUi.Format("S.History.EmptyNoRecords", ShortcutText());
        }

        if (view.FavoritesOnly && view.NormalizedSearchText is not null)
        {
            return StringsUi.Format("S.History.EmptySearchAndFavorites", view.NormalizedSearchText);
        }

        if (view.FavoritesOnly)
        {
            return StringsUi.Get("S.History.EmptyFavorites");
        }

        if (view.NormalizedSearchText is not null)
        {
            return StringsUi.Format("S.History.EmptySearch", view.NormalizedSearchText);
        }

        return StringsUi.Get("S.History.EmptyOther");
    }

    /// <summary>当前截图快捷键（空状态里提示"按 X 截个图"）。</summary>
    private static string ShortcutText()
    {
        var shortcut = HistoryShortcutProvider.ScreenshotShortcut;
        return string.IsNullOrWhiteSpace(shortcut) ? AppSettings.DefaultScreenshotShortcut : shortcut;
    }

    private void OnClosedInternal(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        ThemeManager.Changed -= OnThemeChanged;
        StringsUi.Changed -= OnLanguageChanged;

        // T28：图库关闭时一并关闭编辑窗口（未保存就等于取消，原图不动）。
        if (Edit is { } edit)
        {
            Edit = null;
            edit.Close();
        }

        if (Preview is { } preview)
        {
            Preview = null;
            preview.Close();
        }

        AppLog.Info("History", "历史图库窗口已关闭（应用继续在托盘运行）");
    }

    private static string DescribeGroups(IReadOnlyList<HistoryDayGroup> groups) =>
        groups.Count == 0
            ? "-"
            : string.Join(" | ", groups.Select(group => $"{group.Title}={group.Count}"));
}
