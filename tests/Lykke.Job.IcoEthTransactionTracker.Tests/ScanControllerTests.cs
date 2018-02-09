using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Controllers;
using Lykke.Job.IcoEthTransactionTracker.Models.Scan;
using Xunit;

namespace Lykke.Job.IcoEthTransactionTracker.Tests
{
    public class ScanControllerTests
    {
        [Fact]
        public async Task Range_ShouldNotUpdateLastProcessed()
        {
            // Arrange
            var transactionServiceTests = new TransactionTrackingServiceTests();
            var lastProcessed = 0UL;
            var scanController = new ScanController(transactionServiceTests.Init(lastProcessed));

            // Act
            await scanController.Range(new RangeRequest { FromHeight = 1, ToHeight = 2 });

            // Assert
            Assert.Equal(lastProcessed, transactionServiceTests.LastProcessed);
        }
    }
}
