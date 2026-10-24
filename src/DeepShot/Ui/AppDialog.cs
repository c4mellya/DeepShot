using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeepShot.Core;
using DeepShot.History;
using DeepShot.Platform;

namespace DeepShot.Ui;

/// <summary>提示框的类型（决定图标与颜色）。</summary>
internal enum AppDialogKind
{
    Information,
    Question,
    Warning,
    Error,
}

/// <summary>
/// 应用自绘提示框：替代 Windows 原生 <see cref="MessageBox"/>，跟随主题、语言和自绘标题栏。
/// </summary>
/// <remarks>
/// <para>
/// <b>两个入口</b>：<see cref="Alert"/>（只有“确定”）与 <see cref="Confirm"/>（“确定/取消”，返回是否确认）。
/// 删除类确认传 <c>destructive: true</c>，确认按钮变红，且默认焦点在“取消”上，防止回车误删。
/// </para>
/// <para>
/// <b>兜底</b>：主题资源还没建好（例如启动极早期）或自绘窗口本身出错时，退回原生 MessageBox，
/// 保证关键错误提示永远不会静默丢失。
/// </para>
/// </remarks>
internal static class AppDialog
{
    /// <summary>只有“确定”的提示。</summary>
    internal static void Alert(Window? owner, string message, string title, AppDialogKind kind = AppDialogKind.Information)
    {
        Show(owner, message, title, kind, confirm: false, destructive: false, okText: null, cancelText: null);
    }

    /// <summary>“确定/取消”确认框，返回用户是否点了确定（关闭窗口 / Esc 视为取消）。</summary>
    /// <param name="destructive">破坏性操作：确认按钮用红色，默认焦点放在取消上。</param>
    internal static bool Confirm(
        Window? owner,
        string message,
        string title,
        AppDialogKind kind = AppDialogKind.Question,
        bool destructive = false,
        string? okText = null,
        string? cancelText = null)
    {
        return Show(owner, message, title, kind, confirm: true, destructive, okText, cancelText);
    }

