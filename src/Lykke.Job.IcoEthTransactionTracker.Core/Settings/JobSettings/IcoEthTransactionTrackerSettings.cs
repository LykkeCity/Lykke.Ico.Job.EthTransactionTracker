using Lykke.SettingsReader.Attributes;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings
{
    public class IcoEthTransactionTrackerSettings
    {
        public DbSettings Db { get; set; }
        public TrackingSettings Tracking { get; set; }
        public int TrackingInterval { get; set; }

        [HttpCheck("api/isalive")]
        public string CommonServiceUrl { get; set; }

        [Optional]
        public string InstanceId { get; set; }
    }
}
