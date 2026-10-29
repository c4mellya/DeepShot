using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DeepShot.Capture;
using DeepShot.Core;
using DeepShot.Core.History;
using DeepShot.Core.Settings;
using DeepShot.Core.Shortcuts;
using DeepShot.Platform;
using DeepShot.Ui;
using DeepShot.Updates;

namespace DeepShot.Settings;

/// <summary>
/// 设置窗口（T29）+ 检查更新对话框（T32）。
/// </summary>
/// <remarks>
/// <para>
/// <b>单实例普通窗口</b>（PLAN：关闭普通窗口留在托盘）：由 <see cref="SettingsWindowHost"/> 保证同时只有一个，
/// 再从托盘"设置"点一次只是把它唤到前面。
/// </para>
/// <para>
/// <b>改动立即生效</b>：每个开关/下拉一改就写 settings.json 并回调 <see cref="ChangeRequested"/>，
/// 由 <c>App</c> 负责真正应用（重新注册热键、切主题、切语言、同步注册表）。
/// 写盘失败时界面回滚到磁盘上的那份值并显示原因 —— 绝不"看起来成功了但重启就没了"。
/// </para>
/// <para>
/// <b>快捷键输入</b>：输入框捕获按键（<see cref="UIElement.PreviewKeyDown"/>），
/// 显示"等待组合键…"→ 合法就写进设置并立即重注册；不合法（缺修饰键、主键不支持）就地提示、不写入。
/// 与另一个功能冲突、或注册时被别的程序占用（Win32 1409）也会明确提示并回滚。
/// </para>
/// </remarks>
public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly ShortcutBox _screenshotBox;
    private readonly ShortcutBox _quickScreenshotBox;
    private readonly ShortcutBox _pinBox;
    private readonly ShortcutBox _quickViewBox;
    private readonly ComboBox _language;
    private readonly SegmentedControl<AppTheme> _theme;
    private readonly SegmentedControl<CaptureSelectionMode> _captureMode;
    private TextBlock _languageLabel = null!;
    private TextBlock _themeLabel = null!;

    private readonly CheckBox _startup;
    private readonly TextBlock _dataPath;
    private readonly TextBlock _dataCounts;
    private readonly TextBlock _retentionRule;
    private readonly TextBox _retentionDays;
    private readonly TextBlock _retentionError;
    private readonly TextBlock _status;
    private readonly TextBlock _header;
    private readonly StackPanel _sections;
    private bool _applying;

    /// <summary>创建一个设置窗口（外部请走 <see cref="SettingsWindowHost.Show"/>）。</summary>
    /// <param name="store">设置存储器（含当前生效的设置）。</param>
    internal SettingsWindow(SettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Title = StringsUi.Get("S.Settings.WindowTitle");
        Width = 680;
        Height = 780;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, ThemeManager.BrushKeys.WindowBackground);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "settings-window");
        AutomationProperties.SetName(this, StringsUi.Get("S.Settings.WindowName"));

        var settings = _store.Current;

        _header = UiKit.Text(StringsUi.Get("S.Settings.Heading"), TextRole.Display);
        _header.FontSize = 28;
        _header.FontWeight = AppFonts.Semibold;
        AutomationProperties.SetAutomationId(_header, "settings-heading");

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

        _language = new ComboBox { MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = Tokens.Text.Body };
        AutomationProperties.SetAutomationId(_language, "settings-language");
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.System, "S.Settings.Language.System"));
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.ZhHans, "S.Settings.Language.ZhHans"));
        _language.Items.Add(new ComboItem<AppLanguage>(AppLanguage.English, "S.Settings.Language.English"));
        _language.SelectionChanged += (_, _) => OnLanguageChanged();

        _theme = new SegmentedControl<AppTheme>();
        _theme.HorizontalAlignment = HorizontalAlignment.Right;
        _theme.Add(AppTheme.System, "S.Settings.Theme.System", "settings-theme-system");
        _theme.Add(AppTheme.Light, "S.Settings.Theme.Light", "settings-theme-light");
        _theme.Add(AppTheme.Dark, "S.Settings.Theme.Dark", "settings-theme-dark");
        AutomationProperties.SetAutomationId(_theme, "settings-theme");
        _theme.UserSelected += OnThemeChosen;

        _captureMode = new SegmentedControl<CaptureSelectionMode>();
        _captureMode.HorizontalAlignment = HorizontalAlignment.Right;
        _captureMode.Add(CaptureSelectionMode.Rectangle, "S.Settings.CaptureMode.Rectangle", "settings-capture-mode-rectangle");
        _captureMode.Add(CaptureSelectionMode.Window, "S.Settings.CaptureMode.Window", "settings-capture-mode-window");
        AutomationProperties.SetAutomationId(_captureMode, "settings-capture-mode");
        _captureMode.UserSelected += OnCaptureModeChosen;

        _startup = new CheckBox
        {
            IsChecked = settings.LaunchAtStartup,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(_startup, "settings-startup");
        AutomationProperties.SetName(_startup, StringsUi.Get("S.Settings.Startup.Enable"));
        _startup.Click += (_, _) => OnStartupToggled();

        _dataPath = UiKit.Text(AppPaths.DataRoot, TextRole.BodyLarge, wrap: true);
        AutomationProperties.SetAutomationId(_dataPath, "settings-data-path");

        _dataCounts = UiKit.Text(string.Empty, TextRole.Caption, wrap: true);
        _dataCounts.Margin = new Thickness(0, 3, 0, 0);
        AutomationProperties.SetAutomationId(_dataCounts, "settings-data-counts");

        _retentionDays = new TextBox
        {
            Width = 88,
            Height = Tokens.Height.Control,
            FontSize = Tokens.Text.Body,
            Padding = new Thickness(10, 0, 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            MaxLength = 4,
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
        _retentionError = UiKit.Text(string.Empty, TextRole.Caption, ThemeManager.BrushKeys.Error, wrap: true);
        _retentionError.Visibility = Visibility.Collapsed;
        _retentionError.Margin = new Thickness(0, 6, 0, 0);
        AutomationProperties.SetAutomationId(_retentionError, "settings-retention-error");
        AutomationProperties.SetLiveSetting(_retentionError, AutomationLiveSetting.Polite);

        _retentionRule = UiKit.Text(DescribeRetentionRule(settings.HistoryRetentionDays), TextRole.Caption, wrap: true);
        AutomationProperties.SetAutomationId(_retentionRule, "settings-retention-rule");

        _status = UiKit.Text(string.Empty, TextRole.Caption, wrap: true);
        _status.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(_status, "settings-status");

        var closeText = StringsUi.Get("S.Settings.Close");
        var closeButton = UiKit.Button(closeText, closeText, Close, ButtonKind.Primary, "settings-close");
        closeButton.MinWidth = 96;
        closeButton.Margin = new Thickness(Tokens.Space.M, 0, 0, 0);

        _sections = new StackPanel { MaxWidth = 640, Margin = new Thickness(28, 0, 28, 28) };

        var scroll = new ScrollViewer
        {
            Content = _sections,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 头部：只有页面标题。
        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 滚动后头部下方才出现一条细线；静止时头部与内容浑然一体。
        var headerLine = UiKit.Divider();
        headerLine.Opacity = 0;
        headerLine.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetRow(headerLine, 0);
        root.Children.Add(headerLine);
        var scrolled = false;
        scroll.ScrollChanged += (_, _) =>
        {
            var now = scroll.VerticalOffset > 1;
            if (now != scrolled)
            {
                scrolled = now;
                UiKit.Fade(headerLine, OpacityProperty, now ? 1 : 0, Tokens.Motion.Normal.TotalMilliseconds);
            }
        };

        var footerGrid = new Grid { MaxWidth = 640 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_status, 0);
        Grid.SetColumn(closeButton, 1);
        footerGrid.Children.Add(_status);
        footerGrid.Children.Add(closeButton);

        var footer = UiKit.FooterBar(footerGrid, new Thickness(28, 12, 28, 12));

        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(scroll);
        root.Children.Add(footer);
        Content = root;
        AppChrome.Apply(this);
        Loaded += (_, _) => UiKit.Entrance(_sections);

        BuildSections();

        // 主题/语言变化时（设置窗口自己触发的，或跟随系统变化）刷新说明文字与标题栏。
        ThemeManager.Changed += OnThemeChanged;
        StringsUi.Changed += OnLanguageChanged;
        Activated += (_, _) =>
        {
            if (_screenshotBox.IsKeyboardFocusWithin || _quickScreenshotBox.IsKeyboardFocusWithin ||
                _pinBox.IsKeyboardFocusWithin || _quickViewBox.IsKeyboardFocusWithin)
                (Application.Current as App)?.BeginShortcutCapture();
        };
        Deactivated += (_, _) => (Application.Current as App)?.EndShortcutCapture();
        Closed += (_, _) =>
        {
            _statusTimer?.Stop();
            (Application.Current as App)?.EndShortcutCapture();
            ThemeManager.Changed -= OnThemeChanged;
            StringsUi.Changed -= OnLanguageChanged;
        };

        SyncControlsFromSettings();
        RefreshDataSummary();
        SourceInitialized += (_, _) => DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);
        DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);

        AppLog.Info(
            "Settings",
            $"设置窗口已创建：{_store.Current} 语言={StringsUi.Current} 主题={ThemeManager.SelectedTheme}" +
            $"（生效 {ThemeManager.EffectiveTheme}） 设置文件={_store.FilePath}");
    }

    /// <summary>用户改了某项设置（<see cref="AppSettings"/> 里可能有多项变化后一起提交）。</summary>
    /// <param name="previous">改动前的设置（App 用来判断哪些部分需要重新应用）。</param>
    /// <param name="current">改动后的设置。</param>
    internal Action<AppSettings, AppSettings>? ChangeRequested { get; set; }

    /// <summary>
    /// 注册快捷键失败时由 App 回调（注册成功返回 null）。设置窗口据此回滚并提示。
    /// </summary>
    internal Func<AppSettings, string?>? ApplyShortcuts { get; set; }

    /// <summary>
    /// T32：设置里的"检查更新"按钮（由 App 打开检查更新对话框；与托盘菜单同一个实现）。
    /// </summary>
    internal Action? CheckUpdates { get; set; }

    /// <summary>
    /// 刷新"当前使用：English"、"当前外观：深色（系统为浅色）"这两行说明。
    /// </summary>
    /// <remarks>
    /// 桌面实测撞出来的：切换主题后只重画了控件颜色，这两行文字还停在旧值
    /// （下拉框里已经是 Dark，旁边却写着 Light）。现在切主题/切语言都重新算一遍。
    /// </remarks>
    private void RefreshEffectiveLabels()
    {
        if (_languageLabel is null || _themeLabel is null)
        {
            return;
        }

        _languageLabel.Text = StringsUi.Format("S.Settings.Language.Effective", StringsUi.IsChinese
            ? StringsUi.Get("S.Settings.Language.ZhHans")
            : StringsUi.Get("S.Settings.Language.English"));
        AutomationProperties.SetName(_languageLabel, _languageLabel.Text);

        _themeLabel.Text = StringsUi.Format("S.Settings.Theme.Effective",
            ThemeManager.EffectiveTheme == AppTheme.Light
                ? StringsUi.Get("S.Settings.Theme.Light")
                : StringsUi.Get("S.Settings.Theme.Dark"),
            ThemeManager.IsSystemUsingLightTheme()
                ? StringsUi.Get("S.Settings.Theme.Light")
                : StringsUi.Get("S.Settings.Theme.Dark"));
        AutomationProperties.SetName(_themeLabel, _themeLabel.Text);
    }

    /// <summary>
    /// 历史保留规则说明：默认天数来自 Core，当前天数来自设置。
    /// </summary>
    internal static string DescribeRetentionRule(int retentionDays = HistoryRetentionPolicy.DefaultRetentionDays) =>
        StringsUi.Format("S.History.RetentionRule", retentionDays);

    /// <summary>更新渠道的说明文字（空地址时是"尚未配置更新渠道"，绝不显示成已是最新版）。</summary>
    private static string DescribeChannel() =>
        string.IsNullOrWhiteSpace(AppInfo.UpdateManifestUrl)
            ? StringsUi.Get("S.Update.NotConfiguredTitle")
            : string.Empty;

    /// <summary>本次窗口里最后一次操作的结果（验证脚本读日志核对）。</summary>
    internal string StatusText => _status.Text;

    // --- 构建界面 ---

    private void BuildSections()
    {
        // 语言切换会重建整块内容，但 _language / _theme / _startup / 几段文字是**复用**的
        // 长期控件：它们还挂在旧的容器上，先摘下来再重新摆放（否则 Add 会抛
        // "Specified element is already the logical child of another element"）。
        foreach (var element in new FrameworkElement[]
                 {
                     _screenshotBox, _quickScreenshotBox, _pinBox, _quickViewBox, _language, _theme, _captureMode, _startup,
                     _dataPath, _dataCounts, _retentionRule, _retentionDays, _retentionError,
                 })
        {
            Detach(element);
        }

        _sections.Children.Clear();

        // 快捷键
        AddSection("S.Settings.Section.Shortcuts", first: true);
        // "清除"做成键帽左侧的紧凑图标按钮：四个键帽的左右边缘仍对齐成一列。
        var quickShortcut = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var clearText = StringsUi.Get("S.QuickView.ClearShortcut");
        var clearQuickShortcut = UiKit.IconButton(AppIcons.Close, clearText, clearText,
            () => ApplyShortcutChange(_store.Current.WithQuickViewShortcut(string.Empty), "快览", StringsUi.Get("S.QuickView.ShortcutDisabled")),
            size: Tokens.Height.Control, iconKey: UiKit.Keys.Secondary, iconSize: 14);
        AutomationProperties.SetAutomationId(clearQuickShortcut, "settings-clear-quick-view-shortcut");
        clearQuickShortcut.VerticalAlignment = VerticalAlignment.Center;
        clearQuickShortcut.Margin = new Thickness(0, 0, Tokens.Space.XS, 0);
        quickShortcut.Children.Add(clearQuickShortcut);
        quickShortcut.Children.Add(_quickViewBox);
        _sections.Children.Add(Cards(
            IconRow(StringsUi.Get("S.Settings.Shortcut.Screenshot"), null, _screenshotBox),
            IconRow(StringsUi.Get("S.Settings.Shortcut.QuickScreenshot"),
                StringsUi.Get("S.Settings.Shortcut.QuickScreenshotHint"), _quickScreenshotBox),
            IconRow(StringsUi.Get("S.Settings.Shortcut.Pin"), null, _pinBox),
            UiKit.AdaptiveSettingsRow(
                UiKit.RowLabel(null, StringsUi.Get("S.QuickView.Shortcut"), null, out _, out _),
                quickShortcut, 200, 0)));

        // 截图方式：分段控件较宽，标题被挤窄时整块换到下一行（不再一字一行）。
        AddSection("S.Settings.Section.Capture");
        _sections.Children.Add(Cards(
            UiKit.AdaptiveSettingsRow(
                UiKit.RowLabel(null, StringsUi.Get("S.Settings.CaptureMode.Label"), StringsUi.Get("S.Settings.CaptureMode.Hint"), out _, out _),
                _captureMode, 220, 0)));

        // 外观（语言 + 主题）。"当前使用 / 当前外观"说明沿用旧行为：放在说明位、默认折叠。
        AddSection("S.Settings.Section.Appearance");
        _sections.Children.Add(Cards(
            UiKit.SettingsRow(
                UiKit.RowLabel(null, StringsUi.Get("S.Settings.Section.Language"), null, out _, out _languageLabel),
                _language),
            UiKit.AdaptiveSettingsRow(
                UiKit.RowLabel(null, StringsUi.Get("S.Settings.Section.Theme"), null, out _, out _themeLabel),
                _theme, 200, 0)));
        RefreshEffectiveLabels();

        // 开机启动
        AddSection("S.Settings.Section.Startup");
        _sections.Children.Add(Cards(
            IconRow(StringsUi.Get("S.Settings.Startup.Enable"), null, _startup)));

        // 数据目录 + 历史保留
        AddSection("S.Settings.Section.Data");
        var dataTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dataTexts.Children.Add(_dataPath);
        dataTexts.Children.Add(_dataCounts);
        var openText = StringsUi.Get("S.Settings.Data.Open");
        var openData = UiKit.Button(openText, openText, OpenDataFolder, ButtonKind.Secondary, "settings-open-data");
        openData.HorizontalAlignment = HorizontalAlignment.Right;

        var retentionDetails = new StackPanel { Margin = new Thickness(0) };
        retentionDetails.Children.Add(_retentionRule);
        retentionDetails.Children.Add(_retentionError);
        var retentionRow = IconRow(StringsUi.Get("S.Settings.Retention.Days"),
            StringsUi.Format("S.Settings.Retention.Hint", AppSettings.MinHistoryRetentionDays, AppSettings.MaxHistoryRetentionDays),
            _retentionDays);
        // 说明紧贴在保留天数行下面（同一块，不加分隔线）。
        ((FrameworkElement)retentionRow).Margin = new Thickness(Tokens.CardRowPadding.Left, Tokens.CardRowPadding.Top, Tokens.CardRowPadding.Right, Tokens.Space.S);
        var retentionBlock = new StackPanel();
        retentionBlock.Children.Add(retentionRow);
        var retentionDetailsRow = UiKit.CardRow(retentionDetails);
        retentionDetailsRow.Padding = new Thickness(Tokens.CardRowPadding.Left, 0, Tokens.CardRowPadding.Right, Tokens.CardRowPadding.Bottom);
        retentionBlock.Children.Add(retentionDetailsRow);
        _sections.Children.Add(Cards(
            UiKit.AdaptiveSettingsRow((dataTexts), openData, 240, 0),
            retentionBlock));

        // 更新：版本 + 渠道 + 检查按钮
        AddSection("S.Settings.Section.Update");
        var version = UiKit.Text(StringsUi.Format("S.Settings.Version", UpdateChecker.GetCurrentVersion()), TextRole.BodyLarge);
        AutomationProperties.SetAutomationId(version, "settings-version");

        var channel = UiKit.Text(DescribeChannel(), TextRole.Caption, wrap: true);
        channel.Margin = new Thickness(0, 3, 0, 0);
        AutomationProperties.SetAutomationId(channel, "settings-update-channel");
        channel.Visibility = channel.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var updateTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        updateTexts.Children.Add(version);
        updateTexts.Children.Add(channel);

        var checkText = StringsUi.Get("S.Settings.Update.Check");
        var check = UiKit.Button(checkText, checkText, () => CheckUpdates?.Invoke(), ButtonKind.Secondary, "settings-check-update");
        check.HorizontalAlignment = HorizontalAlignment.Right;
        _sections.Children.Add(Cards(UiKit.AdaptiveSettingsRow((updateTexts), check, 240, 0)));

        ApplyLocalizedLabels();
    }

    /// <summary>分组标题（14 Semibold）；组与组之间 24（含上一张卡片的 4）。</summary>
    private void AddSection(string key, bool first = false)
    {
        var text = StringsUi.Get(key);
        var header = UiKit.Text(text, TextRole.BodyLarge);
        header.FontWeight = AppFonts.Semibold;
        header.Margin = new Thickness(0, first ? 0 : Tokens.Space.L + 4, 0, Tokens.Space.S);
        AutomationProperties.SetAutomationId(header, "settings-section-" + text);
        _sections.Children.Add(header);
    }

    /// <summary>每一行一张卡片，卡片之间 4。</summary>
    private static Panel Cards(params UIElement[] rows)
    {
        var panel = new StackPanel();
        foreach (var row in rows)
        {
            var card = UiKit.CardShell(row);
            card.Margin = new Thickness(0, 0, 0, Tokens.Space.XS);
            panel.Children.Add(card);
        }

        return panel;
    }

    /// <summary>设置行（左文右控件）。</summary>
    private static FrameworkElement IconRow(string title, string? description, FrameworkElement control) =>
        UiKit.SettingsRow(UiKit.RowLabel(null, title, description, out _, out _), control);

    /// <summary>头部：页面标题（28 Semibold）。</summary>
    private FrameworkElement BuildHeader()
    {
        var host = new Border { MaxWidth = 640, Margin = new Thickness(28, 10, 28, 20), Child = _header };
        return host;
    }

    /// <summary>把控件从它的父容器上摘下来（父级是 <see cref="Panel"/>/<see cref="Decorator"/> 时）。</summary>
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl content when ReferenceEquals(content.Content, element):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case null:
                break;
        }

        // 上一轮可能是放进网格/面板里的 Field：再沿 LogicParent 往上一层摘。
        if (element.Parent is FrameworkElement parent && parent.Parent is Panel outer)
        {
            outer.Children.Remove(parent);
        }
    }

    /// <summary>把控件文字按当前语言刷新（语言切换后调用）。</summary>
    private void ApplyLocalizedLabels()
    {
        AutomationProperties.SetName(_startup, StringsUi.Get("S.Settings.Startup.Enable"));

        // 下拉项文字（重建项文本，保持选中值不变）。
        UpdateComboTexts(_language, new[] { AppLanguage.System, AppLanguage.ZhHans, AppLanguage.English },
            new[] { "S.Settings.Language.System", "S.Settings.Language.ZhHans", "S.Settings.Language.English" });
        _theme.RefreshTexts();
        _captureMode.RefreshTexts();
        AutomationProperties.SetName(_retentionDays, StringsUi.Get("S.Settings.Retention.Days"));
        AutomationProperties.SetHelpText(_retentionDays,
            StringsUi.Format("S.Settings.Retention.Hint", AppSettings.MinHistoryRetentionDays, AppSettings.MaxHistoryRetentionDays));
        _retentionRule.Text = DescribeRetentionRule(_store.Current.HistoryRetentionDays);
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

    private static void SelectCombo<T>(ComboBox combo, T value) where T : struct, Enum
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

    private sealed class ComboItem<T>(T value, string key) where T : struct, Enum
    {
        public T Value { get; } = value;

        public string Key { get; } = key;

        /// <summary>界面上显示的文字（随语言变化）。</summary>
        public string Text => StringsUi.Get(Key);

        public override string ToString() => Text;
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

    /// <summary>
    /// 提交一份新设置：先写盘（失败即回滚并提示），成功后再交给 App 应用。
    /// </summary>
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
                continue;

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
        if (!Commit(next, StringsUi.Format("S.Settings.Shortcut.RegisteredHint", formatted)))
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
            AppLog.Info("Settings", $"{which}快捷键已生效：{formatted}");
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

    // --- 语言 / 主题 / 开机启动 ---

    private void OnLanguageChanged()
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
        if (_applying)
        {
            return;
        }

        var next = _store.Current.WithTheme(theme);
        if (next.Theme == _store.Current.Theme)
        {
            return;
        }

        Commit(next, StringsUi.Get("S.Settings.Saved"));
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

    private void SyncRetentionControls()
    {
        _retentionDays.Text = _store.Current.HistoryRetentionDays.ToString();
        _retentionRule.Text = DescribeRetentionRule(_store.Current.HistoryRetentionDays);
        _retentionError.Text = string.Empty;
        _retentionError.Visibility = Visibility.Collapsed;
    }

    private void CommitRetentionDays()
    {
        if (_applying) return;
        if (!int.TryParse(_retentionDays.Text, out var days) ||
            days is < AppSettings.MinHistoryRetentionDays or > AppSettings.MaxHistoryRetentionDays)
        {
            _retentionError.Text = StringsUi.Format("S.Settings.Retention.Invalid",
                AppSettings.MinHistoryRetentionDays, AppSettings.MaxHistoryRetentionDays);
            _retentionError.Visibility = Visibility.Visible;
            return;
        }

        if (days == _store.Current.HistoryRetentionDays)
        {
            SyncRetentionControls();
            return;
        }

        if (Commit(_store.Current with { HistoryRetentionDays = days },
                StringsUi.Format("S.Settings.Retention.Saved", days)))
        {
            SyncRetentionControls();
        }
    }

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
        var images = DataFolderSummary.CountFiles(AppPaths.ImagesDirectory, "*.png");
        var thumbs = DataFolderSummary.CountFiles(AppPaths.ThumbsDirectory, "*.png");
        var records = HistoryWindowCounts.TryCount();
        _dataCounts.Text = StringsUi.Format("S.Settings.Data.Counts", records, images, thumbs);
        AppLog.Info(
            "Settings",
            $"数据目录统计：记录={records} 图片文件={images} 缩略图文件={thumbs} 目录={AppPaths.DataRoot}");
    }

    private DispatcherTimer? _statusTimer;

    private void ShowStatus(string message, bool error)
    {
        _statusTimer?.Stop();
        _status.Text = message;
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            error ? ThemeManager.BrushKeys.Error : ThemeManager.BrushKeys.Success);

        // 成功提示 2 秒后自动消失；错误一直留着。
        if (!error)
        {
            if (_statusTimer is null)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _statusTimer.Tick += (_, _) =>
                {
                    _statusTimer.Stop();
                    _status.Text = string.Empty;
                };
            }

            _statusTimer.Start();
        }

        AppLog.Info("Settings", $"设置窗口状态：{message}");
    }

    // --- 主题 / 语言变化后的本地刷新 ---

    private void OnThemeChanged(AppTheme effective)
    {
        // 颜色全部走动态资源，已经随主题自动更新；这里只需要刷新说明文字与系统标题栏。
        RefreshEffectiveLabels();
        DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);
        AppLog.Info("Settings", $"设置窗口已按新主题刷新：{effective}（{ThemeManager.Palette}）");
    }

    private void OnLanguageChanged(AppLanguage language)
    {
        Title = StringsUi.Get("S.Settings.WindowTitle");
        AutomationProperties.SetName(this, StringsUi.Get("S.Settings.WindowName"));
        _header.Text = StringsUi.Get("S.Settings.Heading");
        BuildSections();
        SyncControlsFromSettings();
        RefreshEffectiveLabels();
        RefreshDataSummary();
        _status.Text = string.Empty;
        AppLog.Info("Settings", $"设置窗口已按新语言重建：{language}");
    }
}

