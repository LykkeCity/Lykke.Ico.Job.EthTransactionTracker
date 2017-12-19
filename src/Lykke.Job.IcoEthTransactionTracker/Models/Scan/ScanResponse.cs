namespace Lykke.Job.IcoEthTransactionTracker.Models.Scan
{
    public class ScanResponse
    {
        public ScanResponse()
        {
        }

        public ScanResponse(int investments)
        {
            Investments = investments;
        }

        public int Investments { get; set; }
    }
}
