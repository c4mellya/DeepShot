using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZeepCut.Core.History;

namespace ZeepCut.History;

/// <summary>
/// 图库里的一个缩略图卡片（T25/T26）：预览图 + 名称 + 时间 + 收藏星标 + 选中后的操作条。
/// </summary>
/// <remarks>
/// <para>
/// 卡片自己**不执行**任何操作，只把"用户想干什么"通过回调交给图库窗口：
/// 这样"贴图不新增历史""删除要确认"这类规则只在一处（<see cref="HistoryActions"/>
/// 与窗口）实现，卡片只负责长相与命中。
/// </para>
/// <para>
/// <b>UIA</b>：卡片、星标、名称、操作按钮都带稳定的 AutomationId
/// （<c>history-card-N</c> / <c>history-star-N</c> / <c>history-name-N</c>），
/// 真实验证脚本按序号定位，不依赖屏幕坐标。
/// </para>
/// </remarks>
internal sealed class HistoryCard : Border
{
    /// <summary>缩略图区域高度（DIP）。</summary>
    internal const double ThumbnailHeight = 132;

    /// <summary>卡片宽度（DIP）。</summary>
    internal const double CardWidth = 226;

    private readonly HistoryRecord _record;
    private readonly Border _thumbnailFrame;
    private readonly Image _picture;
    private readonly TextBlock _nameBlock;
    private readonly Button _starButton;
    private readonly Grid _actionBar;
    private bool _selected;

    /// <param name="record">记录。</param>
    /// <param name="index">本页序号（用于 AutomationId 与"第 N 条"日志）。</param>
    /// <param name="preview">缩略图（可能为 null：缩略图文件丢失时显示占位）。</param>
    /// <param name="callbacks">各类操作回调。</param>
    internal HistoryCard(
        HistoryRecord record,
        int index,
        BitmapSource? preview,
        HistoryCardCallbacks callbacks)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        Index = index;

