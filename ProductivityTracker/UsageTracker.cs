using System;
using System.Diagnostics;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProductivityTracker
{
    [SupportedOSPlatform("windows")]
    internal sealed class UsageTracker : IDisposable
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private readonly UsageStore _store;
        private readonly object _stateGate = new();
        private SystemActivityMonitor? _systemMonitor;
        private DateTime? _suspendStartedUtc;
        private DateTime _nextHistoryRefreshUtc;
        private int _historyReadRunning;
        private int _generation;
        private bool _sessionEnding;
        private Timer? _sampleTimer;
        private Timer? _flushTimer;
        private FileStream? _collectorLease;
        private int _sampleCount;
        private int _idleThresholdSeconds = 60;

        public UsageTracker(UsageStore store)
        {
            _store = store;
        }

        public bool IsTracking { get; private set; }
        internal bool HasCollectorLease
        {
            get
            {
                lock (_stateGate)
                {
                    return _collectorLease != null;
                }
            }
        }
        private string? _notificationWarning;
        private string? _historyWarning;
        public string? SystemHistoryWarning => _notificationWarning ?? _historyWarning;

        public int IdleThresholdSeconds
        {
            get => Volatile.Read(ref _idleThresholdSeconds);
            set => Volatile.Write(
                ref _idleThresholdSeconds,
                Math.Clamp(value, 5, 3600));
        }

        public event EventHandler? UsageChanged;

        public void Start()
        {
            lock (_stateGate)
            {
                if (IsTracking)
                {
                    return;
                }

                _collectorLease ??= _store.TryAcquireCollectorLease();
                if (_collectorLease == null)
                {
                    return;
                }
                IsTracking = true;
                _sessionEnding = false;
                _generation++;
                try
                {
                    _systemMonitor = new SystemActivityMonitor();
                    _systemMonitor.Changed += SystemActivityChanged;
                    _notificationWarning = null;
                    App.Log("System activity notifications active; initiallyLocked=" + _systemMonitor.IsLocked);
                }
                catch (Exception ex) when (ex is ExternalException or InvalidOperationException or SecurityException)
                {
                    App.Log("System activity notification registration failed: " + ex);
                    _notificationWarning = AppStrings.Get("SystemNotificationsUnavailable");
                }
                _sampleTimer?.Dispose();
                _flushTimer?.Dispose();
                _sampleTimer = new Timer(
                    Sample,
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1));
                _flushTimer = new Timer(
                    Flush,
                    null,
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(30));
                QueueSystemHistory();
            }
        }

        public void Stop()
        {
            StopCore(true);
        }

        public void StopWithoutFlush()
        {
            StopCore(false);
        }

        private void StopCore(bool flush)
        {
            SystemActivityMonitor? monitor;
            lock (_stateGate)
            {
                if (!IsTracking)
                {
                    if (_collectorLease != null && (!flush || _store.Flush()))
                    {
                        _collectorLease.Dispose();
                        _collectorLease = null;
                    }
                    return;
                }

                IsTracking = false;
                _generation++;
                _sampleTimer?.Dispose();
                _sampleTimer = null;
                _flushTimer?.Dispose();
                _flushTimer = null;
                monitor = _systemMonitor;
                if (monitor != null)
                {
                    monitor.Changed -= SystemActivityChanged;
                    _systemMonitor = null;
                }
                _suspendStartedUtc = null;
                _store.CloseCurrent(DateTime.UtcNow);
                if (!flush || _store.Flush())
                {
                    _collectorLease?.Dispose();
                    _collectorLease = null;
                }
            }
            monitor?.Dispose();
        }

        public void Dispose()
        {
            Stop();
            _collectorLease?.Dispose();
            _collectorLease = null;
        }

        private void Sample(object? state)
        {
            lock (_stateGate)
            {
                if (!IsTracking || _suspendStartedUtc.HasValue || _sessionEnding)
                {
                    return;
                }

                string appName;
                string processName;
                bool isActive;
                InactivityReason? inactiveReason = null;

                if (_systemMonitor?.IsLocked == true)
                {
                    appName = "Inactive";
                    processName = string.Empty;
                    isActive = false;
                    inactiveReason = InactivityReason.Locked;
                }
                else
                {
                    var idleSeconds = GetIdleSeconds();
                    isActive = !UsageTrackerSettings.IsPaused() &&
                        idleSeconds < UsageTrackerSettings.GetIdleThresholdSeconds();
                    if (isActive)
                    {
                        var application = GetForegroundApplication();
                        processName = application.ProcessName;
                        appName = _store.ApplicationNames.Resolve(application);
                    }
                    else
                    {
                        appName = "Inactive";
                        processName = string.Empty;
                    }
                }

                _store.RecordSample(
                    DateTime.UtcNow,
                    appName,
                    processName,
                    isActive,
                    inactiveReason);
            }

            var sampleNumber = Interlocked.Increment(ref _sampleCount);
            if (sampleNumber % 5 == 0)
            {
                UsageChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Flush(object? state)
        {
            lock (_stateGate)
            {
                if (!IsTracking || _sessionEnding)
                {
                    return;
                }
                _store.Flush();
                if (DateTime.UtcNow >= _nextHistoryRefreshUtc)
                {
                    QueueSystemHistory();
                }
            }
        }

        private void SystemActivityChanged(SystemActivityChange change, DateTime timestampUtc)
        {
            lock (_stateGate)
            {
                if (!IsTracking)
                {
                    return;
                }
                if (change == SystemActivityChange.Resume && !_suspendStartedUtc.HasValue)
                {
                    _store.EndCurrentAtLastSample();
                }
                else
                {
                    _store.CloseCurrent(timestampUtc);
                }
                if (change == SystemActivityChange.Suspend)
                {
                    _suspendStartedUtc ??= timestampUtc;
                    _store.RecordSample(timestampUtc, "Inactive", string.Empty, false, InactivityReason.Sleep);
                }
                else if (change == SystemActivityChange.Resume)
                {
                    if (_suspendStartedUtc.HasValue)
                    {
                        _store.ApplySystemIntervals(new[]
                        {
                            new SystemInactiveInterval(_suspendStartedUtc.Value, timestampUtc, InactivityReason.Sleep),
                        });
                        _suspendStartedUtc = null;
                    }
                    QueueSystemHistory();
                    _nextHistoryRefreshUtc = DateTime.UtcNow.AddSeconds(5);
                }
                else if (change == SystemActivityChange.SessionEnding)
                {
                    _sessionEnding = true;
                    _generation++;
                }
                if (!_suspendStartedUtc.HasValue && !_sessionEnding)
                {
                    Sample(null);
                }
                _store.Flush();
            }
            UsageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void QueueSystemHistory()
        {
            _nextHistoryRefreshUtc = DateTime.UtcNow.AddMinutes(5);
            if (Interlocked.CompareExchange(ref _historyReadRunning, 1, 0) != 0)
            {
                return;
            }
            var generation = _generation;
            var fromUtc = _store.TrackingStartedUtc;
            _ = Task.Run(() =>
            {
                try
                {
                    var intervals = SystemEventHistory.Read(fromUtc, DateTime.UtcNow);
                    lock (_stateGate)
                    {
                        if (!IsTracking || _generation != generation)
                        {
                            return;
                        }
                        if (_store.ApplySystemIntervals(intervals))
                        {
                            _store.Flush();
                        }
                        _historyWarning = null;
                        App.Log($"Windows power history recovered {intervals.Count} confirmed interval(s).");
                    }
                    UsageChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or
                    SecurityException or System.Xml.XmlException or FormatException)
                {
                    App.Log("System event history could not be read: " + ex);
                    _historyWarning = AppStrings.Get("PowerHistoryUnavailable");
                }
                finally
                {
                    Interlocked.Exchange(ref _historyReadRunning, 0);
                }
            });
        }

        private static uint GetIdleSeconds()
        {
            var lastInput = new LastInputInfo
            {
                cbSize = (uint)Marshal.SizeOf<LastInputInfo>(),
            };

            if (!GetLastInputInfo(ref lastInput))
            {
                return 0;
            }

            var currentTick = unchecked((uint)GetTickCount64());
            var elapsedMilliseconds = unchecked(currentTick - lastInput.dwTime);
            return elapsedMilliseconds / 1000;
        }

        private static ApplicationIdentity GetForegroundApplication()
        {
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return new ApplicationIdentity("desktop");
            }

            _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
            if (processId == 0)
            {
                return new ApplicationIdentity("desktop");
            }

            var processHandle = OpenProcess(
                ProcessQueryLimitedInformation,
                false,
                processId);
            if (processHandle != IntPtr.Zero)
            {
                try
                {
                    var capacity = 1024;
                    var buffer = new StringBuilder(capacity);
                    if (QueryFullProcessImageName(
                        processHandle,
                        0,
                        buffer,
                        ref capacity) &&
                        capacity > 0)
                    {
                        var path = buffer.ToString();
                        var fileName = Path.GetFileName(path);
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            return new ApplicationIdentity(fileName, path,
                                GetProcessPackageName(processHandle), GetProcessAppUserModelId(processHandle));
                        }
                    }
                }
                finally
                {
                    CloseHandle(processHandle);
                }
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                var path = process.MainModule?.FileName;
                return new ApplicationIdentity(Path.GetFileName(path ?? "Unknown.exe"), path);
            }
            catch (ArgumentException)
            {
                return new ApplicationIdentity("desktop");
            }
            catch (InvalidOperationException)
            {
                return new ApplicationIdentity("desktop");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return new ApplicationIdentity("desktop");
            }
        }

        private static string? GetProcessPackageName(IntPtr process)
        {
            uint size = 0;
            if (GetPackageFullName(process, ref size, null) != 122)
            {
                return null;
            }
            var buffer = new StringBuilder((int)size);
            return GetPackageFullName(process, ref size, buffer) == 0 ? buffer.ToString() : null;
        }

        private static string? GetProcessAppUserModelId(IntPtr process)
        {
            uint size = 0;
            if (GetApplicationUserModelId(process, ref size, null) != 122)
            {
                return null;
            }
            var buffer = new StringBuilder((int)size);
            return GetApplicationUserModelId(process, ref size, buffer) == 0 ? buffer.ToString() : null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ulong GetTickCount64();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint desiredAccess,
            bool inheritHandle,
            uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            IntPtr process,
            uint flags,
            StringBuilder name,
            ref int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder? appId);
    }
}
