using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProductivityTracker
{
    internal sealed class UsageDataFile
    {
        public int Version { get; set; } = 1;
        public DateTime? TrackingStartedUtc { get; set; }
        public List<UsageSegment> Segments { get; set; } = new();
    }

    internal sealed class SettingsData
    {
        public int IdleThresholdSeconds { get; set; } = 60;
    }

    [JsonSerializable(typeof(UsageDataFile))]
    [JsonSerializable(typeof(SettingsData))]
    internal partial class StorageJsonContext : JsonSerializerContext
    {
    }
}
