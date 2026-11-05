using System;
using System.Collections.Generic;
using System.Linq;

namespace ProductivityTracker
{
    internal sealed class HourlyActivity
    {
        public int Hour { get; init; }
        public double ActiveSeconds { get; set; }
        public double InactiveSeconds { get; set; }
        public double PoweredDownSeconds { get; set; }
        public double RecordedSeconds => ActiveSeconds + InactiveSeconds;
        public double AwakeRecordedSeconds => Math.Max(0, RecordedSeconds - PoweredDownSeconds);
        public double ActiveShare => AwakeRecordedSeconds > 0 ? ActiveSeconds / AwakeRecordedSeconds : 0;
        public double ActiveMinutes => RecordedSeconds > 0 ? ActiveSeconds / 60 : double.NaN;
        public double InactiveMinutes => RecordedSeconds > 0 ? InactiveSeconds / 60 : double.NaN;
    }

    internal sealed class WeekdayActivity
    {
        public DayOfWeek Day { get; init; }
        public int RecordedDays { get; init; }
        public double ActiveSeconds { get; init; }
        public double InactiveSeconds { get; init; }
        public double RecordedSeconds => ActiveSeconds + InactiveSeconds;
        public double ActiveShare => RecordedSeconds > 0 ? ActiveSeconds / RecordedSeconds : 0;
        public double AverageActiveSeconds => ActiveSeconds / RecordedDays;
        public double AverageRecordedSeconds => RecordedSeconds / RecordedDays;
    }

    internal sealed class ActivityStretch
    {
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public double Seconds => (EndUtc - StartUtc).TotalSeconds;
    }

    internal sealed class ActivityInsights
    {
        public DateTime HistoryStartDate { get; init; }
        public int RecordedDays { get; init; }
        public IReadOnlyList<HourlyActivity> Hours { get; init; } = Array.Empty<HourlyActivity>();
        public HourlyActivity? MostActiveHour { get; init; }
        public HourlyActivity? LeastActiveHour { get; init; }
        public WeekdayActivity? MostActiveWeekday { get; init; }
        public WeekdayActivity? LeastActiveWeekday { get; init; }
        public ActivityStretch? LongestActiveStretch { get; init; }
        public ActivityStretch? LongestInactiveStretch { get; init; }
        public double ActiveSeconds => Hours.Sum(hour => hour.ActiveSeconds);
        public double RecordedSeconds => Hours.Sum(hour => hour.RecordedSeconds);
        public double AwakeRecordedSeconds => Hours.Sum(hour => hour.AwakeRecordedSeconds);
        public double PoweredDownSeconds => Hours.Sum(hour => hour.PoweredDownSeconds);
        public double? ActiveShare => AwakeRecordedSeconds > 0 ? ActiveSeconds / AwakeRecordedSeconds : null;
    }

    internal static class UsageAnalytics
    {
        public const int HistoryDays = 28;
        private static readonly TimeSpan SamplingGapTolerance = TimeSpan.FromSeconds(3);

