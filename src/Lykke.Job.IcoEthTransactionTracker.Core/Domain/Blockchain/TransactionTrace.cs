using System;
using System.Collections.Generic;
using System.Text;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain
{
    public class TransactionTrace
    {
        public TransactionTraceAction Action { get; set; }
        public string BlockHash { get; set; }
        public string TransactionHash { get; set; }
    }
}
