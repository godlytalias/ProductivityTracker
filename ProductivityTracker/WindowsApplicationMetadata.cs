using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace ProductivityTracker
{
    [SupportedOSPlatform("windows10.0.17763.0")]
    internal sealed class WindowsApplicationMetadata
    {
        private sealed record PackageApplication(string PackageName, string AppUserModelId, string ExecutablePath);
        private readonly SemaphoreSlim _indexGate = new(1, 1);
        private Dictionary<string, List<PackageApplication>> _applications = new(StringComparer.OrdinalIgnoreCase);
        private DateTimeOffset _indexedAt = DateTimeOffset.MinValue;

        public async Task<string?> LookupAsync(ApplicationIdentity identity)
        {
            var executable = identity.ExecutablePath;
            try
            {
                var packageName = identity.PackageFullName;
                var appId = identity.AppUserModelId;
                if (string.IsNullOrWhiteSpace(packageName) && string.IsNullOrWhiteSpace(executable))
                {
                    var applications = await GetApplicationsAsync();
                    if (applications.TryGetValue(ApplicationNames.ProcessStem(identity.ProcessName), out var matches) &&
                        matches.Count == 1)
                    {
                        packageName = matches[0].PackageName;
                        appId = matches[0].AppUserModelId;
                        executable ??= matches[0].ExecutablePath;
                    }
                }
                if (!string.IsNullOrWhiteSpace(packageName))
                {
                    var package = new PackageManager().FindPackageForUser(string.Empty, packageName);
                    if (package != null)
                    {
                        var entries = await package.GetAppListEntriesAsync();
                        var entry = entries.FirstOrDefault(item =>
                            string.Equals(item.AppUserModelId, appId, StringComparison.OrdinalIgnoreCase));
                        if (entry == null && string.IsNullOrWhiteSpace(appId) && entries.Count == 1)
                        {
                            entry = entries[0];
                        }
                        var displayName = entry?.DisplayInfo.DisplayName;
                        if (ApplicationNames.IsDisplayName(displayName))
                        {
                            return displayName;
                        }
                        if (ApplicationNames.IsDisplayName(package.DisplayName))
                        {
                            return package.DisplayName;
                        }
                    }
                }
            }
            catch (Exception ex) when (IsMetadataError(ex))
            {
            }

            executable ??= FindExecutable(identity.ProcessName);
            if (string.IsNullOrWhiteSpace(executable))
            {
                return null;
            }
            try
            {
                var version = FileVersionInfo.GetVersionInfo(executable);
                return ApplicationNames.IsDisplayName(version.FileDescription) ? version.FileDescription
                    : ApplicationNames.IsDisplayName(version.ProductName) ? version.ProductName : null;
            }
            catch (Exception ex) when (IsMetadataError(ex))
            {
                return null;
            }
        }

        private async Task<Dictionary<string, List<PackageApplication>>> GetApplicationsAsync()
        {
            await _indexGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (DateTimeOffset.UtcNow - _indexedAt < TimeSpan.FromMinutes(2))
                {
                    return _applications;
                }
                var applications = new Dictionary<string, List<PackageApplication>>(StringComparer.OrdinalIgnoreCase);
                foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
                {
                    try
                    {
                        if (package.IsFramework || package.IsResourcePackage)
                        {
                            continue;
                        }
                        var folder = package.InstalledLocation.Path;
                        var manifest = XDocument.Load(Path.Combine(folder, "AppxManifest.xml"));
                        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                        foreach (var app in manifest.Root?.Element(ns + "Applications")?.Elements(ns + "Application")
                            ?? Enumerable.Empty<XElement>())
                        {
                            var executable = (string?)app.Attribute("Executable");
                            var id = (string?)app.Attribute("Id");
                            if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }
                            var stem = ApplicationNames.ProcessStem(executable);
                            if (!applications.TryGetValue(stem, out var matches))
                            {
                                applications[stem] = matches = new List<PackageApplication>();
                            }
                            matches.Add(new PackageApplication(package.Id.FullName,
                                package.Id.FamilyName + "!" + id, Path.Combine(folder, executable)));
                        }
                    }
                    catch (Exception ex) when (IsMetadataError(ex))
                    {
                    }
                }
                _applications = applications;
                _indexedAt = DateTimeOffset.UtcNow;
                return applications;
            }
            finally
            {
                _indexGate.Release();
            }
        }

        private static string? FindExecutable(string processName)
        {
            var stem = ApplicationNames.ProcessStem(processName);
            var runningPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcessesByName(stem))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            runningPaths.Add(path);
                        }
                    }
                    catch (Exception ex) when (IsMetadataError(ex))
                    {
                    }
                }
            }
            if (runningPaths.Count > 1)
            {
                return null;
            }
            if (runningPaths.Count == 1)
            {
                return runningPaths.Single();
            }

            var fileName = stem + ".exe";
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var prefix in new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\",
                })
                {
                    try
                    {
                        using var key = root.OpenSubKey(prefix + fileName);
                        if (key?.GetValue(null) is string value)
                        {
                            var path = Environment.ExpandEnvironmentVariables(value.Trim('"'));
                            if (File.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
                    catch (Exception ex) when (IsMetadataError(ex))
                    {
                    }
                }
            }
            var systemPath = Path.Combine(Environment.SystemDirectory, fileName);
            return File.Exists(systemPath) ? systemPath : null;
        }

        private static bool IsMetadataError(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or COMException or XmlException or
                ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or SecurityException;
    }
}
