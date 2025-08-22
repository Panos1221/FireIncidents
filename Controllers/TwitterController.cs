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
            
            // For now, return a page with test tweet data since SociableKit requires JavaScript
            // TODO: Replace with actual tweets when we find a server-side solution
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
