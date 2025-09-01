using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FireIncidents.Models;

namespace FireIncidents.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        
        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }
        
        public IActionResult Index()
        {
            ViewBag.Show112Warnings = _configuration.GetValue<bool>("Features:Show112Warnings", true);
            return View();
        }
        
        public IActionResult Feeds()
        {
            return View();
        }
        
        [HttpGet]
        public async Task<IActionResult> RSSProxy(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL parameter is required");
            }
            
            try
            {
                var httpClient = _httpClientFactory.CreateClient("RSSFeed");
                var response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Content(content, "application/xml");
                }
                else
                {
                    _logger.LogWarning($"Failed to fetch RSS feed from {url}. Status: {response.StatusCode}");
                    return StatusCode((int)response.StatusCode, $"Failed to fetch RSS feed: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching RSS feed from {url}");
                return StatusCode(500, "Internal server error while fetching RSS feed");
            }
        }
        
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}