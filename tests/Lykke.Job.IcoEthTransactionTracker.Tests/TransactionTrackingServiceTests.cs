using System;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Ico.Core;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoEthTransactionTracker.Services;
using Moq;
using Nethereum.Hex.HexTypes;
using Nethereum.Signer;
using Nethereum.Util;
using Xunit;

namespace Lykke.Job.IcoEthTransactionTracker.Tests
{
    public class TransactionTrackingServiceTests
    {
        private ILog _log;
        private TrackingSettings _trackingSettings;
        private Mock<ICampaignInfoRepository> _campaignInfoRepository;
        private Mock<IInvestorAttributeRepository> _investorAttributeRepository;
        private Mock<IQueuePublisher<TransactionMessage>> _transactionQueue;
        private Mock<IBlockchainReader> _blockchainReader;
        private string _lastProcessed = string.Empty;

        private TransactionTrackingService Init(
            ulong lastProcessed = 0, 
            ulong lastConfirmed = 5, 
            ulong startHeight = 0, 
            Func<ulong, bool, TransactionTrace[]> txFactory = null)
        {
            Func<ulong, bool, TransactionTrace[]> defaultTxFactory = (h, p) => new TransactionTrace[]
            {
                new TransactionTrace
                {
                    Action = new TransactionTraceAction { To = $"testAddress_{h}", Value = new HexBigInteger(h) },
                    BlockHash = $"testBlock_{h}",
                    TransactionHash = $"testTransaction_{h}"
                }
            };

            txFactory = txFactory ?? defaultTxFactory;

            _lastProcessed = lastProcessed.ToString();
            _trackingSettings = new TrackingSettings { ConfirmationLimit = 0, StartHeight = startHeight, EthNetwork="testnet" };
            _log = new LogToMemory();
            _campaignInfoRepository = new Mock<ICampaignInfoRepository>();

            _campaignInfoRepository
                .Setup(m => m.GetValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockEth)))
                .Returns(() => Task.FromResult(_lastProcessed));

            _campaignInfoRepository
                .Setup(m => m.SaveValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockEth), It.IsAny<string>()))
                .Callback((CampaignInfoType t, string v) => _lastProcessed = v)
                .Returns(() => Task.CompletedTask);

            _investorAttributeRepository = new Mock<IInvestorAttributeRepository>();

            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(
                    It.IsIn(new InvestorAttributeType[] { InvestorAttributeType.PayInBtcAddress, InvestorAttributeType.PayInEthAddress }),
                    It.IsAny<string>()))
                .Returns(() => Task.FromResult("test@test.test"));

            _transactionQueue = new Mock<IQueuePublisher<TransactionMessage>>();

            _transactionQueue
                .Setup(m => m.SendAsync(It.IsAny<TransactionMessage>()))
                .Returns(() => Task.CompletedTask);

            _blockchainReader = new Mock<IBlockchainReader>();

            _blockchainReader
                .Setup(m => m.GetLastConfirmedHeightAsync(It.IsAny<ulong>()))
                .Returns(() => Task.FromResult(lastConfirmed));

            _blockchainReader
                .Setup(m => m.GetBlockByHeightAsync(It.IsInRange(lastProcessed, lastConfirmed, Range.Inclusive)))
                .Returns((ulong h) => Task.FromResult(new BlockInformation
                {
                    BlockId = h.ToString(),
                    IsEmpty = false,
                    Height = h,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)h)
                }));

            _blockchainReader
                .Setup(m => m.GetBlockTransactionsAsync(It.IsInRange(lastProcessed, lastConfirmed, Range.Inclusive), It.Is<bool>(v => v == true)))
                .Returns((ulong h, bool p) => Task.FromResult(txFactory(h, p)));

            return new TransactionTrackingService(
                _log,
                _trackingSettings,
                _campaignInfoRepository.Object,
                _investorAttributeRepository.Object,
                _transactionQueue.Object,
                _blockchainReader.Object);
        }

        [Fact]
        public async void ShouldProcessBlocksFromStartToEnd()
        {
            // Arrange
            var lastConfirmed = 5UL;
            var svc = Init(lastConfirmed: lastConfirmed); ;

            // Act
            await svc.Track();

            // Assert
            Assert.Equal(lastConfirmed.ToString(), _lastProcessed);
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly((int)lastConfirmed));
        }

        [Fact]
        public async void ShouldSendCorrectMessage()
        {
            // Arrange
            var testAddress = EthECKey.GenerateKey().GetPublicAddress();
            var testAddressLowerCase = testAddress.ToLower();
            var testBlockHash = "testBlock";
            var testTransactionHash = "testTransaction";
            var wei = 1;
            var eth = UnitConversion.Convert.FromWei(wei);
            var svc = Init(
                lastProcessed: 0, 
                lastConfirmed: 1,
                txFactory: (h, p) => new TransactionTrace[]
                {
                    new TransactionTrace
                    {
                        Action = new TransactionTraceAction { To = testAddressLowerCase, Value = new HexBigInteger(wei) },
                        BlockHash = testBlockHash,
                        TransactionHash = testTransactionHash
                    }
                });

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.Is<TransactionMessage>(msg =>
                msg.Amount == eth &&
                msg.BlockId == testBlockHash &&
                msg.CreatedUtc == DateTimeOffset.FromUnixTimeSeconds(1).UtcDateTime &&
                msg.Currency == CurrencyType.Ether &&
                msg.PayInAddress == testAddress &&
                msg.TransactionId == testTransactionHash &&
                msg.UniqueId == testTransactionHash)));
        }
    }
}
