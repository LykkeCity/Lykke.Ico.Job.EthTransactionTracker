using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Nethereum.Web3;
using Nethereum.RPC.Eth.DTOs;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Nethereum.Util;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly string _component = nameof(TransactionTrackingService);
        private readonly string _process = nameof(Execute);
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ICampaignInfoRepository _campaignInfoRepository;
        private readonly IQueuePublisher<BlockchainTransactionMessage> _transactionQueue;
        private readonly Web3 _web3;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings,
            ICampaignInfoRepository campaignInfoRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _transactionQueue = transactionQueue;
            _web3 = new Web3(trackingSettings.EthereumUrl);
        }

        public async Task Execute()
        {
            ulong lastProcessedHeight = 0;
            var lastConfirmed = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            if (!ulong.TryParse(await _campaignInfoRepository.GetValueAsync(CampaignInfoType.LastProcessedBlockEth), out lastProcessedHeight) || 
                lastProcessedHeight == 0)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            if (lastProcessedHeight >= lastConfirmed.Value)
            {
                // all processed or start height is greater than current height
                return;
            }

            var from = lastProcessedHeight + 1;
            var to = lastConfirmed.Value;
            var blockCount = to - lastProcessedHeight;
            var blockRange = blockCount > 1 ? $"[{from} - {to}]" : $"[{to}]";
            var txCount = 0;

            await _log.WriteInfoAsync(_component, _process, _trackingSettings.EthereumNetwork, $"Processing block(s) {blockRange} started");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
            }

            await _log.WriteInfoAsync(_component, _process, _trackingSettings.EthereumNetwork, $"{blockCount} block(s) processed; {txCount} payment transactions queued");
        }

        private async Task<int> ProcessBlock(ulong height)
        {
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new BlockParameter(height));
            var paymentTx = block.Transactions
                .Where(t => !string.IsNullOrWhiteSpace(t.To))
                .Where(t => t.Value != null && t.Value.Value > 0)
                .ToList();
           
            foreach (var tx in paymentTx)
            {
                var blockTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value);
                var amount = UnitConversion.Convert.FromWei(tx.Value.Value);

                await _transactionQueue.SendAsync(new BlockchainTransactionMessage
                {
                    BlockId = tx.BlockHash,
                    BlockTimestamp = blockTimestamp,
                    TransactionId = tx.TransactionHash,
                    DestinationAddress = tx.To,
                    CurrencyType = CurrencyType.Ether,
                    Amount = amount
                });
            }

            await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockEth, height.ToString());

            return paymentTx.Count;
        }
    }
}
