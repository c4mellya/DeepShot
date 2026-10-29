using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepShot.Core;
using DeepShot.Core.Settings;
using DeepShot.Core.Shortcuts;
using DeepShot.Platform;
using DeepShot.Ui;
using DeepShot.Updates;

namespace DeepShot.Settings;

/// <summary>
/// 设置页（UserControl）：单列滚动页，版式对齐 Windows 11 照片/设置应用的设置页。
/// </summary>
/// <remarks>
/// <para>
/// 导航由宿主外壳的左侧窗格负责，本页不再有侧栏。内容直接铺在窗口底色上：
/// 页标题（28 Semibold）→ 操作结果条（只在有结果时展开）→ 各分组依次排列
/// （分组标题 14 Semibold，组间 28）→ 每个设置一张卡片
/// （<c>DeepShot.Card</c>，内边距 16,12；标题 14 主色 + 可选说明 12 次要色 + 右侧控件），卡片间距 4。
/// </para>
/// <para>
/// <b>即改即存</b>：每个控件一改就写 settings.json 并抛 <see cref="ChangeRequested"/>，
/// 由 <c>App</c> 负责真正应用（重新注册热键、切品牌/明暗、切语言、同步注册表）。
/// 写盘失败时界面回滚到磁盘上的那份值并提示；注册失败（被别的程序占用）同样把设置改回去，
/// 用户看到的键必须与真正生效的一致。
/// </para>
/// <para>
/// <b>页面生命周期由宿主（MainWindow）驱动</b>：<see cref="Enter"/>（每次切到本页）、
/// <see cref="Leave"/>（切走）、<see cref="ClosePage"/>（窗口关闭）。
/// 页面本身不是自绘窗口，不使用 <c>Window</c> 专属属性/事件。
/// </para>
/// <para>
/// <b>快捷键录入</b>：输入框捕获按键（<see cref="ShortcutBox"/>，本文件内的私有嵌套类，
/// 不依赖即将删除的旧设置窗口）。录入期间 <c>App</c> 暂停全局热键；焦点进入/离开、
/// 页面离开、页面关闭都会成对调用 <c>BeginShortcutCapture</c>/<c>EndShortcutCapture</c>。
/// 窗口失活由 MainWindow 负责。
/// </para>
/// </remarks>
internal sealed class SettingsPage : UserControl
{
    /// <summary>卡片内宽窄于此宽度时，设置行的控件换到文字下方（说明仍自动换行）。</summary>
    private const double NarrowContentWidth = 480;

    /// <summary>行内控件统一高度。</summary>
    private const double ControlHeight = Tokens.Height.Control;

    /// <summary>分组标题上方间距（上一张卡片已带 4 的下边距，合计 28）。</summary>
    private const double SectionGap = 24;

    /// <summary>说明文字色。</summary>
    private const string DescriptionKey = ThemeManager.BrushKeys.SecondaryForeground;

    /// <summary>成功提示停留时间（失败提示保留到下一次操作）。</summary>
    private static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(2);

    private readonly SettingsStore _store;

    private readonly ShortcutBox _screenshotBox;
    private readonly ShortcutBox _quickScreenshotBox;
    private readonly ShortcutBox _pinBox;
    private readonly ShortcutBox _quickViewBox;
    private readonly SegmentedControl<CaptureSelectionMode> _captureMode;
    private readonly SegmentedControl<AppTheme> _theme;
    private readonly ComboBox _language;
    private readonly CheckBox _startup;
    private readonly TextBox _retentionDays;

    private readonly TextBlock _heading;
    private readonly TextBlock _status;
    private readonly TextBlock _languageSummary;
    private readonly TextBlock _modeSummary;

    private TextBlock _dataPath = null!;
    private TextBlock _dataCounts = null!;
    private TextBlock _retentionHint = null!;
    private TextBlock _retentionRule = null!;
    private TextBlock _retentionError = null!;
    private TextBlock _updateVersion = null!;
    private TextBlock _updateChannel = null!;
    private Button _openDataButton = null!;
    private Button _checkUpdateButton = null!;
    private ScrollViewer _scroll = null!;

    private readonly List<Button> _clearShortcutButtons = new();

    private readonly List<(TextBlock Block, Func<string> Text)> _localized = new();
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>结果条外层（裁切 + 高度动画），内层是 InfoBar 样式的卡片。</summary>
    private readonly Border _statusHost;
    private readonly Border _statusBar;
    private readonly Viewbox _statusSuccessIcon;
    private readonly Viewbox _statusErrorIcon;
    private bool _statusShown;

    private bool _applying;
    private bool _closed;

    /// <summary>创建设置页。</summary>
    /// <param name="store">设置存储器（含当前生效的设置）。</param>
    internal SettingsPage(SettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "settings-page");
        AutomationProperties.SetName(this, StringsUi.Get("S.Settings.WindowName"));

        var settings = _store.Current;

        // 页标题、状态行、"当前生效"说明是长期文字块：语言/主题切换时就地改字。
        _heading = NewText(string.Empty, 28, AppFonts.Semibold, UiKit.Keys.Foreground, lineHeight: 36);
        AutomationProperties.SetAutomationId(_heading, "settings-heading");

