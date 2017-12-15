using System.Threading.Tasks;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Services
{
    public interface ITransactionTrackingService
    {
        Task Track();
    }
}
