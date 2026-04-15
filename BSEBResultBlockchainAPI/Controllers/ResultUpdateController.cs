using BSEBResultBlockchainAPI.Services;
using BSEBResultBlockchainAPI.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BSEBResultBlockchainAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResultUpdateController : ControllerBase
    {
        private readonly IResultUpdateService _updateService;

        public ResultUpdateController(IResultUpdateService updateService)
        {
            _updateService = updateService;
        }

        [HttpPost("Updatereultdata")]
        public async Task<IActionResult> Updatereultdata(string rollCode, string rollNo)
        {
            if (string.IsNullOrWhiteSpace(rollCode) || string.IsNullOrWhiteSpace(rollNo))
                return BadRequest("rollCode and rollNo are required");

          
            try
            {
                var result = await _updateService.UpdateSingleResultAsync(rollCode, rollNo);

                return Ok(new
                {
                    message = "Process completed",
                    status = result.ToString()
                });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new
                {
                    message = "Unable to connect to remote server",
                    error = ex.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Internal server error",
                    error = ex.Message
                });
            }
        }
    }
}
