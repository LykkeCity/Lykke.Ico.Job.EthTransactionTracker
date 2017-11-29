using System;
using System.Collections.Generic;
using System.Text;
using Nethereum.Hex.HexTypes;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain
{
    public class TransactionTraceAction
    {
        public string From { get; set; }
        public string To { get; set; }
        public HexBigInteger Value { get; set; }
    }
}
