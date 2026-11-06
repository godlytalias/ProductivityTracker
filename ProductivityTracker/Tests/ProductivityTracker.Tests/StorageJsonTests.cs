using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class StorageJsonTests
    {
        [TestMethod]
        public void GeneratedContextReadsLegacyHistoryWithoutNewFields()
        {
            const string json = "{\"Version\":1,\"Segments\":[{\"StartUtc\":\"2026-10-30T08:00:00Z\"," +
                "\"EndUtc\":\"2026-10-30T09:00:00Z\",\"AppName\":\"Editor\",\"ProcessName\":\"editor.exe\",\"IsActive\":true}]}";
            var data = JsonSerializer.Deserialize(json, StorageJsonContext.Default.UsageDataFile)!;
            Assert.AreEqual(1, data.Version);
            Assert.IsNull(data.TrackingStartedUtc);
            Assert.AreEqual(1, data.Segments.Count);
            Assert.AreEqual("Editor", data.Segments[0].AppName);
            Assert.AreEqual(DateTimeKind.Utc, data.Segments[0].StartUtc.Kind);
            Assert.IsNull(data.Segments[0].InactiveReason);
        }

        [TestMethod]
        public void GeneratedContextPreservesPowerStatesAndDoesNotPersistComputedProperties()
        {
            var start = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
            var data = new UsageDataFile
            {
                TrackingStartedUtc = start,
                Segments =
                {
                    new UsageSegment { StartUtc = start, EndUtc = start.AddHours(1),
                        AppName = "Inactive", InactiveReason = InactivityReason.Sleep },
                },
            };
            var json = JsonSerializer.Serialize(data, StorageJsonContext.Default.UsageDataFile);
            using var document = JsonDocument.Parse(json);
            var segment = document.RootElement.GetProperty("Segments")[0];
            Assert.AreEqual("Sleep", segment.GetProperty("InactiveReason").GetString());
            Assert.IsFalse(segment.TryGetProperty("IsPoweredDown", out _));
            var restored = JsonSerializer.Deserialize(json, StorageJsonContext.Default.UsageDataFile)!;
            Assert.AreEqual(start, restored.TrackingStartedUtc);
            Assert.AreEqual(start.AddHours(1), restored.Segments[0].EndUtc);
            Assert.IsTrue(restored.Segments[0].IsPoweredDown);
        }

        [TestMethod]
        public void GeneratedSettingsContextPreservesExistingNamesAndDefaults()
        {
            var defaults = JsonSerializer.Deserialize("{}", StorageJsonContext.Default.SettingsData)!;
            Assert.AreEqual(60, defaults.IdleThresholdSeconds);
            Assert.IsTrue(defaults.KeepTrackingAfterClosing);
            Assert.IsTrue(defaults.RunAtSignIn);
            var settings = JsonSerializer.Deserialize("{\"IdleThresholdSeconds\":120}",
                StorageJsonContext.Default.SettingsData)!;
            Assert.AreEqual(120, settings.IdleThresholdSeconds);
            Assert.IsTrue(settings.KeepTrackingAfterClosing);
            Assert.IsTrue(settings.RunAtSignIn);
        }

        [TestMethod]
        public void GeneratedSettingsContextPreservesOptOutsAndIdleThreshold()
        {
            var settings = JsonSerializer.Deserialize(
                "{\"IdleThresholdSeconds\":120,\"KeepTrackingAfterClosing\":false,\"RunAtSignIn\":false}",
                StorageJsonContext.Default.SettingsData)!;
            var json = JsonSerializer.Serialize(settings, StorageJsonContext.Default.SettingsData);
            var restored = JsonSerializer.Deserialize(json, StorageJsonContext.Default.SettingsData)!;
            Assert.AreEqual(120, restored.IdleThresholdSeconds);
            Assert.IsFalse(restored.KeepTrackingAfterClosing);
            Assert.IsFalse(restored.RunAtSignIn);
        }
    }
}
