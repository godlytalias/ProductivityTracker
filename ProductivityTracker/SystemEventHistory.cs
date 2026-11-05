using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.Versioning;

namespace ProductivityTracker
{
    [SupportedOSPlatform("windows")]
    internal static class SystemEventHistory
    {
        public static IReadOnlyList<SystemInactiveInterval> Read(DateTime fromUtc, DateTime untilUtc)
        {
            var start = fromUtc.AddDays(-1).ToString("O", CultureInfo.InvariantCulture);
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[TimeCreated[@SystemTime >= '{start}'] and " +
                "((Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-General'] and (EventID=12 or EventID=13)) or " +
                "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and " +
                "(EventID=42 or EventID=107 or EventID=506 or EventID=507)))]]");
            using var reader = new EventLogReader(query);
            var events = new List<string>();
            while (reader.ReadEvent() is EventRecord entry)
            {
                using (entry)
                {
                    events.Add(entry.ToXml());
                }
            }
            return SystemInactivity.ReadEventIntervals(events, fromUtc, untilUtc);
        }
    }
}
