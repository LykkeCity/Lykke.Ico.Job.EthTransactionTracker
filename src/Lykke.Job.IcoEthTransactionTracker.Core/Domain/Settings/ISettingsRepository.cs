using System.Threading.Tasks;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Domain.Settings
{
    public interface ISettingsRepository
    {
        Task<ulong> GetLastProcessedBlockHeightAsync();
        Task UpdateLastProcessedBlockHeightAsync(ulong height);
    }
}
