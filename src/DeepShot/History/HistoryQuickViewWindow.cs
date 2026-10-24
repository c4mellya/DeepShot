using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeepShot.Capture;
using DeepShot.Core.History;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>A transient recent-image browser; the full history gallery remains independent.</summary>
internal sealed class HistoryQuickViewWindow : Window
{
    private const int RecentLimit = 20;
    /// <summary>未选中缩略图只轻微压暗：0.6 会把浅色截图洗成一片灰，0.72 仍能一眼分出选中项。</summary>
    private const double UnselectedThumbnailOpacity = 0.72;
    private readonly HistoryStore _store;
    private readonly Action<HistoryRecord?, bool> _openGallery;
    private readonly Action _openSettings;
    private readonly Image _picture;
    private readonly Grid _pictureStage;
    private readonly Border _imageFrame;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _emptyHint;
    private readonly TextBlock _name;
    private readonly TextBlock _dimensions;
    private readonly TextBlock _counter;
    private readonly TextBlock _status;
    private readonly StackPanel _thumbnails;
    private readonly ScrollViewer _strip;
    private readonly Grid _meta;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly List<Button> _imageActions = new();
    private readonly List<Border> _thumbnailBorders = new();
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private IReadOnlyList<HistoryRecord> _records = Array.Empty<HistoryRecord>();
    private int _index;
    private int _wheelDelta;
    private int _interactionDepth;
    private bool _closed;
    private readonly NativeMethods.Point _anchor;
    private readonly bool _anchorAtTopLeft;

    private HistoryRecord? Current => _records.Count == 0 ? null : _records[_index];

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
        Height = 532;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;

        // 不用 AllowsTransparency：分层窗口会走软件渲染、丢掉 ClearType，整窗发糊。
        // 圆角与阴影交给 DWM（Win11 原生圆角；WindowChrome 的 1px 玻璃边让无边框窗口也有系统阴影）。
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

