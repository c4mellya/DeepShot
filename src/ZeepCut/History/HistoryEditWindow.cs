using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ZeepCut.Core.Annotations;
using ZeepCut.Core.Geometry;
using ZeepCut.Core.History;
using ZeepCut.Ui;
using ZeepCut.Editor;
using ZeepCut.Platform;

namespace ZeepCut.History;

/// <summary>
/// T28 的历史再编辑窗口：打开一条历史记录，在它现有的图片上**追加**标注，保存即覆盖同一条记录。
/// </summary>
/// <remarks>
/// <para>
/// <b>复用标注组件</b>（PLAN）：标注数据模型与撤销栈用 Core 的
/// <see cref="AnnotationSession"/> / <see cref="AnnotationDocument"/>；绘制、命中测试、选择/移动/
/// 缩放/删除、文字输入与导出合成全部复用截图遮罩用的
/// <c>AnnotationLayer</c> / <c>AnnotationRenderer</c> / <c>AnnotationExportRenderer</c>。
/// 本窗口只做"摆放 + 鼠标 DIP ↔ 图像像素换算 + 保存/取消"，不重新实现任何标注逻辑。
/// </para>
/// <para>
/// <b>覆盖语义</b>（PLAN：「旧标注已属于图片像素，只能追加」）：打开时把记录**当前**的图片当作
/// 背景位图，会话从一个空文档开始 —— 旧标注已经是像素，命中测试碰不到它们，自然不可拆分修改。
/// 保存走 <see cref="HistoryRepository.UpdateImage"/>（先写 <c>.tmp</c> → 校验长度 → 替换 → 更新行），
/// 保留 Id / 名称 / 创建时间 / 收藏，只换图片、缩略图、宽高与修改时间，**不生成新记录**。
/// </para>
/// <para>
/// <b>首次解释</b>：第一次点“保存并覆盖”时弹一次说明（覆盖不保留旧版本），确认后才写盘；
/// 说明过之后本次运行不再重复（PLAN：首次使用时解释覆盖不保留旧版本）。提示文案在
/// Core 的 <see cref="HistoryOverwritePlan.DescribeOverwriteExplanation"/>，可单测。
/// </para>
/// <para>
/// <b>取消</b>：直接关窗，不写任何文件、不动数据库（PLAN：取消编辑不改原图）。
/// 编辑期间这条记录被删除时，保存会提示"记录已不存在"并且不写文件、不新建记录。
/// </para>
/// <para>
/// <b>贴图不受影响</b>：贴图窗口持有创建时解码的快照位图，与本窗口的编辑、与磁盘上的历史文件
/// 都没有共享引用；覆盖只替换 <c>images/</c> 下那一个文件，已打开的贴图保持原样
/// （PLAN：贴图使用创建时快照，历史覆盖或清理不改变已有贴图）。
/// </para>
/// <para>
/// <b>UIA</b>：窗口、每个工具按钮、撤销/重做/删除、保存并覆盖、取消都带稳定的 AutomationId，
/// 真实桌面验证脚本按名字点击，不依赖坐标。
/// </para>
/// </remarks>
internal sealed class HistoryEditWindow : Window
{
    /// <summary>
    /// 保存按钮的文案（PLAN 要求明确为“保存并覆盖”，T31 起来自语言资源）。
    /// </summary>
    internal static string SaveButtonText => StringsUi.Get("S.History.EditSave");

    /// <summary>保存按钮的 AutomationId（验证脚本按它点击）。</summary>
    internal const string SaveButtonId = "history-edit-save";

    /// <summary>图像四周留白（DIP）。</summary>
    private const double CanvasPadding = 16;

    /// <summary>右侧工具栏宽度（DIP）。</summary>
    private const double SidebarWidth = 208;

    /// <summary>手柄命中半径与文档命中容差（物理像素，与遮罩一致）。</summary>
    private const int HandleRadius = 8;
    private const int HitTolerance = 4;

    /// <summary>选中态虚线框线宽与手柄边长（DIP）。</summary>
    private const double OutlineThickness = 1.5;
    private const double EditorHandleSize = 7;

    /// <summary>本次运行是否已经解释过覆盖语义（静态 = 跨窗口、跨记录，一次即够）。</summary>
    private static bool _overwriteExplained;

    /// <summary>
    /// 覆盖说明真正显示过之后的回调（T27/T28 接线：把它记到设置文件里，
    /// 之后启动就不再重复解释）。为 null 时什么也不做。
    /// </summary>
    internal static Action? OnExplanationShown;

    /// <summary>本次运行是否已解释过覆盖语义（验证/诊断读取）。</summary>
    internal static bool WasExplainedThisRun => _overwriteExplained;

    /// <summary>仅测试/诊断用：重置"已解释覆盖"标记。</summary>
    internal static void ResetOverwriteExplanation() => _overwriteExplained = false;

    private readonly HistoryStore _store;
    private readonly HistoryRecord _record;
    private readonly BitmapSource _background;
    private readonly AnnotationSession _session;
    private readonly int _textureSize;

    private readonly Image _picture;
    private readonly ScrollViewer _scroll;
    private readonly AnnotationLayer _layer;
    private readonly Canvas _editorCanvas;
    private readonly Path _editorOutline;
    private readonly List<(AnnotationHandle Handle, Rectangle Element)> _editorHandles = new();
    private readonly Border _textEditorHost;
    private readonly TextBox _textEditor;
    private readonly TextBlock _status;
    private readonly TextBlock _info;
    private TextBlock _hint = null!;

    /// <summary>只读字段的占位写法在 C# 里不成立，这里不做声明；侧栏标题随语言重建即可。</summary>
    private readonly List<(AnnotationTool? Tool, Button Element)> _toolButtons = new();
    private readonly Dictionary<AnnotationTool, string> _toolLabels = new();
    private readonly Button _saveButton;

    /// <summary>图像左上角在画布内的位置（DIP）；缩放时改变。</summary>
    private Point _origin;
    private double _zoom = 1.0;

