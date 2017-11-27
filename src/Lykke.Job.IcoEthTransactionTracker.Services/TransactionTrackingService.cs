using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.ProcessedBlock;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly string _component = nameof(TransactionTrackingService);
        private readonly string _process = nameof(Execute);
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly IProcessedBlockRepository _processedBlockRepository;
        private readonly IQueuePublisher<BlockchainTransactionMessage> _transactionQueue;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings,
            IProcessedBlockRepository processedBlockRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _processedBlockRepository = processedBlockRepository;
            _transactionQueue = transactionQueue;
        }

        public Task Execute()
        {
            throw new NotImplementedException();
        }
    }
}