    private static bool Show(
        Window? owner,
        string message,
        string title,
        AppDialogKind kind,
        bool confirm,
        bool destructive,
        string? okText,
        string? cancelText)
    {
        try
        {
            if (IsThemeReady())
            {
                var dialog = new AppDialogWindow(owner, message, title, kind, confirm, destructive, okText, cancelText);
                return dialog.ShowDialog() == true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Dialog", "自绘提示框显示失败，退回系统提示框", ex);
        }

        return FallbackToNative(owner, message, title, kind, confirm);
    }

    private static bool IsThemeReady() =>
        Application.Current is { } app && app.Resources.Contains(ThemeManager.BrushKeys.WindowBackground);

    private static bool FallbackToNative(Window? owner, string message, string title, AppDialogKind kind, bool confirm)
    {
        var image = kind switch
        {
            AppDialogKind.Error => MessageBoxImage.Error,
            AppDialogKind.Warning => MessageBoxImage.Warning,
            AppDialogKind.Question => MessageBoxImage.Question,
            _ => MessageBoxImage.Information,
        };
        var buttons = confirm ? MessageBoxButton.OKCancel : MessageBoxButton.OK;
        var result = owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
        return result == MessageBoxResult.OK;
    }
}

/// <summary>自绘提示框窗口本体（只由 <see cref="AppDialog"/> 创建）。</summary>
internal sealed class AppDialogWindow : Window
{
    internal AppDialogWindow(
        Window? owner,
        string message,
        string title,
        AppDialogKind kind,
        bool confirm,
        bool destructive,
        string? okText,
        string? cancelText)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, ThemeManager.BrushKeys.WindowBackground);
        SetResourceReference(FontFamilyProperty, ThemeManager.FontFamilyKey);
        AutomationProperties.SetAutomationId(this, "app-dialog");
        AutomationProperties.SetName(this, title);
        AppChrome.SyncDarkTitleBar(this);

        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
        }
        else
        {
            // 没有父窗口（托盘程序的启动/全局错误）：居中、置顶、在任务栏可见，确保用户看得到。
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Topmost = true;
        }

        // 图标徽标（统一走 UiKit.StatusBadge：淡色圆底 + 细环 + 同色矢量图标）。
        var (iconData, toneKey) = ToneFor(kind, destructive);
        var badge = UiKit.StatusBadge(iconData, toneKey, 44);

        // 文字区：标题不是应用名时（如"删除截图"）在正文上方再给一行醒目标题，
        // 让对话框一眼就说清"要做什么"；应用名已经在标题栏里，不重复。
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, AppInfo.Name, StringComparison.OrdinalIgnoreCase))
        {
            var heading = UiKit.Text(title, TextRole.Subtitle, wrap: true);
            heading.Margin = new Thickness(0, 0, 0, Tokens.Space.XS + 2);
            AutomationProperties.SetAutomationId(heading, "app-dialog-heading");
            textStack.Children.Add(heading);
        }

        var body = UiKit.Text(message, TextRole.BodyLarge, wrap: true);
        body.LineHeight = 22;
        AutomationProperties.SetAutomationId(body, "app-dialog-message");
        AutomationProperties.SetName(body, message);
        textStack.Children.Add(body);

        // 长文本（异常信息）限高滚动，避免对话框撑出屏幕。
        var scroller = new ScrollViewer
        {
            Content = textStack,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(Tokens.Space.L, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(scroller, 1);
        content.Children.Add(badge);
        content.Children.Add(scroller);

        // 按钮：确认 = 主按钮（破坏性操作 = 危险红），取消 = 次按钮；统一 UiKit 规格。
        var okLabel = okText ?? StringsUi.Get("S.Dialog.Ok");
        var ok = UiKit.Button(okLabel, okLabel, Accept, destructive ? ButtonKind.Danger : ButtonKind.Primary, "app-dialog-ok");
        ok.MinWidth = 88;

        Button focusTarget = ok;
        Button? cancel = null;
        if (confirm)
        {
            var cancelLabel = cancelText ?? StringsUi.Get("S.Dialog.Cancel");
            cancel = UiKit.Button(cancelLabel, cancelLabel, () => DialogResult = false, ButtonKind.Secondary, "app-dialog-cancel");
            cancel.MinWidth = 88;
            cancel.IsCancel = true;

            // 破坏性操作默认落在“取消”，回车不会误删。
            if (destructive)
            {
                cancel.IsDefault = true;
                focusTarget = cancel;
            }
            else
            {
                ok.IsDefault = true;
            }
        }
        else
        {
            ok.IsDefault = true;
            ok.IsCancel = true;
        }

        // 内容区 + 底部操作栏（面板色底 + 顶部分隔线，与设置窗口的底栏同款）。
        var buttons = cancel is null ? UiKit.ButtonBar(ok) : UiKit.ButtonBar(cancel, ok);
        content.Margin = new Thickness(Tokens.DialogPadding.Left, Tokens.DialogPadding.Top + 4, Tokens.DialogPadding.Right, Tokens.DialogPadding.Bottom);

        var panel = new StackPanel();
        panel.Children.Add(content);
        panel.Children.Add(UiKit.FooterBar(buttons));
        Content = panel;
        AppChrome.Apply(this, allowMaximize: false, allowMinimize: false);

        Loaded += (_, _) =>
        {
            Activate();
            focusTarget.Focus();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
    }

    private void Accept() => DialogResult = true;

    private static (string Icon, string ToneKey) ToneFor(AppDialogKind kind, bool destructive) => kind switch
    {
        AppDialogKind.Error => (AppIcons.Close, ThemeManager.BrushKeys.Error),
        AppDialogKind.Warning => (AppIcons.Warning, destructive ? ThemeManager.BrushKeys.Error : ThemeManager.BrushKeys.Warning),
        AppDialogKind.Question => (AppIcons.Help, ThemeManager.BrushKeys.Accent),
        _ => (AppIcons.Info, ThemeManager.BrushKeys.Accent),
    };
}
