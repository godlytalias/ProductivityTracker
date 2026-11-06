using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProductivityTracker
{
    internal static class BackgroundWorkerManager
    {
        public const string MutexName = "Local\\ProductivityTracker.Worker";
        public const string ReadyEventName = "Local\\ProductivityTracker.Worker.Ready";
        public const string StopEventName = "Local\\ProductivityTracker.Worker.Stop";

        public static async Task<string?> StartAsync()
        {
            if (IsReady())
            {
                return null;
            }
            try
            {
                if (!IsRunning())
                {
                    var executablePath = StartupTaskManager.GetCollectorExecutablePath();
                    if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    {
                        return AppStrings.Get("ExecutableNotFound");
                    }
                    var start = new ProcessStartInfo(executablePath)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    start.ArgumentList.Add("--worker");
                    using var process = Process.Start(start);
                    if (process == null)
                    {
                        return AppStrings.Get("BackgroundStartTimedOut");
                    }
                }

                var timer = Stopwatch.StartNew();
                while (timer.Elapsed < TimeSpan.FromSeconds(10))
                {
                    if (IsReady())
                    {
                        return null;
                    }
                    await Task.Delay(100);
                }
                return AppStrings.Get("BackgroundStartTimedOut");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                InvalidOperationException or UnauthorizedAccessException or IOException)
            {
                return AppStrings.Format("BackgroundStartFailedFormat", ex.Message);
            }
        }

        public static async Task<string?> StopAsync()
        {
            if (!IsRunning())
            {
                return null;
            }
            if (!EventWaitHandle.TryOpenExisting(StopEventName, out var stop))
            {
                return AppStrings.Get("BackgroundStopUnavailable");
            }

            using (stop)
            {
                var timer = Stopwatch.StartNew();
                while (!IsReady() && IsRunning() && timer.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(100);
                }
                stop.Set();
                while (IsRunning() && timer.Elapsed < TimeSpan.FromSeconds(15))
                {
                    await Task.Delay(100);
                }
            }
            return IsRunning() ? AppStrings.Get("BackgroundStopTimedOut") : null;
        }

        private static bool IsReady()
        {
            if (!EventWaitHandle.TryOpenExisting(ReadyEventName, out var ready))
            {
                return false;
            }
            using (ready)
            {
                return ready.WaitOne(0);
            }
        }

        public static bool IsRunning()
        {
            try
            {
                if (!Mutex.TryOpenExisting(MutexName, out var mutex))
                {
                    return false;
                }

                mutex.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        public static bool IsPaused()
        {
            return File.Exists(UsageTrackerSettings.PauseFilePath);
        }
    }
}
