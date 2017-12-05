using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
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
        private readonly IInvestorAttributeRepository _investorAttributeRepository;
        private readonly IQueuePublisher<BlockchainTransactionMessage> _transactionQueue;
        private readonly IBlockchainReader _blockchainReader;
        private readonly AddressUtil _addressUtil = new AddressUtil();
        private readonly string _network;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings,
            ICampaignInfoRepository campaignInfoRepository,
            IInvestorAttributeRepository investorAttributeRepository,
            IQueuePublisher<BlockchainTransactionMessage> transactionQueue,
            IBlockchainReader blockchainReader)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _investorAttributeRepository = investorAttributeRepository;
            _transactionQueue = transactionQueue;
            _blockchainReader = blockchainReader;
            _network = _trackingSettings.EthereumNetwork.ToLower();
        }

        public async Task Execute()
        {
            var lastConfirmedHeight = await _blockchainReader.GetLastConfirmedHeightAsync(_trackingSettings.ConfirmationLimit);
            var lastProcessedBlockEth = await _campaignInfoRepository.GetValueAsync(CampaignInfoType.LastProcessedBlockEth);

            if (!ulong.TryParse(lastProcessedBlockEth, out var lastProcessedHeight) || lastProcessedHeight == 0)
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

            await _log.WriteInfoAsync(_component, _process, _network,
                $"Processing block(s) {blockRange} started in network");

            for (var h = from; h <= to; h++)
            {
                txCount += await ProcessBlock(h);
                await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockEth, h.ToString());
            }

            await _log.WriteInfoAsync(_component, _process, _network, 
                $"Processing block(s) {blockRange} completed; {blockCount} block(s) processed; {txCount} investments queued");
        }

        public async Task<int> ProcessBlock(ulong height)
        {
            var block = await _blockchainReader.GetBlockInfoAsync(height);

            // check if there is any transaction within block
            if (block.IsEmpty)
            {
                await _log.WriteInfoAsync(_component, nameof(ProcessBlock), _network, 
                    $"Block [{height}] is empty; block skipped");
                return 0;
            }

            // but even non-empty block can contain zero payment transactions
            var transactions = await _blockchainReader.GetBlockTransactionsAsync(height, paymentsOnly: true);
            var count = 0;

            foreach (var tx in transactions)
            {
                var destinationAddress = _addressUtil.ConvertToChecksumAddress(tx.Action.To); // lower-case to checksum representation
                var investorEmail = await _investorAttributeRepository.GetInvestorEmailAsync(InvestorAttributeType.PayInEthAddress, destinationAddress);

                if (string.IsNullOrWhiteSpace(investorEmail))
                {
                    // destination address is not a cash-in address of any ICO investor
                    continue;
                }

                var amount = UnitConversion.Convert.FromWei(tx.Action.Value.Value); //  WEI to ETH
                var link = _network == "mainnet" ?
                    $"https://etherscan.io/tx/{tx.TransactionHash}" : 
                    $"https://{_network}.etherscan.io/tx/{tx.TransactionHash}";

                await _transactionQueue.SendAsync(new BlockchainTransactionMessage
                {
                    InvestorEmail = investorEmail,
                    BlockId = tx.BlockHash,
                    BlockTimestamp = block.Timestamp,
                    TransactionId = tx.TransactionHash,
                    DestinationAddress = destinationAddress,
                    CurrencyType = CurrencyType.Ether,
                    Amount = amount,
                    Link = link
                });

                count++;
            }

            await _log.WriteInfoAsync(_component, nameof(ProcessBlock), _network, 
                $"Block [{height}] processed; {count} investments queued");

            return transactions.Length;
        }
    }
}
