using Microsoft.AspNetCore.Mvc;

namespace FireIncidents.Controllers
{
    [ApiController]
    [Route("Status")]
    public class StatusController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public StatusController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("CheckFireServiceStatus")]
        public async Task<IActionResult> CheckFireServiceStatus()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = await client.GetAsync("https://museum.fireservice.gr/symvanta/");
                if (response.IsSuccessStatusCode)
                    return Ok(new { status = "up" });
            }
            catch { }
            return Ok(new { status = "down" });
        }
    }
}