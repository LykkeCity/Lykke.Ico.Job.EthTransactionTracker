using System;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Domain
{
    public class InfuraException : Exception
    {
        public InfuraException(string message, Exception ex)
            : base(message, ex)
        {
            
        }
    }
}
