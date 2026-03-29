using Microsoft.AspNetCore.Mvc;

namespace AuthService.Controllers.Health;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<object> Get()
    {
        return Ok(new
        {
            status = "ok",
            message = "API is running",
            timestamp = DateTime.UtcNow
        });
    }
}