        var root = new Grid { Margin = new Thickness(Tokens.Space.L) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- 头部：标题 + 计数（左），历史 / 设置 / 关闭（右上角入口） ---
        var header = new Grid { Margin = new Thickness(Tokens.Space.XS, 0, 0, Tokens.Space.M) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = UiKit.Text(StringsUi.Get("S.QuickView.Title"), TextRole.Subtitle);
        heading.TextTrimming = TextTrimming.None;
        heading.VerticalAlignment = VerticalAlignment.Center;
        // 计数用等宽数字："3 / 12" 翻页时不左右跳。
        _counter = UiKit.Text(string.Empty, TextRole.Caption);
        _counter.TextTrimming = TextTrimming.None;
        _counter.VerticalAlignment = VerticalAlignment.Center;
        _counter.Margin = new Thickness(Tokens.Space.S + 2, 1, 0, 0);
        System.Windows.Documents.Typography.SetNumeralAlignment(_counter, FontNumeralAlignment.Tabular);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleRow.Children.Add(heading);
        titleRow.Children.Add(_counter);
        header.Children.Add(titleRow);

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(HeaderButton(AppIcons.Gallery, "S.Common.History", "quick-view-history", () => OpenGallery(false)));
        tools.Children.Add(HeaderButton(AppIcons.Settings, "S.Common.Settings", "quick-view-settings", OpenSettings));
        tools.Children.Add(HeaderButton(AppIcons.Close, "S.Common.Close", "quick-view-close", Close));
        Grid.SetColumn(tools, 1);
        header.Children.Add(tools);
        Add(root, header, 0);

        // --- 大图 + 两侧悬停出现的翻页按钮 ---
        _picture = new Image
        {
            Stretch = Stretch.Uniform,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_picture, "quick-view-image");
        _picture.ToolTip = StringsUi.Get("S.History.DragHint");
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        HistoryImageDrag.Attach(_picture, source =>
        {
            if (Current is { } record) Drag(record, source);
        });

        // 图片垫一块投影底板 + 细描边：浮在浅灰画框里，像一张照片而不是一块贴图。
        _pictureStage = UiKit.Elevated(_picture, cornerRadius: 2, outline: true, blur: 18, depth: 4, opacity: 0.16);
        _pictureStage.Margin = new Thickness(Tokens.Space.M);

        _emptyText = UiKit.Text(string.Empty, TextRole.BodyStrong, wrap: true);
        _emptyHint = UiKit.Text(StringsUi.Get("S.QuickView.EmptyHint"), TextRole.Caption, wrap: true);
        _empty = UiKit.EmptyState(AppIcons.Image, _emptyText, _emptyHint);

        _previous = NavButton(AppIcons.ChevronLeft, "S.QuickView.Previous", "quick-view-previous", () => Select(_index - 1), HorizontalAlignment.Left);
        _next = NavButton(AppIcons.ChevronRight, "S.QuickView.Next", "quick-view-next", () => Select(_index + 1), HorizontalAlignment.Right);

        var imageHost = new Grid { Background = Brushes.Transparent };
        imageHost.Children.Add(_pictureStage);
        imageHost.Children.Add(_empty);
        imageHost.Children.Add(_previous);
        imageHost.Children.Add(_next);
        imageHost.MouseEnter += (_, _) => FadeNav(1);
        imageHost.MouseLeave += (_, _) => FadeNav(0);

        _imageFrame = new Border
        {
            CornerRadius = new CornerRadius(Tokens.Radius.Card),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = imageHost,
        };
        _imageFrame.SetResourceReference(Border.BackgroundProperty, UiKit.Keys.SurfaceAlt);
        _imageFrame.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Divider);
        _imageFrame.SizeChanged += (_, _) =>
        {
            // First layout pass: decode immediately so the image is crisp on first paint.
            if (_picture.Source is null) RefreshPicture();
            else OnImageFrameResized();
        };
        Add(root, _imageFrame, 1);

        // --- 名称 + 尺寸 ---
        _name = UiKit.Text(string.Empty, TextRole.BodyStrong);
        _name.VerticalAlignment = VerticalAlignment.Center;
        _dimensions = UiKit.Text(string.Empty, TextRole.Caption);
        _dimensions.TextTrimming = TextTrimming.None;
        _dimensions.VerticalAlignment = VerticalAlignment.Center;
        _dimensions.Margin = new Thickness(Tokens.Space.M, 0, 0, 0);
        System.Windows.Documents.Typography.SetNumeralAlignment(_dimensions, FontNumeralAlignment.Tabular);
        var meta = _meta = new Grid { Margin = new Thickness(Tokens.Space.XS, Tokens.Space.M, Tokens.Space.XS, Tokens.Space.S + 2) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.Children.Add(_name);
        Grid.SetColumn(_dimensions, 1);
        meta.Children.Add(_dimensions);
        Add(root, meta, 2);

        // --- 缩略图条（超出宽度时，被截断的一侧淡出，提示还能滚动） ---
        _thumbnails = new StackPanel { Orientation = Orientation.Horizontal };
        var strip = _strip = new ScrollViewer
        {
            Content = _thumbnails,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        strip.ScrollChanged += (_, _) => UpdateStripFade();
        Add(root, strip, 3);

        // --- 操作：复制（主）/ 贴图 / 标注 ---
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var column = 0;
        foreach (var item in new (string Key, string Id, string Icon, Action Action, bool Primary)[]
        {
            ("S.Common.Copy", "quick-view-copy", AppIcons.Copy, Copy, true),
            ("S.Common.Pin", "quick-view-pin", AppIcons.Pin, Pin, false),
            ("S.Common.Annotation", "quick-view-edit", AppIcons.Annotate, () => OpenGallery(true), false),
        })
        {
            var text = StringsUi.Get(item.Key);
            var button = UiKit.Button(
                text,
                text,
                item.Action,
                item.Primary ? ButtonKind.Primary : ButtonKind.Secondary,
                item.Id,
                item.Icon);
            // 三个按钮按列等分铺满：高度取 Large，左右内距收窄，避免英文长词被挤。
            button.MinWidth = 0;
            button.Height = Tokens.Height.Large - 2;
            button.Padding = new Thickness(Tokens.Space.M, 0, Tokens.Space.M, 0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(column == 0 ? 0 : Tokens.Space.S, 0, 0, 0);
            Grid.SetColumn(button, column++);
            _imageActions.Add(button);
            actions.Children.Add(button);
        }

        // 与上方（缩略图条，或空状态时的画框）的间距放在按钮行上：名称行/缩略图条收起时间距不丢。
        actions.Margin = new Thickness(0, Tokens.Space.M, 0, 0);
        Add(root, actions, 4);

        // --- 仅在拖出 / 贴图 / 失败时才出现的一行提示 ---
        _status = UiKit.Text(string.Empty, TextRole.Caption, wrap: true);
        _status.Margin = new Thickness(Tokens.Space.XS, Tokens.Space.S + 2, Tokens.Space.XS, 0);
        _status.Visibility = Visibility.Collapsed;
        Add(root, _status, 5);

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            Child = root,
        };
        card.SetResourceReference(Border.BackgroundProperty, UiKit.Keys.Surface);
        card.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Divider);
        Content = card;

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
            Activate();
            Focus();
        };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_closed && _interactionDepth == 0 && !IsActive) Close();
        }));
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            RefreshPicture();
        };
        Closed += (_, _) =>
        {
            _resizeTimer.Stop();
            _closed = true;
        };
        PreviewMouseWheel += OnMouseWheel;
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
        if (y < -16 || y > header.ActualHeight + 12) return;
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
        var button = UiKit.IconButton(icon, name, name, action, size: 32, iconKey: UiKit.Keys.Secondary);
        button.Margin = new Thickness(Tokens.Space.XS / 2, 0, 0, 0);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    /// <summary>图片两侧的翻页按钮：平时透明，鼠标移入图片区域才淡入。</summary>
    private static Button NavButton(string icon, string nameKey, string id, Action action, HorizontalAlignment side)
    {
        var name = StringsUi.Get(nameKey);
        var button = UiKit.IconButton(icon, name, name, action, size: 34, iconKey: UiKit.Keys.Foreground, iconSize: 18);
        button.HorizontalAlignment = side;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(Tokens.Space.S + 2, 0, Tokens.Space.S + 2, 0);
        button.Opacity = 0;
        button.BorderThickness = new Thickness(1);
        button.SetResourceReference(Control.BackgroundProperty, UiKit.Keys.Surface);
        button.SetResourceReference(Control.BorderBrushProperty, UiKit.Keys.Divider);
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private double ThumbnailOpacity(int index) => index == _index ? 1 : UnselectedThumbnailOpacity;

    /// <summary>缩略图条两端的淡出遮罩：只在那一侧还有被截断的内容时出现。</summary>
    private void UpdateStripFade()
    {
        var width = _strip.ViewportWidth;
        if (width <= 0) return;
        var fadeLeft = _strip.HorizontalOffset > 0.5;
        var fadeRight = _strip.HorizontalOffset + width < _strip.ExtentWidth - 0.5;
        if (!fadeLeft && !fadeRight)
        {
            _strip.OpacityMask = null;
            return;
        }

        var edge = Math.Min(0.25, 28 / width);
        var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        mask.GradientStops.Add(new GradientStop(fadeLeft ? Colors.Transparent : Colors.Black, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Black, edge));
        mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - edge));
        mask.GradientStops.Add(new GradientStop(fadeRight ? Colors.Transparent : Colors.Black, 1));
        mask.Freeze();
        _strip.OpacityMask = mask;
    }

    private void FadeNav(double to)
    {
        if (_previous.IsEnabled) UiKit.Fade(_previous, UIElement.OpacityProperty, to);
        if (_next.IsEnabled) UiKit.Fade(_next, UIElement.OpacityProperty, to);
    }

    internal void Reload()
    {
        if (_closed || _interactionDepth != 0) return;
        var selectedId = Current?.Id;
        try
        {
            _records = _store.GetPage(new HistoryQuery { Limit = RecentLimit }).Items;
            _index = Math.Max(0, _records.ToList().FindIndex(record => record.Id == selectedId));
            _wheelDelta = 0;
            BuildThumbnails();
            ShowCurrent();
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "加载历史快览失败", ex);
            _records = Array.Empty<HistoryRecord>();
            _index = 0;
            BuildThumbnails();
            ShowCurrent();
            _emptyText.Text = StringsUi.Get("S.QuickView.LoadFailed");
            _emptyHint.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildThumbnails()
    {
        _thumbnails.Children.Clear();
        _thumbnailBorders.Clear();
        // Cached previews show immediately; the rest decode off the UI thread so the flyout opens without waiting.
        var pending = new List<(HistoryRecord Record, Image Picture)>();
        for (var i = 0; i < _records.Count; i++)
        {
            var index = i;
            var record = _records[i];
            // 64×48 外框 = 2px 选中环 + 2px 留缝 + 56×40 圆角图（内 6 / 外 10，同心圆角）。
            var picture = new Image { Source = _store.TryGetCachedPreview(record), Stretch = Stretch.UniformToFill, IsHitTestVisible = false, Clip = new RectangleGeometry(new Rect(0, 0, 56, 40), Tokens.Radius.Small, Tokens.Radius.Small) };
            if (picture.Source is null) pending.Add((record, picture));
            var border = new Border
            {
                Width = 64, Height = 48, Padding = new Thickness(2), CornerRadius = new CornerRadius(Tokens.Radius.Popup),
                Margin = new Thickness(0, 0, Tokens.Space.XS + 2, 0), BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent, Opacity = UnselectedThumbnailOpacity,
                Cursor = Cursors.Hand, ToolTip = record.Name,
                Child = picture
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            border.SetResourceReference(Border.BackgroundProperty, UiKit.Keys.SurfaceAlt);
            AutomationProperties.SetName(border, record.Name);
            AutomationProperties.SetAutomationId(border, $"quick-view-thumbnail-{i}");
            HistoryImageDrag.Attach(border, source => Drag(record, source));
            border.MouseLeftButtonUp += (_, e) => { Select(index); e.Handled = true; };
            // 悬停：未选中的缩略图提亮，离开时回到当前状态应有的透明度。
            border.MouseEnter += (_, _) => UiKit.Fade(border, UIElement.OpacityProperty, 1);
            border.MouseLeave += (_, _) => UiKit.Fade(border, UIElement.OpacityProperty, ThumbnailOpacity(index));
            _thumbnailBorders.Add(border);
            _thumbnails.Children.Add(border);
        }

        if (pending.Count > 0) _ = FillThumbnailsAsync(pending);
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

    private void Select(int index)
    {
        if (_closed || _interactionDepth != 0 || _records.Count == 0) return;
        index = Math.Clamp(index, 0, _records.Count - 1);
        if (index == _index) return;
        _index = index;
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        var record = Current;
        _picture.Source = null;
        bool hasImage;
        try { hasImage = record is not null && RefreshPicture(); }
        catch (Exception ex) { AppLog.Error("History", "快览显示图片失败", ex); hasImage = false; }
        _picture.Visibility = _picture.Source is null ? Visibility.Hidden : Visibility.Visible;
        _pictureStage.Visibility = _picture.Visibility;
        _empty.Visibility = _picture.Source is null ? Visibility.Visible : Visibility.Collapsed;
        _emptyText.Text = StringsUi.Get(record is null ? "S.QuickView.Empty" : "S.History.DragMissing");
        _emptyHint.Visibility = record is null ? Visibility.Visible : Visibility.Collapsed;
        // 没有记录时收起名称行与缩略图条，让空状态独占画框。
        _meta.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        _strip.Visibility = _records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _name.Text = record?.Name ?? string.Empty;
        _name.ToolTip = record?.Name;
        _dimensions.Text = record is null ? string.Empty : $"{record.Width}×{record.Height}";
        _counter.Text = record is null ? string.Empty : $"{_index + 1} / {_records.Count}";
        _previous.IsEnabled = record is not null && _index > 0;
        _next.IsEnabled = record is not null && _index + 1 < _records.Count;
        _previous.Visibility = _previous.IsEnabled ? Visibility.Visible : Visibility.Hidden;
        _next.Visibility = _next.IsEnabled ? Visibility.Visible : Visibility.Hidden;
        foreach (var button in _imageActions) button.IsEnabled = record is not null;
        for (var i = 0; i < _thumbnailBorders.Count; i++)
        {
            var selected = i == _index;
            if (selected)
                _thumbnailBorders[i].SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Accent);
            else
                _thumbnailBorders[i].BorderBrush = Brushes.Transparent;
            UiKit.Fade(_thumbnailBorders[i], UIElement.OpacityProperty, ThumbnailOpacity(i));
        }

        if (record is not null) _thumbnailBorders[_index].BringIntoView();
        ClearStatus();
    }

    private void OnImageFrameResized()
    {
        // Avoid re-decoding large screenshots during every layout pass of a resize.
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    /// <summary>Decode at the on-screen size so large screenshots stay sharp.</summary>
    private bool RefreshPicture()
    {
        var record = Current;
        if (record is null)
        {
            _picture.Source = null;
            return false;
        }
        var width = (int)Math.Ceiling(_imageFrame.ActualWidth);
        var height = (int)Math.Ceiling(_imageFrame.ActualHeight);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (width < 16 || height < 16)
        {
            // Before layout: show the cached copy; SizeChanged decodes the display-size version.
            _picture.Source = record.Width > 0 ? _store.TryLoadPreview(record) : null;
            return _picture.Source is not null;
        }
        var image = HistoryActions.LoadScaled(_store, record, width, height, scale);
        _picture.Source = image;
        return image is not null;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Handle at the window before the image/thumbnail ScrollViewer can scroll or zoom.
        e.Handled = true;
        if (_interactionDepth != 0) return;
        _wheelDelta += e.Delta;
        var steps = _wheelDelta / 120;
        _wheelDelta %= 120;
        if (steps != 0) Select(_index - steps);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Left:
            case Key.Up: Select(_index - 1); break;
            case Key.Right:
            case Key.Down: Select(_index + 1); break;
            case Key.Home: Select(0); break;
            case Key.End: Select(_records.Count - 1); break;
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

    private void Drag(HistoryRecord record, DependencyObject source) =>
        Run(() => SetStatus(HistoryActions.DragFile(_store, record, source)));

    private void SetStatus(string message)
    {
        // Empty messages (drag cancelled and other no-ops) must not leave a visible status row.
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
