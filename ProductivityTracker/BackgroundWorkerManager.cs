using System;
using System.IO;
using System.Threading;

namespace ProductivityTracker
{
    internal static class BackgroundWorkerManager
    {
        public const string MutexName = "Local\\ProductivityTracker.Worker";

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