        Width = CardWidth;
        Margin = new Thickness(0, 0, 12, 12);
        Background = HistoryUi.CardBackground;
        BorderBrush = HistoryUi.Border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8);
        Cursor = Cursors.Hand;
        ToolTip = $"{record.Name}\n{FormatTime(record)}\n{record.Width}×{record.Height} · " +
                  $"{HistorySourceText.Describe(record.Source)}";

        AutomationProperties.SetName(this, $"历史记录 {record.Name}");
        AutomationProperties.SetAutomationId(this, $"history-card-{index}");

        _picture = new Image
        {
            Source = preview,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);

        _thumbnailFrame = new Border
        {
            Height = ThumbnailHeight,
            Background = HistoryUi.ThumbnailBackground,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = _picture,
        };

        if (preview is null)
        {
            _thumbnailFrame.Child = new Grid
            {
                Children =
                {
                    HistoryUi.Text("缩略图缺失", 12, HistoryUi.SecondaryForeground),
                },
            };
            ((Grid)_thumbnailFrame.Child).Children[0].SetValue(
                HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            ((Grid)_thumbnailFrame.Child).Children[0].SetValue(
                VerticalAlignmentProperty,
                VerticalAlignment.Center);
        }

        // 星标压在缩略图左上角：不占布局高度，点它不会触发卡片选中/预览。
        _starButton = new Button
        {
            Content = record.IsFavorite ? "★" : "☆",
            Width = 26,
            Height = 26,
            FontSize = 15,
            FontFamily = new FontFamily(HistoryUi.FontFamilyName),
            Foreground = record.IsFavorite ? HistoryUi.StarBrush : HistoryUi.StarInactiveBrush,
            Background = HistoryUi.Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderBrush = HistoryUi.Border,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4),
            ToolTip = record.IsFavorite ? "取消收藏" : "收藏（收藏项不会被 30 天清理）",
        };
        AutomationProperties.SetName(_starButton, record.IsFavorite ? "取消收藏" : "收藏");
        AutomationProperties.SetAutomationId(_starButton, $"history-star-{index}");
        _starButton.Click += (_, e) =>
        {
            e.Handled = true;
            callbacks.ToggleFavorite?.Invoke(_record);
        };

        var thumbnailHost = new Grid();
        thumbnailHost.Children.Add(_thumbnailFrame);
        thumbnailHost.Children.Add(_starButton);

        _nameBlock = HistoryUi.Text(
            record.Name,
            13,
            HistoryUi.Foreground,
            FontWeights.SemiBold);
        _nameBlock.Margin = new Thickness(2, 6, 2, 0);
        AutomationProperties.SetAutomationId(_nameBlock, $"history-name-{index}");

        var meta = HistoryUi.Text(
            $"{FormatTime(record)} · {record.Width}×{record.Height} · " +
            $"{HistorySourceText.Describe(record.Source)}",
            11,
            HistoryUi.SecondaryForeground);
        meta.Margin = new Thickness(2, 1, 2, 0);
        AutomationProperties.SetAutomationId(meta, $"history-meta-{index}");

        _actionBar = BuildActionBar(callbacks);
        _actionBar.Visibility = Visibility.Collapsed;
        _actionBar.Margin = new Thickness(0, 6, 0, 0);

        var stack = new StackPanel();
        stack.Children.Add(thumbnailHost);
        stack.Children.Add(_nameBlock);
        stack.Children.Add(meta);
        stack.Children.Add(_actionBar);
        Child = stack;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    /// <summary>本页序号（日志/AutomationId 用）。</summary>
    internal int Index { get; }

    /// <summary>这条记录（窗口按 Id 对账）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>是否被选中（选中后展开操作条）。</summary>
    internal bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            BorderBrush = value ? HistoryUi.SelectedBorder : HistoryUi.Border;
            BorderThickness = new Thickness(value ? 2 : 1);
            _actionBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>收藏状态变化后更新星标（不重建控件，避免丢失焦点）。</summary>
    internal void ApplyFavorite(bool favorite)
    {
        _starButton.Content = favorite ? "★" : "☆";
        _starButton.Foreground = favorite ? HistoryUi.StarBrush : HistoryUi.StarInactiveBrush;
        AutomationProperties.SetName(_starButton, favorite ? "取消收藏" : "收藏");
    }

    /// <summary>重命名后更新名称（不重建控件）。</summary>
    internal void ApplyName(string name)
    {
        _nameBlock.Text = name;
        ToolTip = $"{name}\n{FormatTime(_record)}\n{_record.Width}×{_record.Height} · " +
                  $"{HistorySourceText.Describe(_record.Source)}";
        AutomationProperties.SetName(this, $"历史记录 {name}");
    }

    /// <summary>格式化时间：今天只显示时分秒，其它日期带上日期。</summary>
    internal static string FormatTime(HistoryRecord record)
    {
        var local = record.CreatedAtLocal;
        var today = DateTime.Now.Date;
        return local.Date == today
            ? "今天 " + local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private Grid BuildActionBar(HistoryCardCallbacks callbacks)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var firstRow = new StackPanel { Orientation = Orientation.Horizontal };
        firstRow.Children.Add(Small("复制", "复制", () => callbacks.Copy?.Invoke(_record)));
        firstRow.Children.Add(Small("贴图", "贴图", () => callbacks.Pin?.Invoke(_record)));

        var secondRow = new StackPanel { Orientation = Orientation.Horizontal };
        secondRow.Children.Add(Small("改名", "重命名", () => callbacks.Rename?.Invoke(_record)));
        secondRow.Children.Add(Small("删除", "删除这条历史", () => callbacks.Delete?.Invoke(_record)));

        // T28：追加标注并覆盖（在现有图片上继续标注，保存回同一条记录）。
        var secondRowExtra = Small("标注", "追加标注并覆盖这条记录", () => callbacks.Edit?.Invoke(_record));
        secondRow.Children.Add(secondRowExtra);

        var thirdRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };
        thirdRow.Children.Add(Small("另存为", "另存为", () => callbacks.SaveAs?.Invoke(_record)));
        thirdRow.Children.Add(Small("预览", "预览大图", () => callbacks.Preview?.Invoke(_record)));

        var stack = new StackPanel();
        stack.Children.Add(firstRow);
        stack.Children.Add(secondRow);
        stack.Children.Add(thirdRow);

        grid.Children.Add(stack);
        AutomationProperties.SetAutomationId(grid, $"history-actions-{Index}");
        return grid;
    }

    private Button Small(string text, string automationName, Action onClick)
    {
        var button = HistoryUi.Button(text, automationName, onClick);
        button.MinWidth = 0;
        button.Padding = new Thickness(7, 2, 7, 2);
        button.Margin = new Thickness(0, 0, 4, 4);
        button.FontSize = 11.5;

        // 显式字体族：只沿用了字号/内边距时按钮可能继承到不带中文字形的字体，
        // 会出现“标注”被画成一个方块/缺字（本轮真实桌面实测到过一次）。
        button.Content = HistoryUi.Text(text, 11.5, HistoryUi.Foreground, measureNaturalWidth: true);
        button.FontFamily = new FontFamily(HistoryUi.FontFamilyName);
        AutomationProperties.SetAutomationId(button, $"history-action-{text}-{Index}");

        // 宽度按文字实测：HistoryUi.Text 默认带 CharacterEllipsis 与 NoWrap，放进 Grid 的
        // 自动列时会被量成很小的宽度（实测“标注”按钮只有 49px、字被裁成一条）。
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        button.MinWidth = Math.Max(0, button.DesiredSize.Width);
        return button;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 双击 = 预览大图（PLAN：图库要能看大图）；单击只选中卡片。
        if (e.ClickCount >= 2)
        {
            RaisePreview();
            e.Handled = true;
        }
    }

    private void RaisePreview()
    {
        if (Tag is Action<HistoryRecord> preview)
        {
            preview(_record);
        }
    }

    /// <summary>把预览回调挂到卡片上（窗口统一设置，不必给构造函数再加参数）。</summary>
    internal void SetDoubleClickPreview(Action<HistoryRecord> preview) => Tag = preview;

    /// <summary>给卡片设置选中状态的回调（单击时通知窗口做单选）。</summary>
    internal void SetClickSelect(Action<HistoryCard> select)
    {
        MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                return;
            }

            select(this);
        };
    }

    /// <summary>
    /// 容器里按下卡片的空白处并拖动 = 滚动图库（触屏/鼠标都能用）。
    /// </summary>
    /// <remarks>
    /// 卡片本身是按钮语义（单击选中、双击预览），所以**不能**靠 <c>BringIntoView</c>
    /// 之类会改变滚动位置的行为来实现；这里只把垂直方向的像素增量交给外层
    /// <see cref="ScrollViewer"/>，横向与其它命中一律不处理。
    /// </remarks>
    internal void SetDragScroll(ScrollViewer? scroll)
    {
        Point? origin = null;
        MouseLeftButtonDown += (_, e) =>
        {
            origin = e.GetPosition(scroll ?? (IInputElement)this);
        };
        MouseMove += (_, e) =>
        {
            if (origin is null || e.LeftButton != MouseButtonState.Pressed || scroll is null)
            {
                return;
            }

            var current = e.GetPosition(scroll);
            var delta = origin.Value.Y - current.Y;
            if (Math.Abs(delta) >= 4)
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + delta);
                origin = current;
            }
        };
        MouseLeftButtonUp += (_, _) => origin = null;
        MouseLeave += (_, _) => origin = null;
    }
}

/// <summary>卡片操作回调集合（全部可选，缺失表示该操作不提供）。</summary>
internal sealed class HistoryCardCallbacks
{
    internal Action<HistoryRecord>? Copy { get; init; }

    internal Action<HistoryRecord>? SaveAs { get; init; }

    internal Action<HistoryRecord>? Pin { get; init; }

    internal Action<HistoryRecord>? Delete { get; init; }

    internal Action<HistoryRecord>? Rename { get; init; }

    internal Action<HistoryRecord>? Preview { get; init; }

    internal Action<HistoryRecord>? ToggleFavorite { get; init; }

    /// <summary>T28：打开“追加标注并覆盖”编辑窗口。</summary>
    internal Action<HistoryRecord>? Edit { get; init; }
}