    private AnnotationTool? _activeTool;
    private TextAnnotation? _editingText;
    private string _editingTextOriginal = string.Empty;
    private bool _pointerDrawing;
    private bool _pointerEditing;
    private bool _dirty;
    private bool _saved;

    /// <summary>物理像素 ↔ DIP 的换算（<see cref="_textureSize"/> 是本机屏幕 DPI，来自窗口）。</summary>
    private int _dpi = 96;

    /// <param name="owner">图库窗口（保存后由它刷新）。</param>
    /// <param name="store">历史写入器。</param>
    /// <param name="record">要编辑的记录。</param>
    /// <param name="image">记录当前的图片（已解码、已冻结；旧标注已在像素里）。</param>
    internal HistoryEditWindow(Window? owner, HistoryStore store, HistoryRecord record, BitmapSource image)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _background = image ?? throw new ArgumentNullException(nameof(image));

        // 会话的图像范围 = 记录当前图片尺寸（标注坐标就是这张图的物理像素）。
        _session = new AnnotationSession(new PixelRect(0, 0, image.PixelWidth, image.PixelHeight));
        _textureSize = Math.Max(1, Math.Min(image.PixelWidth, image.PixelHeight));

        Title = StringsUi.Format("S.History.EditTitle", record.Name);
        Width = Math.Min(1180, Math.Max(760, image.PixelWidth / 1.25 + SidebarWidth + 90));
        Height = Math.Min(860, Math.Max(560, image.PixelHeight / 1.25 + 190));
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = HistoryUi.WindowBackground;
        FontFamily = new FontFamily(HistoryUi.FontFamilyName);
        AutomationProperties.SetAutomationId(this, "history-edit-window");
        AutomationProperties.SetName(this, StringsUi.Get("S.History.EditWindowName"));

        // --- 图像显示：ScrollViewer + Image（Stretch.None = 1 图像像素显示成 1 个设备无关像素） ---
        _picture = new Image
        {
            Source = image,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.NearestNeighbor);
        AutomationProperties.SetAutomationId(_picture, "history-edit-image");

        _layer = new AnnotationLayer(
            _session.Document,
            _textureSize,
            () => _session.Preview,
            image,
            new PixelRect(0, 0, image.PixelWidth, image.PixelHeight));

