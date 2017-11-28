using System;
using System.Numerics;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Nethereum.Hex.HexTypes;
using Nethereum.Util;
using Nethereum.Web3;

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
        private string _network;

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
            if (string.IsNullOrWhiteSpace(_network))
            {
                _network = await GetNetwork();
            }

            ulong lastProcessedHeight = 0;
            ulong lastConfirmedHeight = await GetLastConfirmedHeightAsync();

            if (!ulong.TryParse(await _campaignInfoRepository.GetValueAsync(CampaignInfoType.LastProcessedBlockEth), out lastProcessedHeight) || 
                lastProcessedHeight == 0)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            if (lastProcessedHeight >= lastConfirmedHeight)
            {
                // all processed or start height is greater than current height
                return;
            }

            var from = lastProcessedHeight + 1;
            var to = lastConfirmedHeight;
            var blockCount = to - lastProcessedHeight;
            var blockRange = blockCount > 1 ? $"[{from} - {to}]" : $"[{to}]";
            var txCount = 0;

            await _log.WriteInfoAsync(_component, _process, _network, $"Processing block(s) {blockRange} started");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
            }

            await _log.WriteInfoAsync(_component, _process, _network, $"{blockCount} block(s) processed; {txCount} payment transactions queued");
        }

        private async Task<int> ProcessBlock(ulong height)
        {
            var hexHeight = new HexBigInteger(height);

            // we need to get header to extract timestamp of block
            var block = await _web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(hexHeight);
            var blockTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value);

            if (block.TransactionHashes.Length == 0)
            {
                // empty block
                return 0;
            }

            // we need to use traces instead of regular data to get all tx info including inner transactions
            var traceParams = new { fromBlock = hexHeight, toBlock = hexHeight };
            var blockTransactions = await _web3.Client.SendRequestAsync<TransactionTrace[]>("trace_filter", null, traceParams);
            
            // amount of payment transactions
            var count = 0;

            foreach (var t in blockTransactions)
            {
                var amount = UnitConversion.Convert.FromWei(t.Action?.Value?.Value ?? BigInteger.Zero);

                if (amount > 0M)
                {
                    await _transactionQueue.SendAsync(new BlockchainTransactionMessage
                    {
                        BlockId = t.BlockHash,
                        BlockTimestamp = blockTimestamp,
                        TransactionId = t.TransactionHash,
                        DestinationAddress = t.Action.To,
                        CurrencyType = CurrencyType.Ether,
                        Amount = amount
                    });

                    count++;
                }
            }

            await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockEth, height.ToString());

            return count;
        }

        private async Task<ulong> GetLastConfirmedHeightAsync()
        {
            var lastHeightHex = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var lastHeight = (ulong)lastHeightHex.Value;

            return lastHeight - _trackingSettings.ConfirmationLimit;
        }

        private async Task<string> GetNetwork()
        {
            return await _web3.Client.SendRequestAsync<string>("parity_chain");
        }

        private async Task<TransactionTrace[]> TraceTransaction(string transactionHash)
        {
            return await _web3.Client.SendRequestAsync<TransactionTrace[]>("trace_transaction", null, transactionHash);
        }

        private class TransactionTrace
        {
            public TransactionTraceAction Action { get; set; }
            public string BlockHash { get; set; }
            public string TransactionHash { get; set; }
            public string Type { get; set; }
        }

        private class TransactionTraceAction
        {
            public string From { get; set; }
            public string To { get; set; }
            public HexBigInteger Value { get; set; }
        }

    }
}
