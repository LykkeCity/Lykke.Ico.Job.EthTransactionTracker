using System;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings
{
    public class TrackingSettings
    {
        public UInt16 ConfirmationLimit { get; set; }
        public String EthUrl { get; set; }
        public String EthNetwork { get; set; }
        public UInt64 StartHeight { get; set; }
        public bool UseTraceFilter { get; set; }
    }
}
