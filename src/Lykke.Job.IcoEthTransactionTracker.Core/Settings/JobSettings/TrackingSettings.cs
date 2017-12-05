using System;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings
{
    public class TrackingSettings
    {
        public UInt16 ConfirmationLimit { get; set; }
        public String EthereumUrl { get; set; }
        public String EthereumNetwork { get; set; }
        public UInt64 StartHeight { get; set; }
    }
}
