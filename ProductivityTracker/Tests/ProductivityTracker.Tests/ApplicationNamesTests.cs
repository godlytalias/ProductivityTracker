using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class ApplicationNamesTests
    {
        private sealed class TestClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        [TestMethod]
        public async Task PendingMetadataNeverBlocksAndNewApplicationsNeedNoAliasEntry()
        {
            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolver = new ApplicationNameResolver(_ => result.Task);
            var identity = new ApplicationIdentity("fresh-app.exe", PackageFullName: "FreshPackage_1.0",
                AppUserModelId: "FreshFamily!Notes");
            Assert.AreEqual("fresh-app", resolver.Resolve(identity));
            result.SetResult("Fresh Notes");
            await WaitForName(resolver, identity, "Fresh Notes");
        }

        [TestMethod]
        public async Task CacheSeparatesPackagedApplicationsSharingAnExecutable()
        {
            var calls = 0;
            var resolver = new ApplicationNameResolver(identity =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<string?>(identity.AppUserModelId);
            });
            var first = new ApplicationIdentity("host.exe", PackageFullName: "Package_1.0",
                AppUserModelId: "Family!First");
            var second = first with { AppUserModelId = "Family!Second" };
            await WaitForName(resolver, first, "Family!First");
            await WaitForName(resolver, second, "Family!Second");
            for (var index = 0; index < 10; index++)
            {
                Assert.AreEqual("Family!First", resolver.Resolve(first));
            }
            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task NegativeCacheRetriesWhenAnApplicationBecomesAvailable()
        {
            var clock = new TestClock();
            var calls = 0;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolver = new ApplicationNameResolver(_ =>
            {
                var call = Interlocked.Increment(ref calls);
                completed.TrySetResult(true);
                return Task.FromResult<string?>(call == 1 ? null : "Newly Installed Notes");
            }, clock);
            var identity = new ApplicationIdentity("new-notes.exe");
            Assert.AreEqual("new-notes", resolver.Resolve(identity));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(30);
            Assert.AreEqual("new-notes", resolver.Resolve(identity));
            clock.Now = clock.Now.AddSeconds(31);
            await WaitForName(resolver, identity, "Newly Installed Notes");
            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task MissingOrUnusableMetadataPreservesRecordedDescriptions()
        {
            var resolver = new ApplicationNameResolver(_ => Task.FromResult<string?>("ms-resource:AppName"));
            var identity = new ApplicationIdentity("custom-editor.exe");
            Assert.AreEqual("Editor", resolver.Resolve(identity, "Editor"));
            await Task.Delay(30);
            Assert.AreEqual("Editor", resolver.ForRecordedName("Editor", "custom-editor.exe"));
            Assert.AreEqual("custom-editor", resolver.Resolve(identity));
            var inaccessible = new ApplicationNameResolver(_ => throw new UnauthorizedAccessException());
            Assert.AreEqual("Saved display name", inaccessible.Resolve(identity, "Saved display name"));
            await Task.Delay(30);
            Assert.AreEqual("Saved display name", inaccessible.Resolve(identity, "Saved display name"));
        }

        [TestMethod]
        public async Task HistoricalTimelineAndSummaryUseMetadataWithoutRewritingHistory()
        {
            var resolver = new ApplicationNameResolver(identity => Task.FromResult<string?>(
                identity.ProcessName == "notes.exe" ? "Package Notes" : "Desktop Workspace"));
            await WaitForName(resolver, new ApplicationIdentity("notes.exe"), "Package Notes");
            await WaitForName(resolver, new ApplicationIdentity("workspace.exe"), "Desktop Workspace");
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
                            AppName = "notes", ProcessName = "notes.exe", IsActive = true },
                        new UsageSegment { StartUtc = start.AddMinutes(1), EndUtc = start.AddMinutes(2),
                            AppName = "Package Notes", ProcessName = "notes.exe", IsActive = true },
                        new UsageSegment { StartUtc = start.AddMinutes(2), EndUtc = start.AddMinutes(3),
                            AppName = "workspace", ProcessName = "workspace.exe", IsActive = true },
                    },
                };
                var file = Path.Combine(folder, "usage.json");
                File.WriteAllText(file, JsonSerializer.Serialize(data, StorageJsonContext.Default.UsageDataFile));
                var original = File.ReadAllBytes(file);
                var store = new UsageStore(folder, resolver);
                var sessions = store.GetSessions(start.ToLocalTime().Date);
                CollectionAssert.AreEquivalent(
                    new[] { "Package Notes", "Package Notes", "Desktop Workspace" },
                    sessions.Select(segment => segment.AppName).ToArray());
                CollectionAssert.AreEquivalent(new[] { "notes.exe", "notes.exe", "workspace.exe" },
                    sessions.Select(segment => segment.ProcessName).ToArray());
                var summary = store.GetDailySummary(start.ToLocalTime().Date);
                Assert.AreEqual(2, summary.Applications.Count);
                Assert.AreEqual("Package Notes", summary.TopAppName);
                Assert.AreEqual(120, summary.TopAppSeconds);
                Assert.AreEqual(180, summary.ActiveSeconds);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(file));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static async Task WaitForName(ApplicationNameResolver resolver, ApplicationIdentity identity, string expected)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (resolver.Resolve(identity) == expected)
                {
                    return;
                }
                await Task.Delay(20);
            }
            Assert.AreEqual(expected, resolver.Resolve(identity));
        }
    }
}
