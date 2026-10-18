using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZeepCut.Core.History;

namespace ZeepCut.History;

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
/// </remarks>
internal sealed class HistoryRenameDialog : Window
{
    private readonly TextBox _input;
    private readonly TextBlock _error;
    private readonly string _currentName;

    /// <param name="owner">父窗口（图库）。</param>
    /// <param name="currentName">当前名称（预填并全选，便于直接覆盖输入）。</param>
    internal HistoryRenameDialog(Window owner, string currentName)
    {
        _currentName = currentName ?? string.Empty;

        Title = "重命名历史记录";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = HistoryUi.WindowBackground;
        FontFamily = new FontFamily(HistoryUi.FontFamilyName);
        AutomationProperties.SetAutomationId(this, "history-rename-dialog");

        _input = new TextBox
        {
            Text = _currentName,
            FontSize = 14,
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 4, 0, 2),
            MaxLength = 120,
        };
        AutomationProperties.SetName(_input, "历史记录名称");
        AutomationProperties.SetAutomationId(_input, "history-rename-input");
        _input.TextChanged += (_, _) => ClearError();
        _input.PreviewKeyDown += OnInputPreviewKeyDown;

        _error = HistoryUi.Text(string.Empty, 12, HistoryUi.ErrorForeground);
        _error.TextWrapping = TextWrapping.Wrap;
        _error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(_error, "history-rename-error");

        var hint = HistoryUi.Text(
            "名称用于图库显示与搜索；不能包含 \\ / : * ? \" < > | 这些字符（会被替换为 _）。",
            11.5,
            HistoryUi.SecondaryForeground);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 6, 0, 0);

        var ok = HistoryUi.Button("确定", "确定重命名", TryAccept, primary: true);
        AutomationProperties.SetAutomationId(ok, "history-rename-ok");
        ok.IsDefault = true;
        var cancel = HistoryUi.Button("取消", "取消重命名", () => { DialogResult = false; });
        AutomationProperties.SetAutomationId(cancel, "history-rename-cancel");
        cancel.IsCancel = true;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        panel.Children.Add(HistoryUi.Text("名称", 12.5, HistoryUi.SecondaryForeground));
        panel.Children.Add(_input);
        panel.Children.Add(_error);
        panel.Children.Add(hint);
        panel.Children.Add(buttons);
        Content = panel;

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
        if (!HistoryRename.TryNormalize(_input.Text, out var name, out var reason))
        {
            ShowError(reason ?? HistoryRename.EmptyNameMessage);
            return;
        }

        Result = name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
        _input.Focus();
    }

    private void ClearError()
    {
        if (_error.Visibility == Visibility.Visible)
        {
            _error.Visibility = Visibility.Collapsed;
        }
    }
}
