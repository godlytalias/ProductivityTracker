using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;

namespace ProductivityTracker
{
    public partial class App : Application
    {
        private Window? _window;

        public static string LogFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductivityTracker",
            "app.log");

        public App()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            UnhandledException += App_UnhandledException;
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Log("Application resource initialization failed: " + ex);
                throw;
            }
            Log("Application constructed.");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                Log("OnLaunched.");
                AppStrings.Initialize();
                if (Environment.GetCommandLineArgs().Concat(
                    args.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Any(argument =>
                        string.Equals(argument, "--worker", StringComparison.OrdinalIgnoreCase)))
                {
                    StartBackgroundWorker();
                    return;
                }

                _window ??= new MainWindow();
                _window.Activate();
                Log("Main window activated.");
            }
            catch (Exception ex)
            {
                Log("Launch failed: " + ex);
            }
        }

        private void StartBackgroundWorker()
        {
            using var workerMutex = new Mutex(
                false,
                BackgroundWorkerManager.MutexName,
                out var createdNew);
            if (!createdNew)
            {
                Exit();
                return;
            }

            using var stop = new EventWaitHandle(false, EventResetMode.ManualReset,
                BackgroundWorkerManager.StopEventName);
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset,
                BackgroundWorkerManager.ReadyEventName);
            stop.Reset();
            ready.Reset();
            var store = new UsageStore();
            using var tracker = new UsageTracker(store);
            try
            {
                var timer = Stopwatch.StartNew();
                while (!tracker.IsTracking && timer.Elapsed < TimeSpan.FromSeconds(10))
                {
                    tracker.Start();
                    if (!tracker.IsTracking && stop.WaitOne(100))
                    {
                        return;
                    }
                }
                if (!tracker.IsTracking)
                {
                    Log("The background collector could not acquire usage storage.");
                    return;
                }
                ready.Set();
                Log("Background collector ready.");
                stop.WaitOne();
            }
            finally
            {
                ready.Reset();
                tracker.Stop();
                Log("Background collector stopped.");
                Exit();
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Log("Unhandled exception: " + e.Message + Environment.NewLine + e.Exception);
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            Log("AppDomain unhandled exception: " + e.ExceptionObject);
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    LogFilePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
