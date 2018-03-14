using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Log;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Blockchain;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Settings;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoEthTransactionTracker.Services;
using Lykke.Service.IcoCommon.Client;
using Lykke.Service.IcoCommon.Client.Models;
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
        private Mock<ISettingsRepository> _settingsRepository;
        private Mock<IIcoCommonServiceClient> _commonServiceClient;
        private Mock<IBlockchainReader> _blockchainReader;
        private ulong _lastProcessed;

        public ulong LastProcessed
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

            _lastProcessed = lastProcessed;
            _trackingSettings = new TrackingSettings { ConfirmationLimit = 0, StartHeight = startHeight, EthNetwork="testnet" };
            _log = new LogToMemory();

            _settingsRepository = new Mock<ISettingsRepository>();

            // get _lastProcessed
            _settingsRepository
                .Setup(m => m.GetLastProcessedBlockHeightAsync())
                .Returns(() => Task.FromResult(_lastProcessed));

            // set _lastProcessed
            _settingsRepository
                .Setup(m => m.UpdateLastProcessedBlockHeightAsync(It.IsAny<ulong>()))
                .Callback((ulong h) => _lastProcessed = h)
                .Returns(() => Task.CompletedTask);

            _commonServiceClient = new Mock<IIcoCommonServiceClient>();

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
                _settingsRepository.Object,
                _blockchainReader.Object,
                _commonServiceClient.Object);
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
            Assert.Equal(lastConfirmed, _lastProcessed);
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Exactly((int)lastConfirmed));
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
            Assert.Equal(lastConfirmed, _lastProcessed);
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
            _commonServiceClient.Verify(m => m.HandleTransactionsAsync(
                It.Is<IList<TransactionModel>>(list => list.Any(msg =>
                    msg.Amount == eth &&
                    msg.BlockId == testBlockHash &&
                    msg.CreatedUtc == DateTimeOffset.FromUnixTimeSeconds(1).UtcDateTime &&
                    msg.Currency == CurrencyType.Eth &&
                    msg.PayInAddress == testAddress &&
                    msg.TransactionId == testTransactionHash &&
                    msg.UniqueId == testTransactionHash))));
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
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoPaymentTransactions()
        {
            // Arrange
            var svc = Init(txFactory: (h, p) => new TransactionTrace[0]);

            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Never);
        }

        [Fact]
        public async void Track_ShouldNotProcess_IfThereIsNoNewData()
        {
            // Arrange
            var svc = Init(1, 1);

            // Act
            await svc.Track();

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Never);
        }

        [Fact]
        public async void ProcessBlockByHeight_ShouldSendMessage()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessBlockByHeight(1);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Exactly(1));
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
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Exactly(1));
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
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Exactly(6));
        }

        [Fact]
        public async void ProcessRange_ShouldSendMessage_IfRangeIsSingleItem()
        {
            // Arrange
            var svc = Init();

            // Act
            await svc.ProcessRange(5, 5);

            // Assert
            _commonServiceClient.Verify(
                m => m.HandleTransactionsAsync(It.IsAny<IList<TransactionModel>>()),
                Times.Exactly(1));
        }
    }
}
