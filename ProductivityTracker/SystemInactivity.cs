using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace ProductivityTracker
{
    internal sealed record SystemInactiveInterval(DateTime StartUtc, DateTime EndUtc, InactivityReason Reason);

    internal static class SystemInactivity
    {
        public static IReadOnlyList<SystemInactiveInterval> ReadEventIntervals(
            IEnumerable<string> eventXml, DateTime fromUtc, DateTime untilUtc)
        {
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            var intervals = new List<SystemInactiveInterval>();
            DateTime? shutdown = null;
            DateTime? sleep = null;
            foreach (var xml in eventXml)
            {
                var entry = XElement.Parse(xml);
                var system = entry.Element(ns + "System");
                var provider = (string?)system?.Element(ns + "Provider")?.Attribute("Name");
                var id = (int?)system?.Element(ns + "EventID");
                var timestamp = ParseUtc((string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime"));
                if (!timestamp.HasValue || timestamp.Value > untilUtc)
                {
                    continue;
                }
                var data = entry.Element(ns + "EventData")?.Elements(ns + "Data")
                    .Where(item => item.Attribute("Name") != null)
                    .GroupBy(item => (string)item.Attribute("Name")!)
                    .ToDictionary(group => group.Key, group => group.First().Value)
                    ?? new Dictionary<string, string>();
                DateTime EventTime(string key) =>
                    data.TryGetValue(key, out var value) ? ParseUtc(value) ?? timestamp.Value : timestamp.Value;

                if (provider == "Microsoft-Windows-Power-Troubleshooter" && id == 1)
                {
                    if (data.TryGetValue("SleepTime", out var start) &&
                        data.TryGetValue("WakeTime", out var end) &&
                        ParseUtc(start) is DateTime startUtc && ParseUtc(end) is DateTime endUtc &&
                        startUtc.Year >= 2000 && endUtc <= timestamp.Value.AddMinutes(5))
                    {
                        Add(startUtc, endUtc, InactivityReason.Sleep);
                    }
                }
                else if (provider == "Microsoft-Windows-Kernel-Power")
                {
                    if (id is 42 or 506)
                    {
                        sleep ??= timestamp.Value;
                    }
                    else if (id is 107 or 507)
                    {
                        if (sleep.HasValue)
                        {
                            Add(sleep.Value, timestamp.Value, InactivityReason.Sleep);
                            sleep = null;
                        }
                    }
                }
                else if (provider == "Microsoft-Windows-Kernel-General")
                {
                    if (id == 13)
                    {
                        shutdown = EventTime("StopTime");
                        sleep = null;
                    }
                    else if (id == 12)
                    {
                        if (shutdown.HasValue)
                        {
                            Add(shutdown.Value, EventTime("StartTime"), InactivityReason.PoweredOff);
                            shutdown = null;
                        }
                        sleep = null;
                    }
                }
            }
            return intervals;

            void Add(DateTime start, DateTime end, InactivityReason reason)
            {
                start = start < fromUtc ? fromUtc : start;
                end = end > untilUtc ? untilUtc : end;
                if (end > start)
                {
                    intervals.Add(new SystemInactiveInterval(start, end, reason));
                }
            }
        }

        public static List<UsageSegment> Overlay(
            IEnumerable<UsageSegment> segments, IEnumerable<SystemInactiveInterval> intervals)
        {
            var result = segments.Select(segment => segment.Clone()).ToList();
            // Confirmed powered-off time takes precedence over overlapping sleep evidence.
            foreach (var interval in intervals
                .Where(interval => interval.EndUtc > interval.StartUtc)
                .OrderBy(interval => interval.Reason == InactivityReason.PoweredOff ? 1 : 0)
                .ThenBy(interval => interval.StartUtc))
            {
                var updated = new List<UsageSegment>();
                foreach (var segment in result)
                {
                    if (segment.EndUtc <= interval.StartUtc || segment.StartUtc >= interval.EndUtc)
                    {
                        updated.Add(segment);
                        continue;
                    }
                    if (segment.StartUtc < interval.StartUtc)
                    {
                        var before = segment.Clone();
                        before.EndUtc = interval.StartUtc;
                        updated.Add(before);
                    }
                    if (segment.EndUtc > interval.EndUtc)
                    {
                        var after = segment.Clone();
                        after.StartUtc = interval.EndUtc;
                        updated.Add(after);
                    }
                }
                updated.Add(new UsageSegment
                {
                    StartUtc = interval.StartUtc,
                    EndUtc = interval.EndUtc,
                    IsActive = false,
                    InactiveReason = interval.Reason,
                    AppName = "Inactive",
                });
                result = updated;
            }

            var merged = new List<UsageSegment>();
            foreach (var segment in result.OrderBy(segment => segment.StartUtc))
            {
                var previous = merged.LastOrDefault();
                if (previous != null && previous.EndUtc == segment.StartUtc &&
                    previous.IsActive == segment.IsActive && previous.InactiveReason == segment.InactiveReason &&
                    previous.AppName == segment.AppName && previous.ProcessName == segment.ProcessName)
                {
                    previous.EndUtc = segment.EndUtc;
                }
                else
                {
                    merged.Add(segment);
                }
            }
            return merged;
        }

        private static DateTime? ParseUtc(string? value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result)
                ? result : null;
        }
    }
}
