using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeepShot.Core.History;
using DeepShot.Ui;

namespace DeepShot.History;

/// <summary>
/// “跳转到日期”弹层（组件库 popover 语言）：年切换 + 12 个月（带记录数）+ 该月每天（带记录数）。
/// 选中某一天后由调用方按日期索引里的 offset 跳到该天。
/// </summary>
internal sealed class DateJumpPopup
{
    private readonly Popup _popup;
    private readonly Border _frame;
    private readonly TextBlock _title;
    private readonly TextBlock _yearText;
    private readonly Button _previousYear;
    private readonly Button _nextYear;
    private readonly Button _today;
    private readonly Button _close;
    private readonly StackPanel _monthGrid;
    private readonly StackPanel _dayGrid;
    private readonly TextBlock _monthLabel;
    private HistoryDateIndex _index = HistoryDateIndex.Empty;
    private Action<DateOnly>? _onPick;
    private int _year;
    private int _month;

    internal DateJumpPopup()
    {
        _popup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Bottom,
            // 外层留 16/4 边距给投影底板，纵向偏移相应减掉；横向在 Show 里按按钮宽度右对齐。
            VerticalOffset = 2,
        };

        _title = UiKit.Text(StringsUi.Get("S.History.DateJump.Title"), TextRole.Subtitle);
        _yearText = UiKit.Text(string.Empty, TextRole.BodyStrong);
        _previousYear = Chevron(AppIcons.ChevronLeft, "history-date-prev-year", () => SetYear(_year - 1));
        _nextYear = Chevron(AppIcons.ChevronRight, "history-date-next-year", () => SetYear(_year + 1));
        _today = UiKit.GhostButton(StringsUi.Get("S.History.DateJump.Today"), null,
            StringsUi.Get("S.History.DateJump.Today"), PickToday);
        _today.Height = Tokens.Height.PageCompact;
        _today.Padding = new Thickness(Tokens.Space.S, 0, Tokens.Space.S, 0);
        AutomationProperties.SetAutomationId(_today, "history-date-today");

        _close = Chevron(AppIcons.Close, "history-date-close", Close);

