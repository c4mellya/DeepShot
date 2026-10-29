using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeepShot.Capture;
using DeepShot.Core.History;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 最近截图快览（Win11 弹出面板）：两列 16:10 纯图片缩略图，无文件名与元信息；
/// 指针所在（或键盘选中）的图片右下角浮出深色玻璃操作条（复制 / 贴图 / 标注），单击图片即复制。
/// 完整历史图库仍独立存在。
/// </summary>
internal sealed class HistoryQuickViewWindow : Window
{
    private const int RecentLimit = 20;
    private const int Columns = 2;

    /// <summary>缩略图外边距：两图之间 8px，且给选中环（向外 3px）留出位置。</summary>
    private const double TileMargin = Tokens.Space.XS;

    private readonly HistoryStore _store;
    private readonly Action<HistoryRecord?, bool> _openGallery;
    private readonly Action _openSettings;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _emptyHint;
    private readonly TextBlock _status;
    private readonly UniformGrid _grid;
    private readonly ScrollViewer _scroll;
    private readonly Border _pill;
    private readonly List<Button> _imageActions = new();
    private readonly List<Tile> _tiles = new();
    private IReadOnlyList<HistoryRecord> _records = Array.Empty<HistoryRecord>();
    private int _index;
    private int _interactionDepth;
    private int _pressedIndex = -1;
    private bool _pillShown;
    private Point? _lastPointer;
    private bool _closed;
    private readonly NativeMethods.Point _anchor;
    private readonly bool _anchorAtTopLeft;

    private HistoryRecord? Current => _records.Count == 0 ? null : _records[_index];

    /// <summary>一张缩略图：外层（承载选中环与操作条）+ 图片层（裁圆角、拖出、单击复制）。</summary>
    private sealed record Tile(Grid Root, Border Ring, Image Picture);

