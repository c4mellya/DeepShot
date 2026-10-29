using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// 图库里的一张图块（参照 Windows 11 照片）：图块就是缩略图本身——8 圆角裁剪 + 1px 内侧发丝线，
/// 不显示文件名、时间、尺寸等文字。悬停时顶部淡入一层柔和暗色渐变，让左上复选框与右上星标在任何图上都清楚；
/// 右下角浮出深色亚克力药丸（复制 / 贴图 / 更多），更多与右键是同一个菜单。
/// </summary>
/// <remarks>
/// <para>
/// 卡片自己<b>不执行</b>任何操作，只把"用户想干什么"通过回调交给图库窗口：
/// 这样"贴图不新增历史""删除要确认"这类规则只在一处（<see cref="HistoryActions"/>
/// 与窗口）实现，卡片只负责长相与命中。
/// </para>
/// <para>
/// <b>UIA</b>：卡片、星标、操作组、菜单项都带稳定的 AutomationId
/// （<c>history-card-N</c> / <c>history-star-N</c> / <c>history-actions-N</c> / <c>history-action-SLUG-N</c>）。
/// 浮层控件用<b>透明度</b>而不是 Visibility 隐藏：始终在 UIA 树里、始终可 Tab 到。
/// </para>
/// <para>
/// <b>状态层次</b>：悬停 = 顶部渐变 + 浮层控件；选中 / 勾选 = 2px 强调色环（勾选时图片再内缩到 0.94）；
/// 键盘焦点 = 2px 焦点环、略淡。
/// </para>
/// </remarks>
internal sealed class HistoryCard : Border
{
    /// <summary>默认卡片宽度（DIP）；实际宽度由图库按可用宽度用 <see cref="SetLayoutWidth"/> 均分。</summary>
    internal const double CardWidth = 240;

    /// <summary>卡片最小宽度：再窄右下角的操作组会压住画面主体。</summary>
    internal const double MinCardWidth = 188;

    /// <summary>卡片之间的横竖间距。</summary>
    internal const double Gap = Tokens.Space.S;

    /// <summary>缩略图高宽比（16:10，横屏截图裁切最少）。</summary>
    private const double ThumbnailAspect = 0.625;

    /// <summary>图块圆角。</summary>
    private const double Radius = Tokens.Radius.Card;

    /// <summary>浮层控件离图块边的距离。</summary>
    private const double OverlayInset = 8;

    /// <summary>星标底圆直径。</summary>
    private const double StarSize = 28;

    /// <summary>操作组里图标按钮的边长。</summary>
    private const double ActionSize = 28;

    /// <summary>顶部渐变高度。</summary>
    private const double ScrimHeight = 56;

    /// <summary>勾选时图片内缩比例。</summary>
    private const double CheckedScale = 0.94;

    /// <summary>顶部渐变：黑色 0.28 → 0，固定 56px 高（不随图块高度拉伸）。</summary>
    private static readonly Brush ScrimBrush = CreateScrimBrush();

    /// <summary>悬浮操作岛 / 星标圆底：半透明深色胶囊 + 白色细描边，透出底图（与星标模板里的颜色一致）。</summary>
    private static readonly Brush IslandFill = Frozen(Color.FromArgb(0x66, 0x20, 0x20, 0x20));

