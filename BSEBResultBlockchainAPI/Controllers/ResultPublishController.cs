using BSEBResultBlockchainAPI.Helpers;
using BSEBResultBlockchainAPI.Services;
using BSEBResultBlockchainAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BSEBResultBlockchainAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultPublishController : ControllerBase
    {
        private readonly IResultPublishService _publishService;
        private readonly IFlureeService _flureeService;

        public ResultPublishController(IResultPublishService publishService, IFlureeService flureeService)
        {
            _publishService = publishService;
            _flureeService= flureeService;
        }

       
        [HttpPost("publish-all")]
        public IActionResult PublishAll()
        {
            // Fire-and-forget — runs in background, API returns immediately
            _ = Task.Run(() => _publishService.PublishAllResultsAsync());
            return Accepted(new { message = "Publishing started in background." });
        }

        [HttpGet("decrypt")]
        public async Task<IActionResult> GetDecrypted(string rollCode, string rollNo)
        {
            try
            {
                var result = await _flureeService.GetDecryptedAllWithVersionAsync(rollCode, rollNo);
                //var result = await _flureeService.GetDecryptedLatestAsync(rollCode, rollNo);

                if (result == null)
                    return NotFound(new { message = "Record not found or empty" });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("decryptString")]
        public IActionResult DecryptString([FromBody] string encryptedWrapper)
        {
            try
            {
                var result = QrUtility.DecryptStringData(encryptedWrapper);

                if (result == null)
                    return NotFound(new { message = "Invalid or empty data" });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
        public class DecryptRequest
        {
            public string EncryptedData { get; set; }
        }
    }
}