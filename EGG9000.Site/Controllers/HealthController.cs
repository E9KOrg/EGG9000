using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EGG9000.Site.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HealthController(ILogger<HealthController> logger) : ControllerBase
    {
        private readonly ILogger<HealthController> _logger = logger;

        [HttpGet]
        public IActionResult Get()
        {
            // Simple health check - can be extended to check DB, services, etc.
            return Ok(new { status = "healthy", timestamp = System.DateTime.UtcNow });
        }
    }
}