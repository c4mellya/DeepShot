using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.History;
using ZeepCut.Core.Settings;
using ZeepCut.Platform;
using ZeepCut.Ui;

namespace ZeepCut.History;

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

    private readonly BitmapSource _image;
    private readonly HistoryRecord _record;
    private readonly Image _picture;
    private readonly ScrollViewer _scroll;
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
        SetResourceReference(BackgroundProperty, HistoryUi.Keys.Window);
        FontFamily = new FontFamily(HistoryUi.FontFamilyName);
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

        // 滚轮 = 缩放（"适应窗口/实际大小"是按钮，不抢滚轮）；ESC 关窗。
        _scroll = new ScrollViewer
        {
            Content = _picture,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(16),
        };
        _scroll.SetResourceReference(Control.BackgroundProperty, HistoryUi.Keys.SurfaceAlt);
        _scroll.PreviewMouseWheel += OnPreviewMouseWheel;

        // --- 顶部：记录信息条 ---
        _status = HistoryUi.Text(DescribeRecord(), 12.5, brushKey: HistoryUi.Keys.Secondary);
        _status.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_status, "history-preview-meta");

        var infoBar = new Border
        {
            Padding = new Thickness(20, 12, 20, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _status,
        };
        infoBar.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.Surface);
        infoBar.SetResourceReference(Border.BorderBrushProperty, HistoryUi.Keys.Divider);

        // --- 底部：居中的幽灵按钮工具条（缩放 | 尺寸 | 复制/贴图） ---
        var copy = Track("copy", HistoryUi.GhostButton(StringsUi.Get("S.Common.Copy"), HistoryIcons.Copy, "预览复制", () => onCopy(_record)));
        var pin = Track("pin", HistoryUi.GhostButton(StringsUi.Get("S.Common.Pin"), HistoryIcons.Pin, "预览贴图", () => onPin(_record)));
        var fit = Track("fit", HistoryUi.GhostButton(StringsUi.Get("S.History.PreviewFit"), HistoryIcons.Fit, "预览适应窗口", FitToWindow));
        var actual = Track(
            "actual",
            HistoryUi.GhostButton(StringsUi.Get("S.History.PreviewActualSize"), HistoryIcons.ActualSize, "预览实际大小", () => SetZoom(1.0)));

        var zoomOut = Track("zoom-out", HistoryUi.IconButton(HistoryIcons.ZoomOut, "预览缩小", "－", () => SetZoom(_zoom / 1.2), 30));
        var zoomIn = Track("zoom-in", HistoryUi.IconButton(HistoryIcons.ZoomIn, "预览放大", "＋", () => SetZoom(_zoom * 1.2), 30));

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        bar.Children.Add(zoomOut);
        bar.Children.Add(zoomIn);
        bar.Children.Add(HistoryUi.VerticalSeparator());
        bar.Children.Add(actual);
        bar.Children.Add(fit);
        bar.Children.Add(HistoryUi.VerticalSeparator());
        bar.Children.Add(copy);
        bar.Children.Add(pin);

        var toolbar = new Border
        {
            Padding = new Thickness(16, 10, 16, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
        toolbar.SetResourceReference(Border.BackgroundProperty, HistoryUi.Keys.Surface);
        toolbar.SetResourceReference(Border.BorderBrushProperty, HistoryUi.Keys.Divider);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(infoBar, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(toolbar, 2);
        grid.Children.Add(infoBar);
        grid.Children.Add(_scroll);
        grid.Children.Add(toolbar);
        Content = grid;

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
        "S.History.PreviewMeta",
        _record.Name,
        _record.Width,
        _record.Height,
        _record.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        HistoryCard.DescribeSource(_record.Source),
        Math.Round(_zoom * 100, 0).ToString(CultureInfo.InvariantCulture));

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

    /// <summary>语言切换：换标题、按钮与元数据行（缩放比例保持）。</summary>
    private void OnLanguageChanged(AppLanguage language)
    {
        Title = StringsUi.Format("S.History.PreviewTitle", _record.Name);
        HistoryUi.SetButtonText(_buttons["copy"], StringsUi.Get("S.Common.Copy"));
        HistoryUi.SetButtonText(_buttons["pin"], StringsUi.Get("S.Common.Pin"));
        HistoryUi.SetButtonText(_buttons["fit"], StringsUi.Get("S.History.PreviewFit"));
        HistoryUi.SetButtonText(_buttons["actual"], StringsUi.Get("S.History.PreviewActualSize"));
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
        _status.Text = DescribeRecord();
        AppLog.Info(
            "History",
            $"历史预览缩放：名称={_record.Name} 缩放={Math.Round(_zoom, 3)} （{_image.PixelWidth}×{_image.PixelHeight}）");
    }

    /// <summary>缩放到刚好看得下整张图（预览窗口很小或图很大时用）。</summary>
    private void FitToWindow()
    {
        var available = _scroll.ActualWidth - 24;
        var availableHeight = _scroll.ActualHeight - 24;
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
