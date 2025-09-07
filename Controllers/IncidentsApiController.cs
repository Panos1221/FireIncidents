using Microsoft.AspNetCore.Mvc;
using FireIncidents.Services;
using FireIncidents.Models;

namespace FireIncidents.Controllers
{
    [Route("api/incidents")]
    [ApiController]
    public class IncidentsApiController : ControllerBase
    {
        private readonly ILogger<IncidentsApiController> _logger;
        private readonly FireServiceScraperService _scraperService;
        private readonly UnifiedGeocodingService _geocodingService;

        public IncidentsApiController(
            ILogger<IncidentsApiController> logger,
            FireServiceScraperService scraperService,
            UnifiedGeocodingService geocodingService)
        {
            _logger = logger;
            _scraperService = scraperService;
            _geocodingService = geocodingService;
        }

        [HttpGet]
        public async Task<ActionResult> GetIncidents(
            [FromQuery] string category = null,
            [FromQuery] string status = null)
        {
            try
            {
                _logger.LogInformation("API request received for incidents");

                _geocodingService.ClearActiveIncidents();

                _logger.LogInformation("Calling scraper service");
                var incidents = await _scraperService.ScrapeIncidentsAsync();

                _logger.LogInformation($"Scraped {incidents.Count} incidents");

                if (!string.IsNullOrEmpty(category))
                {
                    _logger.LogInformation($"Filtering by category: {category}");
                    incidents = incidents.Where(i => i.Category == category).ToList();
                }

                if (!string.IsNullOrEmpty(status))
                {
                    _logger.LogInformation($"Filtering by status: {status}");
                    incidents = incidents.Where(i => i.Status == status).ToList();
                }

                _logger.LogInformation($"After filtering: {incidents.Count} incidents");

                var geocodedIncidents = new List<GeocodedIncident>();
                foreach (var incident in incidents)
                {
                    try
                    {
                        _logger.LogInformation($"Geocoding incident: {incident.Location}, {incident.Municipality}, {incident.Region}");
                        var geocodedIncident = await _geocodingService.GeocodeIncidentAsync(incident);

                        if (geocodedIncident.IsGeocoded)
                        {
                            _logger.LogInformation($"Successfully geocoded to: {geocodedIncident.Latitude}, {geocodedIncident.Longitude}");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to geocode incident. Using default coordinates.");
                        }

                        geocodedIncidents.Add(geocodedIncident);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error geocoding incident: {incident.Location}");

                        var fallbackIncident = new GeocodedIncident
                        {
                            Status = incident.Status,
                            Category = incident.Category,
                            Region = incident.Region,
                            Municipality = incident.Municipality,
                            Location = incident.Location,
                            StartDate = incident.StartDate,
                            LastUpdate = incident.LastUpdate,
                            Latitude = 38.2,  
                            Longitude = 23.8
                        };

                        geocodedIncidents.Add(fallbackIncident);
                    }
                }

                _logger.LogInformation($"Returning {geocodedIncidents.Count} geocoded incidents");
                return Ok(geocodedIncidents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving incidents");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }

        [HttpPost("create-test")]
        public async Task<ActionResult<GeocodedIncident>> CreateTestIncident([FromBody] FireIncident incident)
        {
            try
            {
                _logger.LogInformation("API request received to create test incident");
                _logger.LogInformation($"Test incident: {incident.Location}, {incident.Municipality}, {incident.Region}");

                var geocodedIncident = await _geocodingService.GeocodeIncidentAsync(incident);

                if (geocodedIncident.IsGeocoded)
                {
                    _logger.LogInformation($"Successfully geocoded test incident to: {geocodedIncident.Latitude}, {geocodedIncident.Longitude}");
                }
                else
                {
                    _logger.LogWarning($"Failed to geocode test incident. Using default coordinates.");
                }

                return Ok(geocodedIncident);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test incident");
                return StatusCode(500, new
                {
                    error = "Internal server error",
                    message = ex.Message
                });
            }
        }
    }
}