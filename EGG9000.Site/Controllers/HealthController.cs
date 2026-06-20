using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EGG9000.Site.Controllers
{
    [AllowAnonymous]
    [ApiController]
    [Route("[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly ILogger<HealthController> _logger;

        public HealthController(ILogger<HealthController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            // Simple health check - can be extended to check DB, services, etc.
            return Ok(new { status = "healthy", timestamp = System.DateTime.UtcNow });
        }
    }
}