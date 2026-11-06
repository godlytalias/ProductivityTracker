using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProductivityTracker
{
    public sealed class UsageStore
    {
        private readonly object _gate = new();
        private readonly List<UsageSegment> _segments = new();
        private UsageSegment? _currentSegment;
        private bool _storageBlocked;
        public DateTime TrackingStartedUtc { get; private set; } = DateTime.UtcNow;

        public UsageStore() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductivityTracker"))
        {
        }

        internal UsageStore(string dataFolder)
        {
            DataFolder = dataFolder;
            DataFilePath = Path.Combine(DataFolder, "usage.json");
            Load();
        }

        public string DataFolder { get; }

        public string DataFilePath { get; }

        public string? LastError { get; private set; }

        internal FileStream? TryAcquireCollectorLease()
        {
            FileStream? lease = null;
            try
            {
                Directory.CreateDirectory(DataFolder);
                lease = new FileStream(Path.Combine(DataFolder, "collector.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                // Reload only after the previous writer has flushed and released its lease.
                ReloadFromDisk();
                EndCurrentAtLastSample();
                return lease;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                lease?.Dispose();
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lease?.Dispose();
                LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
                return null;
            }
        }

        public void RecordSample(
            DateTime sampleTimeUtc,
            string appName,
            string processName,
            bool isActive,
            InactivityReason? inactiveReason = null)
        {
            lock (_gate)
            {
                if (_currentSegment != null &&
                    _currentSegment.AppName == appName &&
                    _currentSegment.ProcessName == processName &&
                    _currentSegment.IsActive == isActive &&
                    _currentSegment.InactiveReason == inactiveReason &&
                    sampleTimeUtc - _currentSegment.EndUtc <= TimeSpan.FromSeconds(3))
                {
                    _currentSegment.EndUtc = sampleTimeUtc;
                    return;
                }

                if (_currentSegment != null)
                {
                    _currentSegment.EndUtc = _currentSegment.EndUtc < _currentSegment.StartUtc
                        ? _currentSegment.StartUtc
                        : _currentSegment.EndUtc;
                    _currentSegment = null;
                }

                _currentSegment = new UsageSegment
                {
                    StartUtc = sampleTimeUtc,
                    EndUtc = sampleTimeUtc,
                    AppName = appName,
                    ProcessName = processName,
                    IsActive = isActive,
                    InactiveReason = isActive ? null : inactiveReason,
                };
                _segments.Add(_currentSegment);
            }
        }

        public void CloseCurrent(DateTime sampleTimeUtc)
        {
            lock (_gate)
            {
                if (_currentSegment != null)
                {
                    _currentSegment.EndUtc = sampleTimeUtc < _currentSegment.StartUtc
                        ? _currentSegment.StartUtc
                        : sampleTimeUtc;
                    _currentSegment = null;
                }
            }
        }

        public void EndCurrentAtLastSample()
        {
            lock (_gate)
            {
                _currentSegment = null;
            }
        }

        public bool Flush()
        {
            lock (_gate)
            {
                if (_storageBlocked)
                {
                    return false;
                }

                try
                {
                    Directory.CreateDirectory(DataFolder);
                    var data = new UsageDataFile
                    {
                        TrackingStartedUtc = TrackingStartedUtc,
                        Segments = _segments
                            .Where(segment =>
                                segment.EndUtc >= segment.StartUtc)
                            .Select(segment => segment.Clone())
                            .ToList(),
                    };

                    var json = JsonSerializer.Serialize(data, StorageJsonContext.Default.UsageDataFile);
                    var tempPath = DataFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);

                    File.Move(tempPath, DataFilePath, true);
                    LastError = null;
                    return true;
                }
                catch (IOException ex)
                {
                    LastError = AppStrings.Format("SaveDataErrorFormat", ex.Message);
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    LastError = AppStrings.Format("SaveDataErrorFormat", ex.Message);
                    return false;
                }
            }
        }

        internal bool ApplySystemIntervals(IEnumerable<SystemInactiveInterval> intervals)
        {
            lock (_gate)
            {
                var clipped = intervals.Select(interval => interval with
                {
                    StartUtc = interval.StartUtc < TrackingStartedUtc ? TrackingStartedUtc : interval.StartUtc,
                }).ToArray();
                if (!clipped.Any(interval => interval.EndUtc > interval.StartUtc))
                {
                    return false;
                }
                var updated = SystemInactivity.Overlay(_segments, clipped);
                var existing = _segments.OrderBy(segment => segment.StartUtc).ToArray();
                if (existing.Length == updated.Count && existing.Zip(updated).All(pair =>
                    pair.First.StartUtc == pair.Second.StartUtc && pair.First.EndUtc == pair.Second.EndUtc &&
                    pair.First.AppName == pair.Second.AppName && pair.First.ProcessName == pair.Second.ProcessName &&
                    pair.First.IsActive == pair.Second.IsActive && pair.First.InactiveReason == pair.Second.InactiveReason))
                {
                    return false;
                }
                _segments.Clear();
                _segments.AddRange(updated);
                _currentSegment = null;
                return true;
            }
        }

        public void ReloadFromDisk()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(DataFilePath))
                    {
                        return;
                    }

                    var json = File.ReadAllText(DataFilePath);
                    var data = JsonSerializer.Deserialize(json, StorageJsonContext.Default.UsageDataFile);
                    if (data?.Segments == null)
                    {
                        return;
                    }

                    _segments.Clear();
                    _segments.AddRange(
                        data.Segments.Where(segment =>
                            segment.StartUtc != default &&
                            segment.EndUtc >= segment.StartUtc));
                    _storageBlocked = false;
                    TrackingStartedUtc = data.TrackingStartedUtc ??
                        (_segments.Count > 0 ? _segments.Min(segment => segment.StartUtc) : TrackingStartedUtc);
                    LastError = null;
                }
                catch (IOException ex)
                {
                    LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
                }
                catch (JsonException ex)
                {
                    LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
                }
            }
        }

        public DailyUsageSummary GetDailySummary(DateTime localDate)
        {
            var date = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local);
            var startUtc = date.ToUniversalTime();
            var endUtc = date.AddDays(1).ToUniversalTime();
            var applicationSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            double activeSeconds = 0;
            double inactiveSeconds = 0;
            double lockedSeconds = 0;
            double sleepSeconds = 0;
            double poweredOffSeconds = 0;
            int sessionCount = 0;

            lock (_gate)
            {
                foreach (var segment in _segments)
                {
                    var overlapStart = segment.StartUtc > startUtc ? segment.StartUtc : startUtc;
                    var overlapEnd = segment.EndUtc < endUtc ? segment.EndUtc : endUtc;
                    if (overlapEnd <= overlapStart)
                    {
                        continue;
                    }

                    sessionCount++;
                    var duration = (overlapEnd - overlapStart).TotalSeconds;
                    if (segment.IsActive)
                    {
                        activeSeconds += duration;
                        var appName = string.IsNullOrWhiteSpace(segment.AppName)
                            ? "Unknown"
                            : segment.AppName;
                        applicationSeconds.TryGetValue(appName, out var existing);
                        applicationSeconds[appName] = existing + duration;
                    }
                    else
                    {
                        inactiveSeconds += duration;
                        if (segment.InactiveReason == InactivityReason.Locked)
                        {
                            lockedSeconds += duration;
                        }
                        else if (segment.InactiveReason == InactivityReason.Sleep)
                        {
                            sleepSeconds += duration;
                        }
                        else if (segment.InactiveReason == InactivityReason.PoweredOff)
                        {
                            poweredOffSeconds += duration;
                        }
                    }
                }
            }

            var applications = applicationSeconds
                .Select(pair => new AppUsageSummary
                {
                    AppName = pair.Key,
                    ActiveSeconds = pair.Value,
                })
                .OrderByDescending(item => item.ActiveSeconds)
                .ToList();

            var topApp = applications.FirstOrDefault();
            return new DailyUsageSummary
            {
                ActiveSeconds = activeSeconds,
                InactiveSeconds = inactiveSeconds,
                LockedSeconds = lockedSeconds,
                SleepSeconds = sleepSeconds,
                PoweredOffSeconds = poweredOffSeconds,
                SessionCount = sessionCount,
                TopAppName = topApp?.AppName ?? "None",
                TopAppSeconds = topApp?.ActiveSeconds ?? 0,
                Applications = applications,
            };
        }

        public IReadOnlyList<UsageSegment> GetSessions(DateTime localDate)
        {
            return GetSessions(localDate.Date, localDate.Date.AddDays(1));
        }

        public IReadOnlyList<UsageSegment> GetSessions(
            DateTime localStartDate,
            DateTime localEndDateExclusive)
        {
            var startUtc = DateTime.SpecifyKind(
                localStartDate.Date, DateTimeKind.Local).ToUniversalTime();
            var endUtc = DateTime.SpecifyKind(
                localEndDateExclusive.Date, DateTimeKind.Local).ToUniversalTime();
            var sessions = new List<UsageSegment>();

            lock (_gate)
            {
                foreach (var segment in _segments)
                {
                    var overlapStart = segment.StartUtc > startUtc ? segment.StartUtc : startUtc;
                    var overlapEnd = segment.EndUtc < endUtc ? segment.EndUtc : endUtc;
                    if (overlapEnd <= overlapStart)
                    {
                        continue;
                    }

                    sessions.Add(new UsageSegment
                    {
                        StartUtc = overlapStart,
                        EndUtc = overlapEnd,
                        AppName = segment.AppName,
                        ProcessName = segment.ProcessName,
                        IsActive = segment.IsActive,
                        InactiveReason = segment.InactiveReason,
                    });
                }
            }

            return sessions
                .OrderByDescending(segment => segment.StartUtc)
                .ToList();
        }

        private void Load()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                if (!File.Exists(DataFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(DataFilePath);
                var data = JsonSerializer.Deserialize(json, StorageJsonContext.Default.UsageDataFile);
                if (data?.Segments == null)
                {
                    throw new JsonException(AppStrings.Get("MissingSegmentsError"));
                }

                lock (_gate)
                {
                    _segments.AddRange(
                        data.Segments.Where(segment =>
                            segment.StartUtc != default &&
                            segment.EndUtc >= segment.StartUtc));
                    TrackingStartedUtc = data.TrackingStartedUtc ??
                        (_segments.Count > 0 ? _segments.Min(segment => segment.StartUtc) : TrackingStartedUtc);
                }
            }
            catch (IOException ex)
            {
                _storageBlocked = true;
                LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _storageBlocked = true;
                LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
            }
            catch (JsonException ex)
            {
                _storageBlocked = true;
                LastError = AppStrings.Format("ReadDataErrorFormat", ex.Message);
            }
        }
    }
}
