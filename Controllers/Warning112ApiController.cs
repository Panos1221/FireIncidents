using Microsoft.AspNetCore.Mvc;
using FireIncidents.Services;
using FireIncidents.Models;

namespace FireIncidents.Controllers
{
    [Route("api/warnings112")]
    [ApiController]
    public class Warning112ApiController : ControllerBase
    {
        private readonly ILogger<Warning112ApiController> _logger;
        private readonly Warning112Service _warning112Service;

        public Warning112ApiController(
            ILogger<Warning112ApiController> logger,
            Warning112Service warning112Service)
        {
            _logger = logger;
            _warning112Service = warning112Service;
        }

        [HttpGet]
        public async Task<ActionResult<List<GeocodedWarning112>>> GetWarnings(
            [FromQuery] string region = null,
            [FromQuery] string municipality = null,
            [FromQuery] string language = "en")
        {
            try
            {
                _logger.LogInformation("API request received for 112 warnings");

                List<GeocodedWarning112> warnings;
                
                if (!string.IsNullOrEmpty(region) || !string.IsNullOrEmpty(municipality))
                {
                    warnings = await _warning112Service.GetWarningsForLocationAsync(region, municipality);
                    _logger.LogInformation($"Retrieved {warnings.Count} warnings for region: {region}, municipality: {municipality}");
                }
                else
                {
                    warnings = await _warning112Service.GetActiveWarningsAsync();
                    _logger.LogInformation($"Retrieved {warnings.Count} active warnings");
                }

                // Filter out expired warnings. Expired warnings are those that 24 hours have passed since their creation date. Warnings could still be active thought. If a new 112 comes then it will get reactivated. 
                warnings = warnings.Where(w => w.IsActive).ToList();

                _logger.LogInformation($"Returning {warnings.Count} active warnings");
                return Ok(warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving 112 warnings");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<Dictionary<string, int>>> GetStatistics()
        {
            try
            {
                _logger.LogInformation("API request received for 112 warnings statistics");

                var statistics = await _warning112Service.GetWarningsStatisticsAsync();

                _logger.LogInformation($"Returning statistics: {statistics.Count} metrics");
                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving 112 warnings statistics");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<List<GeocodedWarning112>>> RefreshWarnings()
        {
            try
            {
                _logger.LogInformation("API request received to refresh 112 warnings");

                // Clear cache to force fresh data
                _warning112Service.ClearCache();

                // Get fresh warnings
                var warnings = await _warning112Service.ScrapeAndProcessWarningsAsync();

                _logger.LogInformation($"Refreshed and returning {warnings.Count} warnings");
                return Ok(warnings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing 112 warnings");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpGet("count")]
        public async Task<ActionResult<int>> GetActiveWarningsCount()
        {
            try
            {
                var count = await _warning112Service.GetActiveWarningsCountAsync();
                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active warnings count");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GeocodedWarning112>> GetWarningById(string id, [FromQuery] string language = "en")
        {
            try
            {
                _logger.LogInformation($"API request received for warning ID: {id}");

                var warnings = await _warning112Service.GetActiveWarningsAsync();
                var warning = warnings.FirstOrDefault(w => w.Id == id);

                if (warning == null)
                {
                    _logger.LogWarning($"Warning not found with ID: {id}");
                    return NotFound(new { error = "Warning not found", id });
                }

                _logger.LogInformation($"Returning warning: {id}");
                return Ok(warning);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving warning with ID: {id}");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpDelete("cache")]
        public ActionResult ClearCache()
        {
            try
            {
                _logger.LogInformation("API request received to clear 112 warnings cache");

                _warning112Service.ClearCache();

                _logger.LogInformation("Successfully cleared 112 warnings cache");
                return Ok(new { message = "Cache cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing 112 warnings cache");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        #region Debugging/Test Endpoints

        [HttpPost("create-test")]
        public ActionResult<GeocodedWarning112> CreateTestWarning([FromBody] CreateTestWarningRequest request)
        {
            try
            {
                _logger.LogInformation("API request received to create test warning");

                var warning = new GeocodedWarning112
                {
                    Id = Guid.NewGuid().ToString(),
                    EnglishContent = request.EnglishContent ?? $"⚠️ Activation 1⃣1⃣2⃣\n🆘 Test wildfire warning\n‼️ If you are in #{request.Municipality} area, stay alert\n‼️ Follow the instructions of the Authorities",
                    GreekContent = request.GreekContent ?? $"⚠️ Ενεργοποίηση 1⃣1⃣2⃣\n🆘 Δοκιμαστική προειδοποίηση πυρκαγιάς\n‼️ Αν βρίσκεστε στην περιοχή #{request.Municipality}, παραμείνετε σε ετοιμότητα\n‼️ Ακολουθείτε τις οδηγίες των Αρχών",
                    Locations = new List<string> { request.Municipality },
                    TweetDate = DateTime.UtcNow,
                    SourceUrl = "https://twitter.com/112Greece",
                    CreatedAt = DateTime.UtcNow
                };

                // Add geocoded location
                warning.GeocodedLocations.Add(new GeocodedWarning112.WarningLocation
                {
                    LocationName = request.Municipality,
                    Latitude = request.Latitude ?? 38.2466, 
                    Longitude = request.Longitude ?? 21.7359,
                    Municipality = request.Municipality,
                    Region = request.Region ?? "Greece",
                    GeocodingSource = "Test input"
                });

                // Store the test warning in the service
                _warning112Service.AddTestWarning(warning);
                
                _logger.LogInformation($"Created test warning for {request.Municipality}");
                return Ok(warning);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test warning");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        public class CreateTestWarningRequest
        {
            public string Municipality { get; set; } = "TestMunicipality";
            public string Region { get; set; } = "Greece";
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public string? EnglishContent { get; set; }
            public string? GreekContent { get; set; }
        }

        [HttpGet("test")]
        public async Task<ActionResult> TestTwitterScraping()
        {
            try
            {
                _logger.LogInformation("API request received to test Twitter scraping");

                var warnings = await _warning112Service.ScrapeAndProcessWarningsAsync();

                return Ok(new
                {
                    message = "Twitter scraping test completed",
                    warningsFound = warnings.Count,
                    warnings = warnings.Select(w => new
                    {
                        id = w.Id,
                        englishContent = !string.IsNullOrEmpty(w.EnglishContent) && w.EnglishContent.Length > 100 
                            ? w.EnglishContent.Substring(0, 100) 
                            : w.EnglishContent,
                        greekContent = !string.IsNullOrEmpty(w.GreekContent) && w.GreekContent.Length > 100 
                            ? w.GreekContent.Substring(0, 100) 
                            : w.GreekContent,
                        locationsCount = w.Locations?.Count ?? 0,
                        locations = w.Locations,
                        geocodedLocationsCount = w.GeocodedLocations?.Count ?? 0,
                        isActive = w.IsActive,
                        iconType = w.IconType,
                        warningType = w.WarningType
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Twitter scraping test");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message,
                    details = ex.ToString()
                });
            }
        }

        #endregion
    }
}
