using Microsoft.AspNetCore.Mvc;
using FireIncidents.Models;
using FireIncidents.Services;

namespace FireIncidents.Controllers
{
    [ApiController]
    public class AlertsController : Controller
    {
        private readonly ILogger<AlertsController> _logger;
        private readonly AlertsStoreService _store;

        public AlertsController(ILogger<AlertsController> logger, AlertsStoreService store)
        {
            _logger = logger;
            _store = store;
        }

        // POST /alerts/update to save incoming tweet/alert data
        [HttpPost]
        [Route("alerts/update")]
        public async Task<IActionResult> Update([FromBody] AlertUpdateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { error = "Text is required" });
            }

            var alert = new Alert
            {
                Text = request.Text.Trim(),
                Timestamp = request.Timestamp ?? DateTime.UtcNow,
                Url = request.Url,
                AlertType = request.AlertType,
                Source = request.Source ?? "twitter"
            };

            await _store.AddAlertAsync(alert);
            _logger.LogInformation("Stored alert {AlertId} of type {Type}", alert.Id, alert.AlertType);
            return Ok(alert);
        }

        // GET /api/alerts to fetch latest alerts as JSON
        [HttpGet]
        [Route("api/alerts")]
        public async Task<IActionResult> GetAlerts()
        {
            var alerts = await _store.GetAlertsAsync();
            return Ok(alerts);
        }

        // GET /alerts to render the alerts page
        [HttpGet]
        [Route("alerts")]
        public async Task<IActionResult> Index()
        {
            var alerts = await _store.GetAlertsAsync();
            return View("Index", alerts);
        }
    }
}