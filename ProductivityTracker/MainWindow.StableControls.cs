using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ProductivityTracker
{
    public sealed partial class MainWindow
    {
        private FrameworkElement _timelineEmpty = null!;

        private void InitializeActivityControls()
        {
            _weeklyChart = new ActivityCanvas(false) { Name = "WeeklyActivityCanvas" };
            _hourlyChart = new ActivityCanvas(true) { Name = "HourlyActivityCanvas", Visibility = Visibility.Collapsed };
            ActivityPlotHost.Children.Add(_weeklyChart);
            ActivityPlotHost.Children.Add(_hourlyChart);

            var rows = new ListView
            {
                Name = "ActivityListView",
                ItemsSource = Sessions,
                SelectionMode = ListViewSelectionMode.None,
                ItemTemplate = (DataTemplate)RootGrid.Resources["TimelineRowTemplate"],
                ItemContainerStyle = new Style(typeof(ListViewItem))
                {
                    Setters =
                    {
                        new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                        new Setter(Control.PaddingProperty, new Thickness(0)),
                    },
                },
            };
            AutomationProperties.SetName(rows, AppStrings.Get("TimelineTitle"));
            ScrollViewer.SetHorizontalScrollBarVisibility(rows, ScrollBarVisibility.Disabled);
            var content = new Grid { RowSpacing = 4, MinWidth = 590 };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 10, 8, 10) };
            var keys = new[] { "StartColumn/Header", "EndColumn/Header", "ApplicationColumn/Header", "DurationColumn/Header", "StateColumn/Header" };
            var minimums = new[] { 100, 100, 140, 76, 90 };
            for (var index = 0; index < keys.Length; index++)
            {
                header.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(index == 2 ? 2 : 1, GridUnitType.Star),
                    MinWidth = minimums[index],
                });
                var label = new TextBlock { Text = AppStrings.Get(keys[index]), FontWeight = FontWeights.SemiBold };
                Grid.SetColumn(label, index);
                header.Children.Add(label);
            }
            content.Children.Add(header);
            Grid.SetRow(rows, 1);
            content.Children.Add(rows);
            _timelineEmpty = (FrameworkElement)((DataTemplate)RootGrid.Resources["TimelineEmptyTemplate"]).LoadContent();
            Grid.SetRow(_timelineEmpty, 1);
            content.Children.Add(_timelineEmpty);
            var horizontalScroller = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
            };
            ActivityTimelineHost.Children.Add(horizontalScroller);
            ActivityTimelineHost.SizeChanged += (_, args) => content.Width = Math.Max(590, args.NewSize.Width);
        }

        private void UpdateTimelineEmptyState()
        {
            _timelineEmpty.Visibility = Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetActivityChartData(ActivityChartData weekly, ActivityChartData hourly)
        {
            ((ActivityCanvas)_weeklyChart).SetData(weekly);
            ((ActivityCanvas)_hourlyChart).SetData(hourly);
        }

        private void SetActivityChartBrushes(Brush active, Brush inactive)
        {
            foreach (var chart in new[] { (ActivityCanvas)_weeklyChart, (ActivityCanvas)_hourlyChart })
            {
                chart.SetBrushes(active, inactive, ChartPeriodText.Foreground, Math.Max(1, _uiSettings.TextScaleFactor));
            }
        }
    }

    internal sealed partial class ActivityCanvas : Canvas
    {
        private readonly bool _byHour;
        private ActivityChartData? _data;
        private Brush _active = new SolidColorBrush(Colors.DodgerBlue);
        private Brush _inactive = new SolidColorBrush(Colors.Gray);
        private Brush _text = new SolidColorBrush(Colors.Black);
        private double _scale = 1;

        public ActivityCanvas(bool byHour)
        {
            _byHour = byHour;
            FlowDirection = FlowDirection.LeftToRight;
            SizeChanged += (_, _) => Render();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ActivityCanvasAutomationPeer(this);

        public void SetData(ActivityChartData data)
        {
            _data = data;
            var descriptions = Enumerable.Range(0, data.Labels.Length)
                .Where(index => double.IsFinite(data.Active[index]))
                .Select(index => AppStrings.Format("ChartPointFormat", data.Labels[index],
                    AppStrings.Get("Active"), data.Active[index], AppStrings.Get("Inactive"), data.Inactive[index], data.UnitLabel));
            AutomationProperties.SetHelpText(this, string.Join("; ", descriptions));
            Render();
        }

        public void SetBrushes(Brush active, Brush inactive, Brush text, double scale)
        {
            _active = active;
            _inactive = inactive;
            _text = text;
            _scale = scale;
            Render();
        }

        private void Render()
        {
            Children.Clear();
            if (_data == null || ActualWidth < 100 || ActualHeight < 100)
            {
                return;
            }
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
            var left = 44 * _scale;
            var top = 43 * _scale;
            var width = Math.Max(1, ActualWidth - left - 12 * _scale);
            var height = Math.Max(1, ActualHeight - top - 52 * _scale);
            var bottom = top + height;
            var activeLegend = Label(AppStrings.Get("Active"), 16 * _scale, 2 * _scale);
            activeLegend.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            DrawLine(0, 10 * _scale, 12 * _scale, 10 * _scale, _active);
            var legendX = 25 * _scale + activeLegend.DesiredSize.Width;
            DrawLine(legendX, 10 * _scale, legendX + 12 * _scale, 10 * _scale, _inactive, true);
            Label(AppStrings.Get("Inactive"), legendX + 16 * _scale, 2 * _scale);
            Label(_data.UnitLabel, 0, 24 * _scale);
            for (var tick = 0; tick <= 4; tick++)
            {
                var y = top + height * (1 - tick / 4.0);
                var line = DrawLine(left, y, left + width, y, _text);
                line.Opacity = tick == 0 ? 0.6 : 0.15;
                var label = Label((_data.Maximum * tick / 4).ToString("0.#", AppStrings.Culture), 0, y - 8 * _scale);
                label.Width = left - 6 * _scale;
                label.TextAlignment = TextAlignment.Right;
            }
            var count = _data.Labels.Length;
            var step = _byHour ? Math.Max(1, (int)Math.Ceiling(28 * _scale * count / width)) : 1;
            var band = width / count;
            for (var index = 0; index < count; index += step)
            {
                var x = _byHour ? left + width * index / (count - 1) : left + band * (index + 0.5);
                var labelWidth = _byHour ? 28 * _scale : band;
                var label = Label(_data.Labels[index], Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, ActualWidth - labelWidth)),
                    bottom + 5 * _scale);
                label.Width = labelWidth;
                label.TextAlignment = TextAlignment.Center;
                label.TextWrapping = TextWrapping.Wrap;
            }
            var axisTitle = Label(_data.AxisLabel, left, ActualHeight - 16 * _scale);
            axisTitle.Width = width;
            axisTitle.TextAlignment = TextAlignment.Center;
            if (_byHour)
            {
                DrawSeries(_data.Active, _active, false);
                DrawSeries(_data.Inactive, _inactive, true);
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    DrawBar(index, _data.Active[index], _active, -1);
                    DrawBar(index, _data.Inactive[index], _inactive, 1);
                }
            }

            void DrawBar(int index, double value, Brush brush, int side)
            {
                if (!double.IsFinite(value))
                {
                    return;
                }
                var barWidth = band * 0.3;
                var barHeight = Math.Max(value > 0 ? 1 : 0, height * value / _data.Maximum);
                var bar = new Rectangle { Width = barWidth, Height = barHeight, Fill = brush };
                AddShape(bar, left + band * (index + 0.5) + (side < 0 ? -barWidth - 1 : 1), bottom - barHeight);
                ToolTipService.SetToolTip(bar, PointDescription(index));
            }

            void DrawSeries(double[] values, Brush brush, bool dashed)
            {
                foreach (var run in ActivityChartPoints.GetLineRuns(values))
                {
                    var line = new Polyline { Stroke = brush, StrokeThickness = 2 * _scale };
                    if (dashed)
                    {
                        line.StrokeDashArray = new DoubleCollection { 3, 2 };
                    }
                    foreach (var point in run)
                    {
                        var x = left + width * point.Index / (count - 1);
                        var y = bottom - height * point.Value / _data.Maximum;
                        line.Points.Add(new Point(x, y));
                        Shape marker = dashed ? new Rectangle() : new Ellipse();
                        marker.Width = marker.Height = 6 * _scale;
                        marker.Fill = brush;
                        AddShape(marker, x - 3 * _scale, y - 3 * _scale);
                        ToolTipService.SetToolTip(marker, PointDescription(point.Index));
                    }
                    AddShape(line, 0, 0);
                }
            }
        }

        private string PointDescription(int index) => AppStrings.Format("ChartPointFormat", _data!.Labels[index],
            AppStrings.Get("Active"), _data.Active[index], AppStrings.Get("Inactive"), _data.Inactive[index], _data.UnitLabel);

        private TextBlock Label(string text, double x, double y)
        {
            var label = new TextBlock { Text = text, Foreground = _text, FontSize = 11 * _scale, IsTextScaleFactorEnabled = false };
            AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Children.Add(label);
            SetLeft(label, x);
            SetTop(label, y);
            return label;
        }

        private Line DrawLine(double x1, double y1, double x2, double y2, Brush brush, bool dashed = false)
        {
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1 };
            if (dashed)
            {
                line.StrokeDashArray = new DoubleCollection { 3, 2 };
            }
            AddShape(line, 0, 0);
            return line;
        }

        private void AddShape(Shape shape, double x, double y)
        {
            AutomationProperties.SetAccessibilityView(shape, AccessibilityView.Raw);
            Children.Add(shape);
            SetLeft(shape, x);
            SetTop(shape, y);
        }
    }

    internal sealed partial class ActivityCanvasAutomationPeer : FrameworkElementAutomationPeer
    {
        public ActivityCanvasAutomationPeer(ActivityCanvas owner) : base(owner) { }
        protected override string GetClassNameCore() => nameof(ActivityCanvas);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
