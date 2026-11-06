using System;
using System.IO;
using System.Text.Json;

namespace ProductivityTracker
{
    internal sealed class UsageTrackerSettings
    {
        private static readonly object SettingsGate = new();
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductivityTracker");

        private static readonly string SettingsFilePath = Path.Combine(
            DataFolder,
            "settings.json");

        public static string PauseFilePath { get; } = Path.Combine(
            DataFolder,
            "paused");

        public static int GetIdleThresholdSeconds() =>
            Math.Clamp(ReadSettings().IdleThresholdSeconds, 5, 3600);

        public static bool GetKeepTrackingAfterClosing() => ReadSettings().KeepTrackingAfterClosing;

        public static bool GetRunAtSignIn() => ReadSettings().RunAtSignIn;

        private static SettingsData ReadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return new SettingsData();
                }

                var json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize(json, StorageJsonContext.Default.SettingsData) ?? new SettingsData();
            }
            catch (IOException)
            {
                return new SettingsData();
            }
            catch (UnauthorizedAccessException)
            {
                return new SettingsData();
            }
            catch (JsonException)
            {
                return new SettingsData();
            }
        }

        public static void SetIdleThresholdSeconds(int idleThresholdSeconds) =>
            UpdateSettings(settings => settings.IdleThresholdSeconds = Math.Clamp(idleThresholdSeconds, 5, 3600));

        public static bool SetKeepTrackingAfterClosing(bool enabled) =>
            UpdateSettings(settings => settings.KeepTrackingAfterClosing = enabled);

        public static bool SetRunAtSignIn(bool enabled) =>
            UpdateSettings(settings => settings.RunAtSignIn = enabled);

        private static bool UpdateSettings(Action<SettingsData> update)
        {
            lock (SettingsGate)
            {
                try
                {
                    var settings = ReadSettings();
                    update(settings);
                    Directory.CreateDirectory(DataFolder);
                    var json = JsonSerializer.Serialize(settings, StorageJsonContext.Default.SettingsData);
                    var temporaryPath = SettingsFilePath + ".tmp";
                    File.WriteAllText(temporaryPath, json);
                    File.Move(temporaryPath, SettingsFilePath, true);
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
        }

        public static bool IsPaused()
        {
            return File.Exists(PauseFilePath);
        }

        public static void SetPaused(bool paused)
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                if (paused)
                {
                    File.WriteAllText(PauseFilePath, string.Empty);
                }
                else if (File.Exists(PauseFilePath))
                {
                    File.Delete(PauseFilePath);
                }
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
