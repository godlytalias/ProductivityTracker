using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ProductivityTracker
{
    internal enum SystemActivityChange
    {
        Locked,
        Unlocked,
        Suspend,
        Resume,
        SessionEnding,
    }

    [SupportedOSPlatform("windows")]
    internal sealed class SystemActivityMonitor : IDisposable
    {
        private volatile bool _locked;
        private volatile bool _disposed;
        private readonly PowerCallback _powerCallback;
        private IntPtr _powerRegistration;
        public bool IsLocked => _locked;
        public event Action<SystemActivityChange, DateTime>? Changed;

        public SystemActivityMonitor()
        {
            _locked = QueryLockedState();
            _powerCallback = PowerNotification;
            try
            {
                // SystemEvents omits automatic-resume messages; use the native callback for all wake paths.
                var subscription = new PowerSubscription
                {
                    Callback = Marshal.GetFunctionPointerForDelegate(_powerCallback),
                };
                var error = PowerRegisterSuspendResumeNotification(2, ref subscription, out _powerRegistration);
                if (error != 0)
                {
                    throw new Win32Exception((int)error);
                }
                // An MTA initializer gives session notifications their own pump in --worker mode.
                Task.Run(() =>
                {
                    SystemEvents.SessionSwitch += SessionSwitch;
                    SystemEvents.SessionEnded += SessionEnded;
                }).GetAwaiter().GetResult();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            var registration = System.Threading.Interlocked.Exchange(ref _powerRegistration, IntPtr.Zero);
            if (registration != IntPtr.Zero)
            {
                PowerUnregisterSuspendResumeNotification(registration);
            }
            GC.KeepAlive(_powerCallback);
            SystemEvents.SessionSwitch -= SessionSwitch;
            SystemEvents.SessionEnded -= SessionEnded;
        }

        private void SessionSwitch(object sender, SessionSwitchEventArgs args)
        {
            if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock)
            {
                _locked = args.Reason == SessionSwitchReason.SessionLock;
                Changed?.Invoke(_locked ? SystemActivityChange.Locked : SystemActivityChange.Unlocked, DateTime.UtcNow);
            }
        }

        private uint PowerNotification(IntPtr context, uint kind, IntPtr setting)
        {
            try
            {
                HandlePowerNotification(kind);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Power notification failed: " + ex);
            }
            return 0;
        }

        internal void HandlePowerNotification(uint kind)
        {
            if (_disposed)
            {
                return;
            }
            if (kind == 4)
            {
                Changed?.Invoke(SystemActivityChange.Suspend, DateTime.UtcNow);
            }
            else if (kind is 7 or 18)
            {
                _locked = QueryLockedState();
                Changed?.Invoke(SystemActivityChange.Resume, DateTime.UtcNow);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint PowerCallback(IntPtr context, uint kind, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerSubscription
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerRegisterSuspendResumeNotification(
            uint flags, ref PowerSubscription recipient, out IntPtr registration);

        [DllImport("powrprof.dll")]
        private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr registration);

        private void SessionEnded(object sender, SessionEndedEventArgs args)
        {
            Changed?.Invoke(SystemActivityChange.SessionEnding, DateTime.UtcNow);
        }

        private static bool QueryLockedState()
        {
            using var process = Process.GetCurrentProcess();
            if (!WTSQuerySessionInformation(IntPtr.Zero, process.SessionId, 25, out var buffer, out var size))
            {
                return false;
            }
            try
            {
                // WTSINFOEX's LEVEL1 union is 8-byte aligned; SessionFlags follows SessionId and SessionState.
                return size >= 20 && Marshal.ReadInt32(buffer) == 1 && Marshal.ReadInt32(buffer, 16) == 0;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }

        [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);
    }
}
