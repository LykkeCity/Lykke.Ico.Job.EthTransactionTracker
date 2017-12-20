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
        private string _lastProcessed;

        public string LastProcessed
        {
            get => _lastProcessed;
            set => _lastProcessed = value;
        }

        public ITransactionTrackingService Init(
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

            // get _lastProcessed
            _campaignInfoRepository
                .Setup(m => m.GetValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockEth)))
                .Returns(() => Task.FromResult(_lastProcessed));

            // set _lastProcessed
            _campaignInfoRepository
                .Setup(m => m.SaveValueAsync(It.Is<CampaignInfoType>(t => t == CampaignInfoType.LastProcessedBlockEth), It.IsAny<string>()))
                .Callback((CampaignInfoType t, string v) => _lastProcessed = v)
                .Returns(() => Task.CompletedTask);

            _investorAttributeRepository = new Mock<IInvestorAttributeRepository>();

            // return test email for any pay-in address
            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(
                    It.IsIn(new InvestorAttributeType[] { InvestorAttributeType.PayInBtcAddress, InvestorAttributeType.PayInEthAddress }),
                    It.IsAny<string>()))
                .Returns(() => Task.FromResult("test@test.test"));

            _transactionQueue = new Mock<IQueuePublisher<TransactionMessage>>();

            _blockchainReader = new Mock<IBlockchainReader>();

            // get height of last confirmed block == lastConfirmed argument
            _blockchainReader
                .Setup(m => m.GetLastConfirmedHeightAsync(It.IsAny<ulong>()))
                .Returns(() => Task.FromResult(lastConfirmed));

            // get block info by height
            _blockchainReader
                .Setup(m => m.GetBlockByHeightAsync(It.IsAny<ulong>()))
                .Returns((ulong h) => Task.FromResult(new BlockInformation
                {
                    BlockId = h.ToString(),
                    IsEmpty = false,
                    Height = h,
                    Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)h)
                }));

            // get block info by id
            // next to lastProcessed block height is used
            _blockchainReader
                .Setup(m => m.GetBlockByIdAsync(It.IsAny<string>()))
                .Returns((string id) => Task.FromResult(new BlockInformation
                {
                    BlockId = id.ToString(),
                    IsEmpty = false,
                    Height = lastProcessed + 1,
                    Timestamp = DateTimeOffset.UtcNow
                }));

            // use factory to get transactions by block height,
            // one transaction is returned by default for any height
            _blockchainReader
                .Setup(m => m.GetBlockTransactionsAsync(It.IsAny<ulong>(), It.IsAny<bool>()))
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
        public async void Track_ShouldProcessBlocksFromStartToEnd()
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
        public async void Track_ShouldUpdateLastProcessed()
        {
            // Arrange
            var lastProcessed = 0UL;
            var lastConfirmed = 5UL;
            var svc = Init(lastProcessed, lastConfirmed);

            // Act
            await svc.Track();

            // Assert
            Assert.Equal(lastConfirmed.ToString(), _lastProcessed);
        }

        [Fact]
        public async void Track_ShouldSendCorrectMessage()
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

        [Fact]
        public async void Track_ShouldThrow_IfBlockchainReaderThrows()
        {
            // Arrange
            var svc = Init(txFactory: (h, p) => throw new Exception());

            // Act
            // Assert
            await Assert.ThrowsAnyAsync<Exception>(async () => await svc.Track());
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfBlockIsEmpty()
        {
            // Arrange
            var svc = Init();

            _blockchainReader
                 .Setup(m => m.GetBlockByHeightAsync(It.IsAny<ulong>()))
                 .Returns((ulong h) => Task.FromResult(new BlockInformation
                 {
                     BlockId = h.ToString(),
                     IsEmpty = true,
                     Height = h,
                     Timestamp = DateTimeOffset.FromUnixTimeSeconds((long)h)
                 }));

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoPaymentTransactions()
        {
            // Arrange
            var svc = Init(txFactory: (h, p) => new TransactionTrace[0]);

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfInvestorEmailNotFound()
        {
            // Arrange
            var svc = Init();
            _investorAttributeRepository
                .Setup(m => m.GetInvestorEmailAsync(It.IsAny<InvestorAttributeType>(), It.IsAny<string>()))
                .Returns(() => Task.FromResult<string>(null));

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoNewData()
        {
            // Arrange
            var svc = Init(1, 1);

            // Act
            await svc.Track();

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Never);
        }

        [Fact]
        public async void ProcessBlockByHeight_ShouldSendMessage()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessBlockByHeight(1);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
        }

        [Fact]
        public async void ProcessBlockById_ShouldSendMessage()
        {
            // Arrange
            var testBlockHash = "testBlock";
            var svc = Init();

            // Act
            await svc.ProcessBlockById(testBlockHash);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
        }

        [Fact]
        public async void ProcessRange_ShouldThrow_IfRangeIsInvalid()
        {
            // Arrange
            var svc = Init();

            // Act
            // Assert
            await Assert.ThrowsAnyAsync<Exception>(async () => await svc.ProcessRange(2, 1));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5UL, 10UL);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(6));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage_IfRangeIsSingleItem()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5, 5);

            // Assert
            _transactionQueue.Verify(m => m.SendAsync(It.IsAny<TransactionMessage>()), Times.Exactly(1));
        }
    }
}
