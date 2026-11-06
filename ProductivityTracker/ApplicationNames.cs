using System;
using System.Collections.Generic;
using System.IO;

namespace ProductivityTracker
{
    internal static class ApplicationNames
    {
        private static readonly IReadOnlyDictionary<string, string> FriendlyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["chrome"] = "Google Chrome",
                ["msedge"] = "Microsoft Edge",
                ["firefox"] = "Mozilla Firefox",
                ["devenv"] = "Visual Studio",
                ["code"] = "Visual Studio Code",
                ["windowsterminal"] = "Windows Terminal",
                ["OUTLOOK"] = "Microsoft Outlook",
                ["WINWORD"] = "Microsoft Word",
                ["EXCEL"] = "Microsoft Excel",
                ["POWERPNT"] = "Microsoft PowerPoint",
                ["Teams"] = "Microsoft Teams",
                ["Teams2"] = "Microsoft Teams",
                ["ms-teams"] = "Microsoft Teams",
                ["msrdc"] = "Remote Desktop",
                ["mstsc"] = "Remote Desktop Connection",
                ["explorer"] = "Windows Explorer",
                ["notepad"] = "Notepad",
                ["ShellExperienceHost"] = "Windows Shell",
                ["StartMenuExperienceHost"] = "Start Menu",
                ["SearchHost"] = "Windows Search",
                ["LockApp"] = "Lock Screen",
                ["ApplicationFrameHost"] = "Modern Windows App",
            };

        public static string FromProcessName(string processName)
        {
            var name = Path.GetFileNameWithoutExtension(processName);
            return string.IsNullOrWhiteSpace(name)
                ? "Unknown"
                : FriendlyNames.TryGetValue(name, out var friendly) ? friendly : name;
        }

        public static string ForRecordedName(string appName, string processName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return FromProcessName(processName);
            }
            if (string.IsNullOrWhiteSpace(processName))
            {
                return FriendlyNames.TryGetValue(Path.GetFileNameWithoutExtension(appName), out var friendly)
                    ? friendly : appName;
            }
            var matchesProcess = string.Equals(appName, Path.GetFileNameWithoutExtension(processName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(appName, Path.GetFileName(processName), StringComparison.OrdinalIgnoreCase);
            return matchesProcess && FriendlyNames.TryGetValue(Path.GetFileNameWithoutExtension(processName), out var knownName)
                ? knownName
                : appName;
        }
    }
}
