using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Charts;
using Microsoft.UI.Xaml.Controls.Tabular;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ProductivityTracker
{
    public sealed partial class MainWindow
    {
        private readonly Samples _days = new(), _activeHours = new(), _inactiveHours = new();
        private readonly Samples _hours = new(), _activeMinutes = new(), _inactiveMinutes = new();
        private readonly CategoryAxis _dayAxis = new(), _hourAxis = new();
        private readonly LinearAxis _weeklyAxis = new() { Minimum = 0 }, _dailyAxis = new() { Minimum = 0, Spacing = 15 };
        private BarSeries _activeBars = null!, _inactiveBars = null!;
        private LineSeries _activeLine = null!, _inactiveLine = null!;

        private void InitializeActivityControls()
        {
            RootGrid.Resources.MergedDictionaries.Add(new TabularControlsResources());
            RootGrid.Resources.MergedDictionaries.Add(new XamlChartsResources());
            _dayAxis.Label = AppStrings.Get("DayAxis/Label");
            _hourAxis.Label = AppStrings.Get("LocalHourAxis/Label");
            _weeklyAxis.Label = AppStrings.Get("HoursAxis/Label");
            _dailyAxis.Label = AppStrings.Get("MinutesAxis/Label");
            var weekly = new Chart { ShowLegend = true, FlowDirection = FlowDirection.LeftToRight,
                Background = new SolidColorBrush(Colors.Transparent) };
            weekly.Data.Add(_days); weekly.Data.Add(_activeHours); weekly.Data.Add(_inactiveHours);
            weekly.Axes.Add(_dayAxis); weekly.Axes.Add(_weeklyAxis);
            _activeBars = new BarSeries { Title = AppStrings.Get("Active"), Orientation = BarOrientation.Vertical,
                XValues = _days, YValues = _activeHours, XAxis = _dayAxis, YAxis = _weeklyAxis };
            _inactiveBars = new BarSeries { Title = AppStrings.Get("Inactive"), Orientation = BarOrientation.Vertical,
                XValues = _days, YValues = _inactiveHours, XAxis = _dayAxis, YAxis = _weeklyAxis };
            weekly.Series.Add(_activeBars); weekly.Series.Add(_inactiveBars);
            var hourly = new Chart { ShowLegend = true, FlowDirection = FlowDirection.LeftToRight, Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Colors.Transparent) };
            hourly.Data.Add(_hours); hourly.Data.Add(_activeMinutes); hourly.Data.Add(_inactiveMinutes);
            hourly.Axes.Add(_hourAxis); hourly.Axes.Add(_dailyAxis);
            _activeLine = new LineSeries { Title = AppStrings.Get("Active"), StrokeThickness = 2, ShowDataMarkers = true,
                MarkerShape = MarkerShape.Circle,
                XValues = _hours, YValues = _activeMinutes, XAxis = _hourAxis, YAxis = _dailyAxis };
            _inactiveLine = new LineSeries { Title = AppStrings.Get("Inactive"), StrokeThickness = 2, ShowDataMarkers = true,
                StrokeDashStyle = StrokeDashStyle.Dash, MarkerShape = MarkerShape.Square,
                XValues = _hours, YValues = _inactiveMinutes, XAxis = _hourAxis, YAxis = _dailyAxis };
            hourly.Series.Add(_activeLine); hourly.Series.Add(_inactiveLine);
            _weeklyChart = weekly;
            _hourlyChart = hourly;
            ActivityPlotHost.Children.Add(weekly);
            ActivityPlotHost.Children.Add(hourly);
            var table = new TableView { Name = "ActivityTable", IsReadOnly = true, SelectionMode = TableViewSelectionMode.None,
                GridLinesVisibility = TableViewGridLinesVisibility.Horizontal,
                CanUserSortColumns = false, EmptyTemplate = (DataTemplate)RootGrid.Resources["TimelineEmptyTemplate"],
                ItemsSource = Sessions };
            var headers = new[] { "StartColumn/Header", "EndColumn/Header", "ApplicationColumn/Header", "DurationColumn/Header", "StateColumn/Header" };
            var paths = new[] { "StartTime", "EndTime", "Application", "Duration", "State" };
            var minimums = new[] { 100, 100, 140, 76, 90 };
            for (var index = 0; index < paths.Length; index++)
            {
                table.Columns.Add(new TableViewTextColumn { Header = AppStrings.Get(headers[index]),
                    Width = new GridLength(index == 2 ? 2 : 1, GridUnitType.Star), MinWidth = minimums[index],
                    Binding = new Binding { Path = new PropertyPath(paths[index]) },
                    CellToolTipBinding = new Binding { Path = new PropertyPath(paths[index]) } });
            }
            AutomationProperties.SetName(table, AppStrings.Get("TimelineTitle"));
            ActivityTimelineHost.Children.Add(table);
        }

        private void UpdateTimelineEmptyState() { }

        private void SetActivityChartData(ActivityChartData weekly, ActivityChartData hourly)
        {
            _days.ItemsSource = weekly.Labels;
            _activeHours.ItemsSource = weekly.Active;
            _inactiveHours.ItemsSource = weekly.Inactive;
            _weeklyAxis.Maximum = weekly.Maximum;
            _hours.ItemsSource = hourly.Labels;
            _activeMinutes.ItemsSource = hourly.Active;
            _inactiveMinutes.ItemsSource = hourly.Inactive;
            _dailyAxis.Maximum = hourly.Maximum;
        }

        private void SetActivityChartBrushes(Brush active, Brush inactive)
        {
            _activeBars.Fill = _activeBars.Stroke = _activeBars.DataMarkerBrush = active;
            _inactiveBars.Fill = _inactiveBars.Stroke = _inactiveBars.DataMarkerBrush = inactive;
            _activeLine.Stroke = _activeLine.DataMarkerBrush = active;
            _inactiveLine.Stroke = _inactiveLine.DataMarkerBrush = inactive;
        }
    }
}
