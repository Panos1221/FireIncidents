using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using FireIncidents.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Text.Encodings.Web;
using System.Net;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FireIncidents.Services
{
    public class GeocodingService
    {
        private readonly ILogger<GeocodingService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly string _nominatimBaseUrl = "https://nominatim.openstreetmap.org/search";

        // Default coordinates for fallback (center of Greece)
        private readonly double _defaultLat = 38.2;
        private readonly double _defaultLon = 23.8;

        // Predefined coordinates for common regions
        private readonly Dictionary<string, (double Lat, double Lon)> _regionCoordinates = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase)
        {
            { "ΠΕΡΙΦΕΡΕΙΑ ΑΤΤΙΚΗΣ", (37.9838, 23.7275) },                      // Athens
            { "ΠΕΡΙΦΕΡΕΙΑ ΚΕΝΤΡΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ", (40.6401, 22.9444) },        // Thessaloniki
            { "ΠΕΡΙΦΕΡΕΙΑ ΔΥΤΙΚΗΣ ΕΛΛΑΔΑΣ", (38.2466, 21.7359) },             // Patras
            { "ΠΕΡΙΦΕΡΕΙΑ ΘΕΣΣΑΛΙΑΣ", (39.6383, 22.4179) },                    // Larissa
            { "ΠΕΡΙΦΕΡΕΙΑ ΚΡΗΤΗΣ", (35.3387, 25.1442) },                       // Heraklion
            { "ΠΕΡΙΦΕΡΕΙΑ ΑΝΑΤΟΛΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ ΚΑΙ ΘΡΑΚΗΣ", (41.1169, 25.4045) }, // Komotini
            { "ΠΕΡΙΦΕΡΕΙΑ ΗΠΕΙΡΟΥ", (39.6675, 20.8511) },                      // Ioannina
            { "ΠΕΡΙΦΕΡΕΙΑ ΠΕΛΟΠΟΝΝΗΣΟΥ", (37.5047, 22.3742) },                 // Tripoli
            { "ΠΕΡΙΦΕΡΕΙΑ ΔΥΤΙΚΗΣ ΜΑΚΕΔΟΝΙΑΣ", (40.3007, 21.7887) },           // Kozani
            { "ΠΕΡΙΦΕΡΕΙΑ ΣΤΕΡΕΑΣ ΕΛΛΑΔΑΣ", (38.9, 22.4331) },                 // Lamia
            { "ΠΕΡΙΦΕΡΕΙΑ ΒΟΡΕΙΟΥ ΑΙΓΑΙΟΥ", (39.1, 26.5547) },                 // Mytilene
            { "ΠΕΡΙΦΕΡΕΙΑ ΝΟΤΙΟΥ ΑΙΓΑΙΟΥ", (36.4335, 28.2183) },              // Rhodes
            { "ΠΕΡΙΦΕΡΕΙΑ ΙΟΝΙΩΝ ΝΗΣΩΝ", (39.6243, 19.9217) }                 // Corfu
        };

        public GeocodingService(ILogger<GeocodingService> logger, IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("Nominatim");
            _cache = cache;

            // Set a longer timeout for geocoding requests
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<GeocodedIncident> GeocodeIncidentAsync(FireIncident incident)
        {
            // Create a geocoded incident with the same properties as the original incident
            var geocodedIncident = new GeocodedIncident
            {
                Status = incident.Status,
                Category = incident.Category,
                Region = incident.Region,
                Municipality = incident.Municipality,
                Location = incident.Location,
                StartDate = incident.StartDate,
                LastUpdate = incident.LastUpdate
            };

            try
            {
                _logger.LogInformation($"Geocoding incident: {incident.Region}, {incident.Municipality}, {incident.Location}");

                // First check if we have this location cached
                string cacheKey = GetCacheKey(incident.Region, incident.Municipality, incident.Location);

                if (_cache.TryGetValue(cacheKey, out (double Lat, double Lon) coordinates))
                {
                    _logger.LogInformation($"Found cached coordinates for {cacheKey}: {coordinates.Lat}, {coordinates.Lon}");
                    geocodedIncident.Latitude = coordinates.Lat;
                    geocodedIncident.Longitude = coordinates.Lon;
                    return geocodedIncident;
                }

                // Try with predefined region coordinates first
                if (!string.IsNullOrEmpty(incident.Region) && _regionCoordinates.TryGetValue(incident.Region, out var regionCoords))
                {
                    _logger.LogInformation($"Using predefined coordinates for region {incident.Region}: {regionCoords.Lat}, {regionCoords.Lon}");
                    geocodedIncident.Latitude = regionCoords.Lat;
                    geocodedIncident.Longitude = regionCoords.Lon;

                    // Cache these coordinates
                    _cache.Set(cacheKey, (regionCoords.Lat, regionCoords.Lon), TimeSpan.FromDays(30));

                    // Try to get more precise coordinates with geocoding in the background
                    Task.Run(async () => {
                        try
                        {
                            // Still try to get more precise coordinates
                            var (lat, lon) = await GeocodeAddressAsync(GetBestAddress(incident));
                            if (lat != 0 && lon != 0)
                            {
                                _logger.LogInformation($"Updated coordinates for {cacheKey} to {lat}, {lon}");
                                _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Background geocoding failed for {cacheKey}");
                        }
                    });

                    return geocodedIncident;
                }

                // Try geocoding with the best address we can construct
                var (lat, lon) = await GeocodeAddressAsync(GetBestAddress(incident));

                if (lat != 0 && lon != 0)
                {
                    geocodedIncident.Latitude = lat;
                    geocodedIncident.Longitude = lon;

                    // Cache the result
                    _cache.Set(cacheKey, (lat, lon), TimeSpan.FromDays(30));

                    return geocodedIncident;
                }

                // If that failed, fall back to default coordinates
                _logger.LogWarning($"Geocoding failed for {incident.Region}, {incident.Municipality}, {incident.Location}. Using default coordinates.");
                geocodedIncident.Latitude = _defaultLat;
                geocodedIncident.Longitude = _defaultLon;

                return geocodedIncident;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding incident: {incident.Region}, {incident.Municipality}, {incident.Location}");

                // Fall back to default coordinates
                geocodedIncident.Latitude = _defaultLat;
                geocodedIncident.Longitude = _defaultLon;

                return geocodedIncident;
            }
        }

        private string GetCacheKey(string region, string municipality, string location)
        {
            // Sanitize the values to create a safe cache key
            string sanitized = $"{SanitizeForCacheKey(region)}_{SanitizeForCacheKey(municipality)}_{SanitizeForCacheKey(location)}";
            return $"geocode_{sanitized}";
        }

        private string SanitizeForCacheKey(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknown";

            // Remove special characters and trim
            return Regex.Replace(input, @"[^\w\s]", "").Trim().Replace(" ", "_").ToLowerInvariant();
        }

        private string GetBestAddress(FireIncident incident)
        {
            var addressParts = new List<string>();

            // Add non-empty parts in order from most specific to least specific
            if (!string.IsNullOrEmpty(incident.Location))
                addressParts.Add(incident.Location);

            if (!string.IsNullOrEmpty(incident.Municipality))
                addressParts.Add(incident.Municipality);

            if (!string.IsNullOrEmpty(incident.Region))
                addressParts.Add(incident.Region);

            // Always add the country
            addressParts.Add("Greece");

            // Join with commas
            return string.Join(", ", addressParts);
        }

        private async Task<(double Lat, double Lon)> GeocodeAddressAsync(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                _logger.LogWarning("Empty address provided for geocoding");
                return (0, 0);
            }

            try
            {
                // Ensure we're not sending requests too quickly (Nominatim has rate limits)
                await Task.Delay(1000);

                _logger.LogInformation($"Geocoding address: {address}");

                // Properly encode the address for URL
                var encodedAddress = Uri.EscapeDataString(address);
                var requestUrl = $"{_nominatimBaseUrl}?q={encodedAddress}&format=json&limit=1&accept-language=el";

                // Add required headers for Nominatim
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "FireIncidentsMapApplication/1.0");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Geocoding request failed with status code {response.StatusCode}");
                    return (0, 0);
                }

                var content = await response.Content.ReadAsStringAsync();

                // Check if we got an empty array
                if (content == "[]" || string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning($"Geocoding returned no results for: {address}");
                    return (0, 0);
                }

                try
                {
                    var results = JsonSerializer.Deserialize<JsonElement[]>(content);

                    if (results != null && results.Length > 0)
                    {
                        var result = results[0];

                        if (result.TryGetProperty("lat", out JsonElement latElement) &&
                            result.TryGetProperty("lon", out JsonElement lonElement))
                        {
                            // Handle different ways lat/lon might be represented
                            if (latElement.ValueKind == JsonValueKind.String &&
                                lonElement.ValueKind == JsonValueKind.String)
                            {
                                if (double.TryParse(latElement.GetString(),
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out double lat) &&
                                    double.TryParse(lonElement.GetString(),
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out double lon))
                                {
                                    _logger.LogInformation($"Successfully geocoded to: {lat}, {lon}");
                                    return (lat, lon);
                                }
                            }
                            else if (latElement.ValueKind == JsonValueKind.Number &&
                                    lonElement.ValueKind == JsonValueKind.Number)
                            {
                                double lat = latElement.GetDouble();
                                double lon = lonElement.GetDouble();
                                _logger.LogInformation($"Successfully geocoded to: {lat}, {lon}");
                                return (lat, lon);
                            }
                        }
                    }

                    _logger.LogWarning($"Could not extract coordinates from geocoding result for: {address}");
                    return (0, 0);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"Error parsing geocoding JSON for address: {address}");
                    _logger.LogDebug($"Raw JSON: {content}");
                    return (0, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding address: {address}");
                return (0, 0);
            }
        }
    }
}