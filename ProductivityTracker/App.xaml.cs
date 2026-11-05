using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;

namespace ProductivityTracker
{
    public partial class App : Application
    {
        private Window? _window;
        private Mutex? _workerMutex;
        private ManualResetEventSlim? _workerWaitHandle;
        private UsageStore? _workerStore;
        private UsageTracker? _workerTracker;

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
            _workerMutex = new Mutex(
                false,
                BackgroundWorkerManager.MutexName,
                out var createdNew);
            if (!createdNew)
            {
                _workerMutex.Dispose();
                _workerMutex = null;
                return;
            }

            _workerStore = new UsageStore();
            _workerTracker = new UsageTracker(_workerStore);
            _workerTracker.Start();

            _workerWaitHandle = new ManualResetEventSlim(false);
            _workerWaitHandle.Wait();
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
