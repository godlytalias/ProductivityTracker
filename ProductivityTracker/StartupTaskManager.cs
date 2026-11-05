using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ProductivityTracker
{
    internal sealed class StartupTaskResult
    {
        public StartupTaskResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }

        public string Message { get; }
    }

    internal static class StartupTaskManager
    {
        public const string TaskName = "ProductivityTracker";
        private const string StartupLauncherFileName = "ProductivityTrackerWorker.cmd";

        public static async Task<bool> IsEnabledAsync()
        {
            if (File.Exists(GetStartupLauncherPath()))
            {
                return true;
            }

            var result = await ExecuteAsync(
                "/Query",
                "/TN",
                TaskName,
                "/FO",
                "LIST",
                "/V");
            return result.Success;
        }

        public static async Task<StartupTaskResult> EnableAsync()
        {
            var executablePath = Environment.ProcessPath;
            var packageNameLength = 0;
            if (GetCurrentPackageFullName(ref packageNameLength, IntPtr.Zero) == 122)
            {
                executablePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "ProductivityTracker.exe");
            }
            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                return new StartupTaskResult(
                    false,
                    AppStrings.Get("ExecutableNotFound"));
            }

            var taskAction = "\"" + executablePath + "\" --worker";

            var scheduledTaskResult = await ExecuteAsync(
                "/Create",
                "/TN",
                TaskName,
                "/TR",
                taskAction,
                "/SC",
                "ONLOGON",
                "/F");
            if (scheduledTaskResult.Success)
            {
                RemoveStartupLauncher();
                return scheduledTaskResult;
            }

            var startupLauncherResult = CreateStartupLauncher(
                executablePath,
                scheduledTaskResult.Message);
            return startupLauncherResult.Success
                ? startupLauncherResult
                : scheduledTaskResult;
        }

        public static async Task<StartupTaskResult> DisableAsync()
        {
            var taskWasEnabled = await IsScheduledTaskEnabledAsync();
            var startupLauncherRemoved = RemoveStartupLauncher();

            if (!taskWasEnabled)
            {
                return new StartupTaskResult(true, AppStrings.Get("StartupAlreadyDisabled"));
            }

            var result = await ExecuteAsync(
                "/Delete",
                "/TN",
                TaskName,
                "/F");
            if (result.Success || startupLauncherRemoved)
            {
                return new StartupTaskResult(true, string.Empty);
            }

            return result;
        }

        private static async Task<bool> IsScheduledTaskEnabledAsync()
        {
            var result = await ExecuteAsync(
                "/Query",
                "/TN",
                TaskName,
                "/FO",
                "LIST",
                "/V");
            return result.Success;
        }

        private static string GetStartupLauncherPath()
        {
            var startupFolder = Environment.GetFolderPath(
                Environment.SpecialFolder.Startup);
            return Path.Combine(startupFolder, StartupLauncherFileName);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int length, IntPtr packageFullName);

        private static StartupTaskResult CreateStartupLauncher(
            string executablePath,
            string scheduledTaskError)
        {
            try
            {
                var launcherPath = GetStartupLauncherPath();
                Directory.CreateDirectory(Path.GetDirectoryName(launcherPath)!);
                File.WriteAllText(
                    launcherPath,
                    "@echo off\r\n" +
                    "start \"\" \"" + executablePath.Replace("\"", "\"\"") + "\" --worker\r\n");
                return new StartupTaskResult(
                    true,
                    AppStrings.Format("StartupFallbackCreatedFormat", scheduledTaskError));
            }
            catch (IOException ex)
            {
                return new StartupTaskResult(
                    false,
                    AppStrings.Format("StartupFallbackFailedFormat", scheduledTaskError, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return new StartupTaskResult(
                    false,
                    AppStrings.Format("StartupFallbackFailedFormat", scheduledTaskError, ex.Message));
            }
        }

        private static bool RemoveStartupLauncher()
        {
            try
            {
                var launcherPath = GetStartupLauncherPath();
                if (!File.Exists(launcherPath))
                {
                    return false;
                }

                File.Delete(launcherPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static async Task<StartupTaskResult> ExecuteAsync(params string[] arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(
                        Environment.SystemDirectory,
                        "schtasks.exe"),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    return new StartupTaskResult(
                        false,
                        AppStrings.Get("TaskSchedulerNotStarted"));
                }

                var standardOutput = await process.StandardOutput.ReadToEndAsync();
                var standardError = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    return new StartupTaskResult(true, string.Empty);
                }

                var detail = string.IsNullOrWhiteSpace(standardError)
                    ? standardOutput.Trim()
                    : standardError.Trim();
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = AppStrings.Format("TaskSchedulerExitCodeFormat", process.ExitCode);
                }

                return new StartupTaskResult(false, detail);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return new StartupTaskResult(
                    false,
                    AppStrings.Format("TaskSchedulerAccessErrorFormat", ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return new StartupTaskResult(
                    false,
                    AppStrings.Format("TaskSchedulerAccessErrorFormat", ex.Message));
            }
        }
    }
}
