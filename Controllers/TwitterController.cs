using Microsoft.AspNetCore.Mvc;

namespace FireIncidents.Controllers
{
    public class TwitterController : Controller
    {
        private readonly ILogger<TwitterController> _logger;

        public TwitterController(ILogger<TwitterController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            _logger.LogInformation("Twitter widget page accessed for scraping");
            
            // Returns a page with RSS.app widget for displaying Twitter feed
            // RSS.app provides both widget and JSON API access
            return View();
        }

        [HttpGet("test-data")]
        public IActionResult TestData()
        {
            _logger.LogInformation("Test Twitter data page accessed for scraping - contains sample tweets");
            return View();
        }

        [HttpGet("raw")]
        [Route("Twitter/Raw")]
        public IActionResult Raw()
        {
            _logger.LogInformation("Raw Twitter widget page accessed for scraping");
            return View();
        }


    }
}
