namespace Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings
{
    public class IcoEthTransactionTrackerSettings
    {
        public AzureQueueSettings AzureQueue { get; set; }
        public DbSettings Db { get; set; }
        public TrackingSettings Tracking { get; set; }
        public int TrackingInterval { get; set; }
    }
}
