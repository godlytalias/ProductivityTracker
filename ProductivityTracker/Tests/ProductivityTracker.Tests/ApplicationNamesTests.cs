using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class ApplicationNamesTests
    {
        [DataTestMethod]
        [DataRow("ms-teams", "Microsoft Teams")]
        [DataRow("MS-TEAMS.EXE", "Microsoft Teams")]
        [DataRow("msrdc", "Remote Desktop")]
        [DataRow("MSRDC.EXE", "Remote Desktop")]
        [DataRow("mstsc", "Remote Desktop Connection")]
        [DataRow("MSTSC.EXE", "Remote Desktop Connection")]
        public void KnownProcessAliasesUseFriendlyNames(string process, string expected)
        {
            Assert.AreEqual(expected, ApplicationNames.FromProcessName(process));
            Assert.AreEqual(expected, ApplicationNames.ForRecordedName(process, process));
        }

        [TestMethod]
        public void ExistingDescriptionsAndUnknownApplicationsArePreserved()
        {
            Assert.AreEqual("Teams for work",
                ApplicationNames.ForRecordedName("Teams for work", "ms-teams.exe"));
            Assert.AreEqual("custom-editor", ApplicationNames.FromProcessName("custom-editor.exe"));
            Assert.AreEqual("Editor", ApplicationNames.ForRecordedName("Editor", "editor.exe"));
            Assert.AreEqual("Microsoft Teams", ApplicationNames.ForRecordedName("ms-teams", string.Empty));
            Assert.AreEqual("Remote Desktop", ApplicationNames.ForRecordedName(string.Empty, "msrdc.exe"));
        }

        [TestMethod]
        public void HistoricalTimelineAndSummaryResolveAliasesWithoutRewritingHistory()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ProductivityTracker.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var start = DateTime.Today.ToUniversalTime().AddHours(1);
                var data = new UsageDataFile
                {
                    TrackingStartedUtc = start,
                    Segments =
                    {
                        new UsageSegment { StartUtc = start, EndUtc = start.AddMinutes(1),
                            AppName = "ms-teams", ProcessName = "ms-teams.exe", IsActive = true },
                        new UsageSegment { StartUtc = start.AddMinutes(1), EndUtc = start.AddMinutes(2),
                            AppName = "Microsoft Teams", ProcessName = "ms-teams.exe", IsActive = true },
                        new UsageSegment { StartUtc = start.AddMinutes(2), EndUtc = start.AddMinutes(3),
                            AppName = "mstsc", ProcessName = "mstsc.exe", IsActive = true },
                    },
                };
                var file = Path.Combine(folder, "usage.json");
                File.WriteAllText(file, JsonSerializer.Serialize(data, StorageJsonContext.Default.UsageDataFile));
                var original = File.ReadAllBytes(file);
                var store = new UsageStore(folder);
                var sessions = store.GetSessions(start.ToLocalTime().Date);
                CollectionAssert.AreEquivalent(
                    new[] { "Microsoft Teams", "Microsoft Teams", "Remote Desktop Connection" },
                    sessions.Select(segment => segment.AppName).ToArray());
                CollectionAssert.AreEquivalent(new[] { "ms-teams.exe", "ms-teams.exe", "mstsc.exe" },
                    sessions.Select(segment => segment.ProcessName).ToArray());
                var summary = store.GetDailySummary(start.ToLocalTime().Date);
                Assert.AreEqual(2, summary.Applications.Count);
                Assert.AreEqual("Microsoft Teams", summary.TopAppName);
                Assert.AreEqual(120, summary.TopAppSeconds);
                Assert.AreEqual(180, summary.ActiveSeconds);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(file));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