    private static readonly Brush IslandBorder = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static readonly Lazy<ControlTemplate> StarTemplate = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="Button">
          <Grid Background="Transparent" SnapsToDevicePixels="True">
            <Ellipse x:Name="Bg" Fill="#66202020"
                     Stroke="#33FFFFFF" StrokeThickness="1" />
            <Ellipse x:Name="Ov" Fill="{DynamicResource Theme.OverlayButtonHover}" Opacity="0" IsHitTestVisible="False" />
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="1" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Ov" Property="Opacity" Value="0.6" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bg" Property="Stroke" Value="{DynamicResource Theme.FocusRing}" />
              <Setter TargetName="Bg" Property="StrokeThickness" Value="2" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    private static readonly Lazy<ControlTemplate> OverlayButtonTemplate = new(() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="Button">
          <Border x:Name="Bg" Background="Transparent" BorderBrush="Transparent" BorderThickness="1"
                  CornerRadius="14" SnapsToDevicePixels="True">
            <ContentPresenter x:Name="Content" HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bg" Property="Background" Value="{DynamicResource Theme.OverlayButtonHover}" />
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="Content" Property="Opacity" Value="0.7" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bg" Property="BorderBrush" Value="{DynamicResource Theme.FocusRing}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """));

    private readonly HistoryRecord _record;
    private readonly HistoryCardCallbacks _callbacks;
    private readonly Border _thumbnailFrame;
    private readonly Image _picture;
    private readonly Grid _imageHost;
    private readonly ScaleTransform _imageScale = new(1, 1);
    private readonly Border _scrim;
    private readonly Button _starButton;
    private readonly System.Windows.Controls.Viewbox _starIcon;
    private readonly FrameworkElement _actionGroup;
    private readonly Button _moreButton;
    private readonly Border _ring;
    private readonly Border _focusRing;
    private readonly CheckBox _checkBox;
    private readonly TranslateTransform _lift = new();
    private ContextMenu? _menu;
    private Action<HistoryCard>? _checkedChanged;
    private bool _checked;
    private bool _selectionMode;
    private bool _selected;
    private bool _hovered;
    private bool _favorite;
    private bool _menuOpen;
    private bool _pointerFocus;
    private double _scaleTarget = 1;

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
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _favorite = record.IsFavorite;
        Index = index;

        Margin = new Thickness(0, 0, Gap, Gap);
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderTransform = _lift;
        Focusable = true;
        FocusVisualStyle = null;

        AutomationProperties.SetName(this, StringsUi.Format("S.History.CardName", DescribeShort(record)));
        AutomationProperties.SetAutomationId(this, $"history-card-{index}");

        // --- 图片：UniformToFill 裁切铺满；像素不够时不放大（见 FitPicture） ---
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

        _thumbnailFrame = new Border { Child = _picture, SnapsToDevicePixels = true };
        _thumbnailFrame.SetResourceReference(BackgroundProperty, UiKit.Keys.SurfaceAlt);
        if (callbacks.Drag is { } drag)
        {
            HistoryImageDrag.Attach(_thumbnailFrame, source => drag(_record, source));
        }

        if (preview is null && !loadingPreview)
        {
            _thumbnailFrame.Child = BuildMissingPlaceholder();
        }
        else if (preview is null)
        {
            // 等后台解码：先露出底色，不显示"缩略图丢失"；SetPreview 到了再淡入。
            _picture.Opacity = 0;
        }

        // 顶部渐变：只为让角标读得清，不拦截命中。
        _scrim = new Border
        {
            Height = ScrimHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ScrimBrush,
            IsHitTestVisible = false,
            Opacity = 0,
        };

        // 圆角裁剪：Border 的 CornerRadius 不裁子元素，只能手动裁。
        var clipped = new Grid();
        clipped.Children.Add(_thumbnailFrame);
        clipped.Children.Add(_scrim);
        clipped.SizeChanged += (_, e) =>
        {
            clipped.Clip = new RectangleGeometry(new Rect(e.NewSize), Radius, Radius);
            FitPicture();
        };

        // 内侧一圈发丝线：白底截图放在浅色窗口上也有清楚的边界。
        var hairline = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Opacity = 0.8,
        };
        hairline.SetResourceReference(BorderBrushProperty, UiKit.Keys.Divider);

        // 勾选时整块图片内缩（照片式），环留在图块边界上。
        _imageHost = new Grid
        {
            RenderTransform = _imageScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _imageHost.Children.Add(clipped);
        _imageHost.Children.Add(hairline);

        // --- 左上：多选复选框 ---
        _checkBox = UiKit.CheckBox($"history-select-{index}", StringsUi.Get("S.History.SelectCard"));
        _checkBox.HorizontalAlignment = HorizontalAlignment.Left;
        _checkBox.VerticalAlignment = VerticalAlignment.Top;
        _checkBox.Margin = new Thickness(OverlayInset, OverlayInset, 0, 0);
        _checkBox.Click += (_, e) =>
        {
            e.Handled = true;
            _checked = _checkBox.IsChecked == true;
            UpdateChrome();
            _checkedChanged?.Invoke(this);
        };

        // --- 右上：星标（深色圆底，任何图上都看得见） ---
        _starIcon = AppIcons.Create(AppIcons.Star, 14, ThemeManager.BrushKeys.OverlayForeground, 1.9);
        _starButton = new Button
        {
            Content = _starIcon,
            Template = StarTemplate.Value,
            Width = StarSize,
            Height = StarSize,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, OverlayInset, OverlayInset, 0),
            FocusVisualStyle = null,
        };
        AutomationProperties.SetAutomationId(_starButton, $"history-star-{index}");
        _starButton.Click += (_, e) =>
        {
            e.Handled = true;
            callbacks.ToggleFavorite?.Invoke(_record);
        };
        ApplyStarVisual();

        // --- 右下：深色亚克力操作组 ---
        _moreButton = OverlayButton(StringsUi.Get("S.History.MoreActions"), Slugs.More, BuildMoreGlyph(), OpenMenuFromButton);
        _actionGroup = BuildActionGroup();

        // 选中 / 勾选：2px 强调色环；键盘焦点：2px 焦点环、略淡。都盖在图片上，用透明度过渡（没有布局抖动）。
        _focusRing = BuildRing(ThemeManager.BrushKeys.FocusRing);
        _ring = BuildRing(UiKit.Keys.Accent);

        var root = new Grid { Background = Brushes.Transparent };
        root.Children.Add(_imageHost);
        root.Children.Add(_checkBox);
        root.Children.Add(_starButton);
        root.Children.Add(_actionGroup);
        root.Children.Add(_focusRing);
        root.Children.Add(_ring);
        Child = root;

        SetLayoutWidth(CardWidth);

        _actionGroup.Opacity = 0;
        _checkBox.Opacity = 0;
        _starButton.Opacity = _favorite ? 1 : 0;

        PreviewMouseLeftButtonDown += OnPreviewPointerDown;
        PreviewMouseRightButtonDown += OnPreviewPointerDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonUp += OnMouseRightButtonUp;
        KeyDown += OnKeyDown;
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
        IsKeyboardFocusedChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                _pointerFocus = false;
            }

            UpdateChrome();
        };
    }

    /// <summary>本页序号（日志/AutomationId 用）。</summary>
    internal int Index { get; }

    /// <summary>这条记录（窗口按 Id 对账）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>是否被选中（选中后 2px 强调色环 + 浮层控件常驻）。</summary>
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
    /// 按图库算好的列宽设置图块宽度；高度跟随宽度（16:10），整行永远铺满、右侧不留空白。
    /// </summary>
    internal void SetLayoutWidth(double width)
    {
        width = Math.Max(MinCardWidth, Math.Floor(width));
        if (Math.Abs(Width - width) < 0.5)
        {
            return;
        }

        Width = width;
        Height = Math.Round(width * ThumbnailAspect);
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
    /// 缩略图像素不够铺满图块时，放大会发糊：改为不放大、居中显示原始像素；够用时才裁切铺满。
    /// 需要已经在可视树里（要读 DPI）；没有图时什么也不做。
    /// </summary>
    private void FitPicture()
    {
        if (_picture.Source is not BitmapSource preview || !IsLoaded || double.IsNaN(Width) || double.IsNaN(Height))
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var needed = Math.Max(
            Width * scale / preview.PixelWidth,
            Height * scale / preview.PixelHeight);
        var upscaled = needed > 1.15;
        _picture.Stretch = upscaled ? Stretch.Uniform : Stretch.UniformToFill;
        _picture.StretchDirection = upscaled ? StretchDirection.DownOnly : StretchDirection.Both;
    }

    /// <summary>缩略图丢失：居中一枚 24px 三级色图标，不加文字（提示与 UIA 名字说明原因）。</summary>
    private static Grid BuildMissingPlaceholder()
    {
        var text = StringsUi.Get("S.History.ThumbnailMissing");
        var icon = AppIcons.Create(AppIcons.Image, 24, ThemeManager.BrushKeys.TertiaryForeground, 1.6);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var holder = new Grid { Background = Brushes.Transparent, ToolTip = text };
        AutomationProperties.SetName(holder, text);
        holder.Children.Add(icon);
        return holder;
    }

    private static Border BuildRing(string brushKey)
    {
        var ring = new Border
        {
            CornerRadius = new CornerRadius(Radius),
            BorderThickness = new Thickness(2),
            Opacity = 0,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        ring.SetResourceReference(BorderBrushProperty, brushKey);
        return ring;
    }

    private static Brush CreateScrimBrush()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, ScrimHeight),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x47, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1));
        brush.Freeze();
        return brush;
    }

    /// <summary>收藏状态变化后更新星标（不重建控件，避免丢失焦点）。</summary>
    internal void ApplyFavorite(bool favorite)
    {
        _favorite = favorite;
        ApplyStarVisual();
        UpdateChrome();
    }

    /// <summary>重命名后更新（图块不显示名称；UIA 名字按时间·尺寸识别，名称变化无需重排）。</summary>
    internal void ApplyName(string name)
    {
        AutomationProperties.SetName(this, StringsUi.Format("S.History.CardName", DescribeShort(_record)));
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

    /// <summary>读屏用的简短描述："时间 · 宽×高"。</summary>
    private static string DescribeShort(HistoryRecord record) =>
        $"{FormatTime(record)} · {record.Width}×{record.Height}";

    private static string StarOn => StringsUi.Get("S.History.StarOn");

    private static string StarOff => StringsUi.Get("S.History.StarOff");

    private static string StarOnHint => StringsUi.Get("S.History.StarOnHint");

    private static string StarOffHint => StringsUi.Get("S.History.StarOffHint");

    /// <summary>
    /// 星标图标：已收藏 = 金色实心（<c>Theme.Star</c>）；未收藏 = 浮层前景色空心。
    /// 同时更新 UIA 名字与提示。
    /// </summary>
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
            path.SetResourceReference(Shape.StrokeProperty, ThemeManager.BrushKeys.OverlayForeground);
            path.Fill = Brushes.Transparent;
        }

        _starButton.ToolTip = _favorite ? StarOnHint : StarOffHint;
        AutomationProperties.SetName(_starButton, _favorite ? StarOn : StarOff);
    }

    /// <summary>按"悬停 / 选中 / 勾选 / 键盘焦点 / 菜单 / 收藏"刷新渐变、浮层控件、环与内缩。</summary>
    private void UpdateChrome()
    {
        // 鼠标点进来的焦点不画焦点环；键盘带进来的（或点了之后又按了键）才画。
        var keyboardFocus = IsKeyboardFocused && !_pointerFocus;
        var focusWithin = IsKeyboardFocusWithin && !(IsKeyboardFocused && _pointerFocus);
        var hot = _hovered || _selected || _menuOpen || focusWithin;
        var strong = _selected || _checked;
        var showCheck = hot || _checked || _selectionMode;
        var ms = Tokens.Motion.Normal.TotalMilliseconds;

        UiKit.Fade(_ring, OpacityProperty, strong ? 1 : 0, ms);
        UiKit.Fade(_focusRing, OpacityProperty, keyboardFocus && !strong ? 0.6 : 0, ms);
        UiKit.Fade(_scrim, OpacityProperty, showCheck ? 1 : 0, ms);
        UiKit.Fade(_checkBox, OpacityProperty, showCheck ? 1 : 0, ms);
        UiKit.Fade(_actionGroup, OpacityProperty, hot ? 1 : 0, ms);
        UiKit.Fade(_starButton, OpacityProperty, hot || _favorite ? 1 : 0, ms);

        var scale = _checked ? CheckedScale : 1;
        if (Math.Abs(scale - _scaleTarget) > 0.001)
        {
            _scaleTarget = scale;
            var animation = new DoubleAnimation(scale, new Duration(Tokens.Motion.Fast)) { EasingFunction = Tokens.Motion.Ease };
            _imageScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            _imageScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }

    /// <summary>右下角深色亚克力药丸：复制 / 贴图 / 更多。</summary>
    private FrameworkElement BuildActionGroup()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(OverlayButton(
            StringsUi.Get("S.Common.Copy"),
            Slugs.Copy,
            AppIcons.Create(AppIcons.Copy, 16, ThemeManager.BrushKeys.OverlayForeground),
            () => _callbacks.Copy?.Invoke(_record)));
        row.Children.Add(OverlayButton(
            StringsUi.Get("S.Common.Pin"),
            Slugs.Pin,
            AppIcons.Create(AppIcons.Pin, 16, ThemeManager.BrushKeys.OverlayForeground),
            () => _callbacks.Pin?.Invoke(_record)));
        row.Children.Add(_moreButton);

        // 操作组自己的 AutomationId 挂在按钮容器上（一个 history-actions-N + 每个按钮 history-action-SLUG-N）。
        AutomationProperties.SetAutomationId(row, $"history-actions-{Index}");

        var pill = new Border
        {
            Child = row,
            Padding = new Thickness(2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius((ActionSize + 6) / 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, OverlayInset, OverlayInset),
            SnapsToDevicePixels = true,
        };
        pill.Background = IslandFill;
        pill.BorderBrush = IslandBorder;
        return pill;
    }

    /// <summary>"更多"图标：三枚 3px 圆点（图标库没有省略号字形）。</summary>
    private static StackPanel BuildMoreGlyph()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse { Width = 3, Height = 3, Margin = new Thickness(i == 0 ? 0 : 2.5, 0, 0, 0) };
            dot.SetResourceReference(Shape.FillProperty, ThemeManager.BrushKeys.OverlayForeground);
            dots.Children.Add(dot);
        }

        return dots;
    }

    /// <summary>
    /// 操作的 AutomationId 后缀（语言无关，验证脚本按它定位）。
    /// </summary>
    /// <remarks>
    /// T31 起这些 id 不再拼中文：固定 slug 让脚本与语言无关
    /// （见 <c>scripts/verify-t25t26.ps1</c> 的 <c>Report-Actions</c>）。
    /// </remarks>
    private static class Slugs
    {
        internal const string Copy = "copy";

        internal const string Pin = "pin";

        internal const string More = "more";

        internal const string Rename = "rename";

        internal const string SaveAs = "save-as";

        internal const string Delete = "delete";

        internal const string Annotation = "annotation";

        internal const string Preview = "preview";
    }

    /// <summary>浮层上的 28 图标按钮：透明底、悬停 OverlayButtonHover；UIA 名字与提示 = 文案。</summary>
    private Button OverlayButton(string text, string automationSlug, UIElement content, System.Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Template = OverlayButtonTemplate.Value,
            Width = ActionSize,
            Height = ActionSize,
            MinWidth = 0,
            MinHeight = 0,
            FocusVisualStyle = null,
            ToolTip = text,
        };
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetAutomationId(button, $"history-action-{automationSlug}-{Index}");
        button.Click += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return button;
    }

    // --- 菜单（更多按钮与右键共用） ---

    private ContextMenu EnsureMenu()
    {
        if (_menu is not null)
        {
            return _menu;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MenuAction(StringsUi.Get("S.Common.Preview"), Slugs.Preview, AppIcons.Preview, () => _callbacks.Preview?.Invoke(_record)));
        menu.Items.Add(MenuAction(StringsUi.Get("S.Common.Annotation"), Slugs.Annotation, AppIcons.Annotate, () => _callbacks.Edit?.Invoke(_record)));
        menu.Items.Add(MenuAction(StringsUi.Get("S.Common.Rename"), Slugs.Rename, AppIcons.Rename, () => _callbacks.Rename?.Invoke(_record)));
        menu.Items.Add(MenuAction(StringsUi.Get("S.Common.SaveAs"), Slugs.SaveAs, AppIcons.SaveAs, () => _callbacks.SaveAs?.Invoke(_record)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction(StringsUi.Get("S.Common.Delete"), Slugs.Delete, AppIcons.Delete, () => _callbacks.Delete?.Invoke(_record), UiKit.Keys.Error));
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            UpdateChrome();
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            UpdateChrome();
        };
        _menu = menu;
        return menu;
    }

    private MenuItem MenuAction(string text, string slug, string iconData, System.Action onClick, string? toneKey = null)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = AppIcons.Create(iconData, 16, toneKey ?? UiKit.Keys.Foreground),
        };
        if (toneKey is not null)
        {
            item.SetResourceReference(Control.ForegroundProperty, toneKey);
        }

        AutomationProperties.SetName(item, text);
        AutomationProperties.SetAutomationId(item, $"history-action-{slug}-{Index}");
        item.Click += (_, _) => onClick();
        return item;
    }

    private void OpenMenuFromButton() => OpenMenu(_moreButton, PlacementMode.Bottom);

    private void OpenMenu(UIElement target, PlacementMode placement)
    {
        var menu = EnsureMenu();
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    // --- 指针与键盘 ---

    private void OnPreviewPointerDown(object sender, MouseButtonEventArgs e)
    {
        // 点在图片上（不是浮层按钮）时把焦点收到图块上，方便接着用键盘；鼠标带来的焦点不画焦点环。
        if (e.OriginalSource is DependencyObject source && IsWithin(source, _imageHost) && !IsKeyboardFocused)
        {
            _pointerFocus = true;
            Focus();
        }
    }

    private static bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 双击 = 预览大图；单击只选中卡片。
        if (e.ClickCount >= 2)
        {
            RaisePreview();
            e.Handled = true;
        }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenMenu(this, PlacementMode.MousePoint);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // 只处理图块自身获焦时的按键：焦点在子按钮上时空格/回车属于那个按钮。
        if (!ReferenceEquals(e.OriginalSource, this))
        {
            return;
        }

        if (_pointerFocus)
        {
            _pointerFocus = false;
            UpdateChrome();
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        switch (key)
        {
            case Key.Enter when modifiers == ModifierKeys.None:
                RaisePreview();
                e.Handled = true;
                break;
            case Key.Space when modifiers == ModifierKeys.None:
                _checked = !_checked;
                _checkBox.IsChecked = _checked;
                UpdateChrome();
                _checkedChanged?.Invoke(this);
                e.Handled = true;
                break;
            case Key.Apps:
            case Key.F10 when modifiers == ModifierKeys.Shift:
                OpenMenu(_moreButton, PlacementMode.Bottom);
                e.Handled = true;
                break;
            case Key.Delete when modifiers == ModifierKeys.None:
                _callbacks.Delete?.Invoke(_record);
                e.Handled = true;
                break;
            case Key.C when modifiers == ModifierKeys.Control:
                _callbacks.Copy?.Invoke(_record);
                e.Handled = true;
                break;
        }
    }

    private void RaisePreview()
    {
        if (Tag is Action<HistoryRecord> preview)
        {
            preview(_record);
        }
        else
        {
            _callbacks.Preview?.Invoke(_record);
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
    /// 容器里按下卡片并拖动 = 滚动图库（触屏/鼠标都能用）。
    /// </summary>
    /// <remarks>
    /// 卡片本身是按钮语义（单击选中、双击预览），所以<b>不能</b>靠 <c>BringIntoView</c>
    /// 之类会改变滚动位置的行为来实现；这里只把垂直方向的像素增量交给外层
    /// <see cref="ScrollViewer"/>，横向与其它命中一律不处理。
    /// 缩略图本身挂了拖出（<see cref="HistoryImageDrag"/>），按下事件会被它吃掉，此时不滚动。
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
