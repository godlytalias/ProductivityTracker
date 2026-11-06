using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ProductivityTracker
{
    internal readonly record struct ApplicationIdentity(
        string ProcessName,
        string? ExecutablePath = null,
        string? PackageFullName = null,
        string? AppUserModelId = null);

    internal static class ApplicationNames
    {
        public static ApplicationNameResolver Current { get; private set; } =
            new(_ => Task.FromResult<string?>(null));

        public static void Initialize(Func<ApplicationIdentity, Task<string?>> lookup) =>
            Current = new ApplicationNameResolver(lookup);

        public static string ProcessStem(string processName)
        {
            var name = Path.GetFileName(processName) ?? string.Empty;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        public static bool IsDisplayName(string? name) =>
            !string.IsNullOrWhiteSpace(name) &&
            !name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class ApplicationNameResolver
    {
        private sealed record CacheEntry(DateTimeOffset Created, Task<string?> Lookup);
        private readonly ConcurrentDictionary<string, Lazy<CacheEntry>> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<ApplicationIdentity, Task<string?>> _lookup;
        private readonly TimeProvider _clock;

        public ApplicationNameResolver(
            Func<ApplicationIdentity, Task<string?>> lookup, TimeProvider? clock = null)
        {
            _lookup = lookup;
            _clock = clock ?? TimeProvider.System;
        }

        public string Resolve(ApplicationIdentity identity, string? recordedName = null)
        {
            var stem = ApplicationNames.ProcessStem(identity.ProcessName);
            var fallback = string.IsNullOrWhiteSpace(recordedName)
                ? string.IsNullOrWhiteSpace(stem) ? "Unknown" : stem
                : recordedName;
            if (string.IsNullOrWhiteSpace(stem) || stem.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            {
                return fallback;
            }

            var key = string.Join("|", identity.ProcessName, identity.ExecutablePath,
                identity.PackageFullName, identity.AppUserModelId);
            Lazy<CacheEntry> CreateEntry() => new(() => new CacheEntry(
                _clock.GetUtcNow(), Task.Run(() => LookupAsync(identity))),
                LazyThreadSafetyMode.ExecutionAndPublication);

            var lazy = _cache.GetOrAdd(key, _ => CreateEntry());
            var entry = lazy.Value;
            if (entry.Lookup.IsCompleted)
            {
                var lifetime = ApplicationNames.IsDisplayName(entry.Lookup.Result)
                    ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);
                if (_clock.GetUtcNow() - entry.Created >= lifetime)
                {
                    var replacement = CreateEntry();
                    if (_cache.TryUpdate(key, replacement, lazy))
                    {
                        entry = replacement.Value;
                    }
                    else
                    {
                        entry = _cache[key].Value;
                    }
                }
            }

            return entry.Lookup.IsCompletedSuccessfully && ApplicationNames.IsDisplayName(entry.Lookup.Result)
                ? entry.Lookup.Result!.Trim() : fallback;
        }

        public string ForRecordedName(string appName, string processName) =>
            Resolve(new ApplicationIdentity(processName), appName);

        private async Task<string?> LookupAsync(ApplicationIdentity identity)
        {
            try
            {
                return await _lookup(identity).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Log("Application display-name lookup failed: " + ex.Message);
                return null;
            }
        }
    }
}
