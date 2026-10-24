using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeepShot.Core.History;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// 重命名对话框（T26）：用 <see cref="HistoryRename"/> 校验，非法输入就地显示原因、不关闭。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 <c>Microsoft.VisualBasic.Interaction.InputBox</c> 或 WinForms 的输入框</b>：
/// 前者要引 VB 运行时且外观是 1990 年代的；后者会启用 <c>UseWindowsForms</c>，
/// 与 WPF 的 <see cref="Application"/> 名称冲突（AGENTS.md 明确禁止）。
/// </para>
/// <para>
/// 校验在中立位置进行：**先校验再返回 <see cref="DialogResult"/>**，因此调用方拿到的
/// <see cref="Result"/> 一定是能直接写库的名称（清洗过、不含非法字符、非空）。
/// Enter = 确定，Esc = 取消。
/// </para>
/// <para>
/// 外观与 <see cref="AppDialogWindow"/> 同一套规格：<see cref="UiKit.StatusBadge"/> 徽标 + 副标题、
/// <see cref="Tokens.DialogPadding"/> 内边距、<see cref="UiKit.FooterBar"/> 底栏里右对齐按钮；
/// 输入框走 <see cref="UiKit.ValidatedField"/>，校验失败时红色描边 + 行内错误提示。
/// </para>
/// </remarks>
internal sealed class HistoryRenameDialog : Window
{
    private readonly TextBox _input;
    private readonly Grid _field;
    private readonly Grid _errorRow;
    private readonly TextBlock _error;
    private readonly string _currentName;

    /// <param name="owner">父窗口（图库）。</param>
    /// <param name="currentName">当前名称（预填并全选，便于直接覆盖输入）。</param>
    internal HistoryRenameDialog(Window owner, string currentName)
    {
        _currentName = currentName ?? string.Empty;

        var title = StringsUi.Get("S.History.RenameTitle");
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, UiKit.Keys.Window);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "history-rename-dialog");
        AppChrome.SyncDarkTitleBar(this);

        // 头部：圆形状态徽标 + 副标题（与提示框 / 检查更新窗口同一套徽标与间距）。
        var tile = UiKit.StatusBadge(AppIcons.Rename, UiKit.Keys.Accent, 44);
        tile.VerticalAlignment = VerticalAlignment.Center;
        var heading = UiKit.Text(title, TextRole.Subtitle, wrap: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.Margin = new Thickness(Tokens.Space.L, 0, 0, 0);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(heading, 1);
        header.Children.Add(tile);
        header.Children.Add(heading);

        // 字段标签：小号强调字（次要色），与输入框左缘对齐。
        var label = UiKit.Text(StringsUi.Get("S.History.RenameLabel"), TextRole.Caption);
        label.FontWeight = AppFonts.Semibold;
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.Margin = new Thickness(0, Tokens.Space.XL, 0, 0);

        _input = new TextBox
        {
            Text = _currentName,
            FontSize = Tokens.Text.BodyLarge,
            MinHeight = Tokens.Height.Large,
            Padding = new Thickness(Tokens.Space.M, Tokens.Space.S, Tokens.Space.M, Tokens.Space.S),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Tokens.Space.S, 0, 0),
            MaxLength = 120,
        };
        AutomationProperties.SetName(_input, StringsUi.Get("S.History.RenameInputName"));
        AutomationProperties.SetAutomationId(_input, "history-rename-input");
        _input.TextChanged += (_, _) => ClearError();
        _input.PreviewKeyDown += OnInputPreviewKeyDown;
        _field = UiKit.ValidatedField(_input);

        // 校验错误：图标 + 错误色说明，默认收起；出错时与输入框红色描边一起出现。
        _errorRow = UiKit.InlineStatus(AppIcons.Alert, UiKit.Keys.Error, out _error);
        _errorRow.Margin = new Thickness(0, Tokens.Space.S + 2, 0, 0);
        _errorRow.Visibility = Visibility.Collapsed;
        _error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_error, "history-rename-error");

        var hint = UiKit.Text(StringsUi.Get("S.History.RenameHint"), TextRole.Caption, wrap: true);
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        hint.Margin = new Thickness(0, Tokens.Space.S + 2, 0, 0);

        var okText = StringsUi.Get("S.History.RenameOk");
        var ok = UiKit.Button(okText, okText, TryAccept, ButtonKind.Primary, "history-rename-ok");
        ok.MinWidth = 88;
        ok.IsDefault = true;
        var cancelText = StringsUi.Get("S.History.RenameCancel");
        var cancel = UiKit.Button(cancelText, cancelText, () => { DialogResult = false; }, ButtonKind.Secondary, "history-rename-cancel");
        cancel.MinWidth = 88;
        cancel.IsCancel = true;

        // 内容区 + 底部操作栏（面板色底 + 顶部分隔线），与提示框、检查更新、设置窗口同款。
        var buttons = UiKit.ButtonBar(cancel, ok);

        var body = new StackPanel
        {
            Margin = new Thickness(Tokens.DialogPadding.Left, Tokens.DialogPadding.Top + 4, Tokens.DialogPadding.Right, Tokens.DialogPadding.Bottom),
        };
        body.Children.Add(header);
        body.Children.Add(label);
        body.Children.Add(_field);
        body.Children.Add(_errorRow);
        body.Children.Add(hint);

        var panel = new StackPanel();
        panel.Children.Add(body);
        panel.Children.Add(UiKit.FooterBar(buttons));
        Content = panel;
        AppChrome.Apply(this, allowMaximize: false, allowMinimize: false);

        if (owner is { IsVisible: true })
        {
            Owner = owner;
        }

        Loaded += (_, _) =>
        {
            Activate();
            _input.Focus();
            _input.SelectAll();
        };
    }

    /// <summary>校验通过的名称（只有 <see cref="Window.DialogResult"/> 为 true 时有效）。</summary>
    internal string Result { get; private set; } = string.Empty;

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter 在输入框里默认只提交默认按钮；这里显式走同一套校验，避免 IME 输入中误提交。
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryAccept();
        }
    }

    private void TryAccept()
    {
        if (!HistoryRename.TryNormalize(_input.Text, out var name, out _))
        {
            // Core 只有"名称为空"这一种失败原因，且文案是固定中文；界面层按当前语言取本地化文案。
            ShowError(StringsUi.Get("S.History.RenameEmpty"));
            return;
        }

        Result = name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        _errorRow.Visibility = Visibility.Visible;
        UiKit.SetFieldInvalid(_field, true);
        _input.Focus();
    }

    private void ClearError()
    {
        if (_error.Visibility == Visibility.Visible)
        {
            _error.Visibility = Visibility.Collapsed;
            _errorRow.Visibility = Visibility.Collapsed;
            UiKit.SetFieldInvalid(_field, false);
        }
    }
}
