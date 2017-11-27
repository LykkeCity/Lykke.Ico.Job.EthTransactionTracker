using Lykke.Job.IcoEthTransactionTracker.Core.Settings.JobSettings;
using Lykke.Job.IcoEthTransactionTracker.Core.Settings.SlackNotifications;

namespace Lykke.Job.IcoEthTransactionTracker.Core.Settings
{
    public class AppSettings
    {
        public IcoEthTransactionTrackerSettings IcoEthTransactionTrackerJob { get; set; }
        public SlackNotificationsSettings SlackNotifications { get; set; }
    }
}