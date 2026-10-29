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
/// 双击缩略图后的大图预览（T25）：Windows“照片”式看图——窗口底色上居中一张图，顶部一条细命令栏。
/// </summary>
/// <remarks>
/// <para>
/// <b>缩放</b>：打开时适应窗口（小图不放大，保持 1:1）；用户缩放后不再自动适应，点“适应窗口”恢复。
/// 滚轮缩放，超出窗口的部分用 <see cref="ScrollViewer"/> 平移，细节无需另存为再看。
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

    /// <summary>命令栏高度（照片查看器同款细栏）。</summary>
    private const double CommandBarHeight = 48;

    /// <summary>图片四周留白：命令栏独占一行，底部无需再给浮层让位。</summary>
    private static readonly Thickness StagePadding = new(32, 8, 32, 32);

    private readonly BitmapSource _image;
    private readonly HistoryRecord _record;
    private readonly Image _picture;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _status;
    private readonly Dictionary<string, Button> _buttons = new();
    private double _zoom = 1.0;

    /// <summary>是否处于“适应窗口”模式：窗口尺寸变化时跟着重新适应；用户手动缩放后退出。</summary>
    private bool _autoFit = true;

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

        // 直角 + 极淡投影 + 1px 细描边：白色截图在浅色底上、深色截图在深色底上都有边界，又不像一张卡片。
        var stage = UiKit.Elevated(_picture, cornerRadius: 0, outline: true, blur: 24, depth: 4, opacity: 0.12);
        // 留白放在内容的 Margin 上而不是 ScrollViewer.Padding：后者在视口外圈，放大后图片会被提前裁掉。
        stage.Margin = StagePadding;

        _scroll = new ScrollViewer
        {
            Content = stage,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
        };
        UiKit.QuietScrollCorner(_scroll);
        _scroll.PreviewMouseWheel += OnPreviewMouseWheel;
        _scroll.SizeChanged += (_, _) =>
        {
            if (_autoFit) FitToWindow(auto: true);
        };

        // --- 顶部命令栏：复制 · 贴图 | 缩小 · 百分比(=1:1) · 放大 · 适应；右侧一行淡色“时间 · 尺寸” ---
        var copy = Track("copy", Command(AppIcons.Copy, "预览复制", "S.Common.Copy", () => onCopy(_record)));
        var pin = Track("pin", Command(AppIcons.Pin, "预览贴图", "S.Common.Pin", () => onPin(_record)));
        var zoomOut = Track("zoom-out", Command(AppIcons.ZoomOut, "预览缩小", "S.History.ZoomOut", () => ZoomBy(1 / 1.2)));
        var actual = Track(
            "actual",
            UiKit.ZoomButton("预览实际大小", StringsUi.Get("S.History.PreviewActualSize"), () => ZoomTo(1.0), _zoom));
        actual.Margin = new Thickness(0, 0, 2, 0);
        var zoomIn = Track("zoom-in", Command(AppIcons.ZoomIn, "预览放大", "S.History.ZoomIn", () => ZoomBy(1.2)));
        var fit = Track("fit", Command(AppIcons.Fit, "预览适应窗口", "S.History.PreviewFit", () => FitToWindow(auto: false)));

        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in new UIElement[] { copy, pin, UiKit.ToolbarSeparator(), zoomOut, actual, zoomIn, fit })
        {
            commands.Children.Add(item);
        }

        _status = UiKit.Text(DescribeRecord(), TextRole.Caption, ThemeManager.BrushKeys.TertiaryForeground);
        _status.TextTrimming = TextTrimming.None;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(Tokens.Space.L, 0, 0, 0);
        System.Windows.Documents.Typography.SetNumeralAlignment(_status, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(_status, "history-preview-meta");

        // 左缘 8 + 按钮内 8：首个图标与标题栏应用图标（左缘 16）竖向对齐；右缘 16 对齐系统标题按钮。
        var commandBar = new Grid { Height = CommandBarHeight, Margin = new Thickness(Tokens.Space.S, 0, Tokens.Space.L, 0) };
        commandBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commandBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandBar.Children.Add(commands);
        Grid.SetColumn(_status, 1);
        commandBar.Children.Add(_status);
        AutomationProperties.SetAutomationId(commandBar, "history-preview-toolbar");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(commandBar, 0);
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(commandBar);
        grid.Children.Add(_scroll);
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

    /// <summary>右侧信息：只有时间与像素尺寸（文件名对挑图没有意义）。</summary>
    private string DescribeRecord() => StringsUi.Format(
        "S.History.PreviewInfo",
        _record.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        _record.Width,
        _record.Height);

    private Button Track(string key, Button button)
    {
        _buttons[key] = button;
        return button;
    }

    /// <summary>命令栏图标按钮：32×32 透明底，悬停反馈来自全局 Ghost 样式。</summary>
    private static Button Command(string icon, string automationName, string toolTipKey, Action action)
    {
        var button = UiKit.IconButton(icon, automationName, StringsUi.Get(toolTipKey), action, size: Tokens.Height.Control);
        button.Margin = new Thickness(0, 0, 2, 0);
        return button;
    }

    /// <summary>主题切换：颜色都是主题资源引用、自动更新；这里只同步系统标题栏的深浅。</summary>
    private void OnThemeChanged(AppTheme effective)
    {
        DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);
        AppLog.Info("History", $"历史预览已按新主题刷新：{effective}");
    }

    /// <summary>语言切换：换标题、按钮提示与信息行（缩放比例保持）。</summary>
    private void OnLanguageChanged(AppLanguage language)
    {
        Title = StringsUi.Format("S.History.PreviewTitle", _record.Name);
        _buttons["copy"].ToolTip = StringsUi.Get("S.Common.Copy");
        _buttons["pin"].ToolTip = StringsUi.Get("S.Common.Pin");
        _buttons["fit"].ToolTip = StringsUi.Get("S.History.PreviewFit");
        _buttons["actual"].ToolTip = StringsUi.Get("S.History.PreviewActualSize");
        _buttons["zoom-out"].ToolTip = StringsUi.Get("S.History.ZoomOut");
        _buttons["zoom-in"].ToolTip = StringsUi.Get("S.History.ZoomIn");
        _status.Text = DescribeRecord();
        AppLog.Info("History", $"历史预览已按新语言刷新：{language}");
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    /// <summary>用户主动缩放：退出“适应窗口”模式。</summary>
    private void ZoomBy(double factor) => ZoomTo(_zoom * factor);

    private void ZoomTo(double zoom)
    {
        _autoFit = false;
        SetZoom(zoom);
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

    /// <summary>
    /// 缩放到刚好看得下整张图并进入自动适应模式。<paramref name="auto"/>（打开 / 窗口尺寸变化）时
    /// 小图不放大、保持 1:1；点“适应窗口”按钮时按可用区域铺满。
    /// </summary>
    private void FitToWindow(bool auto)
    {
        _autoFit = true;
        var available = _scroll.ActualWidth - StagePadding.Left - StagePadding.Right;
        var availableHeight = _scroll.ActualHeight - StagePadding.Top - StagePadding.Bottom;
        if (available <= 0 || availableHeight <= 0)
        {
            return;
        }

        var fit = Math.Min(available / _image.PixelWidth, availableHeight / _image.PixelHeight);
        var target = auto ? Math.Min(1.0, fit) : fit;
        if (Math.Abs(target - _zoom) > 0.0005)
        {
            SetZoom(target);
        }
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
