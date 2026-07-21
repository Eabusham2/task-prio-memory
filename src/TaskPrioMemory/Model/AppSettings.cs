using System.Runtime.Serialization;

namespace TaskPrioMemory.Model
{
    /// <summary>Small persisted settings blob.</summary>
    [DataContract]
    public class AppSettings
    {
        /// <summary>How often (seconds) to scan for newly launched processes. Larger = lower usage.</summary>
        [DataMember(Order = 0)]
        public int PollSeconds { get; set; } = 5;

        /// <summary>Start hidden in the tray instead of showing the window.</summary>
        [DataMember(Order = 1)]
        public bool StartMinimized { get; set; } = true;

        /// <summary>Re-apply the preference every scan even if it was applied once (guards apps that reset it).</summary>
        [DataMember(Order = 2)]
        public bool ReapplyContinuously { get; set; } = false;

        // DataContractJsonSerializer skips field initializers when deserializing,
        // so a settings file from an older version would leave missing fields at
        // their zero values (PollSeconds = 0 → 1-second scans). Restore defaults
        // here; present fields are overwritten right after this runs.
        [OnDeserializing]
        private void SetDefaults(StreamingContext _)
        {
            PollSeconds = 5;
            StartMinimized = true;
        }

        public int NormalizedPollMs()
        {
            int s = PollSeconds;
            if (s < 1) s = 1;
            if (s > 3600) s = 3600;
            return s * 1000;
        }
    }
}
