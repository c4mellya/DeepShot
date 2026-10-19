using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DeepShot.Core.History;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 图库里的一个缩略图卡片：圆角 12 的卡片 + 顶部圆角裁剪的缩略图 + 名称/时间/尺寸，
/// 星标（右上）与操作图标条（底部悬浮）在悬停 / 选中 / 键盘焦点时浮现。
/// </summary>
/// <remarks>
/// <para>
/// 卡片自己<b>不执行</b>任何操作，只把"用户想干什么"通过回调交给图库窗口：
/// 这样"贴图不新增历史""删除要确认"这类规则只在一处（<see cref="HistoryActions"/>
/// 与窗口）实现，卡片只负责长相与命中。
/// </para>
/// <para>
/// <b>UIA</b>：卡片、星标、名称、操作按钮都带稳定的 AutomationId
/// （<c>history-card-N</c> / <c>history-star-N</c> / <c>history-name-N</c> / <c>history-action-SLUG-N</c>）。
/// 操作条和星标用<b>透明度</b>而不是 Visibility 来隐藏：它们始终在 UIA 树里、始终可 Tab 到，
/// 键盘聚焦到其中任何一个按钮时整条会浮现出来。
/// </para>
/// <para>
/// <b>颜色</b>全部是 <c>Theme.*</c> 资源引用：换主题自动更新，卡片不需要再手动刷新。
/// </para>
/// </remarks>
internal sealed class HistoryCard : Border
{
    /// <summary>缩略图区域高度（DIP）。</summary>
    internal const double ThumbnailHeight = 144;

    /// <summary>卡片宽度（DIP）。</summary>
    internal const double CardWidth = 240;

    private const double Radius = 12;

    private static readonly Effect HoverShadow = CreateShadow();

    private static readonly Lazy<ControlTemplate> StarTemplate = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="Button">
          <Grid Background="Transparent" SnapsToDevicePixels="True">
            <Ellipse x:Name="Bg" Fill="{DynamicResource Theme.Surface}" Opacity="0.94"
                     Stroke="{DynamicResource Theme.Divider}" StrokeThickness="1" />
            <Ellipse x:Name="Ov" Fill="{DynamicResource Theme.Foreground}" Opacity="0" IsHitTestVisible="False" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.08" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.16" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bg" Property="Stroke" Value="{DynamicResource Theme.Accent}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    private readonly HistoryRecord _record;
    private readonly Border _thumbnailFrame;
    private readonly Image _picture;
    private readonly TextBlock _nameBlock;
    private readonly Button _starButton;
    private readonly System.Windows.Controls.Viewbox _starIcon;
    private readonly FrameworkElement _actionPill;
    private readonly FrameworkElement _sourceChip;
    private readonly Border _ring;
    private readonly Border _tint;
    private readonly CheckBox _checkBox;
    private readonly Border _shadow;
    private Action<HistoryCard>? _checkedChanged;
    private bool _checked;
    private bool _selectionMode;
    private bool _selected;
    private bool _hovered;
    private bool _favorite;

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
        _favorite = record.IsFavorite;
        Index = index;

        Width = CardWidth;
        Margin = new Thickness(0, 0, 16, 16);
        CornerRadius = new CornerRadius(Radius);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        this.Ref(BackgroundProperty, HistoryUi.Keys.Surface).Ref(BorderBrushProperty, HistoryUi.Keys.Divider);
        ToolTip = $"{record.Name}\n{FormatTime(record)}\n{record.Width}×{record.Height} · " +
                  $"{DescribeSource(record.Source)}";

        AutomationProperties.SetName(this, StringsUi.Format("S.History.CardName", record.Name));
        AutomationProperties.SetAutomationId(this, $"history-card-{index}");

        // --- 缩略图：UniformToFill 裁切铺满，顶部圆角 ---
        _picture = new Image
        {
            Source = preview,
            Stretch = Stretch.UniformToFill,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);

