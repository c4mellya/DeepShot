using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepShot.Core.History;
using DeepShot.Core.Settings;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.History;

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

    /// <summary>后台读缩略图的并发数：解码是 CPU + 磁盘活，3 路足够铺满一屏又不挤占其它线程。</summary>
    private const int PreviewWorkers = 3;

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
    private readonly TextBlock _subtitle;
    private readonly Button _refreshButton;

    /// <summary>空状态徽标直径（没有记录 / 没有匹配两种徽标同尺寸，切换时不跳）。</summary>
    private const double EmptyBadgeSize = 64;

    /// <summary>日期标题行复选框槽展开后的宽度：22px 复选框 + 10px 与标题的间距。</summary>
    private const double GroupBoxSlotWidth = 22 + Tokens.Space.S + 2;

    /// <summary>下一次重建卡片时播放进入动画（打开窗口、切换筛选、搜索时；收藏/重命名/删除后的刷新不播）。</summary>
    private bool _animateEntrance;

    /// <summary>当前每张卡片的宽度（按可用宽度均分出来的列宽）。</summary>
    private double _cardWidth = HistoryCard.CardWidth;

    // --- 多选 ---
    private readonly HashSet<Guid> _checkedIds = new();
    private readonly List<GroupSection> _sections = new();
    private GroupSection? _currentSection;
    private readonly Border _selectionBar;
    private readonly Grid _selectionHost;
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

    /// <summary>后台读缩略图的取消源：重建列表或关闭窗口时作废还没读完的那批。</summary>
    private CancellationTokenSource _previewLoadCts = new();

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
        SetResourceReference(BackgroundProperty, UiKit.Keys.Window);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "history-gallery-window");
        AutomationProperties.SetName(this, StringsUi.Get("S.History.WindowName"));

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // --- 顶栏：图标 + 标题/条数 | 搜索框 | 全部/收藏分段 | 刷新 ---
        var header = new Grid { Margin = new Thickness(Tokens.Space.XL, Tokens.Space.S, Tokens.Space.XL, Tokens.Space.L + 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _title = UiKit.Text(StringsUi.Get("S.History.Title"), TextRole.Title);

        // 标题下一行小字：当前共多少条 / 筛选命中多少条（等宽数字，刷新时不跳）。
        _subtitle = UiKit.Text(string.Empty, TextRole.Caption);
        _subtitle.Margin = new Thickness(0, 1, 0, 0);
        Typography.SetNumeralAlignment(_subtitle, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(_subtitle, "history-count");

        var titleText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(_title);
        titleText.Children.Add(_subtitle);

        var appIcon = AppChrome.IconImage(32);
        appIcon.Margin = new Thickness(0, 0, Tokens.Space.M, 0);
        appIcon.VerticalAlignment = VerticalAlignment.Center;
        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space.XL, 0),

            // 固定最小宽度：副标题在"共 9 条"与"0 条匹配（库内总计 9 条）"之间切换时，
            // 居中的搜索框不会随输入左右跳动。
            MinWidth = 232,
        };
        titleRow.Children.Add(appIcon);
        titleRow.Children.Add(titleText);
        Grid.SetColumn(titleRow, 0);
        header.Children.Add(titleRow);

        // 搜索框直接用全局 TextBox 样式（同一套边框 / 悬停 / 聚焦光晕），左右留出图标和清除按钮的位置。
        _search = new TextBox
        {
            Height = Tokens.Height.Control,
            FontSize = Tokens.Text.Body,
            Padding = new Thickness(34, 0, 32, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = StringsUi.Get("S.History.SearchPlaceholder"),
        };
        AutomationProperties.SetName(_search, StringsUi.Get("S.History.SearchName"));
        AutomationProperties.SetAutomationId(_search, "history-search");
        _search.TextChanged += (_, _) => OnSearchTextChanged();
        _search.PreviewKeyDown += OnSearchKeyDown;

        _searchHint = UiKit.Text(StringsUi.Get("S.History.SearchName"), TextRole.Body, UiKit.Keys.Secondary);
        _searchHint.VerticalAlignment = VerticalAlignment.Center;
        _searchHint.Margin = new Thickness(36, 0, 34, 0);
        _searchHint.IsHitTestVisible = false;

        _searchClear = UiKit.IconButton(
            AppIcons.Close,
            StringsUi.Get("S.History.SearchClear"),
            StringsUi.Get("S.History.SearchClear"),
            () =>
            {
                _search.Text = string.Empty;
                _search.Focus();
            },
            size: 26,
            iconKey: UiKit.Keys.Secondary,
            iconSize: 12);
        _searchClear.HorizontalAlignment = HorizontalAlignment.Right;
        _searchClear.VerticalAlignment = VerticalAlignment.Center;
        _searchClear.Margin = new Thickness(0, 0, 4, 0);
        _searchClear.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_searchClear, "history-search-clear");

        var searchIcon = AppIcons.Create(AppIcons.Search, 15, UiKit.Keys.Secondary);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.VerticalAlignment = VerticalAlignment.Center;
        searchIcon.Margin = new Thickness(12, 0, 0, 0);

        var searchGrid = new Grid();
        searchGrid.Children.Add(_search);
        searchGrid.Children.Add(searchIcon);
        searchGrid.Children.Add(_searchHint);
        searchGrid.Children.Add(_searchClear);

        _searchPill = new Border
        {
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space.M, 0),
            Child = searchGrid,
        };
        _searchPill.MouseLeftButtonDown += (_, _) => _search.Focus();
        Grid.SetColumn(_searchPill, 1);
        header.Children.Add(_searchPill);

        _filter = new HistoryTabStrip(
            showAll: () => SetFavoritesOnly(false),
            showFavorites: () => SetFavoritesOnly(true),
            StringsUi.Get("S.History.FilterAll"),
            StringsUi.Get("S.History.FilterFavorites"));
        _filter.Margin = new Thickness(0, 0, Tokens.Space.S, 0);
        _filter.VerticalAlignment = VerticalAlignment.Center;
        _filter.Apply(false);
        Grid.SetColumn(_filter, 2);
        header.Children.Add(_filter);

        _refreshButton = UiKit.IconButton(
            AppIcons.Refresh,
            StringsUi.Get("S.History.Refresh"),
            StringsUi.Get("S.History.Refresh") + " (F5)",
            Reload,
            size: Tokens.Height.Control,
            iconSize: 15);
        UiKit.StyleButton(_refreshButton, ButtonKind.Secondary);
        AutomationProperties.SetAutomationId(_refreshButton, "history-refresh");
        Grid.SetColumn(_refreshButton, 3);
        header.Children.Add(_refreshButton);

        Grid.SetRow(header, 0);
        content.Children.Add(header);

        // --- 中间：日期分区 + 卡片（与空状态互斥显示） ---
        _cards = new StackPanel { Orientation = Orientation.Vertical, Margin = ScrollPadding(false) };

        // 列数按可用宽度自动算：卡片等宽铺满整行，右侧不留参差的空白。
        _cards.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                ApplyCardLayout();
            }
        };
        _scroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // 内边距放在内容的 Margin 上而不是 ScrollViewer.Padding：后者会把 ScrollContentPresenter 缩进去，
            // 边缘卡片的悬停投影被它的裁剪框齐刷刷切掉一刀。
        };
        UiKit.QuietScrollCorner(_scroll);
        _scroll.ScrollChanged += OnScrollChanged;

        _emptyTitle = UiKit.Text(StringsUi.Get("S.History.EmptyTitle"), TextRole.Subtitle);
        AutomationProperties.SetAutomationId(_emptyTitle, "history-empty-title");

        _emptyBody = UiKit.Text(string.Empty, TextRole.Body, UiKit.Keys.Secondary, wrap: true);
        _emptyBody.MaxWidth = 420;
        AutomationProperties.SetAutomationId(_emptyBody, "history-empty-text");

        _clearFilters = UiKit.Button(
            StringsUi.Get("S.History.ClearFilters"),
            StringsUi.Get("S.History.ClearFilters"),
            ClearFilters,
            ButtonKind.Secondary,
            "history-clear-filters",
            AppIcons.Close);
        _clearFilters.HorizontalAlignment = HorizontalAlignment.Center;
        _clearFilters.Margin = new Thickness(0, Tokens.Space.L + 2, 0, 0);

        _empty = UiKit.EmptyState(AppIcons.Image, _emptyTitle, _emptyBody, EmptyBadgeSize);
        _empty.Children.Add(_clearFilters);
        _empty.Visibility = Visibility.Collapsed;
        _empty.Margin = new Thickness(Tokens.Space.XL, 0, Tokens.Space.XL, 48);
        AutomationProperties.SetAutomationId(_empty, "history-empty-state");

        // --- 多选操作条：勾选 ≥1 张时从底部滑入的悬浮工具条 ---
        _selectionCount = UiKit.Text(string.Empty, TextRole.BodyStrong);
        _selectionCount.VerticalAlignment = VerticalAlignment.Center;
        _selectionCount.Margin = new Thickness(Tokens.Space.M, 0, Tokens.Space.XS, 0);
        Typography.SetNumeralAlignment(_selectionCount, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(_selectionCount, "history-selection-count");

        _selectAllButton = UiKit.GhostButton(
            StringsUi.Get("S.History.SelectAll"),
            null,
            StringsUi.Get("S.History.SelectAll"),
            SelectAllOrClear);
        AutomationProperties.SetAutomationId(_selectAllButton, "history-select-all");

        _deleteSelectedButton = UiKit.Button(
            StringsUi.Get("S.History.DeleteSelected"),
            StringsUi.Get("S.History.DeleteSelected"),
            DeleteSelected,
            ButtonKind.Danger,
            "history-delete-selected",
            AppIcons.Delete);
        _deleteSelectedButton.Height = 30;
        _deleteSelectedButton.MinWidth = 0;
        _deleteSelectedButton.Padding = new Thickness(12, 0, 14, 0);
        _deleteSelectedButton.Margin = new Thickness(Tokens.Space.XS, 0, 0, 0);

        _clearSelectionButton = UiKit.IconButton(
            AppIcons.Close,
            StringsUi.Get("S.History.ClearSelection"),
            StringsUi.Get("S.History.ClearSelection") + " (Esc)",
            ClearSelection,
            size: 30,
            iconKey: UiKit.Keys.Secondary,
            iconSize: 13);
        _clearSelectionButton.Margin = new Thickness(Tokens.Space.XS, 0, 0, 0);
        AutomationProperties.SetAutomationId(_clearSelectionButton, "history-clear-selection");

        _selectionHost = UiKit.FloatingBar(
            _selectionCount,
            UiKit.ToolbarSeparator(),
            _selectAllButton,
            _deleteSelectedButton,
            _clearSelectionButton);
        _selectionHost.Margin = new Thickness(Tokens.Space.XL, 0, Tokens.Space.XL, Tokens.Space.XL);
        _selectionHost.Visibility = Visibility.Collapsed;
        _selectionHost.Opacity = 0;
        _selectionHost.RenderTransform = _barShift;

        // FloatingBar = [投影底板, 面板, …]：面板就是操作条本体。
        _selectionBar = (Border)_selectionHost.Children[1];
        AutomationProperties.SetAutomationId(_selectionBar, "history-selection-bar");

        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_empty);
        body.Children.Add(_selectionHost);
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
            _animateEntrance = true;
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

            var countText = _view.HasFilter
                ? StringsUi.Format("S.History.CountFiltered", page.TotalCount, _totalCount)
                : StringsUi.Format("S.History.Count", page.TotalCount);
            _subtitle.Text = countText;
            LogResult(countText);

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
            _subtitle.Text = string.Empty;
            ShowEmpty(
                StringsUi.Get("S.History.ReadFailedTitle"),
                StringsUi.Format("S.History.ReadFailedBody", ex.Message),
                showClearFilters: false);
            LogResult(StringsUi.Get("S.History.ReadFailedStatus"));
        }
        finally
        {
            _busy = false;
            _animateEntrance = false;
        }
    }

    /// <summary>重建卡片：按查询顺序编号（AutomationId 与"第 N 条"日志都用它），每个日期一个带标题的分区。</summary>
    private void RebuildCards(IReadOnlyList<HistoryRecord> records)
    {
        CancelPreviewLoad();
        _cards.Children.Clear();
        _loaded.Clear();
        _selected = null;
        _currentWrap = null;
        _currentDate = null;
        _sections.Clear();
        _currentSection = null;

        AppendGroups(_groups);

        if (_animateEntrance)
        {
            // 首屏卡片依次浮现（错开 12ms，最多错开 8 张）；屏幕外的卡片不排队，滚到时已经就位。
            for (var i = 0; i < Math.Min(_loaded.Count, 24); i++)
            {
                _loaded[i].PlayEntrance(i);
            }
        }

        // 勾选集合按 Id 保存：仍在列表里的保持勾选，不在了（被删/被筛掉）的丢弃。
        _checkedIds.IntersectWith(_loaded.Select(card => card.Record.Id));
        RefreshSelectionUi();
    }

    /// <summary>把若干日期分组追加到列表末尾；与当前最后一个分区同一天的接着放进去（加载更多时用）。</summary>
    private void AppendGroups(IReadOnlyList<HistoryDayGroup> groups)
    {
        // 命中内存缓存的卡片当场带图；其余先放空底色，缩略图交给后台读完再填，滚动和输入不被解码卡住。
        var pending = new List<HistoryCard>();
        foreach (var group in groups)
        {
            if (_currentWrap is null || _currentDate != group.Date)
            {
                var section = new StackPanel();
                var info = BuildGroupHeader(group, first: _cards.Children.Count == 0, out var headerElement);
                _sections.Add(info);
                _currentSection = info;
                section.Children.Add(headerElement);
                // 右边距 −Gap：每行最后一张卡片的右间距伸进滚动区内边距，整行与标题行右端对齐。
                _currentWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, -HistoryCard.Gap, 0) };
                section.Children.Add(_currentWrap);
                _cards.Children.Add(section);
                _currentDate = group.Date;
            }

            foreach (var record in group.Items)
            {
                var cached = _store.TryGetCachedPreview(record);
                var card = CreateCard(record, _loaded.Count, cached, loadingPreview: cached is null);
                card.IsChecked = _checkedIds.Contains(record.Id);
                card.SetSelectionMode(_selectionMode);
                card.SetLayoutWidth(_cardWidth);
                _currentWrap.Children.Add(card);
                _loaded.Add(card);
                _currentSection?.Cards.Add(card);
                if (cached is null)
                {
                    pending.Add(card);
                }
            }
        }

        if (pending.Count > 0)
        {
            _ = LoadPreviewsAsync(pending, _previewLoadCts.Token);
        }
    }

    /// <summary>
    /// 后台读取这批卡片的缩略图：最多 <see cref="PreviewWorkers"/> 路并发，按卡片顺序（先上后下）逐张填入。
    /// 解码在线程池，填图回到 UI 线程；列表重建或窗口关闭时取消，晚到的结果直接丢弃。
    /// </summary>
    private async Task LoadPreviewsAsync(IReadOnlyList<HistoryCard> cards, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var next = 0;

        async Task Worker()
        {
            while (!token.IsCancellationRequested)
            {
                // 每个 Worker 都在 UI 线程续接，next 不需要原子操作。
                var index = next++;
                if (index >= cards.Count)
                {
                    return;
                }

                System.Windows.Media.Imaging.BitmapSource? preview;
                try
                {
                    preview = await _store.LoadPreviewAsync(cards[index].Record, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!token.IsCancellationRequested)
                {
                    cards[index].SetPreview(preview);
                }
            }
        }

        try
        {
            await Task.WhenAll(Enumerable.Range(0, Math.Min(PreviewWorkers, cards.Count)).Select(_ => Worker()));
            if (!token.IsCancellationRequested)
            {
                AppLog.Info("History", $"历史图库缩略图后台加载完成：{cards.Count} 张 用时={stopwatch.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"历史图库缩略图后台加载失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>作废还在后台读的缩略图（列表重建 / 窗口关闭）。</summary>
    private void CancelPreviewLoad()
    {
        _previewLoadCts.Cancel();
        _previewLoadCts = new CancellationTokenSource();
    }

    private HistoryCard CreateCard(
        HistoryRecord record,
        int index,
        System.Windows.Media.Imaging.BitmapSource? preview,
        bool loadingPreview = false)
    {
        var card = new HistoryCard(record, index, preview, BuildCallbacks(), loadingPreview);
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
        var text = UiKit.Text(group.Title, TextRole.BodyStrong);
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(text, $"history-group-{dateKey}");
        AutomationProperties.SetName(text, StringsUi.Format("S.History.GroupTitle", group.Title));

        var box = UiKit.CheckBox($"history-group-select-{dateKey}", StringsUi.Format("S.History.SelectGroup", group.Title));
        box.IsThreeState = true;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Opacity = 0;

        // 复选框放在固定宽度的槽里，平时只是透明（仍占位、仍可点击、UI 自动化有真实边界），
        // 悬停 / 选择模式时淡入；标题不随悬停左右跳动。
        var slot = new Border
        {
            Width = GroupBoxSlotWidth,
            VerticalAlignment = VerticalAlignment.Center,
            Child = box,
        };
        var section = new GroupSection(group.Date, box);
        box.GotKeyboardFocus += (_, _) => UpdateGroupBoxVisibility(section);
        box.LostKeyboardFocus += (_, _) => UpdateGroupBoxVisibility(section);
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

        var badge = UiKit.Pill(group.Count.ToString(CultureInfo.InvariantCulture), UiKit.Keys.Secondary);
        badge.Margin = new Thickness(Tokens.Space.S, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;

        var line = UiKit.Divider();
        line.Margin = new Thickness(Tokens.Space.M + 2, 0, 0, 0);
        line.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid
        {
            Margin = new Thickness(0, first ? Tokens.Space.XS : Tokens.Space.L + 2, 0, Tokens.Space.M),
            MinHeight = 24,
            Background = Brushes.Transparent,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(slot, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(badge, 2);
        Grid.SetColumn(line, 3);
        grid.Children.Add(slot);
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
        UiKit.Fade(section.Box, UIElement.OpacityProperty, visible ? 1 : 0, Tokens.Motion.Normal.TotalMilliseconds);
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
        UiKit.SetButtonText(_selectAllButton, allText);
        AutomationProperties.SetName(_selectAllButton, allText);
        var delete = StringsUi.Get("S.History.DeleteSelected");
        AutomationProperties.SetName(_deleteSelectedButton, delete);
        AutomationProperties.SetName(_clearSelectionButton, StringsUi.Get("S.History.ClearSelection"));
        _clearSelectionButton.ToolTip = StringsUi.Get("S.History.ClearSelection") + " (Esc)";
    }

    private void AnimateSelectionBar(bool show)
    {
        _cards.Margin = ScrollPadding(show);
        var ease = Tokens.Motion.Ease;
        var duration = show ? Tokens.Motion.Normal : Tokens.Motion.Fast;

        if (show)
        {
            _selectionHost.Visibility = Visibility.Visible;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
        var slide = new System.Windows.Media.Animation.DoubleAnimation(show ? 0 : 18, duration) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            // 动画结束后释放时钟并落实终值：静止时不留任何变换/半透明，内容才是逐像素清晰的。
            if (show && _selectionMode)
            {
                _selectionHost.BeginAnimation(UIElement.OpacityProperty, null);
                _selectionHost.Opacity = 1;
                _barShift.BeginAnimation(TranslateTransform.YProperty, null);
                _barShift.Y = 0;
            }
            else if (!show && !_selectionMode)
            {
                _selectionHost.Visibility = Visibility.Collapsed;
            }
        };

        _selectionHost.BeginAnimation(UIElement.OpacityProperty, fade);
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
        var confirmed = AppDialog.Confirm(
            this,
            message,
            StringsUi.Get("S.History.DeleteDialogTitle"),
            AppDialogKind.Warning,
            destructive: true);

        if (!confirmed)
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
        var retentionDays = (Application.Current as App)?.SettingsStore?.Current.HistoryRetentionDays
                            ?? HistoryRetentionPolicy.DefaultRetentionDays;
        if (record.IsFavorite)
        {
            var warning = HistoryRetentionPolicy.DescribeFavoriteRemovalWarning(record, DateTime.UtcNow, retentionDays);
            if (warning is not null)
            {
                AppLog.Info(
                    "History",
                    $"取消过期收藏确认框已打开：名称={record.Name} Id={record.Id} " +
                    $"创建={HistoryRecord.FormatUtc(record.CreatedAtUtc)} " +
                    $"已过期=True 剩余天数=0");
                var confirmed = AppDialog.Confirm(
                    this,
                    warning,
                    StringsUi.Get("S.History.UnfavoriteDialogTitle"),
                    AppDialogKind.Warning,
                    destructive: true);

                if (!confirmed)
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
        else if (HistoryRetentionPolicy.IsExpired(updated, DateTime.UtcNow, retentionDays))
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
        _animateEntrance = true;
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
        _animateEntrance = true;
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
        _animateEntrance = true;
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

        var confirmed = AppDialog.Confirm(
            this,
            message,
            StringsUi.Get("S.History.DeleteDialogTitle"),
            AppDialogKind.Warning,
            destructive: true);

        if (!confirmed)
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

    // --- 卡片网格 ---

    /// <summary>滚动区内边距：右侧少留一点给滚动条；多选操作条出现时底部让出它的高度。</summary>
    private static Thickness ScrollPadding(bool selectionBar) =>
        new(Tokens.Space.XL, 2, Tokens.Space.M, selectionBar ? 92 : Tokens.Space.L);

    /// <summary>
    /// 按可用宽度算列数与列宽：每列不窄于 <see cref="HistoryCard.MinCardWidth"/>，
    /// 卡片等宽铺满整行（最后一张的右间距由 WrapPanel 的负边距吃掉）。
    /// </summary>
    private void ApplyCardLayout()
    {
        var available = _cards.ActualWidth + HistoryCard.Gap;
        if (available <= HistoryCard.Gap)
        {
            return;
        }

        var columns = Math.Max(1, (int)Math.Floor(available / (HistoryCard.MinCardWidth + HistoryCard.Gap)));
        var width = Math.Floor(available / columns - HistoryCard.Gap);
        if (Math.Abs(width - _cardWidth) < 0.5)
        {
            return;
        }

        _cardWidth = width;
        foreach (var card in _loaded)
        {
            card.SetLayoutWidth(width);
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

        // 徽标随情形换图标：被筛掉 = 放大镜，一条都没有 / 读取失败 = 图片。
        // 用强调色调：灰底灰图标在浅灰窗口底上发闷，强调色淡底与顶部选中分段、聚焦搜索框呼应。
        var badge = UiKit.StatusBadge(showClearFilters ? AppIcons.Search : AppIcons.Image, UiKit.Keys.Accent, EmptyBadgeSize);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        // UIElementCollection 不支持索引赋值替换（会报"索引已在使用"），先移除再插入。
        _empty.Children.RemoveAt(0);
        _empty.Children.Insert(0, badge);
        _clearFilters.Visibility = showClearFilters ? Visibility.Visible : Visibility.Collapsed;
        var wasHidden = _empty.Visibility != Visibility.Visible;
        _empty.Visibility = Visibility.Visible;
        _scroll.Visibility = Visibility.Collapsed;
        if (wasHidden)
        {
            UiKit.Entrance(_empty);
        }
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
        UiKit.SetButtonText(_clearFilters, StringsUi.Get("S.History.ClearFilters"));
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

        // 托盘常驻期间不留缩略图：作废后台读取并清空内存缓存。
        CancelPreviewLoad();
        _store.ClearPreviewCache();

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
