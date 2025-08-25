using Microsoft.AspNetCore.Mvc;

namespace COA.CodeSearch.McpServer.Controllers;

/// <summary>
/// API controller for health checks
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Get service health status
    /// </summary>
    /// <returns>Health status information</returns>
    [HttpGet]
    public ActionResult<object> GetHealth()
    {
        return Ok(new 
        { 
            Status = "Healthy", 
            Service = "CodeSearch",
            Version = "2.0.0",
            Mode = "HTTP",
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}