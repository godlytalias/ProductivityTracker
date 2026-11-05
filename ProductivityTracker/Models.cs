using System;
using System.Text.Json.Serialization;

namespace ProductivityTracker
{
    [JsonConverter(typeof(JsonStringEnumConverter<InactivityReason>))]
    public enum InactivityReason
    {
        Idle,
        Locked,
        Sleep,
        PoweredOff,
    }

    public sealed class UsageSegment
    {
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public string AppName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public InactivityReason? InactiveReason { get; set; }
        [JsonIgnore]
        public bool IsPoweredDown => !IsActive &&
            InactiveReason is InactivityReason.Sleep or InactivityReason.PoweredOff;

        public UsageSegment Clone()
        {
            return new UsageSegment
            {
                StartUtc = StartUtc,
                EndUtc = EndUtc,
                AppName = AppName,
                ProcessName = ProcessName,
                IsActive = IsActive,
                InactiveReason = InactiveReason,
            };
        }
    }

    public sealed class AppUsageSummary
    {
        public string AppName { get; set; } = string.Empty;
        public double ActiveSeconds { get; set; }
    }

    public sealed class DailyUsageSummary
    {
        public double ActiveSeconds { get; set; }
        public double InactiveSeconds { get; set; }
        public double LockedSeconds { get; set; }
        public double SleepSeconds { get; set; }
        public double PoweredOffSeconds { get; set; }
        public double IdleSeconds => Math.Max(
            0, InactiveSeconds - LockedSeconds - SleepSeconds - PoweredOffSeconds);
        public int SessionCount { get; set; }
        public string TopAppName { get; set; } = "None";
        public double TopAppSeconds { get; set; }
        public System.Collections.Generic.List<AppUsageSummary> Applications { get; set; } = new();
    }
}
