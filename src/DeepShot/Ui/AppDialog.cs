using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
    private const string TriangleIcon = "M12,3 L22,20 H2 Z M12,10 V14 M12,17 V17.5";
    private const string Circle = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3";

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
        SourceInitialized += (_, _) => DwmApi.TrySetDarkTitleBar(this, ThemeManager.Palette.IsDark);

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

        // 图标徽标：淡色圆底 + 矢量图标。
        var (iconData, toneKey) = ToneFor(kind, destructive);
        var tint = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Opacity = 0.16 };
        tint.SetResourceReference(Border.BackgroundProperty, toneKey);
        var glyph = HistoryIcons.Create(iconData, 22, toneKey, 2.0);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var badge = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top };
        badge.Children.Add(tint);
        badge.Children.Add(glyph);

        var body = new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, ThemeManager.BrushKeys.Foreground);
        AutomationProperties.SetAutomationId(body, "app-dialog-message");
        AutomationProperties.SetName(body, message);

        // 长文本（异常信息）限高滚动，避免对话框撑出屏幕。
        var scroller = new ScrollViewer
        {
            Content = body,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(scroller, 1);
        content.Children.Add(badge);
        content.Children.Add(scroller);

        var ok = HistoryUi.Button(okText ?? StringsUi.Get("S.Dialog.Ok"), okText ?? StringsUi.Get("S.Dialog.Ok"), Accept, primary: true);
        AutomationProperties.SetAutomationId(ok, "app-dialog-ok");
        ok.Margin = new Thickness(0);
        if (destructive)
        {
            ok.SetResourceReference(Control.BackgroundProperty, ThemeManager.BrushKeys.Error);
            ok.SetResourceReference(Control.BorderBrushProperty, ThemeManager.BrushKeys.Error);
            ok.SetResourceReference(Control.ForegroundProperty, ThemeManager.BrushKeys.OnAccent);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0),
        };

        Button focusTarget = ok;
        if (confirm)
        {
            var cancelLabel = cancelText ?? StringsUi.Get("S.Dialog.Cancel");
            var cancel = HistoryUi.Button(cancelLabel, cancelLabel, () => DialogResult = false);
            AutomationProperties.SetAutomationId(cancel, "app-dialog-cancel");
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);

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

        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        panel.Children.Add(content);
        panel.Children.Add(buttons);
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
        AppDialogKind.Error => (Circle + " M9,9 L15,15 M15,9 L9,15", ThemeManager.BrushKeys.Error),
        AppDialogKind.Warning => (TriangleIcon, destructive ? ThemeManager.BrushKeys.Error : ThemeManager.BrushKeys.Warning),
        AppDialogKind.Question => (Circle + " M9.5,9.5 A2.5,2.5 0 1 1 12,12 V14 M12,17 V17.5", ThemeManager.BrushKeys.Accent),
        _ => (Circle + " M12,11 V17 M12,7.5 V8", ThemeManager.BrushKeys.Accent),
    };
}
