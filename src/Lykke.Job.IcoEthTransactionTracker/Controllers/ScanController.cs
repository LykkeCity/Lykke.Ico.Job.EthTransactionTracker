using System.Threading.Tasks;
using Lykke.Job.IcoEthTransactionTracker.Core.Services;
using Lykke.Job.IcoEthTransactionTracker.Models.Scan;
using Microsoft.AspNetCore.Mvc;

namespace Lykke.Job.IcoEthTransactionTracker.Controllers
{
    [Route("api/[controller]/[action]")]
    public class ScanController : Controller
    {
        private readonly ITransactionTrackingService _transactionTrackingService;

        public ScanController(ITransactionTrackingService transactionTrackingService)
        {
            _transactionTrackingService = transactionTrackingService;
        }

        [HttpPost()]
        public async Task<IActionResult> Block([FromBody]BlockRequest block)
        {
            if (block.Height.HasValue)
            {
                return Json(new ScanResponse(await _transactionTrackingService.ProcessBlockByHeight(block.Height.Value)));
            }
            else if (!string.IsNullOrWhiteSpace(block.Id))
            {
                return Json(new ScanResponse(await _transactionTrackingService.ProcessBlockById(block.Id)));
            }
            else
            {
                return BadRequest();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Range([FromBody]RangeRequest range)
        {
            return Json(new ScanResponse(await _transactionTrackingService.ProcessRange(range.FromHeight, range.ToHeight, saveProgress: false)));
        }
    }
}