        // 选中态层：虚线框 + 手柄，不参与命中测试（命中走 AnnotationEditController）。
        _editorOutline = new Path
        {
            Stroke = HistoryUi.Frozen(Color.FromRgb(0x00, 0xC8, 0xFF)),
            StrokeThickness = OutlineThickness,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        _editorCanvas = new Canvas { IsHitTestVisible = false };
        _editorCanvas.Children.Add(_editorOutline);
        foreach (var handle in Enum.GetValues<AnnotationHandle>())
        {
            if (handle is AnnotationHandle.None or AnnotationHandle.Move)
            {
                continue;
            }

            var element = new Rectangle
            {
                Width = EditorHandleSize,
                Height = EditorHandleSize,
                Fill = Brushes.White,
                Stroke = HistoryUi.Frozen(Color.FromArgb(0xCC, 0x10, 0x3A, 0x50)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            _editorHandles.Add((handle, element));
            _editorCanvas.Children.Add(element);
        }

        var canvasHost = new Canvas();
        canvasHost.Children.Add(_picture);
        canvasHost.Children.Add(_layer);
        canvasHost.Children.Add(_editorCanvas);
        canvasHost.Background = HistoryUi.ThumbnailBackground;

        _textEditor = new TextBox
        {
            Foreground = HistoryUi.Foreground,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 14,
            FontFamily = new FontFamily(HistoryUi.FontFamilyName),
            Padding = new Thickness(2, 1, 2, 1),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        _textEditor.TextChanged += (_, _) =>
        {
            if (_session.IsTextEditing)
            {
                _session.UpdateTextPreview(_textEditor.Text);
                _layer.InvalidateVisual();
            }
        };
        _textEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                e.Handled = true;
                CommitText("输入框 Enter");
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelText("输入框 Esc");
            }
        };

        _textEditorHost = new Border
        {
            Background = Brushes.White,
            BorderBrush = HistoryUi.SelectedBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = _textEditor,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(_textEditorHost, "标注文字输入框");
        AutomationProperties.SetAutomationId(_textEditorHost, "history-edit-textbox");
        canvasHost.Children.Add(_textEditorHost);

        _scroll = new ScrollViewer
        {
            Content = canvasHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = HistoryUi.ThumbnailBackground,
        };

        // --- 头部：记录信息 ---
        _info = HistoryUi.Text(DescribeRecord(record, image), 12.5, HistoryUi.SecondaryForeground);
        _info.Margin = new Thickness(16, 10, 16, 8);
        AutomationProperties.SetAutomationId(_info, "history-edit-record-info");

        _hint = HistoryUi.Text(
            StringsUi.Get("S.History.EditHint"),
            12,
            HistoryUi.SecondaryForeground);
        _hint.HorizontalAlignment = HorizontalAlignment.Left;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.Margin = new Thickness(16, 0, 16, 6);
        AutomationProperties.SetAutomationId(_hint, "history-edit-hint");

        // --- 右侧工具栏 ---
        var sidebar = BuildSidebar(out _saveButton);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_scroll, 0);
        Grid.SetColumn(sidebar, 1);
        body.Children.Add(_scroll);
        body.Children.Add(sidebar);

        // --- 底部状态条 ---
        _status = HistoryUi.Text(StringsUi.Get("S.History.EditStatusHint"), 12, HistoryUi.SecondaryForeground);
        _status.Margin = new Thickness(16, 6, 16, 10);
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(_status, "history-edit-status");
        AutomationProperties.SetName(_status, _status.Text);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_info, 0);
        Grid.SetRow(_hint, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(_status, 2);

        var header = new StackPanel();
        header.Children.Add(_info);
        header.Children.Add(_hint);
        Grid.SetRow(header, 0);
        root.Children.Add(header);
        root.Children.Add(body);
        root.Children.Add(_status);
        Content = root;

        _scroll.PreviewMouseLeftButtonDown += OnCanvasMouseDown;
        _scroll.PreviewMouseLeftButtonUp += OnCanvasMouseUp;
        _scroll.PreviewMouseMove += OnCanvasMouseMove;
        _scroll.MouseLeave += (_, _) => UpdateCursor(null);
        _scroll.PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        Loaded += (_, _) =>
        {
            // 标注层按"物理像素/DPR"换算：把窗口实际 DPI 交给它，预览与绘制才与屏幕一致。
            _dpi = ReadDpi();
            _layer.DpiOverride = _dpi;
            FitToWindow();
            Relayout();
            UpdateEditorVisuals();
            Activate();
            Focus();
        };

        AppLog.Info(
            "History",
            $"历史编辑窗口已创建：名称={record.Name} Id={record.Id} 图片={image.PixelWidth}x{image.PixelHeight} " +
            $"收藏={record.IsFavorite} 创建={HistoryRecord.FormatUtc(record.CreatedAtUtc)} " +
            $"来源={HistorySourceText.Describe(record.Source)}（旧标注已在像素中，只能追加）");
    }

    /// <summary>打开时的记录（图库关闭窗口时写日志用）。</summary>
    internal HistoryRecord Record => _record;

    /// <summary>
    /// 从设置里恢复"已经解释过覆盖语义"：为 true 时不会再弹说明（PLAN：首次使用时解释）。
    /// </summary>
    internal static void ApplyPersistedExplanation(bool alreadyExplained)
    {
        if (alreadyExplained)
        {
            _overwriteExplained = true;
        }
    }

    /// <summary>保存是否成功（关闭后由图库读取，决定是否 Reload 并提示）。</summary>
    internal bool WasSaved => _saved;

    /// <summary>保存后得到的记录（未保存为 null）。</summary>
    internal HistoryRecord? SavedRecord { get; private set; }

    /// <summary>本次追加的标注数量（日志/提示用）。</summary>
    internal int AddedAnnotationCount => _session.Count;

    /// <summary>会话（验证/诊断读取撤销栈深度等）。</summary>
    internal AnnotationSession Session => _session;

    /// <summary>当前选中的绘制工具（null = 选择模式）。</summary>
    internal AnnotationTool? ActiveTool => _activeTool;

    /// <summary>图像左上角在画布内的位置（DIP）。</summary>
    internal Point ImageOrigin => _origin;

    // --- 侧栏 ---

    private Border BuildSidebar(out Button saveButton)
    {
        var panel = new StackPanel { Width = SidebarWidth - 20 };

        panel.Children.Add(SectionLabel(StringsUi.Get("S.Annotation.Section.Tools")));
        var toolGrid = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Select"), null, "select");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Rectangle"), AnnotationTool.Rectangle, "rectangle");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Ellipse"), AnnotationTool.Ellipse, "ellipse");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Arrow"), AnnotationTool.Arrow, "arrow");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Line"), AnnotationTool.Line, "line");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Freehand"), AnnotationTool.Freehand, "freehand");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Text"), AnnotationTool.Text, "text");
        AddToolButton(toolGrid, StringsUi.Get("S.Annotation.Tool.Mosaic"), AnnotationTool.Mosaic, "mosaic");
        panel.Children.Add(toolGrid);

        panel.Children.Add(SectionLabel(StringsUi.Get("S.Annotation.Section.Color")));
        var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var color in AnnotationPalette.Colors)
        {
            var swatch = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 5, 5),
                Background = HistoryUi.Frozen(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue)),
                BorderBrush = HistoryUi.Border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = color.ToString(),
            };
            var captured = color;
            swatch.Click += (_, _) =>
            {
                _session.Settings.Color = captured;
                AppLog.Info("History", $"编辑窗口切换颜色：{captured}");
            };
            AutomationProperties.SetName(swatch, $"颜色{color}");
            AutomationProperties.SetAutomationId(swatch, $"history-edit-color-{color}");
            colors.Children.Add(swatch);
        }

        panel.Children.Add(colors);

        panel.Children.Add(SectionLabel(StringsUi.Get("S.Annotation.Section.Thickness")));
        var thicknesses = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var thickness in AnnotationPalette.Thicknesses)
        {
            var button = HistoryUi.Button(
                thickness.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"粗细{thickness}",
                () =>
                {
                    _session.Settings.Thickness = thickness;
                    AppLog.Info("History", $"编辑窗口切换粗细：{thickness} 像素");
                });
            button.MinWidth = 34;
            button.Padding = new Thickness(6, 2, 6, 2);
            button.Margin = new Thickness(0, 0, 5, 5);
            AutomationProperties.SetAutomationId(button, $"history-edit-thickness-{thickness}");
            thicknesses.Children.Add(button);
        }

        panel.Children.Add(thicknesses);

        panel.Children.Add(SectionLabel(StringsUi.Get("S.Annotation.Section.Edit")));
        var undo = HistoryUi.Button(StringsUi.Get("S.Common.Undo"), StringsUi.Get("S.Common.Undo"), () => Undo("侧栏"));
        var redo = HistoryUi.Button(StringsUi.Get("S.Common.Redo"), StringsUi.Get("S.Common.Redo"), () => Redo("侧栏"));
        var remove = HistoryUi.Button(
            StringsUi.Get("S.History.EditDeleteSelected"),
            StringsUi.Get("S.History.EditDeleteSelected"),
            () => DeleteSelected("侧栏"));
        AutomationProperties.SetAutomationId(undo, "history-edit-undo");
        AutomationProperties.SetAutomationId(redo, "history-edit-redo");
        AutomationProperties.SetAutomationId(remove, "history-edit-delete");
        var editRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        editRow.Children.Add(undo);
        editRow.Children.Add(redo);
        editRow.Children.Add(remove);
        panel.Children.Add(editRow);

        panel.Children.Add(SectionLabel(StringsUi.Get("S.Annotation.Section.Zoom")));
        var zoomRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var zoomOut = HistoryUi.Button("－", "编辑缩小", () => SetZoom(_zoom / 1.25));
        var zoomIn = HistoryUi.Button("＋", "编辑放大", () => SetZoom(_zoom * 1.25));
        var zoomReset = HistoryUi.Button("1:1", "编辑实际大小", () => SetZoom(1.0));
        var zoomFit = HistoryUi.Button(StringsUi.Get("S.History.EditFit"), StringsUi.Get("S.History.EditFit"), FitToWindow);
        foreach (var button in new[] { zoomOut, zoomIn, zoomReset, zoomFit })
        {
            button.MinWidth = 40;
            button.Padding = new Thickness(6, 2, 6, 2);
            button.Margin = new Thickness(0, 0, 5, 5);
            zoomRow.Children.Add(button);
        }

        AutomationProperties.SetAutomationId(zoomOut, "history-edit-zoom-out");
        AutomationProperties.SetAutomationId(zoomIn, "history-edit-zoom-in");
        AutomationProperties.SetAutomationId(zoomReset, "history-edit-zoom-reset");
        AutomationProperties.SetAutomationId(zoomFit, "history-edit-zoom-fit");
        panel.Children.Add(zoomRow);

        saveButton = HistoryUi.Button(SaveButtonText, SaveButtonText, () => Save("侧栏按钮"), primary: true);
        AutomationProperties.SetAutomationId(saveButton, SaveButtonId);
        saveButton.MinWidth = 0;
        saveButton.ToolTip = StringsUi.Get("S.History.EditSaveHint");
        AutomationProperties.SetHelpText(saveButton, saveButton.ToolTip?.ToString() ?? string.Empty);

        var cancel = HistoryUi.Button(
            StringsUi.Get("S.History.EditCancel"),
            StringsUi.Get("S.History.EditCancelName"),
            () => Cancel(confirm: true));
        AutomationProperties.SetAutomationId(cancel, "history-edit-cancel");

        var actionRow = new StackPanel { Orientation = Orientation.Vertical };
        saveButton.Margin = new Thickness(0, 0, 0, 6);
        saveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancel.Margin = new Thickness(0, 0, 0, 0);
        cancel.HorizontalAlignment = HorizontalAlignment.Stretch;
        actionRow.Children.Add(saveButton);
        actionRow.Children.Add(cancel);
        panel.Children.Add(actionRow);

        return new Border
        {
            Width = SidebarWidth,
            Background = HistoryUi.CardBackground,
            BorderBrush = HistoryUi.Border,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(10, 10, 10, 10),
            Child = panel,
        };
    }

    private static TextBlock SectionLabel(string text)
    {
        var block = HistoryUi.Text(text, 12, HistoryUi.Foreground, FontWeights.SemiBold);
        block.Margin = new Thickness(2, 4, 0, 4);
        return block;
    }

    private void AddToolButton(Panel host, string label, AnnotationTool? tool, string slug)
    {
        var button = HistoryUi.Button(label, label, () => SelectTool(tool));
        button.MinWidth = 52;
        button.Padding = new Thickness(6, 3, 6, 3);
        button.Margin = new Thickness(0, 0, 5, 5);

        // AutomationId 用语言无关的 slug（T31）：切到英文后脚本仍按同一个 id 找得到。
        AutomationProperties.SetAutomationId(button, $"history-edit-tool-{slug}");
        _toolButtons.Add((tool, button));
        _toolLabels[tool ?? AnnotationTool.Rectangle] = label;
        host.Children.Add(button);
    }

    // --- 布局与缩放 ---

    private int ReadDpi()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } target)
            {
                return Math.Max(96, (int)Math.Round(96 * target.TransformToDevice.M11));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"读取编辑窗口 DPI 失败（按 96 处理）：{ex.Message}");
        }

        return 96;
    }

    /// <summary>按当前缩放摆放图片、标注层与选中态层（全部换算到画布 DIP）。</summary>
    private void Relayout()
    {
        var width = _background.PixelWidth * _zoom;
        var height = _background.PixelHeight * _zoom;

        var viewportWidth = Math.Max(0, _scroll.ViewportWidth);
        var viewportHeight = Math.Max(0, _scroll.ViewportHeight);
        _origin = new Point(
            Math.Max(CanvasPadding, (viewportWidth - width) / 2),
            Math.Max(CanvasPadding, (viewportHeight - height) / 2));

        Canvas.SetLeft(_picture, _origin.X);
        Canvas.SetTop(_picture, _origin.Y);
        _picture.Width = width;
        _picture.Height = height;

        Canvas.SetLeft(_layer, _origin.X);
        Canvas.SetTop(_layer, _origin.Y);
        _layer.Width = width;
        _layer.Height = height;

        Canvas.SetLeft(_editorCanvas, _origin.X);
        Canvas.SetTop(_editorCanvas, _origin.Y);
        _editorCanvas.Width = width;
        _editorCanvas.Height = height;

        var hostWidth = Math.Max(viewportWidth, _origin.X * 2 + width);
        var hostHeight = Math.Max(viewportHeight, _origin.Y * 2 + height);
        if (_scroll.Content is FrameworkElement content)
        {
            content.Width = hostWidth;
            content.Height = hostHeight;
        }

        UpdateTextEditorPlacement();
        UpdateEditorVisuals();
    }

    private void SetZoom(double zoom)
    {
        var clamped = Math.Clamp(zoom, 0.1, 4.0);
        if (Math.Abs(clamped - _zoom) < 0.0001)
        {
            return;
        }

        _zoom = clamped;
        Relayout();
        AppLog.Info("History", $"编辑窗口缩放：{_zoom * 100:0}%（1 图像像素 = {_zoom * 96 / _dpi:0.###} DIP）");
    }

    private void FitToWindow()
    {
        var viewportWidth = Math.Max(64, _scroll.ViewportWidth) - (CanvasPadding * 2);
        var viewportHeight = Math.Max(64, _scroll.ViewportHeight) - (CanvasPadding * 2);
        var scale = Math.Min(viewportWidth / _background.PixelWidth, viewportHeight / _background.PixelHeight);
        SetZoom(scale);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return; // 不按 Ctrl 时滚轮 = 滚动画面
        }

        e.Handled = true;
        SetZoom(e.Delta > 0 ? _zoom * 1.1 : _zoom / 1.1);
    }

    /// <summary>画布 DIP → 图像的物理像素坐标（可能超出图像，由 Core 夹取/判定）。</summary>
    private PixelPoint ToImage(Point dip, bool clamp = true)
    {
        // dip 是 ScrollViewer 坐标系，与图片/标注层所在 Canvas 的坐标系一致（无偏移）。
        var localX = (dip.X - _origin.X) * _dpi / 96.0 / _zoom;
        var localY = (dip.Y - _origin.Y) * _dpi / 96.0 / _zoom;

        var x = localX;
        var y = localY;
        if (clamp)
        {
            x = Math.Clamp(x, 0, Math.Max(0, _background.PixelWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, _background.PixelHeight - 1));
        }

        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }

    /// <summary>图像物理像素 → 画布 DIP（标注层与选中态层都用这个换算）。</summary>
    private Point ToDip(int x, int y) => new(
        _origin.X + (x * _zoom * 96.0 / _dpi),
        _origin.Y + (y * _zoom * 96.0 / _dpi));

    // --- 鼠标 ---

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || IsInsideTextEditor(e.GetPosition(_scroll)))
        {
            return;
        }

        _scroll.Focus();

        if (_session.IsTextEditing)
        {
            CommitText("点击其它位置");
        }

        var dip = e.GetPosition(_scroll);
        var point = ToImage(dip);

        // 1) 选择模式：先试手柄，再试命中已有**本次**标注。
        if (_activeTool is null && _session.Count > 0)
        {
            var editor = _session.Editing;
            if (editor is not null)
            {
                if (editor.HasSelection && editor.HitTestHandle(point, HandleRadius) != AnnotationHandle.None)
                {
                    if (editor.BeginDrag(point, HandleRadius, HitTolerance))
                    {
                        _pointerEditing = true;
                        _scroll.CaptureMouse();
                        e.Handled = true;
                        UpdateEditorVisuals();
                        AppLog.Info("History", $"编辑窗口开始缩放标注：手柄={editor.ActiveHandle} 图像={point}");
                        return;
                    }
                }

                var hit = _session.Document.HitTest(point, HitTolerance);
                if (hit is not null)
                {
                    if (e.ClickCount >= 2 && hit is TextAnnotation text)
                    {
                        editor.Select(text);
                        BeginTextEdit(text);
                        e.Handled = true;
                        UpdateEditorVisuals();
                        return;
                    }

                    if (editor.BeginDrag(point, HandleRadius, HitTolerance))
                    {
                        _pointerEditing = true;
                        _scroll.CaptureMouse();
                        e.Handled = true;
                        UpdateEditorVisuals();
                        AppLog.Info("History", $"编辑窗口开始移动标注：类型={hit.Kind} 图像={point}");
                        return;
                    }
                }
                else if (editor.HasSelection)
                {
                    editor.ClearSelection();
                    UpdateEditorVisuals();
                }
            }

            return;
        }

        // 2) 绘制工具：文字走输入框，其余走拖拽草稿。
        if (!_activeTool.HasValue)
        {
            return;
        }

        if (_activeTool.Value == AnnotationTool.Text)
        {
            BeginText(point);
            e.Handled = true;
            return;
        }

        try
        {
            _session.BeginDraft(point);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(StringsUi.Format("S.History.EditCannotDraw", ex.Message), isError: true);
            return;
        }

        _pointerDrawing = true;
        _scroll.CaptureMouse();
        e.Handled = true;
        AppLog.Info("History", $"编辑窗口开始画标注：工具={_activeTool} 起点图像={point} 图片={_background.PixelWidth}x{_background.PixelHeight}");
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var dip = e.GetPosition(_scroll);

        if (_pointerEditing)
        {
            _session.Editing?.DragTo(ToImage(dip));
            _layer.InvalidateVisual();
            UpdateEditorVisuals();
            return;
        }

        if (_pointerDrawing)
        {
            _session.UpdateDraft(ToImage(dip));
            _layer.InvalidateVisual();
            return;
        }

        UpdateCursor(dip);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pointerEditing)
        {
            _pointerEditing = false;
            if (_scroll.IsMouseCaptured)
            {
                _scroll.ReleaseMouseCapture();
            }

            var editor = _session.Editing;
            if (editor is not null)
            {
                editor.DragTo(ToImage(e.GetPosition(_scroll)));
                var description = editor.EndDrag();
                MarkDirty();
                AppLog.Info(
                    "History",
                    $"编辑窗口标注编辑结束：方式={editor.Mode} 记录={description ?? "无变化"} " +
                    $"文档标注数={_session.Count} 撤销栈={_session.History.UndoCount}");
            }

            _layer.InvalidateVisual();
            UpdateEditorVisuals();
            e.Handled = true;
            return;
        }

        if (!_pointerDrawing)
        {
            return;
        }

        _pointerDrawing = false;
        if (_scroll.IsMouseCaptured)
        {
            _scroll.ReleaseMouseCapture();
        }

        _session.UpdateDraft(ToImage(e.GetPosition(_scroll)));
        var tool = _activeTool?.ToString() ?? "无";
        var committed = _session.CommitDraft();
        if (committed)
        {
            MarkDirty();
        }

        AppLog.Info(
            "History",
            $"编辑窗口标注抬起：工具={tool} 已提交={committed} 文档标注数={_session.Count} " +
            $"撤销栈={_session.History.UndoCount}");
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
        e.Handled = true;
    }

    private void UpdateCursor(Point? dip)
    {
        if (_activeTool.HasValue)
        {
            Cursor = Cursors.Cross;
            return;
        }

        var editor = _session.Editing;
        if (editor is { HasSelection: true } && dip is { } point)
        {
            var image = ToImage(point, clamp: false);
            var handle = editor.HitTestHandle(image, HandleRadius);
            if (handle != AnnotationHandle.None)
            {
                Cursor = handle switch
                {
                    AnnotationHandle.TopLeft or AnnotationHandle.BottomRight => Cursors.SizeNWSE,
                    AnnotationHandle.TopRight or AnnotationHandle.BottomLeft => Cursors.SizeNESW,
                    AnnotationHandle.Left or AnnotationHandle.Right => Cursors.SizeWE,
                    AnnotationHandle.Top or AnnotationHandle.Bottom => Cursors.SizeNS,
                    _ => Cursors.SizeAll,
                };
                return;
            }

            if (_session.Document.HitTest(image, HitTolerance) is not null)
            {
                Cursor = Cursors.SizeAll;
                return;
            }
        }

        Cursor = Cursors.Arrow;
    }

    // --- 文字输入 ---

    private void BeginText(PixelPoint point)
    {
        try
        {
            var pending = _session.BeginText(point);
            _textEditor.Text = string.Empty;
            _session.UpdateTextPreview(string.Empty);
            _textEditor.FontSize = Math.Max(8, pending.FontSize * _zoom * 96.0 / _dpi);
            _textEditorHost.Visibility = Visibility.Visible;
            UpdateTextEditorPlacement();
            _textEditor.Focus();
            Keyboard.Focus(_textEditor);
            _textEditor.CaretIndex = 0;
            AppLog.Info("History", $"编辑窗口文字输入开始：图像坐标={point}（Enter 提交、Esc 取消）");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(StringsUi.Format("S.History.EditCannotType", ex.Message), isError: true);
        }
    }

    private void BeginTextEdit(TextAnnotation annotation)
    {
        _editingText = annotation;
        _editingTextOriginal = annotation.Text;
        _textEditor.Text = annotation.Text;
        _session.UpdateTextPreview(annotation.Text);
        _textEditor.FontSize = Math.Max(8, annotation.FontSize * _zoom * 96.0 / _dpi);
        _textEditorHost.Visibility = Visibility.Visible;
        UpdateTextEditorPlacement(annotation.Position);
        _textEditor.Focus();
        Keyboard.Focus(_textEditor);
        _textEditor.CaretIndex = _textEditor.Text.Length;
        AppLog.Info(
            "History",
            $"编辑窗口重新编辑文字：图像坐标={annotation.Position} 原内容=\"{Preview(annotation.Text)}\"");
    }

    private void UpdateTextEditorPlacement(PixelPoint? explicitPoint = null)
    {
        var point = explicitPoint
                     ?? _session.PendingText?.Position
                     ?? (_editingText?.Position);
        if (point is not { } position)
        {
            return;
        }

        var dip = ToDip(position.X, position.Y);
        var available = Math.Max(120, (_background.PixelWidth * _zoom) - (dip.X - _origin.X) - 8);
        _textEditorHost.Width = Math.Min(available, 420);
        Canvas.SetLeft(_textEditorHost, dip.X);
        Canvas.SetTop(_textEditorHost, dip.Y);
    }

    private void CommitText(string source)
    {
        var text = _textEditor.Text;
        if (_editingText is { } editing)
        {
            _editingText = null;
            editing.Text = _editingTextOriginal;
            var description = _session.Editing?.SetSelectedText(text);
            if (description is not null)
            {
                MarkDirty();
            }

            AppLog.Info("History", $"编辑窗口文字修改提交（{source}）：记录={description ?? "无变化"}");
        }
        else if (_session.IsTextEditing)
        {
            var committed = _session.CommitText(text);
            if (committed)
            {
                MarkDirty();
            }

            AppLog.Info("History", $"编辑窗口文字提交（{source}）：长度={text.Length} 已提交={committed} 文档标注数={_session.Count}");
        }

        _textEditorHost.Visibility = Visibility.Collapsed;
        _textEditor.Text = string.Empty;
        _scroll.Focus();
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
    }

    private void CancelText(string source)
    {
        if (_editingText is { } editing)
        {
            editing.Text = _editingTextOriginal;
            _editingText = null;
        }

        _session.CancelText();
        _textEditorHost.Visibility = Visibility.Collapsed;
        _textEditor.Text = string.Empty;
        _scroll.Focus();
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
        AppLog.Info("History", $"编辑窗口文字输入取消（{source}）：不产生标注");
    }

    private bool IsInsideTextEditor(Point dip)
    {
        if (_textEditorHost.Visibility != Visibility.Visible)
        {
            return false;
        }

        var left = Canvas.GetLeft(_textEditorHost);
        var top = Canvas.GetTop(_textEditorHost);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return false;
        }

        var width = _textEditorHost.ActualWidth > 0 ? _textEditorHost.ActualWidth : _textEditorHost.Width;
        var height = _textEditorHost.ActualHeight > 0 ? _textEditorHost.ActualHeight : 30;
        return dip.X >= left && dip.X <= left + width && dip.Y >= top && dip.Y <= top + height;
    }

    // --- 工具 / 撤销 / 删除 ---

    private void SelectTool(AnnotationTool? tool)
    {
        if (_session.IsTextEditing || _editingText is not null)
        {
            CommitText("切换工具");
        }

        if (_session.IsDrafting)
        {
            _session.CancelDraft();
        }

        try
        {
            _session.SetActiveTool(tool);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(StringsUi.Format("S.History.EditToolSwitchFailed", ex.Message), isError: true);
            return;
        }

        _activeTool = tool;
        foreach (var (candidate, element) in _toolButtons)
        {
            var selected = candidate == tool;
            element.Background = selected ? HistoryUi.SelectedBorder : HistoryUi.CardBackground;
            element.Foreground = selected ? HistoryUi.Frozen(Colors.White) : HistoryUi.Foreground;
            element.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        UpdateCursor(null);
        AppLog.Info("History", $"编辑窗口切换工具：{(tool?.ToString() ?? "选择")} 文档标注数={_session.Count}");
    }

    private void Undo(string source)
    {
        var description = _session.History.NextUndoDescription ?? "无";
        var done = _session.Undo();
        AppLog.Info("History", $"编辑窗口撤销（{source}）：{description} 成功={done} 文档标注数={_session.Count}");
        if (!done)
        {
            SetStatus(StringsUi.Get("S.History.EditNothingToUndo"));
            return;
        }

        MarkDirty();
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
    }

    private void Redo(string source)
    {
        var description = _session.History.NextRedoDescription ?? "无";
        var done = _session.Redo();
        AppLog.Info("History", $"编辑窗口重做（{source}）：{description} 成功={done} 文档标注数={_session.Count}");
        if (!done)
        {
            SetStatus(StringsUi.Get("S.History.EditNothingToRedo"));
            return;
        }

        MarkDirty();
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
    }

    private void DeleteSelected(string source)
    {
        var description = _session.DeleteSelected();
        AppLog.Info("History", $"编辑窗口删除标注（{source}）：记录={description ?? "无"} 文档标注数={_session.Count}");
        if (description is null)
        {
            SetStatus(StringsUi.Get("S.History.EditSelectFirst"));
            return;
        }

        MarkDirty();
        _layer.InvalidateVisual();
        UpdateEditorVisuals();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_session.IsTextEditing || _editingText is not null)
        {
            return; // 输入框自己处理 Enter/Esc
        }

        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (isCtrl && e.Key is Key.Z or Key.Y)
        {
            e.Handled = true;
            if (e.Key == Key.Z)
            {
                Undo("Ctrl+Z");
            }
            else
            {
                Redo("Ctrl+Y");
            }

            return;
        }

        if (isCtrl && e.Key == Key.S)
        {
            e.Handled = true;
            Save("Ctrl+S");
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                if (_session.HasSelection)
                {
                    e.Handled = true;
                    DeleteSelected(e.Key.ToString());
                }

                break;

            case Key.Escape:
                e.Handled = true;
                if (_session.IsDrafting)
                {
                    _session.CancelDraft();
                    _pointerDrawing = false;
                    _layer.InvalidateVisual();
                    return;
                }

                if (_session.Editing is { IsDragging: true } dragging && dragging.CancelDrag())
                {
                    _pointerEditing = false;
                    _layer.InvalidateVisual();
                    UpdateEditorVisuals();
                    return;
                }

                if (_session.HasSelection)
                {
                    _session.ClearSelection();
                    UpdateEditorVisuals();
                    return;
                }

                Cancel(confirm: true);
                break;
        }
    }

    /// <summary>把选中标注的虚线框与手柄画到画布 DIP。</summary>
    private void UpdateEditorVisuals()
    {
        var editor = _session.Editing;
        if (editor is null || !editor.HasSelection)
        {
            _editorOutline.Visibility = Visibility.Collapsed;
            foreach (var (_, element) in _editorHandles)
            {
                element.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var bounds = editor.SelectionBounds;
        if (bounds.IsEmpty)
        {
            bounds = new PixelRect(bounds.Left, bounds.Top, Math.Max(2, bounds.Width), Math.Max(2, bounds.Height));
        }

        var topLeft = ToDip(bounds.Left, bounds.Top);
        var bottomRight = ToDip(bounds.Right, bounds.Bottom);
        _editorOutline.Data = new RectangleGeometry(
            new Rect(topLeft.X, topLeft.Y, Math.Max(1, bottomRight.X - topLeft.X), Math.Max(1, bottomRight.Y - topLeft.Y)));
        _editorOutline.Visibility = Visibility.Visible;

        var visible = new HashSet<AnnotationHandle>();
        foreach (var (handle, rect) in editor.GetHandleRects())
        {
            var element = _editorHandles.FirstOrDefault(pair => pair.Handle == handle).Element;
            if (element is null)
            {
                continue;
            }

            var a = ToDip(rect.Left, rect.Top);
            var b = ToDip(rect.Right, rect.Bottom);
            Canvas.SetLeft(element, a.X);
            Canvas.SetTop(element, a.Y);
            element.Width = EditorHandleSize;
            element.Height = EditorHandleSize;
            element.Visibility = Visibility.Visible;
            visible.Add(handle);
        }

        foreach (var (handle, element) in _editorHandles)
        {
            if (!visible.Contains(handle))
            {
                element.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void MarkDirty()
    {
        if (_dirty)
        {
            return;
        }

        _dirty = true;
        SetStatus(StringsUi.Format("S.History.EditMarkedDirty", _session.Count));
    }

    // --- 保存并覆盖（T28 核心） ---

    /// <summary>
    /// 保存并覆盖：合成新图 → （首次時解释覆盖语义）→ 更新同一条记录。
    /// </summary>
    /// <param name="source">触发来源（日志用）。</param>
    internal void Save(string source)
    {
        if (_saved)
        {
            return;
        }

        if (_session.IsTextEditing || _editingText is not null)
        {
            CommitText("保存前提交文字");
        }

        if (_session.IsDrafting)
        {
            _session.CancelDraft();
        }

        _session.ClearSelection();

        // 1) 记录还在吗？（编辑期间可能被别的窗口/图库删掉）
        HistoryRecord? latest;
        try
        {
            latest = _store.Repository.GetById(_record.Id);
        }
        catch (Exception ex)
        {
            AppLog.Error("History", $"编辑窗口保存前读取记录失败：Id={_record.Id}", ex);
            SetStatus(StringsUi.Format("S.History.EditRecordMissing", ex.Message), isError: true);
            return;
        }

        if (latest is null)
        {
            var message = HistoryOverwritePlan.DescribeDeleted(_record);
            AppLog.Warn("History", $"编辑窗口保存失败（{source}）：记录已被删除 Id={_record.Id} 名称={_record.Name}，不写任何文件");
            SetStatus(message, isError: true);
            MessageBox.Show(this, message, SaveButtonText, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 2) 首次使用：解释覆盖不保留旧版本（取消就不保存）。提示只在本次运行的第一次保存时出现。
        if (!_overwriteExplained)
        {
            AppLog.Info("History", "首次使用覆盖编辑，显示覆盖语义说明");
            var answer = MessageBox.Show(
                this,
                HistoryOverwritePlan.DescribeOverwriteExplanation(latest.Name),
                SaveButtonText,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.OK);

            if (answer != MessageBoxResult.OK)
            {
                AppLog.Info("History", "覆盖语义说明被取消，本次不保存（留在编辑窗口）");
                SetStatus(StringsUi.Get("S.History.EditSaveCancelled"));
                return;
            }

            _overwriteExplained = true;
            OnExplanationShown?.Invoke();
        }

        // 3) 合成新图：背景 = 记录当前图片（旧标注已在像素里），再叠加本次追加的标注。
        BitmapSource composed;
        byte[] imageBytes;
        byte[]? thumbnailBytes;
        try
        {
            composed = Compose();
            imageBytes = HistoryThumbnail.EncodePng(composed);
            thumbnailBytes = HistoryThumbnail.CreatePngBytes(composed);
        }
        catch (Exception ex)
        {
            AppLog.Error("History", "编辑窗口合成新图失败（原图未改动）", ex);
            SetStatus(StringsUi.Format("S.History.EditComposeFailed", ex.Message), isError: true);
            return;
        }

        // 4) 覆盖写入同一条记录（保留 Id/名称/创建时间/收藏；先写 tmp 再替换）。
        var countBefore = SafeCount();
        try
        {
            var merged = HistoryOverwritePlan.MergeLatest(_record, latest);
            var updated = _store.Repository.UpdateImage(
                merged.Id,
                imageBytes,
                composed.PixelWidth,
                composed.PixelHeight,
                thumbnailBytes);

            if (updated is null)
            {
                var message = HistoryOverwritePlan.DescribeDeleted(_record);
                AppLog.Warn("History", $"编辑窗口保存失败：覆盖时记录已不存在 Id={_record.Id}");
                SetStatus(message, isError: true);
                MessageBox.Show(this, message, SaveButtonText, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SavedRecord = updated;
            _saved = true;

            var countAfter = SafeCount();
            AppLog.Info(
                "History",
                $"编辑窗口保存并覆盖成功：名称={updated.Name} Id={updated.Id} " +
                $"新尺寸={updated.Width}x{updated.Height} 图片字节={imageBytes.Length} " +
                $"缩略图字节={thumbnailBytes?.Length ?? 0} 追加标注={_session.Count} " +
                $"创建时间保留={HistoryRecord.FormatUtc(updated.CreatedAtUtc)} " +
                $"收藏保留={updated.IsFavorite} 修改时间={HistoryRecord.FormatUtc(updated.UpdatedAtUtc)} " +
                $"编辑期间被改名/改收藏={HistoryOverwritePlan.WasModifiedElsewhere(_record, latest)} " +
                $"历史条数 {countBefore} → {countAfter}（不新增记录） 图片文件={updated.RelativeImagePath}");

            SetStatus(HistoryOverwritePlan.DescribeSaved(updated, _session.Count));
            Close();
        }
        catch (Exception ex)
        {
            // 覆盖先写 tmp 再替换，失败时原图还在（PLAN：保存失败不先销毁原图）。
            AppLog.Error("History", $"编辑窗口保存并覆盖失败（原图未改动）：Id={_record.Id}", ex);
            SetStatus(StringsUi.Format("S.History.EditSaveFailed", ex.Message), isError: true);
            MessageBox.Show(
                this,
                StringsUi.Format("S.History.EditSaveFailedBody", ex.Message),
                SaveButtonText,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private int SafeCount()
    {
        try
        {
            return _store.Repository.Count();
        }
        catch (Exception ex)
        {
            AppLog.Warn("History", $"统计历史条数失败（忽略）：{ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// 合成新图：以记录当前图片为背景，把本次追加的标注画上去（与截图导出同一套渲染器）。
    /// 尺寸必须等于原图片尺寸（追加标注不改变图片尺寸）。
    /// </summary>
    private BitmapSource Compose()
    {
        if (_session.Count == 0)
        {
            // 没有追加任何标注：直接把原图（已冻结位图）编码回去，像素不变。
            return _background;
        }

        var annotations = _session.Document.ToList();
        var rect = new PixelRect(0, 0, _background.PixelWidth, _background.PixelHeight);
        var composed = AnnotationExportRenderer.Composite(_background, annotations, rect, _dpi);

        if (composed.PixelWidth != _background.PixelWidth || composed.PixelHeight != _background.PixelHeight)
        {
            throw new InvalidOperationException(
                $"合成尺寸 {composed.PixelWidth}x{composed.PixelHeight} 与原图 " +
                $"{_background.PixelWidth}x{_background.PixelHeight} 不一致。");
        }

        return composed;
    }

    private void Cancel(bool confirm)
    {
        if (_saved)
        {
            Close();
            return;
        }

        if (_dirty && confirm)
        {
            var answer = MessageBox.Show(
                this,
                StringsUi.Format("S.History.EditCancelConfirm", _session.Count),
                StringsUi.Get("S.History.EditCancelDialogTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (answer != MessageBoxResult.OK)
            {
                SetStatus(StringsUi.Get("S.History.EditStayed"));
                return;
            }
        }

        AppLog.Info(
            "History",
            $"编辑窗口取消编辑：名称={_record.Name} Id={_record.Id} 追加标注={_session.Count} 个已丢弃，" +
            "原图未修改");
        Close();
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.Foreground = isError ? HistoryUi.ErrorForeground : HistoryUi.SecondaryForeground;
        AutomationProperties.SetName(_status, message);
        AppLog.Info("History", $"编辑窗口状态：{message}");
    }

    private static string DescribeRecord(HistoryRecord record, BitmapSource image) =>
        StringsUi.Format(
            "S.History.EditRecordInfo",
            record.Name,
            image.PixelWidth,
            image.PixelHeight,
            record.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            HistoryCard.DescribeSource(record.Source)) +
        (record.IsFavorite ? StringsUi.Get("S.History.EditFavoritedBadge") : string.Empty);

    private static string Preview(string text)
    {
        var single = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        return single.Length <= 40 ? single : single[..40] + "…";
    }
}
