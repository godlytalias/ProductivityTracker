using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class UsageStoreRetentionTests
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ProductivityTracker.Tests-" + Guid.NewGuid().ToString("N"));
        private sealed class TestClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
            public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [TestMethod]
        public void MonthCutoffRemovesExpiredIntervalsAndClipsOnlyTheOverlappingPart()
        {
            var cutoff = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
            WriteHistory(
                Segment(cutoff.AddHours(-2), cutoff, "Expired"),
                Segment(cutoff.AddMinutes(-1), cutoff.AddMinutes(1), "Crossing"),
                Segment(cutoff.AddMinutes(1), cutoff.AddMinutes(2), "Recent"));
            var store = new UsageStore(_folder, clock: new TestClock());
            var sessions = store.GetSessions(cutoff.ToLocalTime().Date);
            Assert.AreEqual(2, sessions.Count);
            Assert.AreEqual(cutoff, sessions.Single(segment => segment.AppName == "Crossing").StartUtc);
            Assert.AreEqual(120, sessions.Sum(segment => (segment.EndUtc - segment.StartUtc).TotalSeconds));
            Assert.IsTrue(store.Flush());
            var saved = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(_folder, "usage.json")),
                StorageJsonContext.Default.UsageDataFile)!;
            Assert.AreEqual(cutoff, saved.TrackingStartedUtc);
            Assert.AreEqual(2, saved.Segments.Count);
        }

        [DataTestMethod]
        [DataRow(2026, 28)]
        [DataRow(2024, 29)]
        public void CalendarMonthHandlesShortFebruaryWithoutTreatingMonthAsThirtyDays(int year, int day)
        {
            var cutoff = new DateTime(year, 2, day, 0, 0, 0, DateTimeKind.Utc);
            WriteHistory(Segment(cutoff.AddMinutes(-1), cutoff, "Expired"),
                Segment(cutoff, cutoff.AddMinutes(1), "Boundary"));
            var clock = new TestClock { Now = new DateTimeOffset(year, 3, 31, 12, 0, 0, TimeSpan.Zero) };
            var sessions = new UsageStore(_folder, clock: clock).GetSessions(cutoff.ToLocalTime().Date);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual("Boundary", sessions[0].AppName);
        }

        [TestMethod]
        public void RetentionCleanupWaitsForTheExclusiveCollectorLease()
        {
            var clock = new TestClock();
            var recent = clock.Now.UtcDateTime.AddMinutes(-1);
            WriteHistory(Segment(recent.AddMonths(-2), recent.AddMonths(-2).AddSeconds(10), "Expired"),
                Segment(recent, recent.AddSeconds(10), "Recent"));
            var path = Path.Combine(_folder, "usage.json");
            var original = File.ReadAllBytes(path);
            var store = new UsageStore(_folder, clock: clock);
            using (var owner = new FileStream(Path.Combine(_folder, "collector.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                store.SaveRetentionIfIdle();
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            }
            store.SaveRetentionIfIdle();
            var saved = JsonSerializer.Deserialize(File.ReadAllText(path), StorageJsonContext.Default.UsageDataFile)!;
            Assert.AreEqual(1, saved.Segments.Count);
            Assert.AreEqual("Recent", saved.Segments[0].AppName);
        }

        [TestMethod]
        public void PowerHistoryCannotReintroduceDataBeforeTheRetentionBoundary()
        {
            var clock = new TestClock();
            var cutoff = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
            WriteHistory(Segment(cutoff.AddMonths(-1), cutoff.AddMonths(-1).AddMinutes(1), "Expired"));
            var store = new UsageStore(_folder, clock: clock);
            Assert.IsFalse(store.ApplySystemIntervals(new[]
            {
                new SystemInactiveInterval(cutoff.AddHours(-2), cutoff, InactivityReason.Sleep),
            }));
            Assert.IsTrue(store.ApplySystemIntervals(new[]
            {
                new SystemInactiveInterval(cutoff.AddMinutes(-1), cutoff.AddMinutes(1), InactivityReason.Sleep),
            }));
            var sessions = store.GetSessions(cutoff.ToLocalTime().Date);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual(cutoff, sessions[0].StartUtc);
        }

        [TestMethod]
        public void DeferredOrMalformedHistoryCannotBeOverwrittenByAnEmptySave()
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, "usage.json");
            File.WriteAllText(path, "{ malformed");
            var store = new UsageStore(_folder, loadImmediately: false);
            Assert.IsNull(store.LastError);
            Assert.IsFalse(store.Flush());
            store.ReloadFromDisk();
            Assert.IsNotNull(store.LastError);
            Assert.IsNull(store.TryAcquireCollectorLease());
            Assert.IsFalse(store.Flush());
            Assert.AreEqual("{ malformed", File.ReadAllText(path));
        }

        [TestMethod]
        public async Task LargeLegacyHistoryIsDeferredThenPrunedAndCompacted()
        {
            Directory.CreateDirectory(_folder);
            var clock = new TestClock();
            var path = Path.Combine(_folder, "usage.json");
            var old = clock.Now.UtcDateTime.AddYears(-1);
            var recent = clock.Now.UtcDateTime.AddMinutes(-1);
            using (var output = File.Create(path))
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("TrackingStartedUtc", old);
                writer.WriteStartArray("Segments");
                for (var index = 0; index < 100000; index++)
                {
                    JsonSerializer.Serialize(writer, Segment(old.AddSeconds(index), old.AddSeconds(index + 1), "Legacy fixture"),
                        StorageJsonContext.Default.UsageSegment);
                }
                JsonSerializer.Serialize(writer, Segment(recent, recent.AddSeconds(10), "Recent"),
                    StorageJsonContext.Default.UsageSegment);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            Assert.IsTrue(new FileInfo(path).Length > 10 * 1024 * 1024);
            UsageStore store;
            using (var inaccessible = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                store = new UsageStore(_folder, clock: clock, loadImmediately: false);
                Assert.IsNull(store.LastError, "Deferred construction must not even attempt to open history.");
                Assert.IsFalse(store.Flush());
            }
            await Task.Run(() => store.ReloadFromDisk());
            var sessions = store.GetSessions(recent.ToLocalTime().Date);
            Assert.AreEqual(1, sessions.Count);
            Assert.AreEqual("Recent", sessions[0].AppName);
            store.SaveRetentionIfIdle();
            Assert.IsTrue(new FileInfo(path).Length < 2048);
        }

        private void WriteHistory(params UsageSegment[] segments)
        {
            Directory.CreateDirectory(_folder);
            var data = new UsageDataFile { TrackingStartedUtc = segments.Min(segment => segment.StartUtc) };
            data.Segments.AddRange(segments);
            File.WriteAllText(Path.Combine(_folder, "usage.json"),
                JsonSerializer.Serialize(data, StorageJsonContext.Default.UsageDataFile));
        }

        private static UsageSegment Segment(DateTime start, DateTime end, string name) =>
            new() { StartUtc = start, EndUtc = end, AppName = name, ProcessName = "fixture.exe", IsActive = true };
    }
}
