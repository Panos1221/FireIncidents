using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace FireIncidents.Controllers
{
    [Route("api/fire-districts")]
    [ApiController]
    public class FireDepartmentDistrictsApiController : ControllerBase
    {
        private readonly ILogger<FireDepartmentDistrictsApiController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IWebHostEnvironment _environment;
        private const string CACHE_KEY = "fire_districts_data";

        public FireDepartmentDistrictsApiController(
            ILogger<FireDepartmentDistrictsApiController> logger,
            IMemoryCache cache,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _cache = cache;
            _environment = environment;
        }

        [HttpGet]
        [ResponseCache(Duration = 2592000, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<ActionResult> GetFireDepartmentDistricts()
        {
            try
            {
                _logger.LogInformation("API request received for fire department districts");

                // Try to get data from cache first
                if (_cache.TryGetValue(CACHE_KEY, out object cachedData))
                {
                    _logger.LogInformation("Returning cached fire department districts data");
                    return Ok(cachedData);
                }

                // Load data from file if not in cache
                var filePath = Path.Combine(_environment.ContentRootPath, "Data", "fire_depts_districts.json");
                
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogError($"Fire departments districts file not found at: {filePath}");
                    return NotFound("Fire departments districts data not available");
                }

                _logger.LogInformation($"Loading fire department districts from file: {filePath}");
                var jsonContent = await System.IO.File.ReadAllTextAsync(filePath);
                
                // Parse and validate JSON
                var districtsData = JsonSerializer.Deserialize<object>(jsonContent);
                
                if (districtsData == null)
                {
                    _logger.LogError("Failed to parse fire departments districts JSON data");
                    return StatusCode(500, "Error parsing districts data");
                }

                // Cache the data
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    Priority = CacheItemPriority.Normal
                };
                
                _cache.Set(CACHE_KEY, districtsData, cacheOptions);
                return Ok(districtsData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving fire department districts data");
                return StatusCode(500, "Internal server error while retrieving districts data");
            }
        }

        [HttpPost("clear-cache")] //used for debug
        public ActionResult ClearCache()
        {
            try
            {
                _cache.Remove(CACHE_KEY);
                _logger.LogInformation("Fire department districts cache cleared");
                return Ok(new { message = "Cache cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing fire department districts cache");
                return StatusCode(500, "Error clearing cache");
            }
        }
    }
}