using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.History;
using ZeepCut.Platform;

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
        Action<HistoryRecord> onPin)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _image = image ?? throw new ArgumentNullException(nameof(image));

        Title = $"预览 · {record.Name}";
        Width = 900;
        Height = 680;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = HistoryUi.WindowBackground;
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

        // 滚轮 = 缩放（"适应窗口/实际大小"是按钮，不抢滚轮）；ESC 关窗。
        _scroll = new ScrollViewer
        {
            Content = _picture,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = HistoryUi.ThumbnailBackground,
            Padding = new Thickness(8),
        };
        _scroll.PreviewMouseWheel += OnPreviewMouseWheel;

        _status = HistoryUi.Text(DescribeRecord(), 12, HistoryUi.SecondaryForeground);
        _status.Margin = new Thickness(14, 8, 14, 0);
        AutomationProperties.SetAutomationId(_status, "history-preview-meta");

        var copy = HistoryUi.Button("复制", "预览复制", () => onCopy(_record));
        var pin = HistoryUi.Button("贴图", "预览贴图", () => onPin(_record));
        var fit = HistoryUi.Button("适应窗口", "预览适应窗口", FitToWindow);
        var actual = HistoryUi.Button("实际大小", "预览实际大小", () => SetZoom(1.0));

        var zoomOut = HistoryUi.Button("－", "预览缩小", () => SetZoom(_zoom / 1.2));
        zoomOut.MinWidth = 40;
        var zoomIn = HistoryUi.Button("＋", "预览放大", () => SetZoom(_zoom * 1.2));
        zoomIn.MinWidth = 40;

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 6, 14, 10),
        };
        bar.Children.Add(zoomOut);
        bar.Children.Add(zoomIn);
        bar.Children.Add(actual);
        bar.Children.Add(fit);
        bar.Children.Add(copy);
        bar.Children.Add(pin);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_status, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(bar, 2);
        grid.Children.Add(_status);
        grid.Children.Add(_scroll);
        grid.Children.Add(bar);
        Content = grid;

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

        AppLog.Info(
            "History",
            $"历史预览已打开：名称={record.Name} Id={record.Id} 图像={_image.PixelWidth}x{_image.PixelHeight} " +
            $"记录尺寸={record.Width}x{record.Height}");
    }

    /// <summary>记录（供验证/诊断读取）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>当前缩放（1.0 = 1 物理像素 : 1 屏幕像素）。</summary>
    internal double Zoom => _zoom;

    private string DescribeRecord() =>
        $"{_record.Name} · {_record.Width}×{_record.Height} px · " +
        $"{_record.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} · " +
        $"{HistorySourceText.Describe(_record.Source)} · " +
        $"缩放 {Math.Round(_zoom * 100, 0).ToString(CultureInfo.InvariantCulture)}%";

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
