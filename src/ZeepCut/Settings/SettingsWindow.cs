using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;
using System.Windows.Threading;
using ZeepCut.Capture;
using ZeepCut.Core;
using ZeepCut.Core.History;
using ZeepCut.Core.Settings;
using ZeepCut.Core.Shortcuts;
using ZeepCut.Platform;
using ZeepCut.Ui;
using ZeepCut.Updates;

namespace ZeepCut.Settings;

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
    private readonly ShortcutBox _pinBox;
    private readonly ComboBox _language;
    private readonly SegmentedControl<AppTheme> _theme;
    private readonly SegmentedControl<CaptureSelectionMode> _captureMode;
    private TextBlock _languageLabel = null!;
    private TextBlock _themeLabel = null!;

    private readonly CheckBox _startup;
    private readonly TextBlock _dataPath;
    private readonly TextBlock _dataCounts;
    private readonly TextBlock _retentionRule;
    private readonly TextBlock _status;
    private readonly TextBlock _header;
    private readonly TextBlock _subtitle;
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

        _header = MakeText(StringsUi.Get("S.Settings.Heading"), 24, ThemeManager.BrushKeys.Foreground, FontWeights.SemiBold);
        AutomationProperties.SetAutomationId(_header, "settings-heading");
        _header.HorizontalAlignment = HorizontalAlignment.Left;

        _subtitle = MakeText(StringsUi.Get("S.Settings.Subtitle"), 12.5, ThemeManager.BrushKeys.SecondaryForeground);
        _subtitle.TextWrapping = TextWrapping.Wrap;
        _subtitle.HorizontalAlignment = HorizontalAlignment.Left;
        _subtitle.Margin = new Thickness(0, 3, 0, 0);

        _screenshotBox = new ShortcutBox("settings-shortcut-screenshot");
        _screenshotBox.SetValue(settings.ScreenshotShortcut);
        _screenshotBox.Committed += OnScreenshotShortcutCommitted;

        _pinBox = new ShortcutBox("settings-shortcut-pin");
        _pinBox.SetValue(settings.PinShortcut);
        _pinBox.Committed += OnPinShortcutCommitted;

        _language = new ComboBox { MinWidth = 190, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
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

        _dataPath = MakeText(AppPaths.DataRoot, 12.5, ThemeManager.BrushKeys.Foreground);
        _dataPath.TextWrapping = TextWrapping.Wrap;
        _dataPath.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_dataPath, "settings-data-path");

        _dataCounts = MakeText(string.Empty, 12, ThemeManager.BrushKeys.SecondaryForeground);
        _dataCounts.HorizontalAlignment = HorizontalAlignment.Left;
        _dataCounts.TextWrapping = TextWrapping.Wrap;
        _dataCounts.Margin = new Thickness(0, 3, 0, 0);
        AutomationProperties.SetAutomationId(_dataCounts, "settings-data-counts");

        _retentionRule = MakeText(DescribeRetentionRule(), 12.5, ThemeManager.BrushKeys.SecondaryForeground);
        _retentionRule.TextWrapping = TextWrapping.Wrap;
        _retentionRule.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_retentionRule, "settings-retention-rule");

        _status = MakeText(string.Empty, 12.5, ThemeManager.BrushKeys.SecondaryForeground);
        _status.HorizontalAlignment = HorizontalAlignment.Left;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(_status, "settings-status");

        var closeButton = MakeButton(StringsUi.Get("S.Settings.Close"), "settings-close", Close, primary: true);
        closeButton.Margin = new Thickness(12, 0, 0, 0);

        _sections = new StackPanel { MaxWidth = 640, Margin = new Thickness(28, 4, 28, 20) };

        var scroll = new ScrollViewer
        {
            Content = _sections,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // 头部：渐变徽标 + 标题/副标题。
        var headerTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        headerTextStack.Children.Add(_header);
        headerTextStack.Children.Add(_subtitle);

        var headerGrid = new Grid { MaxWidth = 640, Margin = new Thickness(28, 26, 28, 16) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = BuildBadge();
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(headerTextStack, 1);
        headerGrid.Children.Add(badge);
        headerGrid.Children.Add(headerTextStack);

        var footerGrid = new Grid { MaxWidth = 640, Margin = new Thickness(28, 12, 28, 14) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_status, 0);
        Grid.SetColumn(closeButton, 1);
        footerGrid.Children.Add(_status);
        footerGrid.Children.Add(closeButton);

        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = footerGrid };
        footer.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.Divider);
        footer.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Surface);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headerGrid, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(headerGrid);
        root.Children.Add(scroll);
        root.Children.Add(footer);
        Content = root;

        BuildSections();

        // 主题/语言变化时（设置窗口自己触发的，或跟随系统变化）刷新说明文字与标题栏。
        ThemeManager.Changed += OnThemeChanged;
        StringsUi.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
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
    /// 30 天保留规则说明：天数来自 Core 的常量（单一事实来源），措辞走语言资源（T31）。
    /// </summary>
    internal static string DescribeRetentionRule() =>
        StringsUi.Format("S.History.RetentionRule", HistoryRetentionPolicy.DefaultRetentionDays);

    /// <summary>更新渠道的说明文字（空地址时是"尚未配置更新渠道"，绝不显示成已是最新版）。</summary>
    private static string DescribeChannel() =>
        string.IsNullOrWhiteSpace(AppInfo.UpdateManifestUrl)
            ? StringsUi.Get("S.Update.NotConfiguredTitle")
            : StringsUi.Format("S.Update.Channel", AppInfo.UpdateManifestUrl);

    /// <summary>本次窗口里最后一次操作的结果（验证脚本读日志核对）。</summary>
    internal string StatusText => _status.Text;

    // --- 构建界面 ---

    /// <summary>头部的品牌徽标：强调色圆角方块 + 白色裁剪图标 + 顶部高光。</summary>
    private static FrameworkElement BuildBadge()
    {
        var icon = new Shapes.Path
        {
            Data = Geometry.Parse("M7,3 V17 H21 M3,7 H17 V21"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        icon.SetResourceReference(Shapes.Shape.StrokeProperty, ThemeManager.BrushKeys.OnAccent);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(icon);

        var gloss = new Border
        {
            CornerRadius = new CornerRadius(14),
            IsHitTestVisible = false,
            Background = new LinearGradientBrush(
                Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                90),
        };

        var grid = new Grid();
        grid.Children.Add(gloss);
        grid.Children.Add(new Viewbox { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = canvas });

        var badge = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(14),
            Child = grid,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Accent);
        return badge;
    }

    private void BuildSections()
    {
        // 语言切换会重建整块内容，但 _language / _theme / _startup / 几段文字是**复用**的
        // 长期控件：它们还挂在旧的容器上，先摘下来再重新摆放（否则 Add 会抛
        // "Specified element is already the logical child of another element"）。
        foreach (var element in new FrameworkElement[]
                 {
                     _screenshotBox, _pinBox, _language, _theme, _captureMode, _startup,
                     _dataPath, _dataCounts, _retentionRule,
                 })
        {
            Detach(element);
        }

        _sections.Children.Clear();

        // 快捷键
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Shortcuts")));
        _sections.Children.Add(Card(
            Row(StringsUi.Get("S.Settings.Shortcut.Screenshot"), null, _screenshotBox),
            Row(StringsUi.Get("S.Settings.Shortcut.Pin"), null, _pinBox)));
        _sections.Children.Add(Hint(StringsUi.Get("S.Settings.Shortcut.Hint")));

        // 截图方式
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Capture")));
        _sections.Children.Add(Card(
            Row(StringsUi.Get("S.Settings.CaptureMode.Label"), StringsUi.Get("S.Settings.CaptureMode.Hint"), _captureMode)));

        // 外观（语言 + 主题）
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Appearance")));
        _sections.Children.Add(Card(
            Row(StringsUi.Get("S.Settings.Section.Language"), null, _language, out _languageLabel),
            Row(StringsUi.Get("S.Settings.Section.Theme"), null, _theme, out _themeLabel)));
        RefreshEffectiveLabels();

        // 开机启动
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Startup")));
        _sections.Children.Add(Card(
            Row(StringsUi.Get("S.Settings.Startup.Enable"), null, _startup)));

        // 数据目录
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Data")));
        var dataLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        dataLeft.Children.Add(_dataPath);
        dataLeft.Children.Add(_dataCounts);
        var openData = MakeButton(StringsUi.Get("S.Settings.Data.Open"), "settings-open-data", OpenDataFolder);
        openData.Margin = new Thickness(0);
        openData.VerticalAlignment = VerticalAlignment.Center;
        openData.HorizontalAlignment = HorizontalAlignment.Right;

        _sections.Children.Add(Card(
            RowCore(dataLeft, openData),
            FullRow(_retentionRule)));

        // 更新：版本 + 渠道 + 检查按钮
        _sections.Children.Add(Section(StringsUi.Get("S.Settings.Section.Update")));
        var version = MakeText(StringsUi.Format("S.Settings.Version", UpdateChecker.GetCurrentVersion()), 13.5, ThemeManager.BrushKeys.Foreground);
        version.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(version, "settings-version");

        var channel = MakeText(DescribeChannel(), 12, ThemeManager.BrushKeys.SecondaryForeground);
        channel.TextWrapping = TextWrapping.Wrap;
        channel.HorizontalAlignment = HorizontalAlignment.Left;
        channel.Margin = new Thickness(0, 2, 0, 0);
        AutomationProperties.SetAutomationId(channel, "settings-update-channel");

        var updateLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        updateLeft.Children.Add(version);
        updateLeft.Children.Add(channel);

        var check = MakeButton(
            StringsUi.Get("S.Settings.Update.Check"),
            "settings-check-update",
            () => CheckUpdates?.Invoke());
        check.Margin = new Thickness(0);
        check.VerticalAlignment = VerticalAlignment.Center;
        check.HorizontalAlignment = HorizontalAlignment.Right;
        _sections.Children.Add(Card(RowCore(updateLeft, check)));

        ApplyLocalizedLabels();
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
        _retentionRule.Text = DescribeRetentionRule();
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

    // --- 布局小件 ---

    /// <summary>分组小标题（卡片上方）。</summary>
    private static FrameworkElement Section(string text)
    {
        var block = MakeText(text, 12.5, ThemeManager.BrushKeys.SecondaryForeground, FontWeights.SemiBold);
        block.HorizontalAlignment = HorizontalAlignment.Left;
        block.Margin = new Thickness(6, 22, 0, 8);
        AutomationProperties.SetAutomationId(block, "settings-section-" + text);
        return block;
    }

    /// <summary>卡片下方的小提示。</summary>
    private static FrameworkElement Hint(string text)
    {
        var block = MakeText(text, 11.5, ThemeManager.BrushKeys.SecondaryForeground);
        block.TextWrapping = TextWrapping.Wrap;
        block.HorizontalAlignment = HorizontalAlignment.Left;
        block.Margin = new Thickness(6, 8, 6, 0);
        return block;
    }

    /// <summary>圆角卡片：行与行之间用细线分隔。</summary>
    private static Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var line = new Border { Height = 1, Margin = new Thickness(18, 0, 0, 0) };
                line.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Divider);
                stack.Children.Add(line);
            }

            stack.Children.Add(rows[i]);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = stack,
        };
        card.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushKeys.Surface);
        card.SetResourceReference(Border.BorderBrushProperty, ThemeManager.BrushKeys.Divider);
        return card;
    }

    private static FrameworkElement FullRow(FrameworkElement content) =>
        new Border { Padding = new Thickness(18, 13, 18, 13), Child = content };

    private static FrameworkElement Row(string title, string? description, FrameworkElement control) =>
        Row(title, description, control, out _);

    /// <summary>设置行：左边标题（+ 说明），右边控件。</summary>
    private static FrameworkElement Row(string title, string? description, FrameworkElement control, out TextBlock descriptionBlock)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = MakeText(title, 13.5, ThemeManager.BrushKeys.Foreground);
        titleBlock.TextWrapping = TextWrapping.Wrap;
        titleBlock.HorizontalAlignment = HorizontalAlignment.Left;
        left.Children.Add(titleBlock);

        descriptionBlock = MakeText(description ?? string.Empty, 12, ThemeManager.BrushKeys.SecondaryForeground);
        descriptionBlock.TextWrapping = TextWrapping.Wrap;
        descriptionBlock.HorizontalAlignment = HorizontalAlignment.Left;
        descriptionBlock.Margin = new Thickness(0, 2, 0, 0);
        descriptionBlock.Visibility = description is null ? Visibility.Collapsed : Visibility.Visible;
        left.Children.Add(descriptionBlock);

        return RowCore(left, control);
    }

    private static FrameworkElement RowCore(FrameworkElement left, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(18, 13, 18, 13) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.Margin = new Thickness(0, 0, 18, 0);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(left);
        grid.Children.Add(control);
        return grid;
    }

    private static TextBlock MakeText(string text, double size, string brushKey, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        block.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.FontFamilyKey);
        if (weight is { } value)
        {
            block.FontWeight = value;
        }

        return block;
    }

    private static Button MakeButton(string text, string automationId, Action onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(18, 7, 18, 7),
            MinWidth = 84,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        if (primary)
        {
            button.BorderThickness = new Thickness(0);
            button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.Accent);
            button.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.OnAccent);
        }
        else
        {
            button.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.SurfaceAlt);
        }

        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
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
            _pinBox.SetValue(settings.PinShortcut);
            SelectCombo(_language, settings.Language);
            _theme.Select(settings.Theme);
            _captureMode.Select(settings.CaptureMode);
            _startup.IsChecked = settings.LaunchAtStartup;
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

    private void OnScreenshotShortcutCommitted(ShortcutSpec? shortcut, string error)
    {
        if (error.Length > 0)
        {
            ShowStatus(error, error: true);
            _screenshotBox.ShowError(error);
            return;
        }

        if (shortcut is null)
        {
            ShowStatus(StringsUi.Get("S.Settings.Shortcut.NeedModifier"), error: true);
            return;
        }

        _store.Current.ShortcutPair(out _, out var pin);
        if (!AppSettings.ValidateShortcut(shortcut.Format(), pin, out var validated, out var conflict) || validated is null)
        {
            var text = DescribeShortcutRejection(shortcut, pin, "S.Settings.Shortcut.Pin");
            ShowStatus(text, error: true);
            _screenshotBox.ShowError(text);
            AppLog.Warn("Settings", $"截图快捷键被拒绝：{shortcut.Format()}（{text}）");
            return;
        }

        var next = _store.Current.WithScreenshotShortcut(validated.Format());
        ApplyShortcutChange(next, "截图", validated.Format());
    }

    private void OnPinShortcutCommitted(ShortcutSpec? shortcut, string error)
    {
        if (error.Length > 0)
        {
            ShowStatus(error, error: true);
            _pinBox.ShowError(error);
            return;
        }

        if (shortcut is null)
        {
            ShowStatus(StringsUi.Get("S.Settings.Shortcut.NeedModifier"), error: true);
            return;
        }

        _store.Current.ShortcutPair(out var screenshot, out _);
        if (!AppSettings.ValidateShortcut(shortcut.Format(), screenshot, out var validated, out var conflict) || validated is null)
        {
            var text = DescribeShortcutRejection(shortcut, screenshot, "S.Settings.Shortcut.Screenshot");
            ShowStatus(text, error: true);
            _pinBox.ShowError(text);
            AppLog.Warn("Settings", $"贴图快捷键被拒绝：{shortcut.Format()}（{text}）");
            return;
        }

        var next = _store.Current.WithPinShortcut(validated.Format());
        ApplyShortcutChange(next, "贴图", validated.Format());
    }

    /// <summary>
    /// 把"快捷键被拒"的原因整理成面向用户的本地化文案（T31）。
    /// </summary>
    /// <remarks>
    /// 与另一个功能重复时用 <c>ConflictLocal</c>（指明撞的是哪个功能、它的当前组合）；
    /// 其它原因（缺修饰键、主键不支持）直接用设置窗口自己的文案，**不再去猜 Core 的中文错误串**。
    /// </remarks>
    private static string DescribeShortcutRejection(
        ShortcutSpec shortcut,
        ShortcutSpec? other,
        string otherLabelKey)
    {
        if (other is not null && shortcut.ConflictsWith(other))
        {
            return StringsUi.Format(
                "S.Settings.Shortcut.ConflictLocal",
                StringsUi.Get(otherLabelKey),
                other.Format());
        }

        return StringsUi.Get("S.Settings.Shortcut.NeedModifier");
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
            _screenshotBox.ShowRegistered();
            _pinBox.ShowRegistered();
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
        _pinBox.SetValue(previous.PinShortcut);
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

    private void ShowStatus(string message, bool error)
    {
        _status.Text = message;
        _status.SetResourceReference(
            TextBlock.ForegroundProperty,
            error ? ThemeManager.BrushKeys.Error : ThemeManager.BrushKeys.Success);
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
        _subtitle.Text = StringsUi.Get("S.Settings.Subtitle");
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
        FontSize = 13;
        FontWeight = FontWeights.SemiBold;
        Padding = new Thickness(10, 6, 10, 6);
        MinWidth = 168;
        TextAlignment = TextAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        Cursor = Cursors.Hand;
        ToolTip = StringsUi.Get("S.Settings.Shortcut.Hint");
        AutomationProperties.SetAutomationId(this, automationId);
        AutomationProperties.SetName(this, StringsUi.Get("S.Settings.Shortcut.Hint"));

        ApplyPalette();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += (_, e) => e.Handled = true;

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

        // Esc 放弃本次修改；Tab 交给焦点系统（否则键盘无法离开这个输入框）。
        if (e.Key == Key.Escape)
        {
            _pendingModifiers = string.Empty;
            ShowRegistered();
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = false;
            return;
        }

        var modifiers = Keyboard.Modifiers switch
        {
            ModifierKeys.Control => ShortcutModifiers.Control,
            ModifierKeys.Alt => ShortcutModifiers.Alt,
            ModifierKeys.Shift => ShortcutModifiers.Shift,
            ModifierKeys.Windows => ShortcutModifiers.Win,
            _ => ShortcutModifiers.None,
        };

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

        _ = modifiers;
        var virtualKey = KeyInterop.VirtualKeyFromKey(e.Key);

        // 只按了修饰键：显示提示，等待主键（用户可以继续按住组合）。
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
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
