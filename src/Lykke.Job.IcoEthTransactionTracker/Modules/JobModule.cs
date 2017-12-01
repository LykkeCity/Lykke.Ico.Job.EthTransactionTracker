using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
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
using Microsoft.Extensions.DependencyInjection;
using Nethereum.JsonRpc.Client;

namespace Lykke.Job.IcoEthTransactionTracker.Modules
{
    public class JobModule : Module
    {
        private readonly IcoEthTransactionTrackerSettings _settings;
        private readonly IReloadingManager<DbSettings> _dbSettingsManager;
        private readonly IReloadingManager<AzureQueueSettings> _azureQueueSettingsManager;
        private readonly ILog _log;
        // NOTE: you can remove it if you don't need to use IServiceCollection extensions to register service specific dependencies
        private readonly IServiceCollection _services;

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
            _services = new ServiceCollection();
        }

        protected override void Load(ContainerBuilder builder)
        {
            // NOTE: Do not register entire settings in container, pass necessary settings to services which requires them
            // ex:
            // builder.RegisterType<QuotesPublisher>()
            //  .As<IQuotesPublisher>()
            //  .WithParameter(TypedParameter.From(_settings.Rabbit.ConnectionString))

            IClient nethereumRpcClient = new RpcClient(new Uri(_settings.Tracking.EthereumUrl));

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
                .WithParameter(TypedParameter.From(nethereumRpcClient));

            builder.RegisterType<TransactionTrackingService>()
                .As<ITransactionTrackingService>()
                .WithParameter(TypedParameter.From(_settings.Tracking));

            RegisterPeriodicalHandlers(builder);

            // TODO: Add your dependencies here

            builder.Populate(_services);
        }

        private void RegisterPeriodicalHandlers(ContainerBuilder builder)
        {
            // TODO: You should register each periodical handler in DI container as IStartable singleton and autoactivate it

            builder.RegisterType<TransactionTrackingHandler>()
                .As<IStartable>()
                .AutoActivate()
                .WithParameter(TypedParameter.From(_settings.TrackingInterval))
                .SingleInstance();
        }

    }
}
