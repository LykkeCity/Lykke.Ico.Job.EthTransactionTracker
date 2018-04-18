using Lykke.AzureStorage.Tables;
using Lykke.AzureStorage.Tables.Entity.Annotation;
using Lykke.AzureStorage.Tables.Entity.ValueTypesMerging;

namespace Lykke.Job.IcoEthTransactionTracker.AzureRepositories.Settings
{
    [ValueTypeMergingStrategyAttribute(ValueTypeMergingStrategy.UpdateAlways)]
    public class SettingsEntity : AzureTableEntity
    {
        public string Value { get; set; }
    }
}
