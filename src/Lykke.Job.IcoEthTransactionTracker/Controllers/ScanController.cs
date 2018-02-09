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

        /// <summary>
        /// Re-process block
        /// </summary>
        /// <param name="block">Block id (hash) or height</param>
        /// <returns></returns>
        [HttpPost("{block}")]
        public async Task<IActionResult> Block([FromRoute]string block)
        {
            block = block.Trim();

            if (ulong.TryParse(block, out var height))
            {
                return Json(new ScanResponse(await _transactionTrackingService.ProcessBlockByHeight(height)));
            }
            else
            {
                return Json(new ScanResponse(await _transactionTrackingService.ProcessBlockById(block)));
            }
        }

        /// <summary>
        /// Re-process range of blocks by height
        /// </summary>
        /// <param name="from">From height</param>
        /// <param name="to">To height</param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Range(
            [FromQuery]ulong from, 
            [FromQuery]ulong to)
        {
            return Json(new ScanResponse(await _transactionTrackingService.ProcessRange(from, to, saveProgress: false)));
        }
    }
}
