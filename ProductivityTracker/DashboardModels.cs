using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace ProductivityTracker
{
    [WinRT.GeneratedBindableCustomProperty]
    public sealed partial class DashboardCard : INotifyPropertyChanged
    {
        private readonly bool _valueIsTimeRange;
        private bool _contextIsTimeRange;

        public DashboardCard(string title, bool valueIsTimeRange = false)
        {
            Title = title;
            _valueIsTimeRange = valueIsTimeRange;
        }

        public string Title { get; }
        public string Value { get; private set; } = AppStrings.Get("NotRecorded");
        public string Context { get; private set; } = string.Empty;
        public string Detail { get; private set; } = string.Empty;
        public bool HasData { get; private set; }
        public double ValueFontSize => HasData ? 22 : 16;
        public TextAlignment ValueTextAlignment => _valueIsTimeRange && HasData && AppStrings.Culture.TextInfo.IsRightToLeft
            ? TextAlignment.Right : TextAlignment.Left;
        public TextAlignment ContextTextAlignment => _contextIsTimeRange && AppStrings.Culture.TextInfo.IsRightToLeft
            ? TextAlignment.Right : TextAlignment.Left;
        private FlowDirection LanguageFlowDirection => AppStrings.Culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        public FlowDirection ValueFlowDirection => _valueIsTimeRange && HasData
            ? FlowDirection.LeftToRight : LanguageFlowDirection;
        public FlowDirection ContextFlowDirection => _contextIsTimeRange
            ? FlowDirection.LeftToRight : LanguageFlowDirection;
        public string AutomationSummary => AppStrings.Format("CardAutomationSummary", Title, Value, Context, Detail);
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(string value, string context, string detail, bool hasData, bool contextIsTimeRange = false)
        {
            if (Value == value && Context == context && Detail == detail && HasData == hasData &&
                _contextIsTimeRange == contextIsTimeRange)
            {
                return;
            }

            Value = value;
            Context = context;
            Detail = detail;
            HasData = hasData;
            _contextIsTimeRange = contextIsTimeRange;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    [WinRT.GeneratedBindableCustomProperty]
    public sealed partial class ActivityRow : INotifyPropertyChanged
    {
        public ActivityRow(DateTime startUtc)
        {
            StartUtc = startUtc;
        }

        public DateTime StartUtc { get; }
        public string StartTime { get; private set; } = string.Empty;
        public string EndTime { get; private set; } = string.Empty;
        public string Application { get; private set; } = string.Empty;
        public string Duration { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(string start, string end, string application, string duration, string state)
        {
            if (StartTime == start && EndTime == end && Application == application &&
                Duration == duration && State == state)
            {
                return;
            }

            StartTime = start;
            EndTime = end;
            Application = application;
            Duration = duration;
            State = state;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
