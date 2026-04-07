using BSEBResultBlockchainAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BSEBResultBlockchainAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ResultPublishController : ControllerBase
    {
        private readonly IResultPublishService _publishService;

        public ResultPublishController(IResultPublishService publishService)
        {
            _publishService = publishService;
        }

       
        [HttpPost("publish-all")]
        public IActionResult PublishAll()
        {
            // Fire-and-forget — runs in background, API returns immediately
            _ = Task.Run(() => _publishService.PublishAllResultsAsync());
            return Accepted(new { message = "Publishing started in background." });
        }
    }
}