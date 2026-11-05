using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    [SupportedOSPlatform("windows")]
    public sealed class WindowsSystemActivityTests
    {
        [TestMethod]
        public void HeadlessMonitorReceivesOwnedWindowNotificationsWithoutChangingThePcState()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows notification integration requires Windows.");
            }
            var changes = new ConcurrentQueue<SystemActivityChange>();
            using var monitor = new SystemActivityMonitor();
            monitor.Changed += (change, _) => changes.Enqueue(change);
            var window = FindOwnedSystemEventsWindow();
            Assert.AreNotEqual(IntPtr.Zero, window, "The headless SystemEvents message pump was not created.");

            SendOwnedNotification(window, 0x02B1, 7, changes, 1);
            Assert.IsTrue(monitor.IsLocked);
            SendOwnedNotification(window, 0x02B1, 8, changes, 2);
            Assert.IsFalse(monitor.IsLocked);
            monitor.HandlePowerNotification(4);
            monitor.HandlePowerNotification(18);
            CollectionAssert.AreEqual(new[]
            {
                SystemActivityChange.Locked, SystemActivityChange.Unlocked,
                SystemActivityChange.Suspend, SystemActivityChange.Resume,
            }, changes.ToArray());
        }

        [TestMethod]
        public void BothAutomaticAndInteractiveResumeCallbacksAreHandled()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows notification integration requires Windows.");
            }
            var changes = new ConcurrentQueue<SystemActivityChange>();
            using var monitor = new SystemActivityMonitor();
            monitor.Changed += (change, _) => changes.Enqueue(change);
            monitor.HandlePowerNotification(4);
            monitor.HandlePowerNotification(18);
            monitor.HandlePowerNotification(7);
            monitor.HandlePowerNotification(10);
            CollectionAssert.AreEqual(new[]
            {
                SystemActivityChange.Suspend, SystemActivityChange.Resume, SystemActivityChange.Resume,
            }, changes.ToArray());
        }

        [TestMethod]
        public void WindowsPowerHistoryQueryExecutesAgainstTheRealSystemLog()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows event history requires Windows.");
            }
            var until = DateTime.UtcNow;
            var from = until.AddDays(-7);
            var intervals = SystemEventHistory.Read(from, until);
            foreach (var interval in intervals)
            {
                Assert.IsTrue(interval.StartUtc >= from && interval.EndUtc <= until);
                Assert.IsTrue(interval.EndUtc > interval.StartUtc);
                Assert.IsTrue(interval.Reason is InactivityReason.Sleep or InactivityReason.PoweredOff);
            }
        }

        private static void SendOwnedNotification(
            IntPtr window, uint message, int value, ConcurrentQueue<SystemActivityChange> changes, int expected)
        {
            using var process = Process.GetCurrentProcess();
            Assert.AreNotEqual(IntPtr.Zero,
                SendMessageTimeout(window, message, (IntPtr)value, (IntPtr)process.SessionId,
                    2, 3000, out _), "The owned notification window did not respond.");
            Assert.IsTrue(SpinWait.SpinUntil(() => changes.Count >= expected, TimeSpan.FromSeconds(3)),
                "The notification did not reach the monitor.");
        }

        private static IntPtr FindOwnedSystemEventsWindow()
        {
            var found = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner == Environment.ProcessId)
                {
                    var name = new StringBuilder(256);
                    GetClassName(window, name, name.Capacity);
                    if (name.ToString().Contains("BroadcastEventWindow", StringComparison.Ordinal))
                    {
                        found = window;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, IntPtr wparam, IntPtr lparam,
            uint flags, uint timeout, out IntPtr result);
    }
}
