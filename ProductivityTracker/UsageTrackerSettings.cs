using System;
using System.IO;
using System.Text.Json;

namespace ProductivityTracker
{
    internal sealed class UsageTrackerSettings
    {
        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductivityTracker");

        private static readonly string SettingsFilePath = Path.Combine(
            DataFolder,
            "settings.json");

        public static string PauseFilePath { get; } = Path.Combine(
            DataFolder,
            "paused");

        public static int GetIdleThresholdSeconds()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return 60;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize(json, StorageJsonContext.Default.SettingsData);
                return Math.Clamp(settings?.IdleThresholdSeconds ?? 60, 5, 3600);
            }
            catch (IOException)
            {
                return 60;
            }
            catch (UnauthorizedAccessException)
            {
                return 60;
            }
            catch (JsonException)
            {
                return 60;
            }
        }

        public static void SetIdleThresholdSeconds(int idleThresholdSeconds)
        {
            var value = Math.Clamp(idleThresholdSeconds, 5, 3600);

            try
            {
                Directory.CreateDirectory(DataFolder);
                var settings = new SettingsData
                {
                    IdleThresholdSeconds = value,
                };
                var json = JsonSerializer.Serialize(settings, StorageJsonContext.Default.SettingsData);
                var temporaryPath = SettingsFilePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, SettingsFilePath, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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
