using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FireIncidents.Models;

namespace FireIncidents.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        
        public HomeController(ILogger<HomeController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }
        
        public IActionResult Index()
        {
            ViewBag.Show112Warnings = _configuration.GetValue<bool>("Features:Show112Warnings", true);
            return View();
        }
        
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}