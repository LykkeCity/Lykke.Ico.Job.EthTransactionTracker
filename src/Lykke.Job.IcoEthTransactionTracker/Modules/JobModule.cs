using Autofac;
using Common.Log;
using Lykke.Ico.Core.Queues;
using Lykke.Ico.Core.Queues.Transactions;
using Lykke.Ico.Core.Repositories.CampaignInfo;
using Lykke.Ico.Core.Repositories.InvestorAttribute;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoEthTransactionTracker.PeriodicalHandlers;
using Lykke.Job.IcoEthTransactionTracker.Services;
using Lykke.SettingsReader;

namespace Lykke.Job.IcoEthTransactionTracker.Modules
{
    public class JobModule : Module
    {
        private readonly IcoEthTransactionTrackerSettings _settings;
        private readonly IReloadingManager<DbSettings> _dbSettingsManager;
        private readonly IReloadingManager<AzureQueueSettings> _azureQueueSettingsManager;
        private readonly ILog _log;

        public JobModule(
            IcoEthTransactionTrackerSettings settings, 
            IReloadingManager<DbSettings> dbSettingsManager, 
            IReloadingManager<AzureQueueSettings> azureQueueSettingsManager,
            ILog log)
        {
            _settings = settings;
            _log = log;
            _dbSettingsManager = dbSettingsManager;
            _azureQueueSettingsManager = azureQueueSettingsManager;
        }

        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterInstance(_log)
                .As<ILog>()
                .SingleInstance();

            builder.RegisterType<HealthService>()
                .As<IHealthService>()
                .SingleInstance();

            builder.RegisterType<StartupManager>()
                .As<IStartupManager>();

            builder.RegisterType<ShutdownManager>()
                .As<IShutdownManager>();

            builder.RegisterType<CampaignInfoRepository>()
                .As<ICampaignInfoRepository>()
                .WithParameter(TypedParameter.From(_dbSettingsManager.Nested(x => x.DataConnString)));

            builder.RegisterType<InvestorAttributeRepository>()
                .As<IInvestorAttributeRepository>()
                .WithParameter(TypedParameter.From(_dbSettingsManager.Nested(x => x.DataConnString)));

            builder.RegisterType<QueuePublisher<BlockchainTransactionMessage>>()
                .As<IQueuePublisher<BlockchainTransactionMessage>>()
                .WithParameter(TypedParameter.From(_azureQueueSettingsManager.Nested(x => x.ConnectionString)));

            builder.RegisterType<BlockchainReader>()
                .As<IBlockchainReader>()
                .WithParameter(TypedParameter.From(_settings.Tracking.EthereumUrl));

            builder.RegisterType<TransactionTrackingService>()
                .As<ITransactionTrackingService>()
                .WithParameter(TypedParameter.From(_settings.Tracking));

            RegisterPeriodicalHandlers(builder);
        }

        private void RegisterPeriodicalHandlers(ContainerBuilder builder)
        {
            builder.RegisterType<TransactionTrackingHandler>()
                .As<IStartable>()
                .AutoActivate()
                .WithParameter(TypedParameter.From(_settings.TrackingInterval))
                .SingleInstance();
        }
    }
}