        // 结果条只在有结果时展开：成功提示 2 秒后收起，失败提示保留到下一次操作。
        _status = NewText(string.Empty, 14, AppFonts.Regular, UiKit.Keys.Foreground, wrap: true, lineHeight: 20);
        AutomationProperties.SetAutomationId(_status, "settings-status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _status.Visibility = Visibility.Collapsed;
        _statusSuccessIcon = AppIcons.Create(AppIcons.Check, 16, UiKit.Keys.Success, 2.2);
        _statusErrorIcon = AppIcons.Create(AppIcons.Alert, 16, UiKit.Keys.Error, 2.0);
        (_statusHost, _statusBar) = BuildStatusBar();
        _statusTimer = new DispatcherTimer { Interval = StatusLifetime };
        _statusTimer.Tick += (_, _) => ClearStatus();

        _languageSummary = NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18);
        _modeSummary = NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18);

        // 快捷键输入框（即改即存；焦点进入/离开时暂停/恢复全局热键）。
        _screenshotBox = new ShortcutBox("settings-shortcut-screenshot");
        _screenshotBox.SetValue(settings.ScreenshotShortcut);
        _screenshotBox.Committed += OnScreenshotShortcutCommitted;

        _quickScreenshotBox = new ShortcutBox("settings-shortcut-quick-screenshot");
        _quickScreenshotBox.SetValue(settings.QuickScreenshotShortcut);
        _quickScreenshotBox.Committed += OnQuickScreenshotShortcutCommitted;

        _pinBox = new ShortcutBox("settings-shortcut-pin");
        _pinBox.SetValue(settings.PinShortcut);
        _pinBox.Committed += OnPinShortcutCommitted;

        _quickViewBox = new ShortcutBox("settings-shortcut-quick-view");
        _quickViewBox.SetValue(settings.QuickViewShortcut);
        _quickViewBox.Committed += OnQuickViewShortcutCommitted;

        _captureMode = new SegmentedControl<CaptureSelectionMode>();
        _captureMode.Add(CaptureSelectionMode.Rectangle, "S.Settings.CaptureMode.Rectangle", "settings-capture-mode-rectangle");
        _captureMode.Add(CaptureSelectionMode.Window, "S.Settings.CaptureMode.Window", "settings-capture-mode-window");
        _captureMode.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(_captureMode, "settings-capture-mode");
        _captureMode.UserSelected += OnCaptureModeChosen;

        _theme = new SegmentedControl<AppTheme>();
        _theme.Add(AppTheme.System, "S.Settings.Theme.System", "settings-theme-mode-system");
        _theme.Add(AppTheme.Light, "S.Settings.Theme.Light", "settings-theme-mode-light");
        _theme.Add(AppTheme.Dark, "S.Settings.Theme.Dark", "settings-theme-mode-dark");
        _theme.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetAutomationId(_theme, "settings-theme-mode");
        _theme.UserSelected += OnThemeChosen;

        _language = new ComboBox
        {
            MinWidth = 190,
            Height = ControlHeight,
            FontSize = Tokens.Text.Body,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_language, "settings-language");
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.System, "S.Settings.Language.System"));
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.ZhHans, "S.Settings.Language.ZhHans"));
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.English, "S.Settings.Language.English"));
        _language.SelectionChanged += (_, _) => OnLanguageSelectionChanged();

        // 全局 CheckBox 样式就是 Fluent 开关，与行高居中对齐。
        _startup = new CheckBox
        {
            IsChecked = settings.LaunchAtStartup,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(_startup, "settings-startup");
        AutomationProperties.SetName(_startup, StringsUi.Get("S.Settings.Startup.Enable"));
        _startup.Click += (_, _) => OnStartupToggled();

        _retentionDays = new TextBox
        {
            Width = 88,
            Height = ControlHeight,
            FontSize = Tokens.Text.Body,
            Padding = new Thickness(10, 0, 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            MaxLength = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(_retentionDays, "settings-retention-days");
        _retentionDays.LostKeyboardFocus += (_, _) => CommitRetentionDays();
        _retentionDays.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitRetentionDays();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SyncRetentionControls();
                e.Handled = true;
            }
        };

        BuildShell();

        _dataTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _dataTimer.Tick += (_, _) =>
        {
            if (!_closed && IsVisible)
            {
                RefreshDataSummary();
            }
        };

        // 主题/语言变化时（本页自己触发的，或跟随系统变化）刷新说明文字与控件文字。
        ThemeManager.Changed += OnThemeChanged;
        StringsUi.Changed += OnStringsChanged;

        UpdateHeading();
        SyncControlsFromSettings();
        RefreshEffectiveLabels();
        RefreshDataSummary();

        AppLog.Info(
            "Settings",
            $"设置页已创建：{_store.Current} 语言={StringsUi.Current} 品牌={ThemeManager.SelectedBrand} " +
            $"主题={ThemeManager.SelectedTheme}（生效 {ThemeManager.EffectiveTheme}） 设置文件={_store.FilePath}");
    }

    /// <summary>用户改了某项设置（<see cref="AppSettings"/> 里可能有多项变化后一起提交）。</summary>
    /// <param name="previous">改动前的设置（App 用来判断哪些部分需要重新应用）。</param>
    /// <param name="current">改动后的设置。</param>
    internal Action<AppSettings, AppSettings>? ChangeRequested { get; set; }

    /// <summary>
    /// 注册快捷键失败时由 App 回调（注册成功返回 null）。设置页据此回滚并提示。
    /// </summary>
    internal Func<AppSettings, string?>? ApplyShortcuts { get; set; }

    /// <summary>设置里的"检查更新"按钮（由 App 打开检查更新对话框；与托盘菜单同一个实现）。</summary>
    internal Action? CheckUpdates { get; set; }

    /// <summary>返回图库（导航已移到宿主外壳，本页不再使用；保留供宿主赋值）。</summary>
    internal Action? BackToGallery { get; set; }

    /// <summary>本次页面里最后一次操作的结果（验证脚本读日志核对）。</summary>
    internal string StatusText => _status.Text;

    /// <summary>每次切到本页：刷新数据统计、快捷键展示与状态。</summary>
    internal void Enter()
    {
        if (_closed)
        {
            return;
        }

        SyncControlsFromSettings();
        RefreshEffectiveLabels();
        RefreshDataSummary();
        StartDataTimer();
        AppLog.Info("Settings", "设置页已进入（已刷新统计与快捷键展示）");
    }

    /// <summary>切走本页：结束快捷键录入、提交保留天数输入、停掉统计定时器。</summary>
    internal void Leave()
    {
        if (_closed)
        {
            return;
        }

        (Application.Current as App)?.EndShortcutCapture();
        CommitRetentionDays();
        StopDataTimer();
        AppLog.Info("Settings", "设置页已离开");
    }

    /// <summary>窗口关闭：退订事件、结束快捷键录入（幂等）。</summary>
    internal void ClosePage()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        (Application.Current as App)?.EndShortcutCapture();
        StopDataTimer();
        _statusTimer.Stop();
        ThemeManager.Changed -= OnThemeChanged;
        StringsUi.Changed -= OnStringsChanged;
        AppLog.Info("Settings", "设置页已关闭（已退订主题/语言变化）");
    }

    // --- 外壳：单列滚动页 ---

    private void BuildShell()
    {
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        _scroll.SetResourceReference(BackgroundProperty, UiKit.Keys.Window);
        UiKit.QuietScrollCorner(_scroll);

        // 内容列最宽 1000、靠左（星号列封顶后余下空间留空，而不是居中）；左右 36、顶部 24。
        var column = new Grid();
        column.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 1000 + 72 });
        column.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var content = new StackPanel { Margin = new Thickness(36, 24, 36, 40) };
        column.Children.Add(content);

        _heading.Margin = new Thickness(0, 0, 0, 20);
        content.Children.Add(_heading);
        content.Children.Add(_statusHost);

        AddGeneralSection(content);
        AddShortcutsSection(content);
        AddAppearanceSection(content);
        AddDataSection(content);
        AddAboutSection(content);

        _scroll.Content = column;
        Content = _scroll;
    }

    /// <summary>
    /// 结果条（InfoBar 样式）：Surface 底 + 1px 描边、圆角 4、16px 成功/错误图标 + 14 正文。
    /// 外层裁切容器做高度/透明度动画，展开收起不让下方内容跳动。
    /// </summary>
    private (Border Host, Border Bar) BuildStatusBar()
    {
        var icons = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 12, 0) };
        icons.Children.Add(_statusSuccessIcon);
        icons.Children.Add(_statusErrorIcon);

        _status.Visibility = Visibility.Visible;
        _status.VerticalAlignment = VerticalAlignment.Center;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_status, 1);
        row.Children.Add(icons);
        row.Children.Add(_status);

        var bar = new Border
        {
            Child = row,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.Radius.Control),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 20),
        };
        bar.SetResourceReference(Border.BackgroundProperty, UiKit.Keys.Surface);
        bar.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Border);

        var host = new Border
        {
            Child = bar,
            ClipToBounds = true,
            Height = 0,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
        };
        return (host, bar);
    }

    // --- 分组内容 ---

    /// <summary>通用：截图方式、开机启动、语言。</summary>
    private void AddGeneralSection(StackPanel page)
    {
        AddGroup(
            page,
            "S.Settings.Pane.General.Title",
            "settings-nav-general",
            Row(RowLabel("S.Settings.CaptureMode.Label", "S.Settings.CaptureMode.Hint"), _captureMode),
            Row(RowLabel("S.Settings.Startup.Enable", "S.Settings.Startup.Detail"), _startup),
            Row(RowTexts(Title("S.Settings.Section.Language"), _languageSummary), _language));
    }

    /// <summary>快捷键：4 个录入框，每个都带「清除」；清空后不注册该热键。</summary>
    private void AddShortcutsSection(StackPanel page)
    {
        AddGroup(
            page,
            "S.Settings.Pane.Shortcuts.Title",
            "settings-nav-shortcuts",
            Row(RowLabel("S.Settings.Shortcut.Screenshot", null),
                ShortcutControl(_screenshotBox, "settings-clear-screenshot-shortcut",
                    () => _store.Current.WithScreenshotShortcut(string.Empty))),
            Row(RowLabel("S.Settings.Shortcut.QuickScreenshot", "S.Settings.Shortcut.QuickScreenshotHint"),
                ShortcutControl(_quickScreenshotBox, "settings-clear-quick-screenshot-shortcut",
                    () => _store.Current.WithQuickScreenshotShortcut(string.Empty))),
            Row(RowLabel("S.Settings.Shortcut.Pin", null),
                ShortcutControl(_pinBox, "settings-clear-pin-shortcut",
                    () => _store.Current.WithPinShortcut(string.Empty))),
            Row(RowLabel("S.QuickView.Shortcut", null),
                ShortcutControl(_quickViewBox, "settings-clear-quick-view-shortcut",
                    () => _store.Current.WithQuickViewShortcut(string.Empty))));

        var note = Loc(
            NewText(StringsUi.Get("S.Settings.Shortcut.Hint"), 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18),
            "S.Settings.Shortcut.Hint");
        note.Margin = new Thickness(0, Tokens.Space.S, 0, 0);
        page.Children.Add(note);
    }

    /// <summary>快捷键输入框 + 清除按钮。</summary>
    private FrameworkElement ShortcutControl(ShortcutBox box, string automationId, Func<AppSettings> cleared)
    {
        var clearText = StringsUi.Get("S.QuickView.ClearShortcut");
        var clear = UiKit.IconButton(
            AppIcons.Close,
            clearText,
            clearText,
            () => ClearShortcut(cleared),
            size: ControlHeight,
            iconKey: UiKit.Keys.Secondary,
            iconSize: 14);
        AutomationProperties.SetAutomationId(clear, automationId);
        clear.VerticalAlignment = VerticalAlignment.Center;
        clear.Margin = new Thickness(0, 0, Tokens.Space.XS, 0);
        _clearShortcutButtons.Add(clear);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        panel.Children.Add(clear);
        panel.Children.Add(box);
        return panel;
    }

    private void ClearShortcut(Func<AppSettings> cleared) =>
        ApplyShortcutChange(cleared(), StringsUi.Get("S.Settings.Shortcut.Cleared"), string.Empty);

    /// <summary>外观：明暗模式（只写设置，由 App 应用主题）。</summary>
    private void AddAppearanceSection(StackPanel page)
    {
        AddGroup(
            page,
            "S.Settings.Pane.Appearance.Title",
            "settings-nav-appearance",
            Row(RowTexts(Title("S.Settings.ThemeMode"), _modeSummary), _theme));
    }

    /// <summary>数据：目录路径 + 记录数 + 打开目录；历史保留天数 + 规则说明 + 校验错误。</summary>
    private void AddDataSection(StackPanel page)
    {
        var dataTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dataTexts.Children.Add(Title("S.Settings.Section.Data"));

        _dataPath = NewText(AppPaths.DataRoot, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18);
        _dataPath.Margin = new Thickness(0, 2, 0, 0);
        AutomationProperties.SetAutomationId(_dataPath, "settings-data-path");
        dataTexts.Children.Add(_dataPath);

        _dataCounts = NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18);
        AutomationProperties.SetAutomationId(_dataCounts, "settings-data-counts");
        dataTexts.Children.Add(_dataCounts);

        var openText = StringsUi.Get("S.Settings.Data.Open");
        _openDataButton = UiKit.Button(openText, openText, OpenDataFolder, ButtonKind.Secondary, "settings-open-data");
        _openDataButton.Height = ControlHeight;
        _openDataButton.MinWidth = 96;
        _openDataButton.HorizontalAlignment = HorizontalAlignment.Right;

        var retentionTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        retentionTexts.Children.Add(Title("S.Settings.Retention.Days"));

        _retentionHint = Loc(
            NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18),
            () => StringsUi.Format(
                "S.Settings.Retention.Hint",
                AppSettings.MinHistoryRetentionDays,
                AppSettings.MaxHistoryRetentionDays));
        _retentionHint.Text = StringsUi.Format(
            "S.Settings.Retention.Hint",
            AppSettings.MinHistoryRetentionDays,
            AppSettings.MaxHistoryRetentionDays);
        _retentionHint.Margin = new Thickness(0, 2, 0, 0);
        retentionTexts.Children.Add(_retentionHint);

        _retentionRule = Loc(
            NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18),
            () => DescribeRetentionRule(_store.Current.HistoryRetentionDays));
        _retentionRule.Text = DescribeRetentionRule(_store.Current.HistoryRetentionDays);
        AutomationProperties.SetAutomationId(_retentionRule, "settings-retention-rule");
        retentionTexts.Children.Add(_retentionRule);

        _retentionError = NewText(string.Empty, 12, AppFonts.Regular, ThemeManager.BrushKeys.Error, wrap: true, lineHeight: 18);
        _retentionError.Margin = new Thickness(0, 4, 0, 0);
        _retentionError.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_retentionError, "settings-retention-error");
        AutomationProperties.SetLiveSetting(_retentionError, AutomationLiveSetting.Polite);
        retentionTexts.Children.Add(_retentionError);

        AddGroup(
            page,
            "S.Settings.Pane.Data.Title",
            "settings-nav-data",
            Row(dataTexts, _openDataButton),
            Row(retentionTexts, _retentionDays));
    }

    /// <summary>
    /// 关于：一张卡片 —— 32px 应用图标 + 「DeepShot」+ 版本（次要色），右侧检查更新；
    /// 未配置更新渠道时卡片里多一行说明。
    /// </summary>
    private void AddAboutSection(StackPanel page)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Title("S.App.Name"));

        _updateVersion = Loc(
            NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, lineHeight: 18),
            () => StringsUi.Format("S.Settings.Version", UpdateChecker.GetCurrentVersion()));
        _updateVersion.Text = StringsUi.Format("S.Settings.Version", UpdateChecker.GetCurrentVersion());
        _updateVersion.Margin = new Thickness(0, 2, 0, 0);
        AutomationProperties.SetAutomationId(_updateVersion, "settings-version");
        texts.Children.Add(_updateVersion);

        _updateChannel = Loc(
            NewText(string.Empty, 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18),
            DescribeChannel);
        _updateChannel.Text = DescribeChannel();
        _updateChannel.Visibility = _updateChannel.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAutomationId(_updateChannel, "settings-update-channel");
        texts.Children.Add(_updateChannel);

        var left = new Grid { VerticalAlignment = VerticalAlignment.Center };
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (AppChrome.AppIcon is { } iconSource)
        {
            var icon = new Image
            {
                Source = iconSource,
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            left.Children.Add(icon);
        }

        Grid.SetColumn(texts, 1);
        left.Children.Add(texts);

        var checkText = StringsUi.Get("S.Settings.Update.Check");
        _checkUpdateButton = UiKit.Button(
            checkText,
            checkText,
            () => CheckUpdates?.Invoke(),
            ButtonKind.Secondary,
            "settings-check-update");
        _checkUpdateButton.Height = ControlHeight;
        _checkUpdateButton.MinWidth = 96;
        _checkUpdateButton.HorizontalAlignment = HorizontalAlignment.Right;

        AddGroup(page, "S.Settings.Pane.About.Title", "settings-nav-about", Row(left, _checkUpdateButton));
    }

    // --- 版式小部件 ---

    /// <summary>
    /// 加一组：分组标题（14 Semibold，上 28 下 8；页内第一组紧接页标题）+ 每行一张卡片（卡片间距 4）。
    /// </summary>
    /// <param name="page">页面内容列。</param>
    /// <param name="groupKey">分组标题文案键。</param>
    /// <param name="automationId">分组标题的自动化 ID（沿用旧侧栏导航项的 ID）。</param>
    /// <param name="rows">各设置行。</param>
    private void AddGroup(StackPanel page, string groupKey, string automationId, params FrameworkElement[] rows)
    {
        // 页标题与结果条之后的第一组不留上边距；其余组上一张卡片已带 4，再补 24 合计 28。
        var first = page.Children.Count <= 2;
        var label = GroupLabel(groupKey);
        label.Margin = new Thickness(0, first ? 0 : SectionGap, 0, Tokens.Space.S);
        AutomationProperties.SetAutomationId(label, automationId);
        page.Children.Add(label);

        foreach (var row in rows)
        {
            var card = new Border
            {
                Child = row,
                Padding = Tokens.CardRowPadding,
                Margin = new Thickness(0, 0, 0, Tokens.Space.XS),
            };
            card.SetResourceReference(StyleProperty, UiKit.StyleKeys.Card);
            page.Children.Add(card);
        }
    }

    /// <summary>分组标题：14 Semibold 主文字色（随语言刷新）。</summary>
    private TextBlock GroupLabel(string key)
    {
        var block = Loc(NewText(StringsUi.Get(key), 14, AppFonts.Semibold, UiKit.Keys.Foreground, lineHeight: 20), key);
        block.Margin = new Thickness(0, 0, 0, Tokens.Space.S);
        return block;
    }

    /// <summary>
    /// 卡片内的设置行：左文右控件；宽度小于 <see cref="NarrowContentWidth"/> 时控件换到文字下方。
    /// </summary>
    /// <param name="left">左侧内容（标题 + 说明）。</param>
    /// <param name="control">右侧控件；为 null 时左侧占满整行。</param>
    private FrameworkElement Row(FrameworkElement left, FrameworkElement? control)
    {
        left.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid { MinHeight = ControlHeight + 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left);

        if (control is null)
        {
            Grid.SetColumnSpan(left, 2);
            return grid;
        }

        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(control);

        bool? stacked = null;
        void Arrange(bool stack)
        {
            if (stacked == stack)
            {
                return;
            }

            stacked = stack;
            Grid.SetRow(control, stack ? 1 : 0);
            Grid.SetColumn(control, stack ? 0 : 1);
            Grid.SetColumnSpan(left, stack ? 2 : 1);
            control.HorizontalAlignment = stack ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            control.Margin = stack
                ? new Thickness(0, Tokens.Space.M, 0, 0)
                : new Thickness(Tokens.Space.L, 0, 0, 0);
        }

        Arrange(false);
        grid.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                Arrange(e.NewSize.Width < NarrowContentWidth);
            }
        };

        return grid;
    }

    /// <summary>左侧文字块：标题 14 + 可选说明 12（次要色，自动换行）。</summary>
    private StackPanel RowLabel(string titleKey, string? descriptionKey)
    {
        var title = Title(titleKey);
        if (descriptionKey is null)
        {
            return RowTexts(title);
        }

        var description = Loc(
            NewText(StringsUi.Get(descriptionKey), 12, AppFonts.Regular, DescriptionKey, wrap: true, lineHeight: 18),
            descriptionKey);
        return RowTexts(title, description);
    }

    /// <summary>行标题文字块（14/20 主色，随语言刷新）。</summary>
    private TextBlock Title(string key)
    {
        var title = NewText(StringsUi.Get(key), 14, AppFonts.Regular, UiKit.Keys.Foreground, wrap: true, lineHeight: 20);
        Loc(title, key);
        return title;
    }

    private static StackPanel RowTexts(TextBlock title, TextBlock? description = null)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(title);
        if (description is not null)
        {
            description.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(description);
        }

        return panel;
    }

    /// <summary>创建一段界面文字（字号/字重/行高/颜色一次给全，颜色走主题资源引用）。</summary>
    private static TextBlock NewText(
        string text,
        double size,
        FontWeight weight,
        string brushKey,
        bool wrap = false,
        double lineHeight = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (lineHeight > 0)
        {
            block.LineHeight = lineHeight;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        block.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);
        return block;
    }

    /// <summary>登记一段随语言刷新的文字（按键名）。</summary>
    private TextBlock Loc(TextBlock block, string key)
    {
        _localized.Add((block, () => StringsUi.Get(key)));
        return block;
    }

    /// <summary>登记一段随语言刷新的文字（动态文案，例如带占位符/当前值）。</summary>
    private TextBlock Loc(TextBlock block, Func<string> text)
    {
        _localized.Add((block, text));
        return block;
    }

    // --- 页标题 ---

    private void UpdateHeading()
    {
        _heading.Text = StringsUi.Get("S.Settings.PageTitle");
        AutomationProperties.SetName(_heading, _heading.Text);
    }

    // --- 提交 ---

    /// <summary>提交一份新设置：先写盘（失败即回滚并提示），成功后再交给 App 应用。</summary>
    private bool Commit(AppSettings next, string successStatus)
    {
        var previous = _store.Current;
        AppSettings saved;
        try
        {
            saved = _store.Save(next);
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings", "保存设置失败（已回滚，磁盘文件未改变）", ex);
            ShowStatus(StringsUi.Format("S.Settings.SaveFailed", ex.Message), error: true);
            SyncControlsFromSettings();
            return false;
        }

        ShowStatus(successStatus, error: false);
        try
        {
            ChangeRequested?.Invoke(previous, saved);
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings", "应用设置时出错（设置已保存）", ex);
            ShowStatus(StringsUi.Format("S.Settings.SaveFailed", ex.Message), error: true);
        }

        return true;
    }

    private void ShowStatus(string message, bool error)
    {
        _statusTimer.Stop();
        _status.Text = message;
        _statusSuccessIcon.Visibility = error ? Visibility.Collapsed : Visibility.Visible;
        _statusErrorIcon.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(_status, message);
        AnimateStatus(show: true);

        // 成功提示 2 秒后自动消失；错误一直留着，直到下一次操作。
        if (!error)
        {
            _statusTimer.Start();
        }

        AppLog.Info("Settings", $"设置页状态：{message}");
    }

    private void ClearStatus()
    {
        _statusTimer.Stop();
        _status.Text = string.Empty;
        AnimateStatus(show: false);
    }

    /// <summary>
    /// 结果条展开/收起：外层高度在 0 与内容高度之间过渡，同时淡入淡出（Normal 时长）；
    /// 已展开时只更新文字，高度跟随内容。
    /// </summary>
    private void AnimateStatus(bool show)
    {
        if (show == _statusShown)
        {
            if (show)
            {
                // 文字变了：从当前高度过渡到新内容高度，避免下方内容跳动。
                AnimateStatusHeight(_statusHost.ActualHeight, MeasureStatusHeight(), keepAuto: true);
            }

            return;
        }

        _statusShown = show;
        if (show)
        {
            _statusHost.Visibility = Visibility.Visible;
            AnimateStatusHeight(0, MeasureStatusHeight(), keepAuto: true);
            UiKit.Fade(_statusHost, OpacityProperty, 1, Tokens.Motion.Normal.TotalMilliseconds);
            return;
        }

        if (!IsLoaded || !IsVisible)
        {
            _statusHost.BeginAnimation(HeightProperty, null);
            _statusHost.BeginAnimation(OpacityProperty, null);
            _statusHost.Height = 0;
            _statusHost.Opacity = 0;
            _statusHost.Visibility = Visibility.Collapsed;
            return;
        }

        AnimateStatusHeight(_statusHost.ActualHeight, 0, keepAuto: false);
        UiKit.Fade(_statusHost, OpacityProperty, 0, Tokens.Motion.Normal.TotalMilliseconds);
    }

    private double MeasureStatusHeight()
    {
        var width = _statusHost.ActualWidth;
        if (width <= 0 && _statusHost.Parent is FrameworkElement parent)
        {
            width = parent.ActualWidth;
        }

        _statusBar.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
        return _statusBar.DesiredSize.Height;
    }

    private void AnimateStatusHeight(double from, double to, bool keepAuto)
    {
        var animation = new DoubleAnimation(from, to, Tokens.Motion.Normal)
        {
            EasingFunction = Tokens.Motion.Ease,
        };
        animation.Completed += (_, _) =>
        {
            if (_statusShown != keepAuto)
            {
                return;   // 动画期间方向已反转，交给新的动画收尾。
            }

            _statusHost.BeginAnimation(HeightProperty, null);
            if (keepAuto)
            {
                _statusHost.Height = double.NaN;   // 展开后高度随内容（换行、改语言）。
            }
            else
            {
                _statusHost.Height = 0;
                _statusHost.Visibility = Visibility.Collapsed;
            }
        };
        _statusHost.BeginAnimation(HeightProperty, animation);
    }

    // --- 快捷键 ---

    private void OnScreenshotShortcutCommitted(ShortcutSpec? shortcut, string error) =>
        CommitShortcut(_screenshotBox, shortcut, error, "S.Settings.Shortcut.Screenshot",
            value => _store.Current.WithScreenshotShortcut(value));

    private void OnQuickScreenshotShortcutCommitted(ShortcutSpec? shortcut, string error) =>
        CommitShortcut(_quickScreenshotBox, shortcut, error, "S.Settings.Shortcut.QuickScreenshot",
            value => _store.Current.WithQuickScreenshotShortcut(value));

    private void OnPinShortcutCommitted(ShortcutSpec? shortcut, string error) =>
        CommitShortcut(_pinBox, shortcut, error, "S.Settings.Shortcut.Pin",
            value => _store.Current.WithPinShortcut(value));

    private void OnQuickViewShortcutCommitted(ShortcutSpec? shortcut, string error) =>
        CommitShortcut(_quickViewBox, shortcut, error, "S.QuickView.Shortcut",
            value => _store.Current.WithQuickViewShortcut(value));

    private void CommitShortcut(ShortcutBox box, ShortcutSpec? shortcut, string error,
        string labelKey, Func<string, AppSettings> update)
    {
        if (error.Length > 0 || shortcut is null)
        {
            var message = error.Length > 0 ? error : StringsUi.Get("S.Settings.Shortcut.NeedModifier");
            ShowStatus(message, error: true);
            box.ShowError(message);
            return;
        }

        var current = _store.Current;
        var entries = new[]
        {
            (_screenshotBox, current.ScreenshotShortcut, "S.Settings.Shortcut.Screenshot"),
            (_quickScreenshotBox, current.QuickScreenshotShortcut, "S.Settings.Shortcut.QuickScreenshot"),
            (_pinBox, current.PinShortcut, "S.Settings.Shortcut.Pin"),
            (_quickViewBox, current.QuickViewShortcut, "S.QuickView.Shortcut"),
        };
        foreach (var (otherBox, text, otherLabelKey) in entries)
        {
            if (ReferenceEquals(box, otherBox) ||
                !ShortcutSpec.TryParse(text, out var other, out _) || !shortcut.ConflictsWith(other))
            {
                continue;
            }

            var message = StringsUi.Format("S.Settings.Shortcut.ConflictLocal",
                StringsUi.Get(otherLabelKey), other.Format());
            ShowStatus(message, error: true);
            box.ShowError(message);
            return;
        }

        ApplyShortcutChange(update(shortcut.Format()), StringsUi.Get(labelKey), shortcut.Format());
    }

    /// <summary>
    /// 保存并（由 App）重新注册。注册失败（被其它程序占用）时回滚设置 —— 用户看到的快捷键
    /// 必须与真正生效的一致，不能"设置里改了但按了没反应"。
    /// </summary>
    private void ApplyShortcutChange(AppSettings next, string which, string formatted)
    {
        var previous = _store.Current;
        var cleared = formatted.Length == 0;
        var message = cleared
            ? StringsUi.Get("S.Settings.Shortcut.Cleared")
            : StringsUi.Format("S.Settings.Shortcut.RegisteredHint", formatted);
        if (!Commit(next, message))
        {
            return;
        }

        var failure = ApplyShortcuts?.Invoke(_store.Current);
        if (string.IsNullOrEmpty(failure))
        {
            SyncControlsFromSettings();
            _screenshotBox.ShowRegistered();
            _quickScreenshotBox.ShowRegistered();
            _pinBox.ShowRegistered();
            _quickViewBox.ShowRegistered();
            AppLog.Info("Settings", cleared ? $"{which}" : $"{which}快捷键已生效：{formatted}");
            return;
        }

        // 注册失败：把设置改回去，再让 App 按旧设置重新注册。
        AppLog.Warn("Settings", $"{which}快捷键 {formatted} 注册失败，回滚：{failure}");
        try
        {
            _store.Save(previous);
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings", "回滚设置失败（磁盘可能仍是被占用的组合）", ex);
        }

        ShowStatus(failure, error: true);
        _screenshotBox.SetValue(previous.ScreenshotShortcut);
        _quickScreenshotBox.SetValue(previous.QuickScreenshotShortcut);
        _pinBox.SetValue(previous.PinShortcut);
        _quickViewBox.SetValue(previous.QuickViewShortcut);
        ApplyShortcuts?.Invoke(_store.Current);
        ChangeRequested?.Invoke(_store.Current, previous);
    }

    // --- 语言 / 品牌 / 明暗 / 截图方式 / 开机启动 ---

    private void OnLanguageSelectionChanged()
    {
        if (_applying || _language.SelectedItem is not ComboItem<AppLanguage> item)
        {
            return;
        }

        var next = _store.Current.WithLanguage(item.Value);
        if (next.Language == _store.Current.Language)
        {
            return;
        }

        Commit(next, StringsUi.Get("S.Settings.Saved"));
    }

    private void OnThemeChosen(AppTheme theme)
    {
        if (_applying || theme == _store.Current.Theme)
        {
            return;
        }

        Commit(_store.Current.WithTheme(theme), StringsUi.Get("S.Settings.Saved"));
    }

    private void OnCaptureModeChosen(CaptureSelectionMode mode)
    {
        if (_applying || mode == _store.Current.CaptureMode)
        {
            return;
        }

        Commit(_store.Current.WithCaptureMode(mode), StringsUi.Get("S.Settings.Saved"));
    }

    private void OnStartupToggled()
    {
        if (_applying)
        {
            return;
        }

        var enabled = _startup.IsChecked == true;
        var next = _store.Current.WithLaunchAtStartup(enabled);

        // 先同步注册表：失败就不保存设置（否则设置说开了、注册表里没有，用户重启才发现）。
        if (!StartupRegistration.Sync(enabled))
        {
            ShowStatus(StringsUi.Get("S.Settings.Startup.Failed"), error: true);
            _applying = true;
            _startup.IsChecked = _store.Current.LaunchAtStartup;
            _applying = false;
            return;
        }

        Commit(next, enabled ? StringsUi.Get("S.Settings.Startup.On") : StringsUi.Get("S.Settings.Startup.Off"));
    }

    // --- 历史保留天数 ---

    private void SyncRetentionControls()
    {
        _retentionDays.Text = _store.Current.HistoryRetentionDays.ToString();
        _retentionRule.Text = DescribeRetentionRule(_store.Current.HistoryRetentionDays);
        _retentionError.Text = string.Empty;
        _retentionError.Visibility = Visibility.Collapsed;
    }

    private void CommitRetentionDays()
    {
        if (_applying)
        {
            return;
        }

        if (!int.TryParse(_retentionDays.Text, out var days) ||
            days is < AppSettings.MinHistoryRetentionDays or > AppSettings.MaxHistoryRetentionDays)
        {
            _retentionError.Text = StringsUi.Format(
                "S.Settings.Retention.Invalid",
                AppSettings.MinHistoryRetentionDays,
                AppSettings.MaxHistoryRetentionDays);
            _retentionError.Visibility = Visibility.Visible;
            return;
        }

        if (days == _store.Current.HistoryRetentionDays)
        {
            SyncRetentionControls();
            return;
        }

        if (Commit(
                _store.Current with { HistoryRetentionDays = days },
                StringsUi.Format("S.Settings.Retention.Saved", days)))
        {
            SyncRetentionControls();
        }
    }

    // --- 数据 ---

    private void OpenDataFolder()
    {
        if (ExternalLinks.TryOpenFolder(AppPaths.DataRoot, out var error))
        {
            ShowStatus(StringsUi.Format("S.Settings.Data.Opened", AppPaths.DataRoot), error: false);
            AppLog.Info("Settings", $"已打开数据目录：{AppPaths.DataRoot}");
            return;
        }

        ShowStatus(StringsUi.Format("S.Settings.Data.OpenFailed", error ?? "?"), error: true);
    }

    private void RefreshDataSummary()
    {
        if (_closed || _dataCounts is null)
        {
            return;
        }

        var images = DataFolderSummary.CountFiles(AppPaths.ImagesDirectory, "*.png");
        var thumbs = DataFolderSummary.CountFiles(AppPaths.ThumbsDirectory, "*.png");
        var records = HistoryWindowCounts.TryCount();
        _dataCounts.Text = StringsUi.Format("S.Settings.Data.Counts", records, images, thumbs);
        AppLog.Info(
            "Settings",
            $"数据目录统计：记录={records} 图片文件={images} 缩略图文件={thumbs} 目录={AppPaths.DataRoot}");
    }

    // --- 同步界面 ↔ 设置 ---

    private void SyncControlsFromSettings()
    {
        _applying = true;
        try
        {
            var settings = _store.Current;
            _screenshotBox.SetValue(settings.ScreenshotShortcut);
            _quickScreenshotBox.SetValue(settings.QuickScreenshotShortcut);
            _pinBox.SetValue(settings.PinShortcut);
            _quickViewBox.SetValue(settings.QuickViewShortcut);
            SelectCombo(_language, settings.Language);
            _theme.Select(settings.Theme);
            _captureMode.Select(settings.CaptureMode);
            _startup.IsChecked = settings.LaunchAtStartup;
            SyncRetentionControls();
        }
        finally
        {
            _applying = false;
        }
    }

    private void SelectCombo<T>(ComboBox combo, T value) where T : struct, Enum
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboItem<T> candidate && EqualityComparer<T>.Default.Equals(candidate.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void UpdateComboTexts<T>(ComboBox combo, T[] values, string[] keys) where T : struct, Enum
    {
        var wasApplying = _applying;
        _applying = true;
        try
        {
            var selected = combo.SelectedItem is ComboItem<T> current ? current.Value : default;
            combo.Items.Clear();
            for (var i = 0; i < values.Length; i++)
            {
                combo.Items.Add(new ComboItem<T>(values[i], keys[i]));
            }

            SelectCombo(combo, selected);
        }
        finally
        {
            _applying = wasApplying;
        }
    }

    /// <summary>
    /// 刷新"当前使用 / 当前品牌 / 当前外观"这三行说明。
    /// </summary>
    /// <remarks>
    /// 桌面实测撞出来的：切换主题后只重画了控件颜色，说明文字还停在旧值
    /// （分段控件里已经是深色，旁边却写着浅色）。所以切主题/切语言都重新算一遍。
    /// </remarks>
    private void RefreshEffectiveLabels()
    {
        _languageSummary.Text = StringsUi.Format(
            "S.Settings.Language.Effective",
            StringsUi.IsChinese
                ? StringsUi.Get("S.Settings.Language.ZhHans")
                : StringsUi.Get("S.Settings.Language.English"));
        AutomationProperties.SetName(_languageSummary, _languageSummary.Text);

        _modeSummary.Text = StringsUi.Get("S.Settings.ThemeMode.Hint");
        AutomationProperties.SetName(_modeSummary, _modeSummary.Text);
        AutomationProperties.SetHelpText(_theme, _modeSummary.Text);
    }

    // --- 定时器 ---

    private void StartDataTimer()
    {
        if (!_dataTimer.IsEnabled)
        {
            _dataTimer.Start();
        }
    }

    private void StopDataTimer() => _dataTimer.Stop();

    // --- 主题 / 语言变化后的本地刷新 ---

    private void OnThemeChanged(AppTheme effective)
    {
        if (_closed)
        {
            return;
        }

        // 颜色全部走动态资源，已经随主题自动更新；这里只需要刷新说明文字。
        RefreshEffectiveLabels();
        AppLog.Info("Settings", $"设置页已按新主题刷新：品牌={ThemeManager.SelectedBrand} " +
                                $"生效={effective}（{ThemeManager.Palette}）");
    }

    private void OnStringsChanged(AppLanguage language)
    {
        if (_closed)
        {
            return;
        }

        RefreshLocalizedTexts();
        RefreshEffectiveLabels();
        RefreshDataSummary();
        ClearStatus();
        AppLog.Info("Settings", $"设置页已按新语言刷新：{language}");
    }

    /// <summary>按当前语言刷新页面上所有文案（就地改字，不重建控件树）。</summary>
    private void RefreshLocalizedTexts()
    {
        foreach (var (block, text) in _localized)
        {
            block.Text = text();
        }

        UiKit.SetButtonText(_openDataButton, StringsUi.Get("S.Settings.Data.Open"));
        UiKit.SetButtonText(_checkUpdateButton, StringsUi.Get("S.Settings.Update.Check"));

        var clearText = StringsUi.Get("S.QuickView.ClearShortcut");
        foreach (var clear in _clearShortcutButtons)
        {
            AutomationProperties.SetName(clear, clearText);
            clear.ToolTip = clearText;
        }

        AutomationProperties.SetName(_startup, StringsUi.Get("S.Settings.Startup.Enable"));
        AutomationProperties.SetName(_retentionDays, StringsUi.Get("S.Settings.Retention.Days"));
        AutomationProperties.SetHelpText(
            _retentionDays,
            StringsUi.Format(
                "S.Settings.Retention.Hint",
                AppSettings.MinHistoryRetentionDays,
                AppSettings.MaxHistoryRetentionDays));

        UpdateComboTexts(
            _language,
            new[] { AppLanguage.System, AppLanguage.ZhHans, AppLanguage.English },
            new[] { "S.Settings.Language.System", "S.Settings.Language.ZhHans", "S.Settings.Language.English" });
        _captureMode.RefreshTexts();
        _theme.RefreshTexts();

        _screenshotBox.RefreshLocalized();
        _quickScreenshotBox.RefreshLocalized();
        _pinBox.RefreshLocalized();
        _quickViewBox.RefreshLocalized();

        if (_retentionError.Visibility == Visibility.Visible)
        {
            _retentionError.Text = StringsUi.Format(
                "S.Settings.Retention.Invalid",
                AppSettings.MinHistoryRetentionDays,
                AppSettings.MaxHistoryRetentionDays);
        }

        AutomationProperties.SetName(this, StringsUi.Get("S.Settings.WindowName"));
        UpdateHeading();
    }

    // --- 辅助 ---

    /// <summary>历史保留规则说明：当前天数来自设置。</summary>
    internal static string DescribeRetentionRule(int retentionDays) =>
        StringsUi.Format("S.History.RetentionRule", retentionDays);

    /// <summary>更新渠道说明：只在未配置时提示一句；已配置时不显示（不暴露接口地址）。</summary>
    private static string DescribeChannel() =>
        string.IsNullOrWhiteSpace(AppInfo.UpdateManifestUrl)
            ? StringsUi.Get("S.Update.NotConfiguredTitle")
            : string.Empty;

    /// <summary>下拉框选项：值 + 文案键（显示文字随语言变化）。</summary>
    private sealed class ComboItem<T>(T value, string key) where T : struct, Enum
    {
        public T Value { get; } = value;

        public string Key { get; } = key;

        /// <summary>界面上显示的文字（随语言变化）。</summary>
        public string Text => StringsUi.Get(Key);

        public override string ToString() => Text;
    }

    /// <summary>
    /// 快捷键输入框：点进去之后直接按键就捕获。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用 <see cref="TextBox"/> 而不是自绘控件：外观与其它输入框一致，但<b>禁止文字输入</b>
    /// （<see cref="UIElement.PreviewTextInput"/> 标记已处理），只处理 <see cref="UIElement.PreviewKeyDown"/>。
    /// </para>
    /// <para>
    /// 只接受 <see cref="ShortcutSpec"/> 支持的键与四个修饰键；单独按修饰键时显示"请按下组合键…"
    /// 等待主键。Esc 放弃本次输入（还原原值），Tab 正常移动焦点。
    /// </para>
    /// <para>
    /// 焦点进入/离开时成对调用 <c>App.BeginShortcutCapture()</c>/<c>EndShortcutCapture()</c>：
    /// 录入期间全局热键暂停，否则用户刚按下的组合会被自己的热键抢走。
    /// </para>
    /// </remarks>
    private sealed class ShortcutBox : TextBox
    {
        private string _value = string.Empty;
        private string _pendingModifiers = string.Empty;
        private bool _showingError;

        private string DisplayText => _value.Length == 0
            ? StringsUi.Get("S.QuickView.ShortcutDisabled")
            : _value;

        internal ShortcutBox(string automationId)
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            // 隐式样式只匹配精确类型，子类要显式引用 TextBox 的样式。
            SetResourceReference(StyleProperty, typeof(TextBox));
            FontSize = Tokens.Text.Body;
            FontWeight = FontWeights.SemiBold;
            // 与其它行内控件同高（32），文字垂直居中。
            Height = ControlHeight;
            Padding = new Thickness(12, 0, 12, 0);
            VerticalContentAlignment = VerticalAlignment.Center;
            MinWidth = 168;
            TextAlignment = TextAlignment.Center;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            ToolTip = StringsUi.Get("S.Settings.Shortcut.Hint");
            AutomationProperties.SetAutomationId(this, automationId);
            AutomationProperties.SetName(this, StringsUi.Get("S.Settings.Shortcut.Hint"));

            ApplyPalette();

            PreviewKeyDown += OnPreviewKeyDown;
            PreviewTextInput += (_, e) => e.Handled = true;
            IsKeyboardFocusWithinChanged += (_, _) =>
            {
                if (Application.Current is not App app)
                {
                    return;
                }

                if (IsKeyboardFocusWithin)
                {
                    app.BeginShortcutCapture();
                }
                else
                {
                    if (!_showingError && _pendingModifiers.Length > 0)
                    {
                        ShowRegistered();
                    }

                    app.EndShortcutCapture();
                }
            };
            PreviewKeyUp += (_, _) =>
            {
                if (_pendingModifiers.Length > 0 && Keyboard.Modifiers == ModifierKeys.None)
                {
                    ShowRegistered();
                }
            };

            // 自动化辅助（只给验证脚本用，见 CommitForTesting）。
            AutomationProperties.SetHelpText(this, "shortcut-capture");
            GotKeyboardFocus += (_, _) =>
            {
                if (!_showingError)
                {
                    SelectAll();
                }
            };
        }

        /// <summary>用户按下了完整组合键（或按 Esc 取消 = <paramref name="shortcut"/> 为 null、error 为空）。</summary>
        internal event Action<ShortcutSpec?, string>? Committed;

        /// <summary>设置显示值（不触发 <see cref="Committed"/>）；不合法的旧值只原样显示。</summary>
        internal void SetValue(string text)
        {
            _value = text;
            _showingError = false;
            Text = DisplayText;
            ApplyPalette();
        }

        /// <summary>
        /// 自动化辅助：模拟"用户输入了一个组合"并走与真实按键完全相同的提交路径。
        /// </summary>
        /// <remarks>
        /// 只用于真实桌面验证（<c>ValuePattern.SetValue</c>）：本机 SendKeys 会被全局热键抢走，
        /// 键盘事件到不了输入框，而冲突提示这条验收必须真的走一遍。
        /// 正常使用时输入框是只读的，这个入口不会被普通打字触发。
        /// </remarks>
        internal void CommitForTesting(string combination)
        {
            if (!ShortcutSpec.TryParse(combination, out var spec, out var error) || spec is null)
            {
                Committed?.Invoke(null, error ?? StringsUi.Get("S.Settings.Shortcut.NeedModifier"));
                return;
            }

            _value = spec.Format();
            Text = _value;
            _showingError = false;
            AppLog.Info("Settings", $"快捷键输入（自动化辅助）：{_value}");
            Committed?.Invoke(spec, string.Empty);
        }

        /// <summary>显示错误（红框 + 原因作为工具提示，输入框保留原值）。</summary>
        internal void ShowError(string message)
        {
            _showingError = true;
            Text = DisplayText;
            ApplyPalette();
            ToolTip = message;
        }

        /// <summary>注册成功后恢复普通外观。</summary>
        internal void ShowRegistered()
        {
            _showingError = false;
            _pendingModifiers = string.Empty;
            Text = DisplayText;
            ApplyPalette();
            ToolTip = StringsUi.Get("S.Settings.Shortcut.Hint");
        }

        /// <summary>语言切换后刷新提示文字（错误提示保留原因不覆盖）。</summary>
        internal void RefreshLocalized()
        {
            if (!_showingError)
            {
                ToolTip = StringsUi.Get("S.Settings.Shortcut.Hint");
            }

            AutomationProperties.SetName(this, StringsUi.Get("S.Settings.Shortcut.Hint"));
        }

        /// <summary>边框/底色走动态资源：正常、出错两种状态之间切换，换主题时自动跟随。</summary>
        internal void ApplyPalette()
        {
            SetResourceReference(BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
            SetResourceReference(ForegroundProperty, ThemeManager.BrushKeys.Foreground);
            SetResourceReference(
                BorderBrushProperty,
                _showingError ? ThemeManager.BrushKeys.Error : ThemeManager.BrushKeys.Border);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (e.IsRepeat)
            {
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey :
                e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

            // 单独 Esc 取消录入，单独 Tab 移动焦点；带修饰键时可作为快捷键主键。
            if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                _pendingModifiers = string.Empty;
                ShowRegistered();
                return;
            }

            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = false;
                return;
            }

            var combined = ShortcutModifiers.None;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                combined |= ShortcutModifiers.Control;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                combined |= ShortcutModifiers.Alt;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                combined |= ShortcutModifiers.Shift;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            {
                combined |= ShortcutModifiers.Win;
            }

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);

            // 只按了修饰键：显示提示，等待主键（用户可以继续按住组合）。
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                _pendingModifiers = Describe(combined);
                Text = StringsUi.Get("S.Settings.Shortcut.Waiting");
                return;
            }

            if (!ShortcutSpec.IsSupportedVirtualKey(virtualKey))
            {
                if (combined == ShortcutModifiers.None)
                {
                    Committed?.Invoke(null, StringsUi.Get("S.Settings.Shortcut.NeedModifier"));
                }
                else
                {
                    Committed?.Invoke(null, StringsUi.Get("S.Settings.Shortcut.SecondKey"));
                }

                return;
            }

            if (combined == ShortcutModifiers.None)
            {
                Committed?.Invoke(null, StringsUi.Get("S.Settings.Shortcut.NeedModifier"));
                return;
            }

            var text = Describe(combined) + "+" + ShortcutSpec.GetKeyName(virtualKey);
            if (!ShortcutSpec.TryParse(text, out var spec, out var error) || spec is null)
            {
                Committed?.Invoke(null, error ?? StringsUi.Get("S.Settings.Shortcut.NeedModifier"));
                return;
            }

            _pendingModifiers = string.Empty;
            _value = spec.Format();
            Text = _value;
            _showingError = false;
            AppLog.Info("Settings", $"快捷键输入捕获：{_value}（修饰键={combined} 主键=0x{virtualKey:X2}）");
            Committed?.Invoke(spec, string.Empty);
        }

        private static string Describe(ShortcutModifiers modifiers)
        {
            var parts = new List<string>(4);
            if (modifiers.HasFlag(ShortcutModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (modifiers.HasFlag(ShortcutModifiers.Alt))
            {
                parts.Add("Alt");
            }

            if (modifiers.HasFlag(ShortcutModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (modifiers.HasFlag(ShortcutModifiers.Win))
            {
                parts.Add("Win");
            }

            return parts.Count == 0 ? string.Empty : string.Join('+', parts);
        }
    }
}
