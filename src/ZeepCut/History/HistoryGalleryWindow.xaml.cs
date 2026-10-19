using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly WrapPanel _cards;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptyBody;
    private readonly Button _clearFilters;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _searchTimer;
    private readonly TextBlock _title;
    private readonly Button _refreshButton;
    private readonly TextBlock _footer;

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

    /// <summary>T27：自动清理后写进状态栏的说明（下次有其它操作时会被覆盖）。</summary>
    private string? _retentionNotice;

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
        Background = HistoryUi.WindowBackground;
        FontFamily = new FontFamily(HistoryUi.FontFamilyName);
        AutomationProperties.SetAutomationId(this, "history-gallery-window");
        AutomationProperties.SetName(this, StringsUi.Get("S.History.WindowName"));

        var content = new Grid { Margin = new Thickness(16, 12, 16, 10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- 第一行：标题 + 搜索 + 收藏开关 + 刷新 ---
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _title = HistoryUi.Text(StringsUi.Get("S.History.Title"), 15, HistoryUi.Foreground, FontWeights.SemiBold);
        _title.HorizontalAlignment = HorizontalAlignment.Left;
        _title.Margin = new Thickness(2, 0, 16, 0);
        Grid.SetColumn(_title, 0);
        header.Children.Add(_title);

        _search = new TextBox
        {
            FontSize = 13.5,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = StringsUi.Get("S.History.SearchPlaceholder"),
            Background = HistoryUi.CardBackground,
            Foreground = HistoryUi.Foreground,
            BorderBrush = HistoryUi.Border,
        };
        AutomationProperties.SetName(_search, StringsUi.Get("S.History.SearchName"));
        AutomationProperties.SetAutomationId(_search, "history-search");
        _search.TextChanged += (_, _) => OnSearchTextChanged();
        _search.PreviewKeyDown += OnSearchKeyDown;
        Grid.SetColumn(_search, 1);
        header.Children.Add(_search);

        _filter = new HistoryTabStrip(
            showAll: () => SetFavoritesOnly(false),
            showFavorites: () => SetFavoritesOnly(true),
            StringsUi.Get("S.History.FilterAll"),
            StringsUi.Get("S.History.FilterFavorites"));
        _filter.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(_filter, 2);
        header.Children.Add(_filter);

        _refreshButton = HistoryUi.Button(
            StringsUi.Get("S.History.Refresh"),
            StringsUi.Get("S.History.Refresh"),
            Reload);
        _refreshButton.Margin = new Thickness(0);
        AutomationProperties.SetAutomationId(_refreshButton, "history-refresh");
        Grid.SetColumn(_refreshButton, 3);
        header.Children.Add(_refreshButton);

        Grid.SetRow(header, 0);
        content.Children.Add(header);

        // --- 第二行：状态条（计数 / 上次操作结果） ---
        _status = HistoryUi.Text(StringsUi.Get("S.History.Loading"), 12, HistoryUi.SecondaryForeground);
        _status.Margin = new Thickness(2, 8, 0, 6);
        AutomationProperties.SetAutomationId(_status, "history-status");
        Grid.SetRow(_status, 1);
        content.Children.Add(_status);

        // --- 第三行：卡片区（卡片与空状态互斥显示） ---
        _cards = new WrapPanel { Orientation = Orientation.Horizontal };
        _scroll = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 4, 0),
        };
        _scroll.ScrollChanged += OnScrollChanged;

        _emptyTitle = HistoryUi.Text(StringsUi.Get("S.History.EmptyTitle"), 15, HistoryUi.Foreground, FontWeights.SemiBold);
        _emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(_emptyTitle, "history-empty-title");

        _emptyBody = HistoryUi.Text(string.Empty, 12.5, HistoryUi.SecondaryForeground);
        _emptyBody.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyBody.TextAlignment = TextAlignment.Center;
        _emptyBody.TextWrapping = TextWrapping.Wrap;
        _emptyBody.MaxWidth = 560;
        _emptyBody.Margin = new Thickness(0, 8, 0, 12);
        AutomationProperties.SetAutomationId(_emptyBody, "history-empty-text");

        _clearFilters = HistoryUi.Button(
            StringsUi.Get("S.History.ClearFilters"),
            StringsUi.Get("S.History.ClearFilters"),
            ClearFilters);
        _clearFilters.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(_clearFilters, "history-clear-filters");

        _empty = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyBody);
        _empty.Children.Add(_clearFilters);
        AutomationProperties.SetAutomationId(_empty, "history-empty-state");

        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_empty);
        Grid.SetRow(body, 2);
        content.Children.Add(body);

        // --- 第四行：底部提示 ---
        _footer = HistoryUi.Text(
            StringsUi.Get("S.History.Footer"),
            11.5,
            HistoryUi.SecondaryForeground);
        _footer.HorizontalAlignment = HorizontalAlignment.Left;
        _footer.TextWrapping = TextWrapping.Wrap;
        _footer.Margin = new Thickness(2, 8, 0, 0);
        Grid.SetRow(_footer, 3);
        content.Children.Add(_footer);

        Content = content;

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

    /// <summary>状态栏当前文本（供验证脚本通过日志核对）。</summary>
    internal string StatusText => _status.Text;

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

            UpdateStatus(_view.HasFilter
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
            UpdateStatus(StringsUi.Get("S.History.ReadFailedStatus"));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>重建卡片：按查询顺序编号（AutomationId 与"第 N 条"日志都用它），每段前插分组标题。</summary>
    private void RebuildCards(IReadOnlyList<HistoryRecord> records)
    {
        _cards.Children.Clear();
        _loaded.Clear();
        _selected = null;

        var index = 0;
        foreach (var group in _groups)
        {
            if (_cards.Children.Count > 0)
            {
                _cards.Children.Add(BuildGroupBreak());
            }

            _cards.Children.Add(BuildGroupHeader(group));

            foreach (var record in group.Items)
            {
                var card = CreateCard(record, index, _store.TryLoadPreview(record));
                _cards.Children.Add(card);
                _loaded.Add(card);
                index++;
            }
        }
    }

    private HistoryCard CreateCard(HistoryRecord record, int index, System.Windows.Media.Imaging.BitmapSource? preview)
    {
        var card = new HistoryCard(record, index, preview, BuildCallbacks());
        card.SetDoubleClickPreview(OpenPreview);
        card.SetClickSelect(SelectCard);
        card.SetDragScroll(_scroll);
        return card;
    }

    private static UIElement BuildGroupHeader(HistoryDayGroup group)
    {
        var text = HistoryUi.Text(
            $"{group.Title}（{group.Count}）",
            13,
            HistoryUi.Foreground,
            FontWeights.SemiBold);
        AutomationProperties.SetAutomationId(
            text,
            $"history-group-{group.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        AutomationProperties.SetName(text, StringsUi.Format("S.History.GroupTitle", group.Title));

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2, 6, 0, 6),
        };
        stack.Children.Add(text);
        return stack;
    }

    /// <summary>
    /// 让分组标题**独占一行**：<see cref="WrapPanel"/> 不认识"整行"，
    /// 所以在标题前插一个足够宽的空占位（宽度 = 可见区宽度），它必然把标题挤到下一行；
    /// 占位本身只占 1px 高，视觉上看不出空隙。
    /// </summary>
    private UIElement BuildGroupBreak()
    {
        var available = _scroll.ActualWidth > 64 ? _scroll.ActualWidth - 32 : 900;
        return new Border { Width = Math.Max(available, 64), Height = 1 };
    }

    private HistoryCardCallbacks BuildCallbacks() => new()
    {
        Copy = record => RunAction(record, StringsUi.Get("S.Common.Copy"), () => HistoryActions.Copy(_store, record)),
        SaveAs = record => RunAction(record, StringsUi.Get("S.Common.SaveAs"), () => HistoryActions.SaveAs(this, _store, record)),
        Pin = record => RunAction(record, StringsUi.Get("S.Common.Pin"), () => HistoryActions.Pin(_store, record)),
        Delete = DeleteRecord,
        Rename = RenameRecord,
        Preview = OpenPreview,
        ToggleFavorite = ToggleFavorite,
        Edit = EditRecord,
    };

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

        UpdateStatus(message);
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
                    UpdateStatus(StringsUi.Format("S.History.KeptFavorite", record.Name));
                    return;
                }

                AppLog.Info("History", $"取消过期收藏已确认：名称={record.Name}，它会在下次清理时被删除");
            }
        }

        var updated = HistoryActions.ToggleFavorite(_store, record);
        if (updated is null)
        {
            UpdateStatus(StringsUi.Get("S.History.FavoriteFailed"));
            return;
        }

        // 先就地更新星标（数据库已经写成功），再按新条件刷新列表：
        // "只看收藏"开着时取消收藏会让这条从列表里消失，这是正确的过滤行为。
        var card = _loaded.FirstOrDefault(c => c.Record.Id == updated.Id);
        card?.ApplyFavorite(updated.IsFavorite);
        Reload();

        if (updated.IsFavorite)
        {
            UpdateStatus(StringsUi.Format("S.History.Favorited", updated.Name));
        }
        else if (HistoryRetentionPolicy.IsExpired(updated, DateTime.UtcNow))
        {
            UpdateStatus(StringsUi.Format("S.History.UnfavoritedExpired", updated.Name));
        }
        else
        {
            UpdateStatus(StringsUi.Format("S.History.Unfavorited", updated.Name));
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
    }

    // --- T26 重命名 ---

    private void RenameRecord(HistoryRecord record)
    {
        AppLog.Info("History", $"历史重命名对话框已打开：Id={record.Id} 当前名称={record.Name}");
        var dialog = new HistoryRenameDialog(this, record.Name);
        if (dialog.ShowDialog() != true)
        {
            AppLog.Info("History", $"历史重命名已取消：Id={record.Id} 名称={record.Name}");
            UpdateStatus(StringsUi.Get("S.History.RenameCancelled"));
            return;
        }

        var updated = HistoryActions.Rename(_store, record, dialog.Result, out var error);
        if (updated is null)
        {
            UpdateStatus(StringsUi.Format("S.History.RenameFailed", error ?? "?"));
            return;
        }

        if (ReferenceEquals(updated, record))
        {
            UpdateStatus(StringsUi.Get("S.History.RenameUnchanged"));
            return;
        }

        Reload();
        _loaded.FirstOrDefault(card => card.Record.Id == updated.Id)?.ApplyName(updated.Name);
        UpdateStatus(StringsUi.Format("S.History.Renamed", updated.Name));
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
            UpdateStatus(StringsUi.Get("S.History.DeleteCancelled"));
            return;
        }

        var result = HistoryActions.Delete(_store, record);
        if (Preview is { } preview && preview.Record.Id == record.Id)
        {
            preview.Close();
        }

        Reload();
        UpdateStatus(result);
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
                UpdateStatus(StringsUi.Get("S.History.EditAlreadyOpen"));
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
            UpdateStatus(StringsUi.Get("S.History.ImageMissingEdit"));
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
            UpdateStatus(StringsUi.Format("S.History.EditOpened", record.Name));
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"打开历史编辑窗口失败：{record.Name}", ex);
            UpdateStatus(StringsUi.Format("S.History.EditOpenFailed", ex.Message));
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
        UpdateStatus(StringsUi.Format(
            "S.History.EditSaveOverwritten",
            saved.Name,
            saved.Width,
            saved.Height,
            countAfter));
    }

    // --- T25 预览 ---

    private void OpenPreview(HistoryRecord record)
    {
        var image = HistoryActions.LoadImage(_store, record);
        if (image is null)
        {
            UpdateStatus(StringsUi.Get("S.History.ImageMissingPreview"));
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
                r => RunAction(r, StringsUi.Get("S.Common.Pin"), () => HistoryActions.Pin(_store, r)));
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
            UpdateStatus(StringsUi.Format("S.History.PreviewOpenFailed", ex.Message));
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
            var added = 0;
            foreach (var record in page.Items.Where(record => !existing.Contains(record.Id)))
            {
                var card = CreateCard(record, _loaded.Count, _store.TryLoadPreview(record));
                _cards.Children.Add(card);
                _loaded.Add(card);
                added++;
            }

            _filteredCount = page.TotalCount;
            AppLog.Info(
                "History",
                $"历史图库加载更多：本页={page.Items.Count} 新增={added} 已显示={_loaded.Count} " +
                $"还有更多={page.HasMore}");
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "历史图库加载更多失败", ex);
            UpdateStatus(StringsUi.Get("S.History.LoadMoreFailed"));
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

    private void UpdateStatus(string message)
    {
        _status.Text = message;
        _status.ToolTip = message;
    }

    /// <summary>
    /// 自动清理删掉记录后在图库状态栏写一句说明（T27）。清理是静默的家务活，
    /// 但用户正看着图库时卡片突然变少会困惑，所以给一行原因。
    /// </summary>
    internal void ShowRetentionNotice(string reason)
    {
        _retentionNotice = reason;
        UpdateStatus(reason);
        AppLog.Info("History", $"历史图库显示清理提示：{reason}");
    }

    /// <summary>最近一次清理提示文本（验证脚本通过 UIA 的 history-status 读取）。</summary>
    internal string? RetentionNotice => _retentionNotice;

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
        UpdateStatus(StringsUi.Format(
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

    /// <summary>主题切换后把新配色套到整棵树（已创建的卡片也会换色）。</summary>
    private void OnThemeChanged(AppTheme effective)
    {
        var palette = ThemeManager.Palette;
        Background = palette.WindowBackgroundBrush;
        HistoryUi.ApplyThemeTo(Content as DependencyObject, _lastPalette);
        _filter?.ApplyPalette();
        DwmApi.TrySetDarkTitleBar(this, palette.IsDark);
        _lastPalette = palette;
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
        HistoryUi.SetButtonText(_refreshButton, StringsUi.Get("S.History.Refresh"));
        HistoryUi.SetButtonText(_clearFilters, StringsUi.Get("S.History.ClearFilters"));
        _footer.Text = StringsUi.Get("S.History.Footer");
        _filter.Recreate(
            StringsUi.Get("S.History.FilterAll"),
            StringsUi.Get("S.History.FilterFavorites"),
            _view.FavoritesOnly);

        // 卡片里的名称/时间/来源/按钮全部重新生成一遍（重建卡片比逐个改文字更不容易漏）。
        Reload();
        AppLog.Info("History", $"历史图库已按新语言刷新：{language}");
    }

    /// <summary>切换前的那套调色板（主题换色时用来识别"哪个颜色是语义色"）。</summary>
    private ThemePalette _lastPalette = ThemeManager.Palette;

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
