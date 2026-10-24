using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeepShot.Core.History;
using DeepShot.Core.Settings;
using DeepShot.Platform;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 双击缩略图后的大图预览（T25）：1:1 显示原图，可滚轮缩放、拖动平移。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要缩放/平移</b>：本机主屏 2560×1440，而图库里常见的记录就是整屏或大窗口截图；
/// 只做"按窗口大小缩放显示"会糊到看不清细节，用户还得另存为再用看图软件打开。
/// 所以预览默认 <see cref="Stretch.None"/>（1 物理像素 = 1 屏幕像素，125% 屏上也不会被放大），
/// 超出窗口的部分用 <see cref="ScrollViewer"/> 横向纵向滚动，滚轮缩放仅作为辅助。
/// </para>
/// <para>
/// <b>句柄</b>：图片由 <see cref="HistoryStore.TryLoad"/> 一次读成冻结位图，预览窗口不持有
/// 文件句柄，所以预览打开期间仍可删除/覆盖该记录（T23 的约束继续成立）。
/// </para>
/// </remarks>
internal sealed class HistoryPreviewWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 4.0;

    /// <summary>图片四周留白；底部额外让出悬浮工具条（约 42px）+ 间距。</summary>
    private static readonly Thickness StagePadding = new(32, 28, 32, 88);

    private readonly BitmapSource _image;
    private readonly HistoryRecord _record;
    private readonly Image _picture;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title;
    private readonly TextBlock _status;
    private readonly Dictionary<string, Button> _buttons = new();
    private double _zoom = 1.0;

    /// <param name="owner">父窗口（图库窗口；非模态展示，方便同时操作图库）。</param>
    /// <param name="record">记录元数据。</param>
    /// <param name="image">已解码的原图（冻结）。</param>
    /// <param name="onCopy">复制回调（复用 <see cref="HistoryActions.Copy"/>）。</param>
    /// <param name="onPin">贴图回调（不新增历史）。</param>
    internal HistoryPreviewWindow(
        Window owner,
        HistoryRecord record,
        BitmapSource image,
        Action<HistoryRecord> onCopy,
        Action<HistoryRecord> onPin,
        Action<HistoryRecord, DependencyObject> onDrag)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _image = image ?? throw new ArgumentNullException(nameof(image));

        Title = StringsUi.Format("S.History.PreviewTitle", record.Name);
        Width = 900;
        Height = 680;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, UiKit.Keys.Window);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "history-preview-window");

        _picture = new Image
        {
            Source = _image,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        AutomationProperties.SetAutomationId(_picture, "history-preview-image");
        _picture.ToolTip = StringsUi.Get("S.History.DragHint");
        HistoryImageDrag.Attach(_picture, source => onDrag(_record, source));

        // 图片垫一块带投影的底板 + 1px 细描边：像一张"放在桌面上的照片"，深色截图在深色底上也有边界。
        var stage = UiKit.Elevated(_picture, cornerRadius: 2, outline: true, blur: 28, depth: 8, opacity: 0.18);
        // 留白放在内容的 Margin 上而不是 ScrollViewer.Padding：后者在视口外圈，放大后图片会被提前裁掉。
        stage.Margin = StagePadding;

        // 滚轮 = 缩放（"适应窗口/实际大小"是按钮，不抢滚轮）；ESC 关窗。
        // 底部多留出悬浮工具条的高度，图片居中时不会被工具条压住。
        _scroll = new ScrollViewer
        {
            Content = stage,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroll.SetResourceReference(Control.BackgroundProperty, UiKit.Keys.SurfaceAlt);
        UiKit.QuietScrollCorner(_scroll);
        _scroll.PreviewMouseWheel += OnPreviewMouseWheel;

        // --- 顶部：记录名称（强调）+ 尺寸 / 时间 / 来源（次要） ---
        _title = UiKit.Text(_record.Name, TextRole.BodyStrong);
        _title.ToolTip = _record.Name;
        _status = UiKit.Text(DescribeRecord(), TextRole.Caption);
        AutomationProperties.SetAutomationId(_status, "history-preview-meta");

        // 与标注编辑窗口同一套顶栏（徽标 + 标题 + 说明行）。
        var infoBar = UiKit.ViewerHeader(AppIcons.Image, _title, _status);

        // --- 底部：浮在图片上的工具条（缩小 · 百分比=1:1 · 放大 · 适应 | 复制 · 贴图） ---
        var zoomOut = Track("zoom-out", UiKit.IconButton(AppIcons.ZoomOut, "预览缩小", StringsUi.Get("S.History.ZoomOut"), () => SetZoom(_zoom / 1.2)));
        var actual = Track(
            "actual",
            UiKit.ZoomButton("预览实际大小", StringsUi.Get("S.History.PreviewActualSize"), () => SetZoom(1.0), _zoom));
        var zoomIn = Track("zoom-in", UiKit.IconButton(AppIcons.ZoomIn, "预览放大", StringsUi.Get("S.History.ZoomIn"), () => SetZoom(_zoom * 1.2)));
        var fit = Track("fit", UiKit.IconButton(AppIcons.Fit, "预览适应窗口", StringsUi.Get("S.History.PreviewFit"), FitToWindow));
        var copy = Track("copy", UiKit.GhostButton(StringsUi.Get("S.Common.Copy"), AppIcons.Copy, "预览复制", () => onCopy(_record)));
        var pin = Track("pin", UiKit.GhostButton(StringsUi.Get("S.Common.Pin"), AppIcons.Pin, "预览贴图", () => onPin(_record)));
        copy.Height = pin.Height = Tokens.Height.Compact + Tokens.Space.XS;

        var toolbar = UiKit.FloatingBar(zoomOut, actual, zoomIn, fit, UiKit.ToolbarSeparator(), copy, pin);
        toolbar.Margin = new Thickness(Tokens.Space.L, 0, Tokens.Space.L, Tokens.Space.L + 4);
        AutomationProperties.SetAutomationId(toolbar, "history-preview-toolbar");

        var stageHost = new Grid();
        stageHost.Children.Add(_scroll);
        stageHost.Children.Add(toolbar);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(infoBar, 0);
        Grid.SetRow(stageHost, 1);
        grid.Children.Add(infoBar);
        grid.Children.Add(stageHost);
        Content = grid;
        AppChrome.Apply(this);

        SourceInitialized += (_, _) => DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);

        if (owner is { IsVisible: true })
        {
            Owner = owner;
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        ThemeManager.Changed += OnThemeChanged;
        StringsUi.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            StringsUi.Changed -= OnLanguageChanged;
        };

        AppLog.Info(
            "History",
            $"历史预览已打开：名称={record.Name} Id={record.Id} 图像={_image.PixelWidth}x{_image.PixelHeight} " +
            $"记录尺寸={record.Width}x{record.Height}");
    }

    /// <summary>记录（供验证/诊断读取）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>当前缩放（1.0 = 1 物理像素 : 1 屏幕像素）。</summary>
    internal double Zoom => _zoom;

    private string DescribeRecord() => StringsUi.Format(
        "S.History.PreviewDetails",
        _record.Width,
        _record.Height,
        _record.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        HistoryCard.DescribeSource(_record.Source));

    private Button Track(string key, Button button)
    {
        _buttons[key] = button;
        return button;
    }

    /// <summary>主题切换：颜色都是主题资源引用、自动更新；这里只同步系统标题栏的深浅。</summary>
    private void OnThemeChanged(AppTheme effective)
    {
        DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);
        AppLog.Info("History", $"历史预览已按新主题刷新：{effective}");
    }

    /// <summary>语言切换：换标题、按钮文字/提示与元数据行（缩放比例保持）。</summary>
    private void OnLanguageChanged(AppLanguage language)
    {
        Title = StringsUi.Format("S.History.PreviewTitle", _record.Name);
        UiKit.SetButtonText(_buttons["copy"], StringsUi.Get("S.Common.Copy"));
        UiKit.SetButtonText(_buttons["pin"], StringsUi.Get("S.Common.Pin"));
        _buttons["fit"].ToolTip = StringsUi.Get("S.History.PreviewFit");
        _buttons["actual"].ToolTip = StringsUi.Get("S.History.PreviewActualSize");
        _buttons["zoom-out"].ToolTip = StringsUi.Get("S.History.ZoomOut");
        _buttons["zoom-in"].ToolTip = StringsUi.Get("S.History.ZoomIn");
        _status.Text = DescribeRecord();
        AppLog.Info("History", $"历史预览已按新语言刷新：{language}");
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(e.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15);
        e.Handled = true;
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _picture.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        UiKit.SetButtonText(_buttons["actual"], UiKit.FormatZoom(_zoom));
        AppLog.Info(
            "History",
            $"历史预览缩放：名称={_record.Name} 缩放={Math.Round(_zoom, 3)} （{_image.PixelWidth}×{_image.PixelHeight}）");
    }

    /// <summary>缩放到刚好看得下整张图（预览窗口很小或图很大时用）。</summary>
    private void FitToWindow()
    {
        // 可用区域 = 滚动区减去四周留白（含给悬浮工具条让出的底部空间）。
        var available = _scroll.ActualWidth - StagePadding.Left - StagePadding.Right;
        var availableHeight = _scroll.ActualHeight - StagePadding.Top - StagePadding.Bottom;
        if (available <= 0 || availableHeight <= 0)
        {
            return;
        }

        SetZoom(Math.Min(available / _image.PixelWidth, availableHeight / _image.PixelHeight));
    }

    /// <summary>
    /// 按屏幕工作区限制初始尺寸（避免在小屏/副屏上开出一个比屏幕还大的窗口）。
    /// </summary>
    internal void ClampToWorkArea()
    {
        try
        {
            var work = SystemParameters.WorkArea;
            Width = Math.Min(Width, Math.Max(MinWidth, work.Width - 60));
            Height = Math.Min(Height, Math.Max(MinHeight, work.Height - 60));
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"预览窗口按工作区限制尺寸失败（忽略）：{ex.Message}");
        }
    }
}
