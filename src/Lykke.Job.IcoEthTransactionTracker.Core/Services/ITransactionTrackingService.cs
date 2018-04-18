using System.Threading.Tasks;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Services
{
    public interface ITransactionTrackingService
    {
        Task Track();
        Task<int> ProcessBlockByHeight(ulong height);
        Task<int> ProcessBlockById(string id);
        Task<int> ProcessRange(ulong fromHeight, ulong toHeight, bool saveProgress = true);
        Task ResetProcessedBlockHeight(ulong height);
    }
}
