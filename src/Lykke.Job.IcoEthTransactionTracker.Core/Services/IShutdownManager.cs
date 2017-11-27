using System.Threading.Tasks;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Services
{
    public interface IShutdownManager
    {
        Task StopAsync();
    }
}