/// <summary>
/// 快捷键输入框：点进去之后直接按键就捕获（T29）。
/// </summary>
/// <remarks>
/// <para>
/// 用 <see cref="TextBox"/> 而不是 <see cref="TextBox"/> 的自定义控件：外观与其它输入框一致，
/// 但**禁止文字输入**（<see cref="PreviewTextInput"/> 标记已处理），只处理 <see cref="PreviewKeyDown"/>。
/// </para>
/// <para>
/// 只接受 <see cref="ShortcutSpec"/> 支持的键（A–Z、0–9、F1–F12）与四个修饰键；
/// 单独按修饰键时显示"请按下组合键…"等待主键。Esc 放弃本次输入（还原原值）。
/// </para>
/// </remarks>
internal sealed class ShortcutBox : TextBox
{
    private string _value = string.Empty;
    private string _pendingModifiers = string.Empty;
    private bool _showingError;

    internal ShortcutBox(string automationId)
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        // 隐式样式只匹配精确类型，子类要显式引用 TextBox 的样式。
        SetResourceReference(StyleProperty, typeof(TextBox));
        FontSize = Tokens.Text.Body;
        FontWeight = FontWeights.SemiBold;
        // 与其它输入框/按钮同高（34），文字垂直居中。
        Height = Tokens.Height.Control;
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
            if (Application.Current is not App app) return;
            if (IsKeyboardFocusWithin) app.BeginShortcutCapture();
            else
            {
                if (!_showingError && _pendingModifiers.Length > 0) ShowRegistered();
                app.EndShortcutCapture();
            }
        };
        PreviewKeyUp += (_, _) =>
        {
            if (_pendingModifiers.Length > 0 && Keyboard.Modifiers == ModifierKeys.None)
                ShowRegistered();
        };

        // 自动化辅助（只给验证脚本用，见 CommitForTesting）。
        AutomationProperties.SetHelpText(this, "shortcut-capture");
        var box = this;
        IsVisibleChanged += (_, _) => _ = box;
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
        Text = text;
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
        Text = _value;
        ApplyPalette();
        ToolTip = message;
    }

    /// <summary>注册成功后恢复普通外观。</summary>
    internal void ShowRegistered()
    {
        _showingError = false;
        _pendingModifiers = string.Empty;
        Text = _value;
        ApplyPalette();
        ToolTip = StringsUi.Get("S.Settings.Shortcut.Hint");
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
        if (e.IsRepeat) return;
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

/// <summary>设置窗口的单实例宿主（T29）：托盘"设置"从这里打开。</summary>
internal static class SettingsWindowHost
{
    private static SettingsWindow? _window;

    /// <summary>当前打开的设置窗口（未打开为 null）。</summary>
    internal static SettingsWindow? Current => _window;

    /// <summary>打开（已有实例则激活）。</summary>
    internal static SettingsWindow? Show(SettingsStore store)
    {
        if (_window is not null)
        {
            AppLog.Info("Settings", "设置窗口已打开，本次只激活已有窗口");
            Activate(_window);
            return _window;
        }

        try
        {
            var window = new SettingsWindow(store);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };
            _window = window;
            window.Show();
            Activate(window);
            AppLog.Info("Settings", "设置窗口已打开");
            return window;
        }
        catch (Exception ex)
        {
            _window = null;
            AppLog.Error("Settings", "打开设置窗口失败", ex);
            return null;
        }
    }

    /// <summary>关闭（退出应用时用）。</summary>
    internal static void CloseIfOpen()
    {
        var window = _window;
        _window = null;
        if (window is null)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", $"关闭设置窗口失败（忽略）：{ex.Message}");
        }
    }

    private static void Activate(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            window.Focus();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", $"激活设置窗口失败（忽略）：{ex.Message}");
        }
    }
}

/// <summary>
/// 数据目录里"有多少条历史记录"（设置窗口显示用）。历史不可用时返回 0。
/// </summary>
internal static class HistoryWindowCounts
{
    private static Func<int>? _provider;

    /// <summary>由 App 在初始化历史后注入（避免设置窗口直接依赖 HistoryStore）。</summary>
    internal static void SetProvider(Func<int>? provider) => _provider = provider;

    internal static int TryCount()
    {
        try
        {
            return _provider?.Invoke() ?? 0;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", $"统计历史记录数失败：{ex.Message}");
            return 0;
        }
    }
}
