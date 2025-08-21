using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace FireIncidents.Services
{
    public class Warning112Service
    {
        private readonly ILogger<Warning112Service> _logger;
        private readonly TwitterScraperService _twitterScraperService;
        private readonly GeocodingService _geocodingService;
        private readonly IMemoryCache _cache;
        
        // Cache key for storing active warnings
        private const string ACTIVE_WARNINGS_CACHE_KEY = "active_112_warnings";
        private const string PAIRED_WARNINGS_CACHE_KEY = "paired_112_warnings";
        private const string TEST_WARNINGS_CACHE_KEY = "test_112_warnings";
        
        // Time-based constants
        private readonly TimeSpan _warningActiveTime = TimeSpan.FromHours(24);
        private readonly TimeSpan _redIconTime = TimeSpan.FromHours(12);
        
        public Warning112Service(
            ILogger<Warning112Service> logger,
            TwitterScraperService twitterScraperService,
            GeocodingService geocodingService,
            IMemoryCache cache)
        {
            _logger = logger;
            _twitterScraperService = twitterScraperService;
            _geocodingService = geocodingService;
            _cache = cache;
        }

        public async Task<List<GeocodedWarning112>> GetActiveWarningsAsync()
        {
            try
            {
                _logger.LogInformation("Getting active 112 warnings...");
                
                var allWarnings = new List<GeocodedWarning112>();
                
                // Get test warnings first
                var testWarnings = GetTestWarnings();
                allWarnings.AddRange(testWarnings);
                _logger.LogInformation($"Found {testWarnings.Count} test warnings");
                
                // Check cache for scraped warnings
                if (_cache.TryGetValue(ACTIVE_WARNINGS_CACHE_KEY, out List<GeocodedWarning112> cachedWarnings))
                {
                    // Filter out expired warnings
                    var activeScrapedWarnings = cachedWarnings.Where(w => w.IsActive).ToList();
                    allWarnings.AddRange(activeScrapedWarnings);
                    _logger.LogInformation($"Found {activeScrapedWarnings.Count} cached scraped warnings");
                }
                else
                {
                    // Scrape fresh warnings
                    var scrapedWarnings = await ScrapeAndProcessWarningsAsync();
                    
                    // Cache the results for 5 minutes
                    _cache.Set(ACTIVE_WARNINGS_CACHE_KEY, scrapedWarnings, TimeSpan.FromMinutes(5));
                    
                    var activeScrapedWarnings = scrapedWarnings.Where(w => w.IsActive).ToList();
                    allWarnings.AddRange(activeScrapedWarnings);
                    _logger.LogInformation($"Found {activeScrapedWarnings.Count} fresh scraped warnings");
                }
                
                // Filter out expired warnings from the combined list
                var activeWarnings = allWarnings.Where(w => w.IsActive).ToList();
                _logger.LogInformation($"Returning {activeWarnings.Count} total active warnings");
                
                return activeWarnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting active 112 warnings");
                return new List<GeocodedWarning112>();
            }
        }

        public async Task<List<GeocodedWarning112>> ScrapeAndProcessWarningsAsync()
        {
            try
            {
                _logger.LogInformation("Scraping and processing 112 warnings...");
                
                // Scrape warnings from Twitter
                var rawWarnings = await _twitterScraperService.ScrapeWarningsAsync();
                
                if (!rawWarnings.Any())
                {
                    _logger.LogWarning("No warnings found from Twitter scraping");
                    return new List<GeocodedWarning112>();
                }
                
                // Pair English and Greek versions of the same warning
                var pairedWarnings = PairWarningsByContent(rawWarnings);
                
                // Geocode the warnings
                var geocodedWarnings = new List<GeocodedWarning112>();
                
                foreach (var warning in pairedWarnings)
                {
                    try
                    {
                        var geocodedWarning = await GeocodeWarningAsync(warning);
                        if (geocodedWarning.HasGeocodedLocations)
                        {
                            geocodedWarnings.Add(geocodedWarning);
                        }
                        else
                        {
                            _logger.LogWarning($"Warning could not be geocoded: {warning.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error geocoding warning: {warning.Id}");
                    }
                }
                
                _logger.LogInformation($"Successfully processed {geocodedWarnings.Count} geocoded warnings");
                
                return geocodedWarnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scraping and processing warnings");
                return new List<GeocodedWarning112>();
            }
        }

        private List<Warning112> PairWarningsByContent(List<Warning112> warnings)
        {
            try
            {
                _logger.LogInformation($"Pairing {warnings.Count} warnings by content similarity...");
                
                var pairedWarnings = new List<Warning112>();
                var usedWarnings = new HashSet<string>();
                
                foreach (var englishWarning in warnings.Where(w => !string.IsNullOrEmpty(w.EnglishContent)))
                {
                    if (usedWarnings.Contains(englishWarning.Id))
                        continue;
                        
                    // Look for corresponding Greek warning
                    var greekWarning = warnings
                        .Where(w => !string.IsNullOrEmpty(w.GreekContent) && 
                                   !usedWarnings.Contains(w.Id))
                        .FirstOrDefault(w => AreWarningsSimilar(englishWarning, w));
                    
                    var pairedWarning = new Warning112
                    {
                        Id = englishWarning.Id,
                        EnglishContent = englishWarning.EnglishContent,
                        GreekContent = greekWarning?.GreekContent ?? "",
                        Locations = englishWarning.Locations, // Use English locations for geocoding
                        TweetDate = englishWarning.TweetDate,
                        SourceUrl = englishWarning.SourceUrl,
                        CreatedAt = englishWarning.CreatedAt
                    };
                    
                    pairedWarnings.Add(pairedWarning);
                    usedWarnings.Add(englishWarning.Id);
                    
                    if (greekWarning != null)
                    {
                        usedWarnings.Add(greekWarning.Id);
                        _logger.LogDebug($"Paired English warning {englishWarning.Id} with Greek warning {greekWarning.Id}");
                    }
                }
                
                // Add unpaired Greek warnings
                foreach (var greekWarning in warnings.Where(w => !string.IsNullOrEmpty(w.GreekContent) && !usedWarnings.Contains(w.Id)))
                {
                    var pairedWarning = new Warning112
                    {
                        Id = greekWarning.Id,
                        EnglishContent = "",
                        GreekContent = greekWarning.GreekContent,
                        Locations = greekWarning.Locations,
                        TweetDate = greekWarning.TweetDate,
                        SourceUrl = greekWarning.SourceUrl,
                        CreatedAt = greekWarning.CreatedAt
                    };
                    
                    pairedWarnings.Add(pairedWarning);
                }
                
                _logger.LogInformation($"Paired warnings result: {pairedWarnings.Count} paired warnings from {warnings.Count} original");
                
                return pairedWarnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pairing warnings");
                return warnings; // Return original list if pairing fails
            }
        }

        private bool AreWarningsSimilar(Warning112 warning1, Warning112 warning2)
        {
            // Check if the warnings are about the same incident based on:
            // 1. Similar time (within 30 minutes)
            // 2. Similar location count
            // 3. Some overlapping location names (transliterated)
            
            var timeDiff = Math.Abs((warning1.TweetDate - warning2.TweetDate).TotalMinutes);
            if (timeDiff > 30)
                return false;
                
            if (Math.Abs(warning1.Locations.Count - warning2.Locations.Count) > 2)
                return false;

            // This is very simple it needs to be more sophisticated. To do list later on
            return true;
        }

        private async Task<GeocodedWarning112> GeocodeWarningAsync(Warning112 warning)
        {
            var geocodedWarning = new GeocodedWarning112
            {
                Id = warning.Id,
                EnglishContent = warning.EnglishContent,
                GreekContent = warning.GreekContent,
                Locations = warning.Locations,
                TweetDate = warning.TweetDate,
                SourceUrl = warning.SourceUrl,
                CreatedAt = warning.CreatedAt
            };
            
            try
            {
                _logger.LogInformation($"Geocoding warning {warning.Id} with {warning.Locations.Count} locations");
                
                foreach (var locationName in warning.Locations)
                {
                    try
                    {
                        var geocodedLocation = await GeocodeLocationAsync(locationName);
                        
                        if (geocodedLocation != null)
                        {
                            geocodedWarning.GeocodedLocations.Add(geocodedLocation);
                            _logger.LogInformation($"Successfully geocoded location '{locationName}' to {geocodedLocation.Latitude}, {geocodedLocation.Longitude}");
                        }
                        else
                        {
                            _logger.LogWarning($"Failed to geocode location: {locationName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error geocoding location '{locationName}'");
                    }
                }
                
                _logger.LogInformation($"Geocoded {geocodedWarning.GeocodedLocations.Count} out of {warning.Locations.Count} locations for warning {warning.Id}");
                
                return geocodedWarning;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding warning {warning.Id}");
                return geocodedWarning;
            }
        }

        private async Task<GeocodedWarning112.WarningLocation> GeocodeLocationAsync(string locationName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationName))
                    return null;
                    
                // Create a temporary FireIncident to use the existing geocoding service
                var tempIncident = new FireIncident
                {
                    Location = locationName,
                    Municipality = "", 
                    Region = "Greece", 
                    Status = "ΣΕ ΕΞΕΛΙΞΗ",
                    Category = "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ",
                    StartDate = DateTime.Now.ToString(),
                    LastUpdate = DateTime.Now.ToString()
                };
                
                var geocodedIncident = await _geocodingService.GeocodeIncidentAsync(tempIncident);
                
                if (geocodedIncident.IsGeocoded)
                {
                    return new GeocodedWarning112.WarningLocation
                    {
                        LocationName = locationName,
                        Latitude = geocodedIncident.Latitude,
                        Longitude = geocodedIncident.Longitude,
                        GeocodingSource = geocodedIncident.GeocodingSource,
                        Municipality = geocodedIncident.Municipality,
                        Region = geocodedIncident.Region
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding location '{locationName}'");
                return null;
            }
        }

        public async Task<List<GeocodedWarning112>> GetWarningsForLocationAsync(string region = null, string municipality = null)
        {
            var allWarnings = await GetActiveWarningsAsync();
            
            if (string.IsNullOrEmpty(region) && string.IsNullOrEmpty(municipality))
                return allWarnings;
                
            return allWarnings.Where(w => 
                w.GeocodedLocations.Any(l => 
                    (string.IsNullOrEmpty(region) || l.Region?.Contains(region, StringComparison.OrdinalIgnoreCase) == true) &&
                    (string.IsNullOrEmpty(municipality) || l.Municipality?.Contains(municipality, StringComparison.OrdinalIgnoreCase) == true)
                )
            ).ToList();
        }

        public void ClearCache()
        {
            _cache.Remove(ACTIVE_WARNINGS_CACHE_KEY);
            _cache.Remove(PAIRED_WARNINGS_CACHE_KEY);
            _cache.Remove(TEST_WARNINGS_CACHE_KEY);
            _logger.LogInformation("Cleared 112 warnings cache");
        }

        public void AddTestWarning(GeocodedWarning112 warning)
        {
            try
            {
                var testWarnings = GetTestWarnings();
                testWarnings.Add(warning);
                
                // Store test warnings for 1 hour
                _cache.Set(TEST_WARNINGS_CACHE_KEY, testWarnings, TimeSpan.FromHours(1));
                
                _logger.LogInformation($"Added test warning {warning.Id} for {warning.Locations?.FirstOrDefault()}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding test warning");
            }
        }

        private List<GeocodedWarning112> GetTestWarnings()
        {
            try
            {
                if (_cache.TryGetValue(TEST_WARNINGS_CACHE_KEY, out List<GeocodedWarning112> testWarnings))
                {
                    // Filter out expired test warnings
                    var activeTestWarnings = testWarnings.Where(w => w.IsActive).ToList();
                    
                    // Update cache if we filtered out expired warnings
                    if (activeTestWarnings.Count != testWarnings.Count)
                    {
                        _cache.Set(TEST_WARNINGS_CACHE_KEY, activeTestWarnings, TimeSpan.FromHours(1));
                    }
                    
                    return activeTestWarnings;
                }
                
                return new List<GeocodedWarning112>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting test warnings");
                return new List<GeocodedWarning112>();
            }
        }

        public async Task<int> GetActiveWarningsCountAsync()
        {
            var warnings = await GetActiveWarningsAsync();
            return warnings.Count;
        }

        public async Task<Dictionary<string, int>> GetWarningsStatisticsAsync()
        {
            var warnings = await GetActiveWarningsAsync();
            
            return new Dictionary<string, int>
            {
                ["TotalActive"] = warnings.Count,
                ["RedIcon"] = warnings.Count(w => w.IconType == "red"),
                ["YellowIcon"] = warnings.Count(w => w.IconType == "yellow"),
                ["WithLocations"] = warnings.Count(w => w.HasGeocodedLocations),
                ["EvacuationWarnings"] = warnings.Count(w => w.WarningType == "Evacuation Warning"),
                ["WildfireWarnings"] = warnings.Count(w => w.WarningType == "Wildfire Warning")
            };
        }
    }
}
