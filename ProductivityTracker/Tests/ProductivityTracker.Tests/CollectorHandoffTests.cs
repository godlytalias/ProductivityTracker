using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class CollectorHandoffTests
    {
        [TestMethod]
        [SupportedOSPlatform("windows")]
        public void FailedFinalSaveRetainsStorageAndUnsavedSamplesUntilRetry()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows file-sharing behavior is required.");
            }
            var folder = Path.Combine(Path.GetTempPath(), "ProductivityTracker.Tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new UsageStore(folder);
                using var tracker = new UsageTracker(store);
                tracker.Start();
                tracker.Stop();
                var start = DateTime.UtcNow.AddMinutes(-1);
                using (var blockedFile = new FileStream(store.DataFilePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    tracker.Start();
                    store.RecordSample(start, "HandoffProbe", "probe.exe", true);
                    store.CloseCurrent(start.AddSeconds(10));
                    tracker.Stop();
                    Assert.IsNotNull(store.LastError);
                    Assert.IsNull(new UsageStore(folder).TryAcquireCollectorLease(),
                        "Another writer must not take over while final samples are unsaved.");
                }
                tracker.Start();
                tracker.Stop();
                Assert.IsNull(store.LastError);
                var successor = new UsageStore(folder);
                using var lease = successor.TryAcquireCollectorLease();
                Assert.IsNotNull(lease);
                Assert.IsTrue(successor.GetSessions(start.ToLocalTime().Date)
                    .Any(segment => segment.AppName == "HandoffProbe"),
                    "Retrying a failed hand-off must not reload over unsaved samples.");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [TestMethod]
        public void NewCollectorReloadsFinalHistoryOnlyAfterPreviousCollectorReleasesStorage()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ProductivityTracker.Tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var dashboard = new UsageStore(folder);
                var worker = new UsageStore(folder);
                var start = DateTime.Today.ToUniversalTime().AddHours(1);
                using (var dashboardLease = dashboard.TryAcquireCollectorLease())
                {
                    Assert.IsNotNull(dashboardLease);
                    dashboard.RecordSample(start, "Editor", "editor.exe", true);
                    dashboard.CloseCurrent(start.AddMinutes(1));
                    Assert.IsNull(worker.TryAcquireCollectorLease());
                    Assert.IsTrue(dashboard.Flush());
                }

                using (var workerLease = worker.TryAcquireCollectorLease())
                {
                    Assert.IsNotNull(workerLease);
                    worker.RecordSample(start.AddMinutes(1), "Browser", "browser.exe", true);
                    worker.CloseCurrent(start.AddMinutes(2));
                    Assert.IsTrue(worker.Flush());
                }

                var restored = new UsageStore(folder).GetSessions(start.ToLocalTime().Date);
                CollectionAssert.AreEquivalent(new[] { "Editor", "Browser" },
                    restored.Select(segment => segment.AppName).ToArray());
                Assert.AreEqual(120, restored.Sum(segment => (segment.EndUtc - segment.StartUtc).TotalSeconds));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}

namespace ProductivityTracker
{
    internal static class App
    {
        public static void Log(string message) { }
    }

    internal static class AppStrings
    {
        public static string Get(string key) => key;
        public static string Format(string key, params object[] values) => key + ": " + string.Join(", ", values);
    }
}
