using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DeepShot.Core.History;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 图库里的一个缩略图卡片：圆角 12 的面板 + 内嵌 6px、同心圆角的缩略图 + 名称/时间/尺寸，
/// 星标（右上）与操作工具条（底部悬浮）在悬停 / 选中 / 键盘焦点时浮现。
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
/// <b>状态层次</b>：悬停 = 上浮 3px + 柔和投影 + 半透明强调色描边；单击选中 = 实心描边 + 外圈淡光晕
/// （与输入框聚焦同一语言）；勾选（多选）= 实心描边 + 整卡淡淡染色。三者可以叠加，但一眼能分开。
/// </para>
/// <para>
/// <b>颜色</b>全部是 <c>Theme.*</c> 资源引用：换主题自动更新，卡片不需要再手动刷新。
/// 投影都在独立的 <see cref="UiKit.ShadowPlate"/> 兄弟元素上，文字与缩略图不会被位图化发虚。
/// </para>
/// </remarks>
internal sealed class HistoryCard : Border
{
    /// <summary>默认卡片宽度（DIP）；实际宽度由图库按可用宽度用 <see cref="SetLayoutWidth"/> 均分。</summary>
    internal const double CardWidth = 240;

    /// <summary>卡片最小宽度：再窄底部操作条就放不下了。</summary>
    internal const double MinCardWidth = 212;

    /// <summary>卡片之间的横竖间距。</summary>
    internal const double Gap = Tokens.Space.L;

    /// <summary>缩略图高宽比（接近 16:10，横屏截图裁切最少）。</summary>
    private const double ThumbnailAspect = 0.62;

    private const double Radius = Tokens.Radius.Card;

    /// <summary>缩略图离卡片边的内嵌距离；缩略图圆角 = 外圆角 − 内嵌，内外同心。</summary>
    private const double Inset = 6;

    private const double ThumbnailRadius = Radius - Inset;

    private static readonly Lazy<ControlTemplate> StarTemplate = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="Button">
          <Grid Background="Transparent" SnapsToDevicePixels="True">
            <Ellipse x:Name="Bg" Fill="{DynamicResource Theme.Surface}"
                     Stroke="{DynamicResource Theme.Border}" StrokeThickness="1" />
            <Ellipse x:Name="Ov" Fill="{DynamicResource Theme.Foreground}" Opacity="0" IsHitTestVisible="False" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.07" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.14" />
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
    private readonly Border _halo;
    private readonly Border _tint;
    private readonly CheckBox _checkBox;
    private readonly Border _shadow;
    private readonly TranslateTransform _lift = new();
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
    /// <param name="loadingPreview">缩略图正在后台读取：为 true 且 <paramref name="preview"/> 为 null 时先不显示"丢失"占位，等 <see cref="SetPreview"/>。</param>
    internal HistoryCard(
        HistoryRecord record,
        int index,
        BitmapSource? preview,
        HistoryCardCallbacks callbacks,
        bool loadingPreview = false)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _favorite = record.IsFavorite;
        Index = index;

        Margin = new Thickness(0, 0, Gap, Gap);
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        RenderTransform = _lift;
        ToolTip = $"{record.Name}\n{FormatTime(record)}\n{record.Width}×{record.Height} · " +
                  $"{DescribeSource(record.Source)}";

        AutomationProperties.SetName(this, StringsUi.Format("S.History.CardName", record.Name));
        AutomationProperties.SetAutomationId(this, $"history-card-{index}");

        // --- 缩略图：UniformToFill 裁切铺满，四角同心圆角 ---
        _picture = new Image
        {
            Source = preview,
            Stretch = Stretch.UniformToFill,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);

        Loaded += (_, _) => FitPicture();

        _thumbnailFrame = new Border
        {
            CornerRadius = new CornerRadius(ThumbnailRadius),
            Child = _picture,
        };
        _thumbnailFrame.SetResourceReference(BackgroundProperty, UiKit.Keys.SurfaceAlt);
        if (callbacks.Drag is { } drag)
        {
            _thumbnailFrame.ToolTip = StringsUi.Get("S.History.DragHint");
            HistoryImageDrag.Attach(_thumbnailFrame, source => drag(_record, source));
        }

