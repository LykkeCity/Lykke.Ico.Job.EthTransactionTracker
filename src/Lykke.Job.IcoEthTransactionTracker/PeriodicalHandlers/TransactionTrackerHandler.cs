﻿using System;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;

namespace Lykke.Job.IcoEthTransactionTracker.PeriodicalHandlers
{
    public class TransactionTrackingHandler : TimerPeriod
    {
        private ILog _log;
        private ITransactionTrackingService _trackingService;

        public TransactionTrackingHandler(int trackingInterval, ILog log, ITransactionTrackingService trackingService) : 
            base(nameof(TransactionTrackingHandler), trackingInterval, log)
        {
            _log = log;
            _trackingService = trackingService;
        }

        public override async Task Execute()
        {
            try
            {
                await _trackingService.Execute();
            }
            catch (Exception ex)
            {
                await _log.WriteErrorAsync(
                    nameof(TransactionTrackingHandler),
                    nameof(Execute),
                    string.Empty,
                    ex);
            }
        }
    }
}
