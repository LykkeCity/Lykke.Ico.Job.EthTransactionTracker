using System;
using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Services
{
    public interface IBlockchainReader
    {
        Task<(DateTimeOffset Timestamp, Boolean IsEmpty)> GetBlockInfoAsync(UInt64 height);
        Task<TransactionTrace[]> GetBlockTransactionsAsync(UInt64 height, bool paymentsOnly = true);
        Task<UInt64> GetLastConfirmedHeightAsync(UInt64 confirmationLimit);
    }
}