        public static ActivityInsights Analyze(
            IEnumerable<UsageSegment> segments,
            DateTime selectedDate,
            TimeZoneInfo? timeZone = null)
        {
            timeZone ??= TimeZoneInfo.Local;
            var selectedDay = selectedDate.Date;
            var historyStart = selectedDay.AddDays(1 - HistoryDays);
            var startUtc = DayStartUtc(historyStart, timeZone);
            var endUtc = DayStartUtc(selectedDay.AddDays(1), timeZone);
            var hours = Enumerable.Range(0, 24)
                .Select(hour => new HourlyActivity { Hour = hour })
                .ToArray();
            var days = new Dictionary<DateTime, (double Active, double Inactive)>();
            var selectedSegments = new List<UsageSegment>();

            foreach (var segment in segments)
            {
                var cursor = AsUtc(segment.StartUtc);
                var end = AsUtc(segment.EndUtc);
                cursor = cursor > startUtc ? cursor : startUtc;
                end = end < endUtc ? end : endUtc;

                while (cursor < end)
                {
                    var localDay = TimeZoneInfo.ConvertTimeFromUtc(cursor, timeZone).Date;
                    var nextDay = DayStartUtc(localDay.AddDays(1), timeZone);
                    var pieceEnd = nextDay < end ? nextDay : end;
                    var seconds = (pieceEnd - cursor).TotalSeconds;
                    if (!segment.IsPoweredDown)
                    {
                        days.TryGetValue(localDay, out var totals);
                        days[localDay] = segment.IsActive
                            ? (totals.Active + seconds, totals.Inactive)
                            : (totals.Active, totals.Inactive + seconds);
                    }

                    if (localDay == selectedDay)
                    {
                        selectedSegments.Add(new UsageSegment
                        {
                            StartUtc = cursor,
                            EndUtc = pieceEnd,
                            IsActive = segment.IsActive,
                            InactiveReason = segment.InactiveReason,
                        });
                        AddHourlyTime(hours, cursor, pieceEnd, segment.IsActive, segment.IsPoweredDown, timeZone);
                    }

                    cursor = pieceEnd;
                }
            }

            var observedHours = hours.Where(hour => hour.AwakeRecordedSeconds > 0).ToArray();
            var peakHour = observedHours
                .OrderByDescending(hour => hour.ActiveShare)
                .ThenByDescending(hour => hour.AwakeRecordedSeconds)
                .ThenBy(hour => hour.Hour)
                .FirstOrDefault();
            var quietHour = observedHours
                .OrderBy(hour => hour.ActiveShare)
                .ThenByDescending(hour => hour.AwakeRecordedSeconds)
                .ThenBy(hour => hour.Hour)
                .FirstOrDefault();
            if (peakHour?.ActiveSeconds == 0)
            {
                peakHour = null;
            }
            if (peakHour == null || quietHour == null ||
                Math.Abs(peakHour.ActiveShare - quietHour.ActiveShare) < 0.000000001)
            {
                quietHour = null;
            }

            var weekdays = days
                .GroupBy(day => day.Key.DayOfWeek)
                .Select(group => new WeekdayActivity
                {
                    Day = group.Key,
                    RecordedDays = group.Count(),
                    ActiveSeconds = group.Sum(day => day.Value.Active),
                    InactiveSeconds = group.Sum(day => day.Value.Inactive),
                })
                .ToArray();
            var peakWeekday = weekdays
                .OrderByDescending(day => day.ActiveShare)
                .ThenByDescending(day => day.RecordedSeconds)
                .ThenBy(day => day.Day)
                .FirstOrDefault();
            var quietWeekday = weekdays
                .OrderBy(day => day.ActiveShare)
                .ThenByDescending(day => day.RecordedSeconds)
                .ThenBy(day => day.Day)
                .FirstOrDefault();
            if (peakWeekday?.ActiveSeconds == 0)
            {
                peakWeekday = null;
            }
            if (peakWeekday == null || quietWeekday == null ||
                Math.Abs(peakWeekday.ActiveShare - quietWeekday.ActiveShare) < 0.000000001)
            {
                quietWeekday = null;
            }

            var ordered = selectedSegments.OrderBy(segment => segment.StartUtc).ToArray();
            return new ActivityInsights
            {
                HistoryStartDate = historyStart,
                RecordedDays = days.Count,
                Hours = hours,
                MostActiveHour = peakHour,
                LeastActiveHour = quietHour,
                MostActiveWeekday = peakWeekday,
                LeastActiveWeekday = quietWeekday,
                LongestActiveStretch = LongestStretch(ordered, true),
                LongestInactiveStretch = LongestStretch(ordered, false),
            };
        }

        private static void AddHourlyTime(
            HourlyActivity[] hours,
            DateTime startUtc,
            DateTime endUtc,
            bool isActive,
            bool isPoweredDown,
            TimeZoneInfo timeZone)
        {
            var cursor = startUtc;
            while (cursor < endUtc)
            {
                // Minute boundaries also split DST changes without assuming a 24-hour day.
                var nextMinute = new DateTime(
                    (cursor.Ticks / TimeSpan.TicksPerMinute + 1) * TimeSpan.TicksPerMinute,
                    DateTimeKind.Utc);
                var pieceEnd = nextMinute < endUtc ? nextMinute : endUtc;
                var hour = TimeZoneInfo.ConvertTimeFromUtc(cursor, timeZone).Hour;
                var seconds = (pieceEnd - cursor).TotalSeconds;
                if (isActive)
                {
                    hours[hour].ActiveSeconds += seconds;
                }
                else
                {
                    hours[hour].InactiveSeconds += seconds;
                    if (isPoweredDown)
                    {
                        hours[hour].PoweredDownSeconds += seconds;
                    }
                }
                cursor = pieceEnd;
            }
        }

        private static ActivityStretch? LongestStretch(
            IEnumerable<UsageSegment> segments,
            bool isActive)
        {
            ActivityStretch? longest = null;
            DateTime? runStart = null;
            var runEnd = DateTime.MinValue;
            foreach (var segment in segments)
            {
                if (segment.IsActive != isActive)
                {
                    runStart = null;
                    continue;
                }

                // App switches leave small sampling gaps; an observed idle interval still ends a run.
                if (runStart == null || segment.StartUtc - runEnd > SamplingGapTolerance)
                {
                    runStart = segment.StartUtc;
                    runEnd = segment.EndUtc;
                }
                else if (segment.EndUtc > runEnd)
                {
                    runEnd = segment.EndUtc;
                }

                if (longest == null || (runEnd - runStart.Value).TotalSeconds > longest.Seconds)
                {
                    longest = new ActivityStretch { StartUtc = runStart.Value, EndUtc = runEnd };
                }
            }
            return longest;
        }

        private static DateTime DayStartUtc(DateTime date, TimeZoneInfo timeZone)
        {
            var local = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            while (timeZone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        }
    }
}