        // 缩略图像素不够铺满卡片时，放大会发糊：改为不放大、居中显示原始像素；够用时才裁切铺满。
        if (preview is not null)
        {
            Loaded += (_, _) =>
            {
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var needed = Math.Max(
                    (CardWidth - 2) * scale / preview.PixelWidth,
                    ThumbnailHeight * scale / preview.PixelHeight);
                if (needed > 1.15)
                {
                    _picture.Stretch = Stretch.Uniform;
                    _picture.StretchDirection = StretchDirection.DownOnly;
                }
            };
        }

        _thumbnailFrame = new Border
        {
            Height = ThumbnailHeight,
            CornerRadius = new CornerRadius(Radius - 1, Radius - 1, 0, 0),
            Child = _picture,
        };
        _thumbnailFrame.SetResourceReference(BackgroundProperty, HistoryUi.Keys.SurfaceAlt);
        if (callbacks.Drag is { } drag)
        {
            _thumbnailFrame.ToolTip = StringsUi.Get("S.History.DragHint");
            HistoryImageDrag.Attach(_thumbnailFrame, source => drag(_record, source));
        }

        // 圆角裁剪：Border 的 CornerRadius 不裁子元素，只能手动裁；矩形比控件高出一个半径，所以下边不被圆化。
        _thumbnailFrame.SizeChanged += (_, e) =>
            _thumbnailFrame.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height + Radius),
                Radius - 1,
                Radius - 1);

        if (preview is null)
        {
            var icon = HistoryIcons.Create(HistoryIcons.Image, 28, HistoryUi.Keys.Secondary, 1.5);
            icon.Margin = new Thickness(0, 0, 0, 6);
            var holder = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            holder.Children.Add(icon);
            holder.Children.Add(HistoryUi.Text(
                StringsUi.Get("S.History.ThumbnailMissing"),
                12,
                brushKey: HistoryUi.Keys.Secondary));
            _thumbnailFrame.Child = holder;
        }

        // --- 左上角：多选复选框 + 紧随其后的来源标签（悬停浮现） ---
        _sourceChip = BuildSourceChip(record);
        _checkBox = HistoryUi.CheckBox($"history-select-{index}", StringsUi.Get("S.History.SelectCard"));
        _checkBox.HorizontalAlignment = HorizontalAlignment.Left;
        _checkBox.VerticalAlignment = VerticalAlignment.Top;
        _checkBox.Margin = new Thickness(9, 9, 0, 0);
        _checkBox.Click += (_, e) =>
        {
            e.Handled = true;
            _checked = _checkBox.IsChecked == true;
            UpdateChrome();
            _checkedChanged?.Invoke(this);
        };

        // --- 右上角星标：矢量图标按钮，点它不会触发卡片选中/预览 ---
        _starIcon = HistoryIcons.Create(HistoryIcons.Star, 16, HistoryUi.Keys.Secondary, 1.8);
        _starButton = new Button
        {
            Content = _starIcon,
            Template = StarTemplate.Value,
            Width = 30,
            Height = 30,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            FocusVisualStyle = null,
        };
        AutomationProperties.SetAutomationId(_starButton, $"history-star-{index}");
        _starButton.Click += (_, e) =>
        {
            e.Handled = true;
            callbacks.ToggleFavorite?.Invoke(_record);
        };
        ApplyStarVisual();

        // --- 底部悬浮的图标操作条 ---
        _actionPill = BuildActionBar(callbacks);

        var thumbnailHost = new Grid();
        thumbnailHost.Children.Add(_thumbnailFrame);
        thumbnailHost.Children.Add(_sourceChip);
        thumbnailHost.Children.Add(_checkBox);
        thumbnailHost.Children.Add(_starButton);
        thumbnailHost.Children.Add(_actionPill);

        // --- 文字区 ---
        _nameBlock = HistoryUi.Text(record.Name, 13.5, weight: FontWeights.SemiBold);
        _nameBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        _nameBlock.TextAlignment = TextAlignment.Left;
        AutomationProperties.SetAutomationId(_nameBlock, $"history-name-{index}");

        var meta = HistoryUi.Text(
            $"{FormatTime(record)} · {record.Width}×{record.Height}",
            11.5,
            brushKey: HistoryUi.Keys.Secondary);
        meta.HorizontalAlignment = HorizontalAlignment.Stretch;
        meta.TextAlignment = TextAlignment.Left;
        meta.Margin = new Thickness(0, 3, 0, 0);
        AutomationProperties.SetAutomationId(meta, $"history-meta-{index}");

        var textArea = new StackPanel { Margin = new Thickness(14, 11, 14, 13) };
        textArea.Children.Add(_nameBlock);
        textArea.Children.Add(meta);

        var stack = new StackPanel();
        stack.Children.Add(thumbnailHost);
        stack.Children.Add(textArea);

        // 悬停 / 选中的强调色描边：叠在卡片边框之上，用透明度过渡（没有布局抖动）。
        _ring = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(-1),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _ring.SetResourceReference(BorderBrushProperty, HistoryUi.Keys.Accent);

        // 勾选后整张卡片淡淡染上强调色。
        _tint = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _tint.SetResourceReference(BackgroundProperty, HistoryUi.Keys.Accent);

        // 悬停阴影放在独立的空壳元素上：Effect 绝不能套在带文字/图标的元素上，否则内容会被整体栅格化而发糊。
        _shadow = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = HoverShadow,
        };
        _shadow.SetResourceReference(BackgroundProperty, HistoryUi.Keys.Surface);

        var root = new Grid();
        root.Children.Add(_shadow);
        root.Children.Add(stack);
        root.Children.Add(_tint);
        root.Children.Add(_ring);
        Child = root;

        _actionPill.Opacity = 0;
        _sourceChip.Opacity = 0;
        _checkBox.Opacity = 0;
        _starButton.Opacity = _favorite ? 1 : 0;

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseEnter += (_, _) =>
        {
            _hovered = true;
            UpdateChrome();
        };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            UpdateChrome();
        };
        IsKeyboardFocusWithinChanged += (_, _) => UpdateChrome();
    }

    /// <summary>本页序号（日志/AutomationId 用）。</summary>
    internal int Index { get; }

    /// <summary>这条记录（窗口按 Id 对账）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>是否被选中（选中后强调色描边 + 操作条常驻）。</summary>
    internal bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateChrome();
        }
    }

    /// <summary>是否被勾选（多选）。程序设置不触发回调。</summary>
    internal bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            _checkBox.IsChecked = value;
            UpdateChrome();
        }
    }

    /// <summary>勾选框被用户点击后的通知（窗口据此维护选择集合）。</summary>
    internal void SetCheckedChanged(Action<HistoryCard> changed) => _checkedChanged = changed;

    /// <summary>选择模式（任意一张被勾选）：所有卡片的复选框常驻显示。</summary>
    internal void SetSelectionMode(bool on)
    {
        if (_selectionMode == on)
        {
            return;
        }

        _selectionMode = on;
        UpdateChrome();
    }

    /// <summary>收藏状态变化后更新星标（不重建控件，避免丢失焦点）。</summary>
    internal void ApplyFavorite(bool favorite)
    {
        _favorite = favorite;
        ApplyStarVisual();
        UpdateChrome();
    }

    /// <summary>重命名后更新名称（不重建控件）。</summary>
    internal void ApplyName(string name)
    {
        _nameBlock.Text = name;
        ToolTip = $"{name}\n{FormatTime(_record)}\n{_record.Width}×{_record.Height} · " +
                  $"{DescribeSource(_record.Source)}";
        AutomationProperties.SetName(this, StringsUi.Format("S.History.CardName", name));
    }

    /// <summary>格式化时间：今天只显示时分秒，其它日期带上日期（"今天"随语言）。</summary>
    internal static string FormatTime(HistoryRecord record)
    {
        var local = record.CreatedAtLocal;
        var today = DateTime.Now.Date;
        return local.Date == today
            ? StringsUi.Format("S.History.TimeToday", local.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
            : local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>来源描述（截图 / 剪贴板贴图 / 历史记录，随语言变化）。</summary>
    internal static string DescribeSource(HistorySource source) => source switch
    {
        HistorySource.Screenshot => StringsUi.Get("S.History.SourceScreenshot"),
        HistorySource.Clipboard => StringsUi.Get("S.History.SourceClipboard"),
        HistorySource.History => StringsUi.Get("S.History.SourceHistory"),
        _ => source.ToString(),
    };

    private static string StarOn => StringsUi.Get("S.History.StarOn");

    private static string StarOff => StringsUi.Get("S.History.StarOff");

    private static string StarOnHint => StringsUi.Get("S.History.StarOnHint");

    private static string StarOffHint => StringsUi.Get("S.History.StarOffHint");

    private static Effect CreateShadow()
    {
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 22,
            ShadowDepth = 5,
            Direction = 270,
            Opacity = 0.2,
        };
        shadow.Freeze();
        return shadow;
    }

    private static FrameworkElement BuildSourceChip(HistoryRecord record)
    {
        var label = HistoryUi.Text(DescribeSource(record.Source), 11, brushKey: HistoryUi.Keys.Secondary, measureNaturalWidth: true);
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(38, 9, 0, 0),
            Height = 22,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = label,
        };
        chip.SetResourceReference(BackgroundProperty, HistoryUi.Keys.Surface);
        chip.SetResourceReference(BorderBrushProperty, HistoryUi.Keys.Divider);
        return chip;
    }

    /// <summary>星标图标：已收藏 = 金色实心；未收藏 = 次要色空心。同时更新 UIA 名字与提示。</summary>
    private void ApplyStarVisual()
    {
        var path = HistoryIcons.PathOf(_starIcon);
        if (_favorite)
        {
            path.SetResourceReference(Shape.StrokeProperty, HistoryUi.Keys.Star);
            path.SetResourceReference(Shape.FillProperty, HistoryUi.Keys.Star);
        }
        else
        {
            path.SetResourceReference(Shape.StrokeProperty, HistoryUi.Keys.Secondary);
            path.Fill = Brushes.Transparent;
        }

        _starButton.ToolTip = _favorite ? StarOnHint : StarOffHint;
        AutomationProperties.SetName(_starButton, _favorite ? StarOn : StarOff);
    }

    /// <summary>按"悬停 / 选中 / 键盘焦点 / 收藏"刷新描边、阴影、上浮、操作条和星标。</summary>
    private void UpdateChrome()
    {
        var focus = IsKeyboardFocusWithin;
        var hot = _hovered || _selected || focus;

        _ring.BorderThickness = new Thickness(_selected || _checked ? 2 : 1.5);
        HistoryUi.Fade(_ring, OpacityProperty, _selected || _checked ? 1 : hot ? 0.55 : 0);
        HistoryUi.Fade(_tint, OpacityProperty, _checked ? 0.1 : 0);
        HistoryUi.Fade(_checkBox, OpacityProperty, hot || _checked || _selectionMode ? 1 : 0);
        HistoryUi.Fade(_actionPill, OpacityProperty, hot ? 1 : 0);
        HistoryUi.Fade(_sourceChip, OpacityProperty, hot ? 1 : 0);
        HistoryUi.Fade(_starButton, OpacityProperty, hot || _favorite ? 1 : 0);

        HistoryUi.Fade(_shadow, OpacityProperty, _hovered ? 1 : 0);
    }

    private FrameworkElement BuildActionBar(HistoryCardCallbacks callbacks)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ActionButton(StringsUi.Get("S.Common.Copy"), null, Slugs.Copy, HistoryIcons.Copy, () => callbacks.Copy?.Invoke(_record)));
        row.Children.Add(ActionButton(StringsUi.Get("S.Common.Pin"), null, Slugs.Pin, HistoryIcons.Pin, () => callbacks.Pin?.Invoke(_record)));
        row.Children.Add(ActionButton(
            StringsUi.Get("S.Common.Preview"),
            StringsUi.Get("S.History.ActionPreviewHint"),
            Slugs.Preview,
            HistoryIcons.Preview,
            () => callbacks.Preview?.Invoke(_record)));

        // T28：追加标注并覆盖（在现有图片上继续标注，保存回同一条记录）。
        row.Children.Add(ActionButton(
            StringsUi.Get("S.Common.Annotation"),
            StringsUi.Get("S.History.ActionAnnotateHint"),
            Slugs.Annotation,
            HistoryIcons.Annotate,
            () => callbacks.Edit?.Invoke(_record)));
        row.Children.Add(ActionButton(
            StringsUi.Get("S.Common.Rename"),
            null,
            Slugs.Rename,
            HistoryIcons.Rename,
            () => callbacks.Rename?.Invoke(_record)));
        row.Children.Add(ActionButton(StringsUi.Get("S.Common.SaveAs"), null, Slugs.SaveAs, HistoryIcons.SaveAs, () => callbacks.SaveAs?.Invoke(_record)));
        row.Children.Add(ActionButton(
            StringsUi.Get("S.Common.Delete"),
            StringsUi.Get("S.History.ActionDeleteHint"),
            Slugs.Delete,
            HistoryIcons.Delete,
            () => callbacks.Delete?.Invoke(_record),
            HistoryUi.Keys.Error));

        var pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = row,
        };
        pill.SetResourceReference(BackgroundProperty, HistoryUi.Keys.Surface);
        pill.SetResourceReference(BorderBrushProperty, HistoryUi.Keys.Divider);

        // 阴影在独立的底衬上，按钮与图标保持清晰（不被 Effect 栅格化）。
        var pillShadow = new Border
        {
            CornerRadius = new CornerRadius(11),
            IsHitTestVisible = false,
            Effect = PillShadow,
        };
        pillShadow.SetResourceReference(BackgroundProperty, HistoryUi.Keys.Surface);

        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 10),
        };
        host.Children.Add(pillShadow);
        host.Children.Add(pill);
        AutomationProperties.SetAutomationId(pill, $"history-actions-{Index}");
        return host;
    }

    private static readonly Effect PillShadow = CreatePillShadow();

    private static Effect CreatePillShadow()
    {
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 10,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = 0.18,
        };
        shadow.Freeze();
        return shadow;
    }

    /// <summary>
    /// 操作按钮的 AutomationId 后缀（语言无关，验证脚本按它定位）。
    /// </summary>
    /// <remarks>
    /// T31 起这些 id 不再拼中文：固定 slug 让脚本与语言无关
    /// （见 <c>scripts/verify-t25t26.ps1</c> 的 <c>Report-Actions</c>）。
    /// </remarks>
    private static class Slugs
    {
        internal const string Copy = "copy";

        internal const string Pin = "pin";

        internal const string Rename = "rename";

        internal const string SaveAs = "save-as";

        internal const string Delete = "delete";

        internal const string Annotation = "annotation";

        internal const string Preview = "preview";
    }

    /// <summary>一个紧凑的图标操作按钮：UIA 名字 = 文案，悬停提示 = 文案或更详细的说明。</summary>
    private Button ActionButton(
        string text,
        string? hint,
        string automationSlug,
        string iconData,
        System.Action onClick,
        string iconKey = HistoryUi.Keys.Foreground)
    {
        var button = HistoryUi.IconButton(iconData, text, hint ?? text, onClick, 28, iconKey, 15);
        AutomationProperties.SetAutomationId(button, $"history-action-{automationSlug}-{Index}");
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
    /// 卡片本身是按钮语义（单击选中、双击预览），所以<b>不能</b>靠 <c>BringIntoView</c>
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

    internal Action<HistoryRecord, DependencyObject>? Drag { get; init; }

    internal Action<HistoryRecord>? SaveAs { get; init; }

    internal Action<HistoryRecord>? Pin { get; init; }

    internal Action<HistoryRecord>? Delete { get; init; }

    internal Action<HistoryRecord>? Rename { get; init; }

    internal Action<HistoryRecord>? Preview { get; init; }

    internal Action<HistoryRecord>? ToggleFavorite { get; init; }

    /// <summary>T28：打开“追加标注并覆盖”编辑窗口。</summary>
    internal Action<HistoryRecord>? Edit { get; init; }
}
