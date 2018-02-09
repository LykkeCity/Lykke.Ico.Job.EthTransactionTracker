using System.Threading.Tasks;
using AzureStorage;
using AzureStorage.Tables;
using Common.Log;
using Lykke.Job.IcoEthTransactionTracker.Core.Domain.Settings;
using Lykke.SettingsReader;

namespace Lykke.Job.IcoEthTransactionTracker.AzureRepositories.Settings
{
    public class SettingsRepository : ISettingsRepository
    {
        private const string LastProcessedBlockHeightProperty = "LastProcessedBlockHeight";
        private readonly INoSQLTableStorage<SettingsEntity> _tableStorage;
        private readonly string _instanceId = null;
        private string GetPartitionKey() => string.IsNullOrWhiteSpace(_instanceId) ? "Default" : _instanceId;
        private string GetRowKey(string property) => property;

        public SettingsRepository(IReloadingManager<string> connectionStringManager, ILog log, string instanceId)
        {
            _tableStorage = AzureTableStorage<SettingsEntity>.Create(connectionStringManager, "IcoEthTransactionTrackerSettings", log);
            _instanceId = instanceId;
        }

        public async Task<ulong> GetLastProcessedBlockHeightAsync()
        {
            var partitionKey = GetPartitionKey();
            var rowKey = GetRowKey(LastProcessedBlockHeightProperty);
            var entity = await _tableStorage.GetDataAsync(partitionKey, rowKey);

            return ulong.TryParse(entity?.Value, out var value) 
                ? value 
                : 0;
        }

        public async Task UpdateLastProcessedBlockHeightAsync(ulong height)
        {
            var partitionKey = GetPartitionKey();
            var rowKey = GetRowKey(LastProcessedBlockHeightProperty);
            var entity = new SettingsEntity
            {
                PartitionKey = partitionKey,
                RowKey = rowKey,
                Value = height.ToString()
            };

            await _tableStorage.InsertOrReplaceAsync(entity);
        }
    }
}
