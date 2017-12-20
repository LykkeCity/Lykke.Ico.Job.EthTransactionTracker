﻿using System;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Nethereum.Util;

namespace Lykke.Job.IcoEthTransactionTracker.Services
{
    public class TransactionTrackingService : ITransactionTrackingService
    {
        private readonly ILog _log;
        private readonly TrackingSettings _trackingSettings;
        private readonly ICampaignInfoRepository _campaignInfoRepository;
        private readonly IInvestorAttributeRepository _investorAttributeRepository;
        private readonly IQueuePublisher<TransactionMessage> _transactionQueue;
        private readonly IBlockchainReader _blockchainReader;
        private readonly AddressUtil _addressUtil = new AddressUtil();
        private readonly string _network;

        public TransactionTrackingService(
            ILog log,
            TrackingSettings trackingSettings,
            ICampaignInfoRepository campaignInfoRepository,
            IInvestorAttributeRepository investorAttributeRepository,
            IQueuePublisher<TransactionMessage> transactionQueue,
            IBlockchainReader blockchainReader)
        {
            _log = log;
            _trackingSettings = trackingSettings;
            _campaignInfoRepository = campaignInfoRepository;
            _investorAttributeRepository = investorAttributeRepository;
            _transactionQueue = transactionQueue;
            _blockchainReader = blockchainReader;
            _network = _trackingSettings.EthNetwork.ToLower();
        }

        public async Task Track()
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
                await _log.WriteInfoAsync(nameof(Track),
                    $"Network: {_network}, LastProcessedHeight: {lastProcessedHeight}, LastConfirmedHeight: {lastConfirmedHeight}",
                    $"No new data");

                return;
            }

            await ProcessRange(
                lastProcessedHeight + 1,
                lastConfirmedHeight,
                saveProgress: true);
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
            var count = 0;

            foreach (var tx in transactions)
            {
                var payInAddress = _addressUtil.ConvertToChecksumAddress(tx.Action.To); // lower-case to checksum representation
                var email = await _investorAttributeRepository.GetInvestorEmailAsync(InvestorAttributeType.PayInEthAddress, payInAddress);

                if (string.IsNullOrWhiteSpace(email))
                {
                    // destination address is not a cash-in address of any ICO investor
                    continue;
                }

                var amount = UnitConversion.Convert.FromWei(tx.Action.Value.Value); //  WEI to ETH
                var link = $"{_trackingSettings.EthTrackerUrl}tx/{tx.TransactionHash}";

                await _transactionQueue.SendAsync(new TransactionMessage
                {
                    Email = email,
                    UniqueId = tx.TransactionHash,
                    BlockId = tx.BlockHash,
                    CreatedUtc = blockInfo.Timestamp.UtcDateTime,
                    TransactionId = tx.TransactionHash,
                    PayInAddress = payInAddress,
                    Currency = CurrencyType.Ether,
                    Amount = amount,
                    Link = link
                });

                count++;
            }

            await _log.WriteInfoAsync(nameof(ProcessBlock),
                $"Network: {_network}, Block: {blockInfo.ToJson()}, Investments: {count}",
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
                    await _campaignInfoRepository.SaveValueAsync(CampaignInfoType.LastProcessedBlockEth, h.ToString());
                }
            }

            await _log.WriteInfoAsync(nameof(ProcessRange),
                $"Network: {_network}, Range: {blockRange}, Investments: {txCount}",
                $"Range processing completed");

            return txCount;
        }
    }
}