        // 圆角裁剪：Border 的 CornerRadius 不裁子元素，只能手动裁。
        _thumbnailFrame.SizeChanged += (_, e) =>
        {
            _thumbnailFrame.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
                ThumbnailRadius,
                ThumbnailRadius);
            FitPicture();
        };

        if (preview is null && !loadingPreview)
        {
            _thumbnailFrame.Child = BuildMissingPlaceholder();
        }
        else if (preview is null)
        {
            // 等后台解码：先露出底色，不显示"缩略图丢失"；SetPreview 到了再淡入。
            _picture.Opacity = 0;
        }

        // 缩略图内侧一圈发丝线：白底截图放在白色卡片上也有清楚的边界。
        var hairline = new Border
        {
            CornerRadius = new CornerRadius(ThumbnailRadius),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        hairline.SetResourceReference(BorderBrushProperty, UiKit.Keys.Divider);

        // --- 左上角：多选复选框 + 紧随其后的来源标签（悬停浮现） ---
        _sourceChip = UiKit.OverlayChip(DescribeSource(record.Source));
        _sourceChip.Margin = new Thickness(36, 8, 0, 0);
        _checkBox = UiKit.CheckBox($"history-select-{index}", StringsUi.Get("S.History.SelectCard"));
        _checkBox.HorizontalAlignment = HorizontalAlignment.Left;
        _checkBox.VerticalAlignment = VerticalAlignment.Top;
        _checkBox.Margin = new Thickness(8, 8, 0, 0);
        _checkBox.Click += (_, e) =>
        {
            e.Handled = true;
            _checked = _checkBox.IsChecked == true;
            UpdateChrome();
            _checkedChanged?.Invoke(this);
        };

        // --- 右上角星标：矢量图标按钮，点它不会触发卡片选中/预览 ---
        _starIcon = AppIcons.Create(AppIcons.Star, 15, UiKit.Keys.Secondary, 1.9);
        _starButton = new Button
        {
            Content = _starIcon,
            Template = StarTemplate.Value,
            Width = 28,
            Height = 28,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
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

        var thumbnailHost = new Grid { Margin = new Thickness(Inset, Inset, Inset, 0) };
        thumbnailHost.Children.Add(_thumbnailFrame);
        thumbnailHost.Children.Add(hairline);
        thumbnailHost.Children.Add(_sourceChip);
        thumbnailHost.Children.Add(_checkBox);
        thumbnailHost.Children.Add(_starButton);
        thumbnailHost.Children.Add(_actionPill);

        // --- 文字区：名称（正文加粗）+ 时间 · 尺寸（说明文字，等宽数字） ---
        _nameBlock = UiKit.Text(record.Name, TextRole.BodyStrong);
        _nameBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(_nameBlock, $"history-name-{index}");

        var meta = UiKit.Text($"{FormatTime(record)} · {record.Width}×{record.Height}", TextRole.Caption);
        meta.HorizontalAlignment = HorizontalAlignment.Stretch;
        meta.Margin = new Thickness(0, 3, 0, 0);
        Typography.SetNumeralAlignment(meta, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(meta, $"history-meta-{index}");

        var textArea = new StackPanel { Margin = new Thickness(Inset + 5, 10, Inset + 5, 12) };
        textArea.Children.Add(_nameBlock);
        textArea.Children.Add(meta);

        var stack = new StackPanel();
        stack.Children.Add(thumbnailHost);
        stack.Children.Add(textArea);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Child = stack,
        };
        surface.SetResourceReference(BackgroundProperty, UiKit.Keys.Surface);
        surface.SetResourceReference(BorderBrushProperty, UiKit.Keys.Divider);

        // 勾选后整张卡片淡淡染上强调色。
        _tint = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _tint.SetResourceReference(BackgroundProperty, UiKit.Keys.Accent);

        // 悬停 / 选中 / 勾选的强调色描边：叠在卡片边框之上，用透明度过渡（没有布局抖动）。
        _ring = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        _ring.SetResourceReference(BorderBrushProperty, UiKit.Keys.Accent);

        // 选中 / 键盘焦点：描边外再一圈 3px 淡强调色光晕（与输入框聚焦同一视觉语言）。
        _halo = new Border
        {
            Margin = new Thickness(-3),
            CornerRadius = new CornerRadius(Radius + 3),
            BorderThickness = new Thickness(3),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _halo.SetResourceReference(BorderBrushProperty, UiKit.Keys.Accent);

        // 两层投影：常驻的极淡贴地影 + 悬停时淡入的大范围柔影（都在独立底板上）。
        var restShadow = UiKit.ShadowPlate(Radius, blur: 6, depth: 1, opacity: 0.05);
        _shadow = UiKit.ShadowPlate(Radius, blur: 28, depth: 10, opacity: 0.16);
        _shadow.Opacity = 0;

        var root = new Grid();
        root.Children.Add(restShadow);
        root.Children.Add(_shadow);
        root.Children.Add(surface);
        root.Children.Add(_tint);
        root.Children.Add(_ring);
        root.Children.Add(_halo);
        Child = root;

        SetLayoutWidth(CardWidth);

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

    /// <summary>是否被选中（选中后强调色描边 + 外圈光晕 + 操作条常驻）。</summary>
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

    /// <summary>
    /// 按图库算好的列宽设置卡片宽度；缩略图高度跟随宽度（固定高宽比），整行永远铺满、右侧不留空白。
    /// </summary>
    internal void SetLayoutWidth(double width)
    {
        width = Math.Max(MinCardWidth, Math.Floor(width));
        if (Math.Abs(Width - width) < 0.5)
        {
            return;
        }

        Width = width;
        _thumbnailFrame.Height = Math.Round((width - Inset * 2) * ThumbnailAspect);
        FitPicture();
    }

    /// <summary>
    /// 进入动画：从下方 10px、透明度 0 浮上来。<paramref name="order"/> 是卡片在首屏的次序，
    /// 每张错开 12ms（最多错开 8 张），整屏在 ~360ms 内落定，不拖沓。
    /// </summary>
    internal void PlayEntrance(int order)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Min(order, 8) * 12);
        var duration = new Duration(Tokens.Motion.Slow);
        var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = Tokens.Motion.Ease };
        fade.Completed += (_, _) =>
        {
            // 落定后释放时钟：静止的卡片不挂着动画，透明度回到真正的 1。
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        var rise = new DoubleAnimation(10, 0, duration) { BeginTime = delay, EasingFunction = Tokens.Motion.Ease };
        Opacity = 0;
        BeginAnimation(OpacityProperty, fade);
        _lift.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>
    /// 后台读完缩略图后填进来；<paramref name="preview"/> 为 null 表示读取失败，显示"缩略图丢失"占位。
    /// 只能在 UI 线程调用，位图必须已冻结。
    /// </summary>
    internal void SetPreview(BitmapSource? preview)
    {
        if (preview is null)
        {
            _thumbnailFrame.Child = BuildMissingPlaceholder();
            return;
        }

        _picture.Source = preview;
        _thumbnailFrame.Child = _picture;
        FitPicture();
        UiKit.Fade(_picture, OpacityProperty, 1, Tokens.Motion.Normal.TotalMilliseconds);
    }

    /// <summary>
    /// 缩略图像素不够铺满卡片时，放大会发糊：改为不放大、居中显示原始像素；够用时才裁切铺满。
    /// 需要已经在可视树里（要读 DPI）；没有图时什么也不做。
    /// </summary>
    private void FitPicture()
    {
        if (_picture.Source is not BitmapSource preview || !IsLoaded || double.IsNaN(Width))
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var needed = Math.Max(
            (Width - Inset * 2) * scale / preview.PixelWidth,
            _thumbnailFrame.Height * scale / preview.PixelHeight);
        var upscaled = needed > 1.15;
        _picture.Stretch = upscaled ? Stretch.Uniform : Stretch.UniformToFill;
        _picture.StretchDirection = upscaled ? StretchDirection.DownOnly : StretchDirection.Both;
    }

    private static StackPanel BuildMissingPlaceholder()
    {
        var badge = UiKit.StatusBadge(AppIcons.Image, UiKit.Keys.Secondary, 40);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        var label = UiKit.Text(StringsUi.Get("S.History.ThumbnailMissing"), TextRole.Caption);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, Tokens.Space.S, 0, 0);
        var holder = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        holder.Children.Add(badge);
        holder.Children.Add(label);
        return holder;
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

    /// <summary>星标图标：已收藏 = 金色实心；未收藏 = 次要色空心。同时更新 UIA 名字与提示。</summary>
    private void ApplyStarVisual()
    {
        var path = AppIcons.PathOf(_starIcon);
        if (_favorite)
        {
            path.SetResourceReference(Shape.StrokeProperty, UiKit.Keys.Star);
            path.SetResourceReference(Shape.FillProperty, UiKit.Keys.Star);
        }
        else
        {
            path.SetResourceReference(Shape.StrokeProperty, UiKit.Keys.Secondary);
            path.Fill = Brushes.Transparent;
        }

        _starButton.ToolTip = _favorite ? StarOnHint : StarOffHint;
        AutomationProperties.SetName(_starButton, _favorite ? StarOn : StarOff);
    }

    /// <summary>按"悬停 / 选中 / 勾选 / 键盘焦点 / 收藏"刷新描边、光晕、染色、投影、上浮、操作条和星标。</summary>
    private void UpdateChrome()
    {
        var focus = IsKeyboardFocusWithin;
        var hot = _hovered || _selected || focus;
        var strong = _selected || _checked;
        var ms = Tokens.Motion.Normal.TotalMilliseconds;

        _ring.BorderThickness = new Thickness(strong ? 2 : 1);
        UiKit.Fade(_ring, OpacityProperty, strong ? 1 : hot ? 0.45 : 0, ms);
        UiKit.Fade(_halo, OpacityProperty, _selected || focus ? 0.2 : 0, ms);
        UiKit.Fade(_tint, OpacityProperty, _checked ? 0.07 : 0, ms);
        UiKit.Fade(_checkBox, OpacityProperty, hot || _checked || _selectionMode ? 1 : 0, ms);
        UiKit.Fade(_actionPill, OpacityProperty, hot ? 1 : 0, ms);
        UiKit.Fade(_sourceChip, OpacityProperty, hot ? 1 : 0, ms);
        UiKit.Fade(_starButton, OpacityProperty, hot || _favorite ? 1 : 0, ms);

        // 悬停上浮 + 柔影：只跟随鼠标，选中/勾选不浮起（避免整行高低不齐）。
        UiKit.Fade(_shadow, OpacityProperty, _hovered ? 1 : 0, ms);
        _lift.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(_hovered ? -3 : 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Tokens.Motion.Ease });
    }

    private FrameworkElement BuildActionBar(HistoryCardCallbacks callbacks)
    {
        // 分组：常用操作 | 删除（危险操作单独隔开，红色图标）。
        var separator = UiKit.VerticalSeparator(16);
        separator.Margin = new Thickness(3, 0, 3, 0);
        var bar = UiKit.FloatingBar(
            ActionButton(StringsUi.Get("S.Common.Copy"), null, Slugs.Copy, AppIcons.Copy, () => callbacks.Copy?.Invoke(_record)),
            ActionButton(StringsUi.Get("S.Common.Pin"), null, Slugs.Pin, AppIcons.Pin, () => callbacks.Pin?.Invoke(_record)),
            ActionButton(
                StringsUi.Get("S.Common.Preview"),
                StringsUi.Get("S.History.ActionPreviewHint"),
                Slugs.Preview,
                AppIcons.Preview,
                () => callbacks.Preview?.Invoke(_record)),

            // T28：追加标注并覆盖（在现有图片上继续标注，保存回同一条记录）。
            ActionButton(
                StringsUi.Get("S.Common.Annotation"),
                StringsUi.Get("S.History.ActionAnnotateHint"),
                Slugs.Annotation,
                AppIcons.Annotate,
                () => callbacks.Edit?.Invoke(_record)),
            ActionButton(
                StringsUi.Get("S.Common.Rename"),
                null,
                Slugs.Rename,
                AppIcons.Rename,
                () => callbacks.Rename?.Invoke(_record)),
            ActionButton(StringsUi.Get("S.Common.SaveAs"), null, Slugs.SaveAs, AppIcons.SaveAs, () => callbacks.SaveAs?.Invoke(_record)),
            separator,
            ActionButton(
                StringsUi.Get("S.Common.Delete"),
                StringsUi.Get("S.History.ActionDeleteHint"),
                Slugs.Delete,
                AppIcons.Delete,
                () => callbacks.Delete?.Invoke(_record),
                UiKit.Keys.Error));
        bar.Margin = new Thickness(0, 0, 0, 8);

        // FloatingBar = [投影底板, 面板]：AutomationId 挂在装着按钮的面板上（与旧版一致）。
        AutomationProperties.SetAutomationId(bar.Children[1], $"history-actions-{Index}");
        return bar;
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
        string iconKey = UiKit.Keys.Foreground)
    {
        var button = UiKit.IconButton(iconData, text, hint ?? text, onClick, 26, iconKey, 14);
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
