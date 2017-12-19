using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Services
{
    public interface IBlockchainReader
    {
        Task<ulong> GetLastConfirmedHeightAsync(ulong confirmationLimit);
        Task<BlockInformation> GetBlockByHeightAsync(ulong height);
        Task<BlockInformation> GetBlockByIdAsync(string id);
        Task<TransactionTrace[]> GetBlockTransactionsAsync(ulong height, bool paymentsOnly = true);
    }
}