        var header = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space.M) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_title);
        Grid.SetColumn(_today, 1);
        _today.HorizontalAlignment = HorizontalAlignment.Right;
        _today.Margin = new Thickness(0, 0, Tokens.Space.S, 0);
        header.Children.Add(_today);
        Grid.SetColumn(_close, 2);
        header.Children.Add(_close);

        var yearRow = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space.S) };
        yearRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        yearRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        yearRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        yearRow.Children.Add(_previousYear);
        Grid.SetColumn(_yearText, 1);
        _yearText.Margin = new Thickness(Tokens.Space.S, 0, Tokens.Space.S, 0);
        _yearText.VerticalAlignment = VerticalAlignment.Center;
        yearRow.Children.Add(_yearText);
        Grid.SetColumn(_nextYear, 2);
        _nextYear.HorizontalAlignment = HorizontalAlignment.Left;
        _nextYear.Margin = new Thickness(0);
        yearRow.Children.Add(_nextYear);

        _monthGrid = new StackPanel();
        _monthLabel = UiKit.Text(string.Empty, TextRole.Caption);
        _monthLabel.Margin = new Thickness(0, Tokens.Space.S, 0, Tokens.Space.XS);
        _dayGrid = new StackPanel();

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(yearRow);
        body.Children.Add(_monthGrid);
        body.Children.Add(_monthLabel);
        body.Children.Add(_dayGrid);

        _frame = new Border
        {
            Width = 344,
            Padding = new Thickness(Tokens.Space.L),
            CornerRadius = new CornerRadius(Tokens.Radius.Card),
            BorderThickness = new Thickness(1),
            Child = body,
        };
        _frame.SetResourceReference(Border.BackgroundProperty, UiKit.Keys.Surface);
        _frame.SetResourceReference(Border.BorderBrushProperty, UiKit.Keys.Border);

        // 投影放在内容后面的兄弟底板上，不挂在 _frame 上（否则整棵子树位图化、文字发虚）。
        var popupRoot = new Grid { Margin = new Thickness(16, 4, 16, 16) };
        popupRoot.Children.Add(UiKit.ShadowPlate(Tokens.Radius.Card, blur: 24, depth: 6, opacity: 0.16));
        popupRoot.Children.Add(_frame);
        _popup.Child = popupRoot;
        AutomationProperties.SetAutomationId(_frame, "history-date-popover");

        _popup.Closed += (_, _) => _onPick = null;
    }

    /// <summary>在某个按钮下方打开弹层。</summary>
    internal void Show(FrameworkElement target, HistoryDateIndex index, DateOnly? prefer, Action<DateOnly> onPick)
    {
        _index = index ?? HistoryDateIndex.Empty;
        _onPick = onPick;
        var initial = prefer ?? DateOnly.FromDateTime(DateTime.Now);
        _year = Math.Clamp(initial.Year, 1, 9999);
        _month = initial.Month;

        _popup.PlacementTarget = target;
        // 弹层右边与按钮右边对齐（外层左边距 16 + 面板宽度）。
        _popup.HorizontalOffset = target.ActualWidth - (16 + _frame.Width);
        BuildMonths();
        _popup.IsOpen = true;
    }

    private void SetYear(int year)
    {
        _year = Math.Clamp(year, 1, 9999);
        BuildMonths();
    }

    private void BuildMonths()
    {
        _yearText.Text = StringsUi.Format("S.History.DateJump.Year", _year);
        _monthGrid.Children.Clear();
        _dayGrid.Children.Clear();
        _monthLabel.Visibility = Visibility.Collapsed;

        var grid = new Grid();
        for (var column = 0; column < 4; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var month = 1; month <= 12; month++)
        {
            var entry = _index.FindMonth(_year, month);
            var button = new Button
            {
                Height = 34,
                Margin = new Thickness(2),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsEnabled = entry is not null,
                Content = MonthContent(month, entry),
                ToolTip = entry is null
                    ? StringsUi.Get("S.History.DateJump.NoRecords")
                    : StringsUi.Format("S.History.DateJump.Count", entry.Count),
            };
            UiKit.StyleButton(button, ButtonKind.Ghost);
            var captured = month;
            button.Click += (_, _) => { _month = captured; BuildDays(); };
            AutomationProperties.SetAutomationId(button, $"history-date-month-{_year:D4}-{month:D2}");
            AutomationProperties.SetName(button, MonthText(month));
            Grid.SetColumn(button, (month - 1) % 4);
            Grid.SetRow(button, (month - 1) / 4);
            grid.Children.Add(button);
        }

        _monthGrid.Children.Add(grid);
    }

    private FrameworkElement MonthContent(int month, HistoryMonthEntry? entry)
    {
        var text = UiKit.Text(MonthText(month), Tokens.Text.Body, measureNaturalWidth: true);
        if (entry is null)
        {
            text.Opacity = 0.45;
            return text;
        }

        var count = UiKit.Text(entry.Count.ToString(CultureInfo.InvariantCulture), 10,
            brushKey: ThemeManager.BrushKeys.TertiaryForeground);
        count.Margin = new Thickness(0, 1, 0, 0);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(text);
        stack.Children.Add(count);
        return stack;
    }

    private void BuildDays()
    {
        _monthLabel.Text = StringsUi.IsChinese
            ? $"{_year} 年 {_month} 月"
            : new DateTime(_year, _month, 1).ToString("yyyy MMMM", CultureInfo.CurrentUICulture);
        _monthLabel.Visibility = Visibility.Visible;
        _dayGrid.Children.Clear();
        _monthGrid.Children.Clear();

        // 返回月份列表的小按钮
        var back = UiKit.GhostButton(StringsUi.Get("S.History.DateJump.BackToMonths"), AppIcons.ChevronLeft,
            StringsUi.Get("S.History.DateJump.BackToMonths"), BuildMonths);
        back.Height = Tokens.Height.PageCompact;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Padding = new Thickness(0);
        _monthGrid.Children.Add(back);

        var grid = new Grid();
        for (var column = 0; column < 7; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var weekdays = StringsUi.IsChinese
            ? new[] { "一", "二", "三", "四", "五", "六", "日" }
            : new[] { "M", "T", "W", "T", "F", "S", "S" };
        for (var i = 0; i < 7; i++)
        {
            var label = UiKit.Text(weekdays[i], 10, brushKey: ThemeManager.BrushKeys.TertiaryForeground);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetColumn(label, i);
            grid.Children.Add(label);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dayCount = DateTime.DaysInMonth(_year, _month);
        var first = new DateTime(_year, _month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // 周一为第一列
        for (var day = 1; day <= dayCount; day++)
        {
            var date = new DateOnly(_year, _month, day);
            var entry = _index.FindDay(date);
            var button = new Button
            {
                Height = 28,
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                IsEnabled = entry is not null,
                Content = UiKit.Text(day.ToString(CultureInfo.InvariantCulture), Tokens.Text.Caption,
                    measureNaturalWidth: true),
                ToolTip = entry is null
                    ? StringsUi.Get("S.History.DateJump.NoRecords")
                    : StringsUi.Format("S.History.DateJump.Count", entry.Count),
            };
            UiKit.StyleButton(button, ButtonKind.Ghost);
            button.Click += (_, _) =>
            {
                var picked = date;
                var onPick = _onPick;
                _popup.IsOpen = false;
                onPick?.Invoke(picked);
            };
            AutomationProperties.SetAutomationId(button, $"history-date-day-{date:yyyy-MM-dd}");
            AutomationProperties.SetName(button, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var cell = (offset + day - 1);
            Grid.SetColumn(button, cell % 7);
            Grid.SetRow(button, 1 + cell / 7);
            if (cell / 7 + 1 > grid.RowDefinitions.Count - 1)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            grid.Children.Add(button);
        }

        _dayGrid.Children.Add(grid);
    }

    private void PickToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var onPick = _onPick;
        _popup.IsOpen = false;
        onPick?.Invoke(today);
    }

    private void Close() => _popup.IsOpen = false;

    private static Button Chevron(string icon, string automationId, Action onClick)
    {
        var button = new Button
        {
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = AppIcons.Create(icon, 14, UiKit.Keys.Secondary),
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, UiKit.StyleKeys.Ghost);
        button.Click += (_, _) => onClick();
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    private static string MonthText(int month) => StringsUi.IsChinese
        ? $"{month} 月"
        : new DateTime(2000, month, 1).ToString("MMM", CultureInfo.CurrentUICulture);
}
