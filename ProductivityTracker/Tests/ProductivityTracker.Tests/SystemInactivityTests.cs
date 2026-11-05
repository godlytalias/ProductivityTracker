using System;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class SystemInactivityTests
    {
        [TestMethod]
        public void EventEvidenceRecoversSleepAndCleanShutdownButNotAnUnexplainedBoot()
        {
            var start = Utc(1);
            var end = Utc(3);
            var intervals = SystemInactivity.ReadEventIntervals(new[]
            {
                Event("Microsoft-Windows-Kernel-General", 12, Utc(1, 8)),
                Event("Microsoft-Windows-Kernel-Power", 42, Utc(1, 10)),
                Event("Microsoft-Windows-Kernel-Power", 107, Utc(1, 11)),
                Event("Microsoft-Windows-Power-Troubleshooter", 1, Utc(1, 11, 1),
                    $"<Data Name=\"SleepTime\">{Utc(1, 10):O}</Data><Data Name=\"WakeTime\">{Utc(1, 11):O}</Data>"),
                Event("Microsoft-Windows-Kernel-General", 13, Utc(1, 22)),
                Event("Microsoft-Windows-Kernel-General", 12, Utc(2, 8)),
            }, start, end);
            var actual = SystemInactivity.Overlay(Array.Empty<UsageSegment>(), intervals);

            Assert.AreEqual(2, actual.Count);
            Assert.AreEqual(Utc(1, 10), actual[0].StartUtc);
            Assert.AreEqual(Utc(1, 11), actual[0].EndUtc);
            Assert.AreEqual(InactivityReason.Sleep, actual[0].InactiveReason);
            Assert.AreEqual(Utc(1, 22), actual[1].StartUtc);
            Assert.AreEqual(Utc(2, 8), actual[1].EndUtc);
            Assert.AreEqual(InactivityReason.PoweredOff, actual[1].InactiveReason);
        }

        [TestMethod]
        public void ReconciliationIsIdempotentPreservesUnrelatedSamplesAndNeverDoubleCounts()
        {
            var original = new[]
            {
                new UsageSegment
                {
                    StartUtc = Utc(1, 8), EndUtc = Utc(1, 12), IsActive = true, AppName = "Editor",
                },
                new UsageSegment
                {
                    StartUtc = Utc(1, 15), EndUtc = Utc(1, 16), IsActive = true, AppName = "Browser",
                },
            };
            var evidence = new[]
            {
                new SystemInactiveInterval(Utc(1, 9), Utc(1, 11), InactivityReason.Sleep),
                new SystemInactiveInterval(Utc(1, 10), Utc(1, 11), InactivityReason.PoweredOff),
            };
            var first = SystemInactivity.Overlay(original, evidence);
            var second = SystemInactivity.Overlay(first, evidence.Reverse());

            Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
            Assert.AreEqual(5, first.Count);
            Assert.AreEqual(Utc(1, 9), first[0].EndUtc);
            Assert.AreEqual(InactivityReason.Sleep, first[1].InactiveReason);
            Assert.AreEqual(InactivityReason.PoweredOff, first[2].InactiveReason);
            Assert.AreEqual(Utc(1, 11), first[3].StartUtc);
            Assert.AreEqual("Browser", first[4].AppName);
            Assert.AreEqual(5 * 3600, first.Sum(segment => (segment.EndUtc - segment.StartUtc).TotalSeconds), 0.001);
            Assert.AreEqual(Utc(1, 12), original[0].EndUtc);
        }

        [TestMethod]
        public void UnmatchedSleepAndShutdownAndInvalidPowerTimestampsStayUnknown()
        {
            var intervals = SystemInactivity.ReadEventIntervals(new[]
            {
                Event("Unrelated-Provider", 42, Utc(1, 8)),
                Event("Microsoft-Windows-Kernel-Power", 107, Utc(1, 9)),
                Event("Microsoft-Windows-Power-Troubleshooter", 1, Utc(1, 11),
                    $"<Data Name=\"SleepTime\">1601-01-01T00:00:00Z</Data><Data Name=\"WakeTime\">{Utc(1, 11):O}</Data>"),
                Event("Microsoft-Windows-Kernel-Power", 42, Utc(1, 12)),
                Event("Microsoft-Windows-Kernel-General", 13, Utc(1, 22)),
            }, Utc(1), Utc(3));

            Assert.AreEqual(0, intervals.Count);
        }

        [TestMethod]
        public void EvidenceIsClippedToRequestedRangeAndOldJsonRemainsCompatible()
        {
            var intervals = SystemInactivity.ReadEventIntervals(new[]
            {
                Event("Microsoft-Windows-Power-Troubleshooter", 1, Utc(2, 9),
                    $"<Data Name=\"SleepTime\">{Utc(1, 23):O}</Data><Data Name=\"WakeTime\">{Utc(2, 9):O}</Data>"),
            }, Utc(2), Utc(2, 8));
            Assert.AreEqual(0, intervals.Count);

            intervals = SystemInactivity.ReadEventIntervals(new[]
            {
                Event("Microsoft-Windows-Kernel-General", 13, Utc(1, 23)),
                Event("Microsoft-Windows-Kernel-General", 12, Utc(2, 9)),
            }, Utc(2), Utc(3));
            Assert.AreEqual(Utc(2), intervals.Single().StartUtc);
            Assert.AreEqual(Utc(2, 9), intervals.Single().EndUtc);

            var legacy = JsonSerializer.Deserialize<UsageSegment>(
                "{\"StartUtc\":\"2026-10-01T08:00:00Z\",\"EndUtc\":\"2026-10-01T09:00:00Z\",\"IsActive\":false}")!;
            Assert.IsFalse(legacy.IsPoweredDown);
            Assert.IsNull(legacy.InactiveReason);
        }

        private static DateTime Utc(int day, int hour = 0, int minute = 0) =>
            new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

        private static string Event(string provider, int id, DateTime timestamp, string data = "") =>
            $"<Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System>" +
            $"<Provider Name=\"{provider}\"/><EventID>{id}</EventID>" +
            $"<TimeCreated SystemTime=\"{timestamp:O}\"/></System><EventData>{data}</EventData></Event>";
    }
}
