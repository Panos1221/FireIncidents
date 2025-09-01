using FireIncidents.Models;
using Microsoft.Extensions.Options;

namespace FireIncidents.Services
{
    public class UnifiedGeocodingService
    {
        private readonly ILogger<UnifiedGeocodingService> _logger;
        private readonly GreekDatasetGeocodingService _greekDatasetService;
        private readonly GeocodingService _backupGeocodingService;
        private readonly GeocodingConfiguration _configuration;

        public UnifiedGeocodingService(
            ILogger<UnifiedGeocodingService> logger,
            GreekDatasetGeocodingService greekDatasetService,
            GeocodingService backupGeocodingService,
            IOptions<GeocodingConfiguration> configuration)
        {
            _logger = logger;
            _greekDatasetService = greekDatasetService;
            _backupGeocodingService = backupGeocodingService;
            _configuration = configuration.Value;
        }

        public void ClearActiveIncidents()
        {
            _greekDatasetService.ClearActiveIncidents();
            _backupGeocodingService.ClearActiveIncidents();
        }

        public async Task<GeocodedIncident> GeocodeIncidentAsync(FireIncident incident)
        {
            try
            {
                if (_configuration.UseGreekDataset)
                {
                    _logger.LogInformation("Using Greek dataset geocoding service (new implementation)");
                    return await _greekDatasetService.GeocodeIncidentAsync(incident);
                }
                else
                {
                    _logger.LogInformation("Using backup geocoding service (original implementation)");
                    return await _backupGeocodingService.GeocodeIncidentAsync(incident);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in unified geocoding service");
                
                if (_configuration.EnableBackupGeocoding && _configuration.UseGreekDataset)
                {
                    _logger.LogWarning("Falling back to backup geocoding service due to error");
                    return await _backupGeocodingService.GeocodeIncidentAsync(incident);
                }
                
                throw;
            }
        }
    }
}