    internal HistoryQuickViewWindow(
        HistoryStore store,
        Action<HistoryRecord?, bool> openGallery,
        Action openSettings,
        NativeMethods.Point anchor,
        bool anchorAtTopLeft)
    {
        _store = store;
        _openGallery = openGallery;
        _openSettings = openSettings;
        _anchor = anchor;
        _anchorAtTopLeft = anchorAtTopLeft;
        Title = StringsUi.Get("S.QuickView.Title");
        Width = 424;
        Height = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        // 不用 AllowsTransparency：分层窗口会走软件渲染、丢掉 ClearType，整窗发糊。
        // 圆角、1px 系统描边与阴影交给 DWM（Win11 原生圆角；WindowChrome 的 1px 玻璃边让无边框窗口也有系统阴影）。
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            UseAeroCaptionButtons = false,
        });
        SetResourceReference(BackgroundProperty, UiKit.Keys.Surface);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        Opacity = 0;
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "history-quick-view");

        var root = new Grid { Margin = new Thickness(Tokens.Space.M) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- 头部：左侧“最近”，右侧 图库 / 设置 / 关闭 ---
        var header = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space.S) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = UiKit.Text(StringsUi.Get("S.QuickView.Title"), Tokens.Text.BodyLarge, weight: AppFonts.Semibold, measureNaturalWidth: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.Margin = new Thickness(Tokens.Space.XS, 0, 0, 0);
        header.Children.Add(heading);

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(HeaderButton(AppIcons.Image, "S.Common.History", "quick-view-history", () => OpenGallery(false)));
        tools.Children.Add(HeaderButton(AppIcons.Settings, "S.Common.Settings", "quick-view-settings", OpenSettings));
        tools.Children.Add(HeaderButton(AppIcons.Close, "S.Common.Close", "quick-view-close", Close));
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);
        Add(root, header, 0);

        // --- 缩略图网格（两列等宽，高度按 16:10 跟随宽度） ---
        _grid = new UniformGrid { Columns = Columns, VerticalAlignment = VerticalAlignment.Top };
        _scroll = new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // 抵消缩略图外边距：图片边缘与标题、右上角按钮对齐。
            Margin = new Thickness(-TileMargin, 0, -TileMargin, 0),
            Focusable = false,
        };
        UiKit.QuietScrollCorner(_scroll);
        _scroll.MouseLeave += (_, _) =>
        {
            _lastPointer = null;
            HidePill();
        };

        _emptyText = UiKit.Text(string.Empty, TextRole.BodyStrong, wrap: true);
        _emptyHint = UiKit.Text(StringsUi.Get("S.QuickView.EmptyHint"), TextRole.Caption, wrap: true);
        _empty = UiKit.EmptyState(AppIcons.Image, _emptyText, _emptyHint);

        var body = new Grid();
        body.Children.Add(_scroll);
        body.Children.Add(_empty);
        Add(root, body, 1);

        // --- 浮在图片右下角的深色玻璃操作条（全窗只有一个，跟随当前图片移动） ---
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in new (string Key, string Id, string Icon, Action Action)[]
        {
            ("S.Common.Copy", "quick-view-copy", AppIcons.Copy, Copy),
            ("S.Common.Pin", "quick-view-pin", AppIcons.Pin, Pin),
            ("S.Common.Annotation", "quick-view-edit", AppIcons.Annotate, () => OpenGallery(true)),
        })
        {
            var text = StringsUi.Get(item.Key);
            var button = OverlayChrome.IconButton(item.Icon, text, width: 28, height: 28);
            button.Content = OverlayChrome.Icon(item.Icon, false, 15);
            OverlayChrome.SetNames(button, text, item.Id);
            var action = item.Action;
            button.Click += (_, _) => action();
            _imageActions.Add(button);
            actions.Children.Add(button);
        }

        _pill = new Border
        {
            Child = actions,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(Tokens.Radius.Control + 2),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(Tokens.Space.S - 2),
            SnapsToDevicePixels = true,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _pill.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.OverlayBar);
        _pill.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.OverlayBarBorder);
        _pill.SetResourceReference(TextElement.ForegroundProperty, ThemeManager.BrushKeys.OverlayForeground);

        // --- 仅在拖出 / 贴图 / 失败时才出现的一行提示 ---
        _status = UiKit.Text(string.Empty, TextRole.Caption, wrap: true);
        _status.Margin = new Thickness(Tokens.Space.XS, Tokens.Space.S, Tokens.Space.XS, 0);
        _status.Visibility = Visibility.Collapsed;
        Add(root, _status, 2);

        Content = root;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);
            DwmApi.TrySetRoundedCorners(this);
            PlaceNearCursor();
        };
        Loaded += (_, _) =>
        {
            PlaceNearCursor();
            Opacity = 1;
            UiKit.Entrance(root, 6);
            Activate();
            Focus();
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_closed && _interactionDepth == 0 && !IsActive) Close();
        }));
        Closed += (_, _) => _closed = true;
        PreviewKeyDown += OnKeyDown;
        // 按钮会自行处理按下事件，因此只有点在标题栏空白处的左键才会冒泡到这里。
        MouseLeftButtonDown += (_, e) => OnHeaderMouseDown(header, e);
        Reload();
    }

    /// <summary>左键按住顶部（标题栏及其上下留白）即可拖动窗口。</summary>
    private void OnHeaderMouseDown(FrameworkElement header, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed) return;
        var y = e.GetPosition(header).Y;
        if (y < -16 || y > header.ActualHeight + 8) return;
        e.Handled = true;
        _interactionDepth++;
        try { DragMove(); }
        catch (InvalidOperationException) { /* 按键已在拖动开始前释放 */ }
        finally { _interactionDepth--; }
    }

    private static void Add(Grid root, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        root.Children.Add(element);
    }

    private static Button HeaderButton(string icon, string nameKey, string id, Action action)
    {
        var name = StringsUi.Get(nameKey);
        var button = UiKit.IconButton(icon, name, name, action, size: Tokens.Height.Control, iconKey: UiKit.Keys.Secondary);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    internal void Reload()
    {
        if (_closed || _interactionDepth != 0) return;
        var selectedId = Current?.Id;
        try
        {
            _records = _store.GetPage(new HistoryQuery { Limit = RecentLimit }).Items;
            _index = Math.Max(0, _records.ToList().FindIndex(record => record.Id == selectedId));
            BuildTiles();
            ShowCurrent();
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "加载历史快览失败", ex);
            _records = Array.Empty<HistoryRecord>();
            _index = 0;
            BuildTiles();
            ShowCurrent();
            _emptyText.Text = StringsUi.Get("S.QuickView.LoadFailed");
            _emptyHint.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildTiles()
    {
        DetachPill();
        _grid.Children.Clear();
        _tiles.Clear();
        // 已缓存的缩略图立即显示，其余在后台解码，弹出面板不必等待。
        var pending = new List<(HistoryRecord Record, Image Picture)>();
        for (var i = 0; i < _records.Count; i++)
        {
            var tile = CreateTile(i, _records[i]);
            if (tile.Picture.Source is null) pending.Add((_records[i], tile.Picture));
            _tiles.Add(tile);
            _grid.Children.Add(tile.Root);
        }

        if (pending.Count > 0) _ = FillThumbnailsAsync(pending);
    }

    /// <summary>
    /// 一张纯图片缩略图：8px 圆角裁切 + 1px 内描边（白色截图在浅色面板上也有边界），
    /// 无文件名与元信息；高度按 16:10 跟随列宽。
    /// </summary>
    private Tile CreateTile(int index, HistoryRecord record)
    {
        var picture = new Image
        {
            Source = _store.TryGetCachedPreview(record),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        // 图片未就绪或原图丢失时，底下的淡色图标就是占位。
        var placeholder = AppIcons.Create(AppIcons.Image, 20, ThemeManager.BrushKeys.TertiaryForeground);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;

        var hairline = UiKit.Hairline(Tokens.Radius.Card);
        hairline.Opacity = 0.7;

        var imageLayer = new Grid();
        imageLayer.SetResourceReference(Panel.BackgroundProperty, UiKit.Keys.SurfaceAlt);
        imageLayer.Children.Add(placeholder);
        imageLayer.Children.Add(picture);
        imageLayer.Children.Add(hairline);
        imageLayer.SizeChanged += (_, e) =>
            imageLayer.Clip = new RectangleGeometry(new Rect(e.NewSize), Tokens.Radius.Card, Tokens.Radius.Card);

        // 选中环：外扩 3px，圆角同心（8 + 3）。
        var ring = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Card + 3),
            Margin = new Thickness(-3),
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        ring.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Accent);

        var root = new Grid { Margin = new Thickness(TileMargin) };
        root.Children.Add(ring);
        root.Children.Add(imageLayer);
        root.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) root.Height = Math.Round(e.NewSize.Width * 10 / 16);
        };
        AutomationProperties.SetName(root, record.Name);
        AutomationProperties.SetAutomationId(root, $"quick-view-thumbnail-{index}");

        // 指针真正移动时才跟随选中：键盘翻页把图片滚到指针下时，不会把选中抢回去。
        root.MouseMove += (_, e) =>
        {
            var position = e.GetPosition(this);
            if (_lastPointer == position) return;
            _lastPointer = position;
            Select(index, showPill: true);
        };

        // 按下记在拖出逻辑之前（拖出会把按下标记为已处理）；在同一张图上松开才算单击 = 复制。
        imageLayer.PreviewMouseLeftButtonDown += (_, e) => _pressedIndex = e.ClickCount == 1 ? index : -1;
        HistoryImageDrag.Attach(imageLayer, source => Drag(record, source));
        imageLayer.MouseLeftButtonUp += (_, e) =>
        {
            var clicked = _pressedIndex == index;
            _pressedIndex = -1;
            if (!clicked) return;
            e.Handled = true;
            Select(index, showPill: true);
            Copy();
        };

        return new Tile(root, ring, picture);
    }

    private async Task FillThumbnailsAsync(List<(HistoryRecord Record, Image Picture)> pending)
    {
        try
        {
            await Task.WhenAll(pending.Select(async item =>
            {
                var preview = await _store.LoadPreviewAsync(item.Record);
                if (!_closed) item.Picture.Source = preview;
            }));
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"快览缩略图后台加载失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Select(int index, bool showPill)
    {
        if (_closed || _interactionDepth != 0 || _records.Count == 0) return;
        index = Math.Clamp(index, 0, _records.Count - 1);
        if (index == _index)
        {
            if (showPill && !_pillShown) ShowPill();
            return;
        }

        _index = index;
        ShowCurrent();
        if (showPill) ShowPill();
    }

    private void ShowCurrent()
    {
        var record = Current;
        _empty.Visibility = record is null ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        _emptyText.Text = StringsUi.Get("S.QuickView.Empty");
        _emptyHint.Visibility = Visibility.Visible;
        foreach (var button in _imageActions) button.IsEnabled = record is not null;
        for (var i = 0; i < _tiles.Count; i++)
        {
            UiKit.Fade(_tiles[i].Ring, UIElement.OpacityProperty, i == _index ? 1 : 0, Tokens.Motion.Fast.TotalMilliseconds);
        }

        if (record is not null) MovePill(_tiles[_index]);
        ClearStatus();
    }

    /// <summary>把唯一的操作条挪到指定图片右下角（保持在最上层）。</summary>
    private void MovePill(Tile tile)
    {
        if (ReferenceEquals(_pill.Parent, tile.Root)) return;
        DetachPill();
        tile.Root.Children.Add(_pill);
        if (_pillShown)
        {
            // 跟随指针换图时淡入，不硬切。
            _pill.Opacity = 0;
            UiKit.Fade(_pill, UIElement.OpacityProperty, 1, Tokens.Motion.Normal.TotalMilliseconds);
        }
    }

    private void DetachPill()
    {
        if (_pill.Parent is Panel parent) parent.Children.Remove(_pill);
    }

    private void ShowPill()
    {
        _pillShown = true;
        _pill.IsHitTestVisible = true;
        UiKit.Fade(_pill, UIElement.OpacityProperty, 1, Tokens.Motion.Normal.TotalMilliseconds);
    }

    private void HidePill()
    {
        _pillShown = false;
        _pill.IsHitTestVisible = false;
        UiKit.Fade(_pill, UIElement.OpacityProperty, 0, Tokens.Motion.Fast.TotalMilliseconds);
    }

    /// <summary>键盘选中：显示操作条，并把该图滚入可见区。</summary>
    private void SelectByKeyboard(int index)
    {
        if (_records.Count == 0) return;
        Select(index, showPill: true);
        _tiles[_index].Root.BringIntoView();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left: SelectByKeyboard(_index - 1); break;
            case Key.Right: SelectByKeyboard(_index + 1); break;
            case Key.Up: SelectByKeyboard(_index - Columns); break;
            case Key.Down: SelectByKeyboard(_index + Columns); break;
            case Key.Home: SelectByKeyboard(0); break;
            case Key.End: SelectByKeyboard(_records.Count - 1); break;
            case Key.Enter: Copy(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Run(Action action)
    {
        _interactionDepth++;
        try { action(); }
        catch (Exception ex)
        {
            AppLog.Error("History", "历史快览操作失败", ex);
            SetStatus(StringsUi.Format("S.QuickView.ActionFailed", ex.Message));
        }
        finally
        {
            _interactionDepth--;
            if (!_closed && !IsActive) Close();
        }
    }

    private void Copy()
    {
        if (Current is not { } record) return;
        Run(() =>
        {
            var image = HistoryActions.LoadImage(_store, record)
                ?? throw new InvalidOperationException(StringsUi.Get("S.History.DragMissing"));
            ClipboardWriter.Write(image);
            Close();
        });
    }

    private void Pin()
    {
        if (Current is { } record) Run(() => SetStatus(HistoryActions.Pin(_store, record)));
    }

    private void Drag(HistoryRecord record, DependencyObject source)
    {
        _pressedIndex = -1;
        Run(() => SetStatus(HistoryActions.DragFile(_store, record, source)));
    }

    private void SetStatus(string message)
    {
        // 空消息（取消拖出等无操作）不能留下一行空白提示。
        _status.Text = message;
        _status.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearStatus() => SetStatus(string.Empty);

    private void OpenSettings()
    {
        Close();
        _openSettings();
    }

    private void OpenGallery(bool edit)
    {
        var record = Current;
        if (edit && record is null) return;
        Close();
        _openGallery(record, edit);
    }

    private void PlaceNearCursor()
    {
        if (_closed) return;
        var cursor = _anchor;
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MonitorInfoEx { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var dpi = ScreenCapture.TryGetMonitorDpi(monitor, out var detected) ? detected : 96;
        var scale = dpi / 96.0;
        var width = Math.Min((int)Math.Round(Width * scale), info.rcWork.Width - 16);
        var height = Math.Min((int)Math.Round(Height * scale), info.rcWork.Height - 16);
        var desiredLeft = _anchorAtTopLeft ? cursor.X : cursor.X - width + 12;
        var desiredTop = _anchorAtTopLeft ? cursor.Y : cursor.Y - height - 12;
        var left = Math.Clamp(desiredLeft, info.rcWork.Left + 8, info.rcWork.Right - width - 8);
        var top = Math.Clamp(desiredTop, info.rcWork.Top + 8, info.rcWork.Bottom - height - 8);
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), left, top, width, height,
            NativeMethods.SWP_NOACTIVATE);
    }
}
