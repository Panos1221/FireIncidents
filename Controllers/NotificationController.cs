using Microsoft.AspNetCore.Mvc;
using FireIncidents.Services;

namespace FireIncidents.Controllers
{
    [Route("api/notifications")]
    [ApiController]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _notificationService;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(NotificationService notificationService, ILogger<NotificationController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpPost("test")]
        public async Task<ActionResult> SendTestNotification([FromQuery] string type = "incident")
        {
            try
            {
                if (type != "incident" && type != "warning112")
                {
                    return BadRequest(new { error = "Type must be 'incident' or 'warning112'" });
                }

                await _notificationService.SendTestNotification(type);
                
                return Ok(new { 
                    message = $"Test {type} notification sent successfully",
                    type = type,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending test notification of type: {type}");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpPost("test/incident")]
        public async Task<ActionResult> SendTestIncidentNotification()
        {
            return await SendTestNotification("incident");
        }

        [HttpPost("test/warning")]
        public async Task<ActionResult> SendTestWarningNotification()
        {
            return await SendTestNotification("warning112");
        }
    }
}

