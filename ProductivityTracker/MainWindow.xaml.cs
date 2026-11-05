using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace ProductivityTracker
{
    public sealed partial class MainWindow : Window
    {
        private readonly UsageStore _store;
        private readonly UsageTracker _tracker;
        private readonly DispatcherQueueTimer _refreshTimer;
        private readonly UISettings _uiSettings = new();
        private readonly AccessibilitySettings _accessibilitySettings = new();
        private readonly DashboardCard[] _dayCards =
        {
            new(AppStrings.Get("ActiveShareTitle")),
            new(AppStrings.Get("MostActiveHourTitle"), true),
            new(AppStrings.Get("LeastActiveHourTitle"), true),
            new(AppStrings.Get("LongestActiveTitle")),
            new(AppStrings.Get("LongestInactiveTitle")),
            new(AppStrings.Get("MostUsedAppTitle")),
        };
        private readonly DashboardCard[] _weekdayCards =
        {
            new(AppStrings.Get("MostActiveWeekdayTitle")),
            new(AppStrings.Get("LeastActiveWeekdayTitle")),
        };
        private DateTime _selectedDate = DateTime.Today;
        private DateTime? _sessionDate;
        private bool _startupSettingInitialized;
        private bool _syncingSettings;
        private bool _backgroundWorkerRunning;
        private bool _userPaused;
        private bool _viewReady;
        private bool _isClosing;
        private bool _restoringDate;
        private bool _hasDayData;
        private bool _hasWeekData;
        private string _weekFootnote = string.Empty;
        private FrameworkElement _weeklyChart = null!;
        private FrameworkElement _hourlyChart = null!;

        public ObservableCollection<ActivityRow> Sessions { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ProductivityTracker.ico"));
            AppWindow.Resize(new SizeInt32(1180, 760));
            ApplyRoundedWindowCorners();
            Activated += MainWindow_Activated;
            AppWindow.Changed += AppWindow_Changed;

            _store = new UsageStore();
            _tracker = new UsageTracker(_store)
            {
                IdleThresholdSeconds = UsageTrackerSettings.GetIdleThresholdSeconds(),
            };
            _tracker.UsageChanged += Tracker_UsageChanged;
            _userPaused = UsageTrackerSettings.IsPaused();
            _backgroundWorkerRunning = BackgroundWorkerManager.IsRunning();

            _refreshTimer = DispatcherQueue.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(2);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.IsRepeating = true;
            _uiSettings.TextScaleFactorChanged += TextScaleFactorChanged;
            _uiSettings.ColorValuesChanged += ChartThemeChanged;
            RootGrid.ActualThemeChanged += ChartThemeChanged;
            RootGrid.Loaded += RootGrid_Loaded;
            Closed += MainWindow_Closed;

            CreateCards(DayCardsGrid, _dayCards);
            CreateCards(WeekdayCardsGrid, _weekdayCards);
            InitializeActivityControls();
            UsageDatePicker.MaxDate = new DateTimeOffset(DateTime.Today);
            UsageDatePicker.Date = new DateTimeOffset(_selectedDate);
            _viewReady = true;
            ApplyChartBrushes();
            RefreshData();
            ApplyResponsiveLayout();
            _refreshTimer.Start();
        }

        private void CreateCards(Grid host, IEnumerable<DashboardCard> cards)
        {
            var template = (DataTemplate)RootGrid.Resources["InsightCardTemplate"];
            foreach (var card in cards)
            {
                var element = (FrameworkElement)template.LoadContent();
                element.DataContext = card;
                host.Children.Add(element);
            }
        }

        private void Tracker_UsageChanged(object? sender, EventArgs e)
        {
            if (!_isClosing)
            {
                DispatcherQueue.TryEnqueue(RefreshData);
            }
        }

        private void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            RefreshData();
        }

        private void RefreshTrackingState()
        {
            _userPaused = UsageTrackerSettings.IsPaused();
            _backgroundWorkerRunning = BackgroundWorkerManager.IsRunning();
            if (_backgroundWorkerRunning)
            {
                _tracker.StopWithoutFlush();
            }
            else if (_userPaused)
            {
                _tracker.Stop();
            }
            else if (!_tracker.IsTracking)
            {
                _tracker.Start();
            }
            TrackingButton.Content = AppStrings.Get(_userPaused ? "Resume" : "Pause");
        }

        private void RefreshData()
        {
            if (!_viewReady || _isClosing)
            {
                return;
            }

            try
            {
                RefreshTrackingState();
                if (_backgroundWorkerRunning)
                {
                    _store.ReloadFromDisk();
                }

                var summary = _store.GetDailySummary(_selectedDate);
                var sessions = _store.GetSessions(_selectedDate);
                var recordedSeconds = summary.ActiveSeconds + summary.InactiveSeconds;
                SelectedDateText.Text = recordedSeconds > 0
                    ? AppStrings.Format("SelectedDayRecordedFormat",
                        AppStrings.Date(_selectedDate, "DashboardDateFormat"), FormatDuration(recordedSeconds),
                        AppStrings.Format("SessionCountFormat", summary.SessionCount))
                    : AppStrings.Format("SelectedDayUnrecordedFormat",
                        AppStrings.Date(_selectedDate, "DashboardDateFormat"));
                InactiveBreakdownText.Text = recordedSeconds > 0
                    ? AppStrings.Format("InactiveBreakdownFormat", FormatDuration(summary.InactiveSeconds),
                        FormatDuration(summary.IdleSeconds), FormatDuration(summary.LockedSeconds),
                        FormatDuration(summary.SleepSeconds), FormatDuration(summary.PoweredOffSeconds))
                    : string.Empty;
                InactiveBreakdownText.Visibility = recordedSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
                SessionCountText.Text = summary.SessionCount > 0
                    ? AppStrings.Format("SessionCountFormat", summary.SessionCount) : AppStrings.Get("NotRecorded");
                UpdateSessions(sessions);

                StatusText.Text = _backgroundWorkerRunning
                    ? AppStrings.Get(_userPaused ? "BackgroundPaused" : "BackgroundActive")
                    : AppStrings.Get(_tracker.IsTracking ? "TrackingHere" : "TrackingPaused");
                if (!string.IsNullOrWhiteSpace(_store.LastError))
                {
                    StatusText.Text += " - " + _store.LastError;
                }
                if (!string.IsNullOrWhiteSpace(_tracker.SystemHistoryWarning))
                {
                    StatusText.Text += " - " + _tracker.SystemHistoryWarning;
                }

                var insights = UsageAnalytics.Analyze(
                    _store.GetSessions(
                        _selectedDate.AddDays(1 - UsageAnalytics.HistoryDays),
                        _selectedDate.AddDays(1)),
                    _selectedDate);
                UpdateCards(insights, summary);
                UpdateCharts(insights);
                UpdateDateNavigation();
            }
            catch (Exception ex)
            {
                App.Log("RefreshData failed: " + ex);
                throw;
            }
        }

        private void UpdateSessions(IReadOnlyList<UsageSegment> sessions)
        {
            if (_sessionDate != _selectedDate)
            {
                Sessions.Clear();
                _sessionDate = _selectedDate;
            }

            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (index >= Sessions.Count || Sessions[index].StartUtc != session.StartUtc)
                {
                    var existingIndex = -1;
                    for (var candidate = index + 1; candidate < Sessions.Count; candidate++)
                    {
                        if (Sessions[candidate].StartUtc == session.StartUtc)
                        {
                            existingIndex = candidate;
                            break;
                        }
                    }
                    if (existingIndex >= 0)
                    {
                        Sessions.Move(existingIndex, index);
                    }
                    else
                    {
                        Sessions.Insert(index, new ActivityRow(session.StartUtc));
                    }
                }

                Sessions[index].Update(
                    session.StartUtc.ToLocalTime().ToString("T", AppStrings.Culture),
                    session.EndUtc.ToLocalTime().ToString("T", AppStrings.Culture),
                    session.IsActive ? LocalizeApplicationName(session.AppName) : AppStrings.Get("NoActiveInteraction"),
                    FormatDuration((session.EndUtc - session.StartUtc).TotalSeconds),
                    session.IsActive ? AppStrings.Get("Active") : AppStrings.Get(session.InactiveReason switch
                    {
                        InactivityReason.Locked => "Locked",
                        InactivityReason.Sleep => "Sleeping",
                        InactivityReason.PoweredOff => "PoweredOff",
                        _ => "Idle",
                    }));
            }
            while (Sessions.Count > sessions.Count)
            {
                Sessions.RemoveAt(Sessions.Count - 1);
            }
            UpdateTimelineEmptyState();
        }

        private void UpdateCards(ActivityInsights insights, DailyUsageSummary summary)
        {
            var hasDayRecordings = insights.RecordedSeconds > 0;
            _dayCards[0].Update(
                insights.ActiveShare?.ToString("P0", AppStrings.Culture) ??
                    AppStrings.Get(hasDayRecordings ? "NoAwakeData" : "NotRecorded"),
                insights.AwakeRecordedSeconds > 0
                    ? AppStrings.Format("ActiveAwakeFormat",
                        FormatDuration(insights.ActiveSeconds), FormatDuration(insights.AwakeRecordedSeconds))
                    : AppStrings.Get("NoAwakeData"),
                AppStrings.Get("PowerExcluded"),
                insights.ActiveShare.HasValue);
            UpdateHourCard(_dayCards[1], insights.MostActiveHour, insights.RecordedSeconds > 0
                ? AppStrings.Get("NoActiveHour") : AppStrings.Get("NotRecorded"));
            UpdateHourCard(_dayCards[2], insights.LeastActiveHour, insights.RecordedSeconds > 0
                ? AppStrings.Get("NoDifference") : AppStrings.Get("NotRecorded"));
            UpdateStretchCard(_dayCards[3], insights.LongestActiveStretch, AppStrings.Get("NoActiveStretch"), hasDayRecordings);
            UpdateStretchCard(_dayCards[4], insights.LongestInactiveStretch, AppStrings.Get("NoInactiveStretch"), hasDayRecordings);
            var topApp = summary.Applications.FirstOrDefault();
            _dayCards[5].Update(
                topApp == null ? AppStrings.Get("NotRecorded") : AppStrings.Isolate(LocalizeApplicationName(topApp.AppName)),
                topApp == null ? AppStrings.Get("NoTopApp")
                    : AppStrings.Format("AppActiveTimeFormat", FormatDuration(topApp.ActiveSeconds)),
                topApp == null ? AppStrings.Get("SelectedDay")
                    : AppStrings.Format("AppActiveShareFormat", topApp.ActiveSeconds / summary.ActiveSeconds),
                topApp != null);
            UpdateWeekdayCard(_weekdayCards[0], insights.MostActiveWeekday, AppStrings.Get("NoActiveWeekday"), insights.RecordedDays > 0);
            UpdateWeekdayCard(_weekdayCards[1], insights.LeastActiveWeekday, AppStrings.Get("NoDifference"), insights.RecordedDays > 0);
            InsightPeriodText.Text = AppStrings.Format("InsightPeriodFormat",
                AppStrings.Date(insights.HistoryStartDate, "MonthDayFormat"),
                AppStrings.Date(_selectedDate, "MonthDayFormat"), insights.RecordedDays);
        }

        private static void UpdateHourCard(DashboardCard card, HourlyActivity? hour, string empty)
        {
            card.Update(
                hour == null ? empty : AppStrings.Isolate(AppStrings.Format("HourRangeFormat", hour.Hour, hour.Hour + 1), true),
                hour == null ? AppStrings.Get("SelectedDay") : AppStrings.Format("ActivePercentFormat", hour.ActiveShare),
                hour == null
                    ? AppStrings.Get("HourRankingExplanation")
                    : AppStrings.Format("ActiveAwakeFormat",
                        FormatDuration(hour.ActiveSeconds), FormatDuration(hour.AwakeRecordedSeconds)),
                hour != null);
        }

        private static void UpdateStretchCard(DashboardCard card, ActivityStretch? stretch, string empty, bool hasRecordings)
        {
            card.Update(
                stretch == null ? (hasRecordings ? empty : AppStrings.Get("NotRecorded")) : FormatDuration(stretch.Seconds),
                stretch == null
                    ? empty
                    : AppStrings.Isolate(AppStrings.Format("TimeRangeFormat",
                        stretch.StartUtc.ToLocalTime(), stretch.EndUtc.ToLocalTime()), true),
                stretch == null
                    ? AppStrings.Get("ObservedIntervalsOnly")
                    : AppStrings.Get("StretchGapExplanation"),
                stretch != null, stretch != null);
        }

        private static void UpdateWeekdayCard(DashboardCard card, WeekdayActivity? weekday, string empty, bool hasRecordings)
        {
            card.Update(
                weekday == null ? (hasRecordings ? empty : AppStrings.Get("NoAwakeData")) : AppStrings.Culture.DateTimeFormat.GetDayName(weekday.Day),
                weekday == null
                    ? AppStrings.Get("WeekdayComparison")
                    : AppStrings.Format("WeekdayShareFormat", weekday.ActiveShare, weekday.RecordedDays),
                weekday == null
                    ? AppStrings.Get("WeekdayRankingExplanation")
                    : AppStrings.Format("WeekdayAverageFormat",
                        FormatDuration(weekday.AverageActiveSeconds), FormatDuration(weekday.AverageRecordedSeconds)),
                weekday != null);
        }

        private void UpdateCharts(ActivityInsights insights)
        {
            var days = new string[7];
            var active = new double[7];
            var inactive = new double[7];
            var missing = new List<string>();
            var maxHours = 1.0;
            _hasWeekData = false;
            for (var index = 0; index < days.Length; index++)
            {
                var day = _selectedDate.AddDays(index - 6);
                var summary = _store.GetDailySummary(day);
                days[index] = AppStrings.Date(day, "ChartDayFormat");
                active[index] = summary.ActiveSeconds / 3600;
                inactive[index] = summary.InactiveSeconds / 3600;
                if (summary.ActiveSeconds + summary.InactiveSeconds > 0)
                {
                    _hasWeekData = true;
                }
                else
                {
                    missing.Add(AppStrings.Date(day, "ChartDayFormat"));
                }
                maxHours = Math.Max(maxHours, Math.Max(active[index], inactive[index]));
                if (summary.ActiveSeconds + summary.InactiveSeconds == 0)
                {
                    active[index] = inactive[index] = double.NaN;
                }
            }
            _weekFootnote = missing.Count == 0
                ? AppStrings.Get("AllDaysRecorded")
                : AppStrings.Format("MissingDaysFormat", string.Join(AppStrings.Get("ListSeparator"), missing));

            SetActivityChartData(
                new ActivityChartData(days, active, inactive, maxHours * 1.08,
                    AppStrings.Get("DayAxis/Label"), AppStrings.Get("HoursAxis/Label")),
                new ActivityChartData(
                    insights.Hours.Select(hour => hour.Hour.ToString("00", AppStrings.Culture)).ToArray(),
                    insights.Hours.Select(hour => hour.ActiveMinutes).ToArray(),
                    insights.Hours.Select(hour => hour.InactiveMinutes).ToArray(),
                    Math.Max(60, insights.Hours.Max(hour => hour.RecordedSeconds) / 60) * 1.08,
                    AppStrings.Get("LocalHourAxis/Label"), AppStrings.Get("MinutesAxis/Label")));
            _hasDayData = insights.RecordedSeconds > 0;
            AutomationProperties.SetName(
                _weeklyChart, AppStrings.Format("WeeklyChartAutomationFormat", _selectedDate));
            AutomationProperties.SetName(
                _hourlyChart, AppStrings.Format("HourlyChartAutomationFormat", _selectedDate));
            UpdateChartView();
        }

        private void UpdateChartView()
        {
            if (!_viewReady || _isClosing)
            {
                return;
            }
            var byHour = ChartModeBar.SelectedItem == HourlyModeItem;
            var hasData = byHour ? _hasDayData : _hasWeekData;
            _weeklyChart.Visibility = !byHour && hasData ? Visibility.Visible : Visibility.Collapsed;
            _hourlyChart.Visibility = byHour && hasData ? Visibility.Visible : Visibility.Collapsed;
            ChartEmptyState.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            ShowWeekButton.Visibility = byHour && _hasWeekData ? Visibility.Visible : Visibility.Collapsed;
            ChartPeriodText.Text = byHour
                ? AppStrings.Date(_selectedDate, "DayDetailFormat")
                : AppStrings.Format("WeekEndingFormat", AppStrings.Date(_selectedDate, "MonthDayFormat"));
            ChartEmptyText.Text = byHour
                ? AppStrings.Format("EmptyDayChartFormat", AppStrings.Date(_selectedDate, "MonthDayFormat"))
                : AppStrings.Get("EmptyWeekChart");
            ChartFootnote.Text = byHour
                ? AppStrings.Get("HourlyFootnote")
                : _weekFootnote;
        }

        private void ChartModeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            UpdateChartView();
        }

        private void ShowWeekButton_Click(object sender, RoutedEventArgs e)
        {
            ChartModeBar.SelectedItem = WeekModeItem;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (!_isClosing)
                {
                    ApplyResponsiveLayout();
                    PageScroller.ChangeView(null, 0, null, true);
                }
            });
        }

        private void Layout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveLayout();
        }

        private void TextScaleFactorChanged(UISettings sender, object args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyResponsiveLayout();
                ApplyChartBrushes();
            });
        }

        private void ChartThemeChanged(object sender, object args)
        {
            DispatcherQueue.TryEnqueue(ApplyChartBrushes);
        }

        private void ApplyChartBrushes()
        {
            if (!_viewReady || _isClosing)
            {
                return;
            }
            var theme = _accessibilitySettings.HighContrast
                ? "HighContrast" : RootGrid.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            var dictionary = (ResourceDictionary)RootGrid.Resources.ThemeDictionaries[theme];
            var active = (Brush)dictionary["ActivePlotBrush"];
            var inactive = (Brush)dictionary["InactivePlotBrush"];
            SetActivityChartBrushes(active, inactive);
        }

        private void ApplyResponsiveLayout()
        {
            if (!_viewReady || _isClosing || PageScroller.ActualWidth <= 0)
            {
                return;
            }
            var scale = Math.Max(1, _uiSettings.TextScaleFactor);
            var width = Math.Min(1800, PageScroller.ActualWidth);
            var effectiveWidth = width / scale;
            var wide = effectiveWidth >= 1600;
            var sideGroups = effectiveWidth >= 1050;
            var headerWidth = RootGrid.ActualWidth / scale;
            var smallHeader = headerWidth < 620;
            var wrappedHeader = headerWidth < 1020;
            SetAutoRows(HeaderGrid, smallHeader ? 3 : wrappedHeader ? 2 : 1);
            SetAutoRows(InsightsBand, sideGroups ? 1 : 2);
            Grid.SetRow(IdentityPanel, 0);
            Grid.SetColumnSpan(IdentityPanel, wrappedHeader ? 3 : 1);
            Grid.SetRow(HeaderActions, smallHeader ? 2 : 0);
            Grid.SetColumn(HeaderActions, smallHeader ? 0 : 2);
            Grid.SetColumnSpan(HeaderActions, smallHeader ? 3 : 1);
            if (wrappedHeader && !smallHeader)
            {
                Grid.SetColumnSpan(IdentityPanel, 2);
            }
            Grid.SetRow(DateNav, wrappedHeader ? 1 : 0);
            Grid.SetColumn(DateNav, wrappedHeader ? 0 : 1);
            Grid.SetColumnSpan(DateNav, wrappedHeader ? 3 : 1);

            InsightsBand.ColumnDefinitions[0].Width = new GridLength(wide ? 6 : 3, GridUnitType.Star);
            InsightsBand.ColumnDefinitions[1].Width = new GridLength(wide ? 2 : 1, GridUnitType.Star);
            Grid.SetColumnSpan(DayGroup, sideGroups ? 1 : 2);
            Grid.SetRow(WeekdayGroup, sideGroups ? 0 : 1);
            Grid.SetColumn(WeekdayGroup, sideGroups ? 1 : 0);
            Grid.SetColumnSpan(WeekdayGroup, sideGroups ? 1 : 2);
            var dayColumns = wide ? 6 : sideGroups ? 3 : effectiveWidth >= 760 ? 3 : effectiveWidth >= 540 ? 2 : 1;
            var weekdayColumns = wide ? 2 : sideGroups ? 1 : effectiveWidth >= 540 ? 2 : 1;
            ArrangeCards(DayCardsGrid, dayColumns, scale);
            ArrangeCards(WeekdayCardsGrid, weekdayColumns, scale);

            var stackedAnalysis = effectiveWidth < 1050;
            Grid.SetRow(TimelineCard, stackedAnalysis ? 1 : 0);
            Grid.SetColumn(TimelineCard, stackedAnalysis ? 0 : 1);
            Grid.SetColumnSpan(TimelineCard, stackedAnalysis ? 2 : 1);
            Grid.SetColumnSpan(ChartCard, stackedAnalysis ? 2 : 1);
            AnalysisGrid.RowDefinitions[0].Height = stackedAnalysis
                ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            var remainingHeight = PageScroller.ActualHeight - InsightsBand.ActualHeight - 16;
            AnalysisGrid.Height = stackedAnalysis ? double.NaN
                : remainingHeight >= 276 * scale ? remainingHeight : 320 * scale;
            ChartCard.Height = stackedAnalysis ? 320 * scale : double.NaN;
            TimelineCard.Height = stackedAnalysis ? 380 * scale : double.NaN;
        }

        private static void ArrangeCards(Grid host, int columns, double scale)
        {
            var rows = (host.Children.Count + columns - 1) / columns;
            if (host.ColumnDefinitions.Count != columns)
            {
                host.ColumnDefinitions.Clear();
                for (var column = 0; column < columns; column++)
                {
                    host.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star),
                    });
                }
            }
            SetAutoRows(host, rows);
            for (var index = 0; index < host.Children.Count; index++)
            {
                var element = (FrameworkElement)host.Children[index];
                Grid.SetRow(element, index / columns);
                Grid.SetColumn(element, index % columns);
                element.MinHeight = 112 * scale;
            }
        }

        private static void SetAutoRows(Grid host, int rows)
        {
            if (host.RowDefinitions.Count == rows)
            {
                return;
            }
            host.RowDefinitions.Clear();
            for (var row = 0; row < rows; row++)
            {
                host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }

        private static string FormatDuration(double seconds)
        {
            var rounded = Math.Max(0, (int)Math.Round(seconds));
            var hours = rounded / 3600;
            var minutes = rounded % 3600 / 60;
            var remainder = rounded % 60;
            if (hours > 0)
            {
                return AppStrings.Format("DurationHoursFormat", hours, minutes);
            }
            return minutes > 0
                ? AppStrings.Format("DurationMinutesFormat", minutes, remainder)
                : AppStrings.Format("DurationSecondsFormat", remainder);
        }

        private void UsageDatePicker_DateChanged(
            CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (!_viewReady || _isClosing || _restoringDate)
            {
                return;
            }
            if (!args.NewDate.HasValue)
            {
                _restoringDate = true;
                sender.Date = new DateTimeOffset(_selectedDate);
                _restoringDate = false;
                return;
            }
            _selectedDate = args.NewDate.Value.Date > DateTime.Today
                ? DateTime.Today : args.NewDate.Value.Date;
            RefreshData();
            AnnounceSelectedDay();
        }

        private void PreviousDayButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedDate(_selectedDate.AddDays(-1));
        }

        private void NextDayButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedDate(_selectedDate.AddDays(1));
        }

        private void TodayButton_Click(object sender, RoutedEventArgs e)
        {
            SetSelectedDate(DateTime.Today);
        }

        private void SetSelectedDate(DateTime date)
        {
            var next = date.Date > DateTime.Today ? DateTime.Today : date.Date;
            if (UsageDatePicker.Date?.Date != next)
            {
                UsageDatePicker.Date = new DateTimeOffset(next);
            }
        }

        private void UpdateDateNavigation()
        {
            NextDayButton.IsEnabled = _selectedDate < DateTime.Today;
            TodayButton.IsEnabled = _selectedDate != DateTime.Today;
            UsageDatePicker.MaxDate = new DateTimeOffset(DateTime.Today);
        }

        private void AnnounceSelectedDay()
        {
            var peer = FrameworkElementAutomationPeer.FromElement(SelectedDateText)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(SelectedDateText);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                AppStrings.Format("SelectedDayNotificationFormat", _selectedDate, Sessions.Count),
                "SelectedDayChanged");
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
            AnnounceSelectedDay();
        }

        private async void SettingsFlyout_Opened(object sender, object args)
        {
            _syncingSettings = true;
            IdleThresholdNumberBox.Value = UsageTrackerSettings.GetIdleThresholdSeconds();
            _syncingSettings = false;
            RunAtSignInToggle.IsEnabled = false;
            var enabled = await StartupTaskManager.IsEnabledAsync();
            if (_isClosing)
            {
                return;
            }
            _syncingSettings = true;
            RunAtSignInToggle.IsOn = enabled;
            _syncingSettings = false;
            _startupSettingInitialized = true;
            RunAtSignInToggle.IsEnabled = true;
        }

        private void IdleThresholdNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (!_viewReady || _isClosing || _syncingSettings || double.IsNaN(args.NewValue))
            {
                return;
            }
            var seconds = (int)Math.Round(args.NewValue);
            _tracker.IdleThresholdSeconds = seconds;
            UsageTrackerSettings.SetIdleThresholdSeconds(seconds);
        }

        private async void RunAtSignInToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_startupSettingInitialized || _syncingSettings || _isClosing)
            {
                return;
            }
            var desired = RunAtSignInToggle.IsOn;
            RunAtSignInToggle.IsEnabled = false;
            try
            {
                var result = desired
                    ? await StartupTaskManager.EnableAsync()
                    : await StartupTaskManager.DisableAsync();
                if (!result.Success && !_isClosing)
                {
                    _syncingSettings = true;
                    RunAtSignInToggle.IsOn = !desired;
                    _syncingSettings = false;
                    await new ContentDialog
                    {
                        Title = AppStrings.Get("StartupUpdateFailed"),
                        Content = result.Message,
                        CloseButtonText = AppStrings.Get("OK"),
                        FlowDirection = RootGrid.FlowDirection,
                        Language = RootGrid.Language,
                        XamlRoot = RootGrid.XamlRoot,
                    }.ShowAsync();
                }
            }
            finally
            {
                if (!_isClosing)
                {
                    RunAtSignInToggle.IsEnabled = true;
                }
            }
        }

        private void TrackingButton_Click(object sender, RoutedEventArgs e)
        {
            _userPaused = !_userPaused;
            UsageTrackerSettings.SetPaused(_userPaused);
            RefreshData();
        }

        private async void OpenDataFolder_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(_store.DataFolder);
        }

        private static string LocalizeApplicationName(string name) => name switch
        {
            "Unknown" => AppStrings.Get("UnknownApp"),
            "desktop" => AppStrings.Get("Desktop"),
            "Modern Windows App" => AppStrings.Get("ModernWindowsApp"),
            "Windows Explorer" => AppStrings.Get("WindowsExplorer"),
            "Notepad" => AppStrings.Get("Notepad"),
            "Windows Shell" => AppStrings.Get("WindowsShell"),
            "Start Menu" => AppStrings.Get("StartMenu"),
            "Windows Search" => AppStrings.Get("WindowsSearch"),
            "Lock Screen" => AppStrings.Get("LockScreen"),
            _ => name,
        };

        private void ApplyRoundedWindowCorners()
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                const int windowCornerPreference = 33;
                var round = 2;
                var result = DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this),
                    windowCornerPreference, ref round, sizeof(int));
                if (result < 0)
                {
                    App.Log($"Rounded window corners unavailable: 0x{result:X8}");
                }
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_isClosing && args.WindowActivationState != WindowActivationState.Deactivated)
            {
                ApplyRoundedWindowCorners();
            }
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (!_isClosing && (args.DidSizeChange || args.DidPresenterChange))
            {
                ApplyRoundedWindowCorners();
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _isClosing = true;
            Activated -= MainWindow_Activated;
            AppWindow.Changed -= AppWindow_Changed;
            App.Log("Main window closed.");
            _refreshTimer.Stop();
            _tracker.UsageChanged -= Tracker_UsageChanged;
            _uiSettings.TextScaleFactorChanged -= TextScaleFactorChanged;
            _uiSettings.ColorValuesChanged -= ChartThemeChanged;
            RootGrid.ActualThemeChanged -= ChartThemeChanged;
            _tracker.Stop();
            if (!_backgroundWorkerRunning)
            {
                _store.Flush();
            }
            _tracker.Dispose();
            Application.Current.Exit();
        }
    }
}
