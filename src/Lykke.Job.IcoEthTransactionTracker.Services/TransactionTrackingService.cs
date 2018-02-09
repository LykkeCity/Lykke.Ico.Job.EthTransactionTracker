using System;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Settings;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Lykke.Service.IcoCommon.Client;
using Lykke.Service.IcoCommon.Client.Models;
using Nethereum.Util;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IBlockchainReader _blockchainReader;
        private readonly IIcoCommonServiceClient _commonServiceClient;
        private readonly AddressUtil _addressUtil = new AddressUtil();
        private readonly string _network;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings,
            ISettingsRepository settingsRepository,
            IBlockchainReader blockchainReader,
            IIcoCommonServiceClient commonServiceClient)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _settingsRepository = settingsRepository;
            _blockchainReader = blockchainReader;
            _commonServiceClient = commonServiceClient;
            _network = _trackingSettings.EthNetwork.ToLower();
        }

        public async Task Track()
        {
            var lastConfirmedHeight = await _blockchainReader.GetLastConfirmedHeightAsync(_trackingSettings.ConfirmationLimit);
            var lastProcessedHeight = await _settingsRepository.GetLastProcessedBlockHeightAsync();

            if (lastProcessedHeight < _trackingSettings.StartHeight)
            {
                lastProcessedHeight = _trackingSettings.StartHeight;
            }

            if (lastProcessedHeight >= lastConfirmedHeight)
            {
                // all processed or start height is greater than current height
                await _log.WriteInfoAsync(nameof(Track),
                    $"Network: {_network}, LastProcessedHeight: {lastProcessedHeight}, LastConfirmedHeight: {lastConfirmedHeight}",
                    $"No new data");

                return;
            }

            await ProcessRange(lastProcessedHeight + 1, lastConfirmedHeight, saveProgress: true);
        }

        public async Task<int> ProcessBlock(BlockInformation blockInfo)
        {
            if (blockInfo == null)
            {
                throw new ArgumentNullException(nameof(blockInfo));
            }

            // check if there is any transaction within block
            if (blockInfo.IsEmpty)
            {
                await _log.WriteInfoAsync(nameof(ProcessBlock),
                    $"Network: {_network}, Block: {blockInfo.ToJson()}",
                    $"Block {blockInfo.Height} is empty, therefore skipped");

                return 0;
            }

            // but even non-empty block can contain zero payment transactions
            var transactions = await _blockchainReader.GetBlockTransactionsAsync(blockInfo.Height, paymentsOnly: true);

            var transactionModels = transactions
                .Select(tx => new TransactionModel()
                {
                    Amount = UnitConversion.Convert.FromWei(tx.Action.Value.Value), //  WEI to ETH
                    BlockId = tx.BlockHash,
                    CreatedUtc = blockInfo.Timestamp.UtcDateTime,
                    Currency = CurrencyType.ETH,
                    PayInAddress = _addressUtil.ConvertToChecksumAddress(tx.Action.To), // lower-case to checksum representation
                    TransactionId = tx.TransactionHash,
                    UniqueId = tx.TransactionHash
                })
                .ToList();

            var count = 0;

            if (transactionModels.Any())
            {
                count = await _commonServiceClient.HandleTransactionsAsync(transactionModels);
            }

            await _log.WriteInfoAsync(nameof(ProcessBlock),
                $"Investments: {count}, Network: {_network}, Block: {blockInfo.ToJson()}",
                $"Block {blockInfo.Height} processed");

            return count;
        }

        public async Task<int> ProcessBlockByHeight(ulong height)
        {
            var blockInfo = await _blockchainReader.GetBlockByHeightAsync(height);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlockByHeight),
                    $"Network: {_network}, Block: {height}",
                    $"Block {height} not found or invalid, therefore skipped");

                return 0;
            }

            return await ProcessBlock(blockInfo);
        }

        public async Task<int> ProcessBlockById(string id)
        {
            var blockInfo = await _blockchainReader.GetBlockByIdAsync(id);
            if (blockInfo == null)
            {
                await _log.WriteWarningAsync(nameof(ProcessBlockById),
                    $"Network: {_network}, Block: {id}",
                    $"Block {id} not found or invalid, therefore skipped");

                return 0;
            }

            return await ProcessBlock(blockInfo);
        }

        public async Task<int> ProcessRange(ulong fromHeight, ulong toHeight, bool saveProgress = true)
        {
            if (fromHeight > toHeight)
            {
                throw new ArgumentException("Invalid range");
            }

            var blockRange = toHeight > fromHeight ? 
                $"[{fromHeight} - {toHeight}, {toHeight - fromHeight + 1}]" : 
                $"[{fromHeight}]";

            var txCount = 0;

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network}, Range: {blockRange}",
                $"Range processing started");

            for (var h = fromHeight; h <= toHeight; h++)
            {
                txCount += await ProcessBlockByHeight(h);

                if (saveProgress)
                {
                    await _settingsRepository.UpdateLastProcessedBlockHeightAsync(h);
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network}, Range: {blockRange}, Investments: {txCount}",
                $"Range processing completed");

            return txCount;
        }

        public async Task ResetProcessedBlockHeight(ulong height)
        {
            await _settingsRepository.UpdateLastProcessedBlockHeightAsync(height);
        }
    }
}
