using System.Text.Json;
using System.Text.Json.Serialization;
using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace FireIncidents.Services
{
    public class GreekDatasetGeocodingService
    {
        private readonly ILogger<GreekDatasetGeocodingService> _logger;
        private readonly IMemoryCache _cache;
        private readonly GeocodingService _backupGeocodingService;
        
        // Dataset cache key
        private const string DATASET_CACHE_KEY = "greek_cities_dataset";
        private const string MUNICIPALITY_INDEX_CACHE_KEY = "municipality_index";
        
        // Track incident coordinates to avoid placing them on top of each other
        private Dictionary<string, List<(double Lat, double Lon)>> _activeIncidentCoordinates;
        
        // In-memory dataset and index
        private List<GreekCityData> _dataset;
        private Dictionary<string, List<GreekCityData>> _municipalityIndex;
        private bool _isInitialized = false;
        private readonly object _initLock = new object();

        public GreekDatasetGeocodingService(
            ILogger<GreekDatasetGeocodingService> logger, 
            IMemoryCache cache,
            GeocodingService backupGeocodingService)
        {
            _logger = logger;
            _cache = cache;
            _backupGeocodingService = backupGeocodingService;
            _activeIncidentCoordinates = new Dictionary<string, List<(double Lat, double Lon)>>();
        }

        // Clear active incidents when starting a new fetch cycle
        public void ClearActiveIncidents()
        {
            _activeIncidentCoordinates.Clear();
            _backupGeocodingService.ClearActiveIncidents();
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                try
                {
                    _logger.LogInformation("Initializing Greek dataset geocoding service...");

                    // Try to load from cache first
                    if (_cache.TryGetValue(DATASET_CACHE_KEY, out List<GreekCityData> cachedDataset) &&
                        _cache.TryGetValue(MUNICIPALITY_INDEX_CACHE_KEY, out Dictionary<string, List<GreekCityData>> cachedIndex))
                    {
                        _dataset = cachedDataset;
                        _municipalityIndex = cachedIndex;
                        _logger.LogInformation("Loaded Greek dataset from cache: {Count} entries", _dataset.Count);
                    }
                    else
                    {
                        // Load from file
                        LoadDatasetFromFile();
                        BuildMunicipalityIndex();
                        
                        // Cache the dataset and index
                        _cache.Set(DATASET_CACHE_KEY, _dataset, new MemoryCacheEntryOptions
                        {
                            Priority = CacheItemPriority.Normal
                        });
                        _cache.Set(MUNICIPALITY_INDEX_CACHE_KEY, _municipalityIndex, new MemoryCacheEntryOptions
                        {
                            Priority = CacheItemPriority.Normal
                        });
                        //_cache.Set(DATASET_CACHE_KEY, _dataset, TimeSpan.FromHours(24));
                        //_cache.Set(MUNICIPALITY_INDEX_CACHE_KEY, _municipalityIndex, TimeSpan.FromHours(24));
                    }

                    _isInitialized = true;
                    _logger.LogInformation("Greek dataset geocoding service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Greek dataset geocoding service");
                    throw;
                }
            }
        }

        private void LoadDatasetFromFile()
        {
            try
            {
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "greek-cities-and-villages-geolocation-dataset.json");
                
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Greek cities dataset file not found at: {filePath}");
                }

                _logger.LogInformation("Loading Greek cities dataset from: {FilePath}", filePath);
                
                var jsonContent = File.ReadAllText(filePath, Encoding.UTF8);
                _dataset = JsonSerializer.Deserialize<List<GreekCityData>>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_dataset == null || _dataset.Count == 0)
                {
                    throw new InvalidOperationException("Failed to load Greek cities dataset or dataset is empty");
                }

                _logger.LogInformation("Successfully loaded {Count} entries from Greek cities dataset", _dataset.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Greek cities dataset from file");
                throw;
            }
        }

        private void BuildMunicipalityIndex()
        {
            _municipalityIndex = new Dictionary<string, List<GreekCityData>>(StringComparer.OrdinalIgnoreCase);

            foreach (var city in _dataset)
            {
                if (string.IsNullOrEmpty(city.Municipality)) continue;

                // Create normalized municipality key
                var normalizedKey = NormalizeMunicipalityName(city.Municipality);
                
                if (!_municipalityIndex.ContainsKey(normalizedKey))
                {
                    _municipalityIndex[normalizedKey] = new List<GreekCityData>();
                }
                
                _municipalityIndex[normalizedKey].Add(city);

                // Also index without "Δημος" prefix for easier matching
                var withoutPrefix = RemoveMunicipalityPrefix(city.Municipality);
                if (!string.IsNullOrEmpty(withoutPrefix) && withoutPrefix != normalizedKey)
                {
                    if (!_municipalityIndex.ContainsKey(withoutPrefix))
                    {
                        _municipalityIndex[withoutPrefix] = new List<GreekCityData>();
                    }
                    _municipalityIndex[withoutPrefix].Add(city);
                }
            }

            _logger.LogInformation("Built municipality index with {Count} unique municipalities", _municipalityIndex.Count);
        }

        public async Task<GeocodedIncident> GeocodeIncidentAsync(FireIncident incident)
        {
            await InitializeAsync();

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
                _logger.LogInformation("Geocoding incident using Greek dataset: {Region}, {Municipality}, {Location}",
                    incident.Region, incident.Municipality, incident.Location);

                // Try to find coordinates using the Greek dataset
                var coordinates = await FindCoordinatesInDataset(incident);
                
                if (coordinates.HasValue)
                {
                    _logger.LogInformation("Found coordinates in Greek dataset: {Lat}, {Lon}", coordinates.Value.Lat, coordinates.Value.Lon);
                    
                    // Apply offset to avoid overlapping markers
                    string locationKey = GetLocationKey(incident);
                    var (offsetLat, offsetLon) = GetOffsetCoordinates(locationKey, coordinates.Value.Lat, coordinates.Value.Lon);
                    
                    geocodedIncident.Latitude = offsetLat;
                    geocodedIncident.Longitude = offsetLon;
                    geocodedIncident.GeocodingSource = "Greek Cities Dataset";
                    
                    return geocodedIncident;
                }
                else
                {
                    _logger.LogWarning("No coordinates found in Greek dataset for: {Municipality}. Falling back to backup geocoding service.", incident.Municipality);
                    
                    // Fall back to the existing geocoding service (Built in dictionary in @GeocodingService, if doesnt work call Nominatim etc)
                    return await _backupGeocodingService.GeocodeIncidentAsync(incident);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error geocoding incident with Greek dataset: {Region}, {Municipality}, {Location}",
                    incident.Region, incident.Municipality, incident.Location);
                
                // Fall back to the existing geocoding service (Built in dictionary in @GeocodingService, if doesnt work call Nominatim etc)
                return await _backupGeocodingService.GeocodeIncidentAsync(incident);
            }
        }

        private async Task<(double Lat, double Lon)?> FindCoordinatesInDataset(FireIncident incident)
        {
            if (string.IsNullOrEmpty(incident.Municipality))
            {
                _logger.LogWarning("Municipality is empty, cannot search in dataset");
                return null;
            }

            // Normalize the scraped municipality name
            var normalizedMunicipality = NormalizeMunicipalityName(incident.Municipality);
            
            _logger.LogDebug("Searching for municipality: '{Original}' -> normalized: '{Normalized}'", 
                incident.Municipality, normalizedMunicipality);

            // Try multiple search strategies
            var searchKeys = GenerateMunicipalitySearchKeys(incident.Municipality);
            
            foreach (var searchKey in searchKeys)
            {
                _logger.LogDebug("Trying search key: '{SearchKey}'", searchKey);
                
                if (_municipalityIndex.TryGetValue(searchKey, out var cityEntries))
                {
                    _logger.LogInformation("Found {Count} entries for search key: '{SearchKey}'", cityEntries.Count, searchKey);
                    
                    // Filter entries that have coordinates and optionally match region
                    var validEntries = FilterValidEntries(cityEntries, incident.Region);
                    
                    // Debug
                    var entriesWithCoords = cityEntries.Where(e => e.Has_Geolocation && e.Latitude != null && e.Longitude != null).Count();
                    _logger.LogDebug("Filtering results for '{SearchKey}': {Total} total entries, {WithCoords} with coordinates, {Valid} after region filtering",
                        searchKey, cityEntries.Count, entriesWithCoords, validEntries.Count);
                    
                    if (validEntries.Any())
                    {
                        // Select the best entry (prefer higher population or specific criteria)
                        var selectedEntry = SelectBestEntry(validEntries);
                        
                        _logger.LogInformation("Selected city: '{City}' in municipality: '{Municipality}' with coordinates: {Lat}, {Lon}",
                            selectedEntry.City, selectedEntry.Municipality, selectedEntry.Latitude, selectedEntry.Longitude);
                        
                        return (Convert.ToDouble(selectedEntry.Latitude), Convert.ToDouble(selectedEntry.Longitude));
                    }
                    else
                    {
                        _logger.LogWarning("No valid entries with coordinates found for search key: '{SearchKey}'. " +
                            "Total entries: {Total}, With coordinates: {WithCoords}, After region filter: {Valid}",
                            searchKey, cityEntries.Count, entriesWithCoords, validEntries.Count);
                        
                        // Debug
                        for (int i = 0; i < Math.Min(3, cityEntries.Count); i++)
                        {
                            var entry = cityEntries[i];
                            _logger.LogDebug("Sample entry {Index}: City='{City}', Municipality='{Municipality}', " +
                                "Region='{Region}', HasGeo={HasGeo}, Lat={Lat}, Lon={Lon}",
                                i + 1, entry.City, entry.Municipality, entry.Region, entry.Has_Geolocation, entry.Latitude, entry.Longitude);
                        }
                    }
                }
            }
            
            // fuzzy matching as last resort
            return await TryFuzzyMatching(incident);
        }

        private List<string> GenerateMunicipalitySearchKeys(string municipalityName)
        {
            var searchKeys = new List<string>();
            
            if (string.IsNullOrEmpty(municipalityName)) return searchKeys;

            // Original normalized name
            var normalized = NormalizeMunicipalityName(municipalityName);
            searchKeys.Add(normalized);
            
            // Convert "Δ. NAME" to "Δημος NAME" format
            if (municipalityName.StartsWith("Δ. ", StringComparison.OrdinalIgnoreCase))
            {
                var withDimos = "Δημος " + municipalityName.Substring(3);
                searchKeys.Add(NormalizeMunicipalityName(withDimos));
            }
            
            // Remove prefix entirely
            var withoutPrefix = RemoveMunicipalityPrefix(municipalityName);
            if (!string.IsNullOrEmpty(withoutPrefix))
            {
                searchKeys.Add(NormalizeMunicipalityName(withoutPrefix));
            }
            
            // Handle special cases like "NAME - OTHER" vs "NAME - DIFFERENT"
            if (municipalityName.Contains(" - "))
            {
                var parts = municipalityName.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // Try just the first part
                    var firstPart = parts[0];
                    if (firstPart.StartsWith("Δ. ", StringComparison.OrdinalIgnoreCase))
                    {
                        firstPart = "Δημος " + firstPart.Substring(3);
                    }
                    searchKeys.Add(NormalizeMunicipalityName(firstPart));
                    
                    // Try different combinations for fuzzy matching
                    foreach (var part in parts)
                    {
                        var cleanPart = RemoveMunicipalityPrefix(part);
                        if (!string.IsNullOrEmpty(cleanPart))
                        {
                            searchKeys.Add(NormalizeMunicipalityName(cleanPart));
                        }
                    }
                }
            }
            
            // Remove duplicates
            return searchKeys.Distinct().ToList();
        }

        private async Task<(double Lat, double Lon)?> TryFuzzyMatching(FireIncident incident)
        {
            _logger.LogDebug("Attempting fuzzy matching for municipality: '{Municipality}'", incident.Municipality);
            
            // Test 1: Try first part matching 
            var firstPartMatch = await TryFirstPartMatching(incident);
            if (firstPartMatch.HasValue)
            {
                return firstPartMatch;
            }
            
            // Test 2: fuzzy matching
            var searchTerms = ExtractSearchTerms(incident.Municipality);
            var bestMatches = new List<(GreekCityData City, double Score, string Reason)>();
            
            foreach (var kvp in _municipalityIndex)
            {
                var municipalityKey = kvp.Key;
                var entries = kvp.Value;
                
                var score = CalculateSimilarityScore(searchTerms, municipalityKey);
                if (score > 0.4) // Lower threshold for more aggressive matching
                {
                    var validEntries = FilterValidEntries(entries, incident.Region);
                    if (validEntries.Any())
                    {
                        var bestEntry = SelectBestEntry(validEntries);
                        bestMatches.Add((bestEntry, score, "Traditional fuzzy match"));
                    }
                }
            }
            
            if (bestMatches.Any())
            {
                var bestMatch = bestMatches.OrderByDescending(m => m.Score).First();
                
                _logger.LogInformation("Fuzzy match found: '{City}' in '{Municipality}' with score: {Score:F2} (Reason: {Reason})",
                    bestMatch.City.City, bestMatch.City.Municipality, bestMatch.Score, bestMatch.Reason);
                
                return (Convert.ToDouble(bestMatch.City.Latitude), Convert.ToDouble(bestMatch.City.Longitude));
            }
            
            _logger.LogWarning("No fuzzy matches found for municipality: '{Municipality}'", incident.Municipality);
            return null;
        }

        private async Task<(double Lat, double Lon)?> TryFirstPartMatching(FireIncident incident)
        {
            if (!incident.Municipality.Contains(" - ")) // (mostly for cases with data in format like "Δ. ΑΓΡΙΝΙΟΥ - ΑΓΡΙΝΙΟΥ", "Δ. ΤΡΟΙΖΗΝΙΑΣ - ΤΡΟΙΖΗΝΟΣ")
            {
                return null;
            }
            
            // Extract the first part of the municipality name
            var firstPart = ExtractFirstMunicipalityPart(incident.Municipality);
            if (string.IsNullOrEmpty(firstPart))
            {
                return null;
            }
            
            _logger.LogDebug("Trying first part matching: '{FirstPart}' from '{FullName}'", 
                firstPart, incident.Municipality);
            
            var candidates = new List<(GreekCityData City, double Score, string FullMunicipalityName)>();
            
            // Search for municipalities that start with the first part
            foreach (var kvp in _municipalityIndex)
            {
                var municipalityKey = kvp.Key;
                var entries = kvp.Value;
                
                // Check if this municipality key contains our first part
                if (municipalityKey.Contains(firstPart.ToUpperInvariant()))
                {
                    var validEntries = FilterValidEntries(entries, incident.Region);
                    if (validEntries.Any())
                    {
                        foreach (var entry in validEntries)
                        {
                            var score = CalculateFirstPartMatchScore(firstPart, entry.Municipality, incident.Region, entry.Region);
                            if (score > 0.5)
                            {
                                candidates.Add((entry, score, entry.Municipality));
                            }
                        }
                    }
                }
            }
            
            if (candidates.Any())
            {
                // Sort by score and region match
                var bestCandidate = candidates.OrderByDescending(c => c.Score).First();
                
                _logger.LogInformation("First part match found: '{City}' in '{Municipality}' with score: {Score:F2} for '{OriginalMunicipality}'",
                    bestCandidate.City.City, bestCandidate.FullMunicipalityName, bestCandidate.Score, incident.Municipality);
                
                // Verify coordinates are in the right region using backup geocoding
                if (await VerifyCoordinatesWithBackup(incident, bestCandidate.City))
                {
                    return (Convert.ToDouble(bestCandidate.City.Latitude), Convert.ToDouble(bestCandidate.City.Longitude));
                }
                else
                {
                    _logger.LogWarning("Coordinate verification failed for match: '{Municipality}'", bestCandidate.FullMunicipalityName);
                }
            }
            
            return null;
        }
        
        private string ExtractFirstMunicipalityPart(string municipalityName)
        {
            if (string.IsNullOrEmpty(municipalityName)) return string.Empty;
            
            // Remove prefix (Δ. or Δημος)
            var cleaned = RemoveMunicipalityPrefix(municipalityName);
            
            // Split by " - " and take the first part
            var parts = cleaned.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return parts[0].Trim();
            }
            
            return cleaned;
        }
        
        private double CalculateFirstPartMatchScore(string searchFirstPart, string datasetMunicipality, string incidentRegion, string datasetRegion)
        {
            var score = 0.0;
            
            // Extract first part from dataset municipality
            var datasetFirstPart = ExtractFirstMunicipalityPart(datasetMunicipality);
            
            // Compare first parts (case insensitive)
            if (string.Equals(searchFirstPart, datasetFirstPart, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.7; // High score for exact first part match
            }
            else if (datasetFirstPart.ToUpperInvariant().Contains(searchFirstPart.ToUpperInvariant()) ||
                     searchFirstPart.ToUpperInvariant().Contains(datasetFirstPart.ToUpperInvariant()))
            {
                score += 0.5; // Medium score for partial match
            }
            else
            {
                return 0; // No match
            }
            
            // Bonus for region match
            if (MatchesRegion(datasetRegion, incidentRegion))
            {
                score += 0.3; // Bonus for region match
            }
            
            return Math.Min(score, 1.0);
        }
        
        private async Task<bool> VerifyCoordinatesWithBackup(FireIncident incident, GreekCityData candidateCity)
        {
            try
            {
                // Create a temporary incident for backup geocoding
                var tempIncident = new FireIncident
                {
                    Region = incident.Region,
                    Municipality = incident.Municipality,
                    Location = incident.Location,
                    Status = incident.Status,
                    Category = incident.Category,
                    StartDate = incident.StartDate,
                    LastUpdate = incident.LastUpdate
                };
                
                // Get coordinates from backup service
                var backupResult = await _backupGeocodingService.GeocodeIncidentAsync(tempIncident);
                
                if (backupResult.IsGeocoded)
                {
                    var candidateLat = Convert.ToDouble(candidateCity.Latitude);
                    var candidateLon = Convert.ToDouble(candidateCity.Longitude);
                    
                    // Calculate distance between coordinates (rough estimation)
                    var distance = CalculateDistance(backupResult.Latitude, backupResult.Longitude, candidateLat, candidateLon);
                    
                    _logger.LogDebug("Distance between backup ({BackupLat}, {BackupLon}) and candidate ({CandidateLat}, {CandidateLon}): {Distance:F2} km",
                        backupResult.Latitude, backupResult.Longitude, candidateLat, candidateLon, distance);
                    
                    // Accept if coordinates are within reasonable distance (50km for Greek municipalities)
                    return distance <= 50.0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not verify coordinates with backup service");
            }
            
            // If verification fails, still accept the match (backup verification is optional)
            return true;
        }
        
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversine formula for calculating distance between two points
            const double earthRadiusKm = 6371.0;
            
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            
            return earthRadiusKm * c;
        }
        
        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private List<GreekCityData> FilterValidEntries(List<GreekCityData> entries, string region)
        {
            var validEntries = entries.Where(e => e.Has_Geolocation && 
                                                 e.Latitude != null && 
                                                 e.Longitude != null).ToList();
            
            _logger.LogDebug("FilterValidEntries: Input {InputCount} entries, {ValidCount} with coordinates", 
                entries.Count, validEntries.Count);
            
            if (!string.IsNullOrEmpty(region) && validEntries.Count > 1)
            {
                // Try to match region for accuracy
                var regionMatches = validEntries.Where(e => 
                    MatchesRegion(e.Region, region)).ToList();
                
                if (regionMatches.Any())
                {
                    _logger.LogDebug("Filtered entries by region match: {Count} -> {FilteredCount}. " +
                        "Incident region: '{IncidentRegion}', Sample dataset region: '{DatasetRegion}'", 
                        validEntries.Count, regionMatches.Count, region, regionMatches.First().Region);
                    return regionMatches;
                }
                else
                {
                    _logger.LogWarning("No region matches found for incident region: '{IncidentRegion}'. " +
                        "Sample dataset regions: '{SampleRegions}'. Returning all valid entries.",
                        region, string.Join(", ", validEntries.Take(3).Select(e => e.Region)));
                    
                    // Return all valid entries even if region doesn't match exactly
                    // This is more forgiving and will allow matches even with region mismatches
                    // Will be improved 
                    return validEntries;
                }
            }
            
            return validEntries;
        }

        private GreekCityData SelectBestEntry(List<GreekCityData> entries)
        {
            if (entries.Count == 1) return entries[0];
            
            // Prefer entries with higher population from the data file. Not the best.
            var withPopulation = entries.Where(e => e.Population > 0).ToList();
            if (withPopulation.Any())
            {
                return withPopulation.OrderByDescending(e => e.Population).First();
            }
            
            // Fall back to first entry
            return entries.First();
        }

        private bool MatchesRegion(string datasetRegion, string incidentRegion)
        {
            if (string.IsNullOrEmpty(datasetRegion) || string.IsNullOrEmpty(incidentRegion))
                return false;
            
            var normalizedDataset = NormalizeRegionName(datasetRegion);
            var normalizedIncident = NormalizeRegionName(incidentRegion);
            
            return normalizedDataset.Contains(normalizedIncident, StringComparison.OrdinalIgnoreCase) ||
                   normalizedIncident.Contains(normalizedDataset, StringComparison.OrdinalIgnoreCase);
        }

        private string NormalizeRegionName(string region)
        {
            if (string.IsNullOrEmpty(region)) return string.Empty;
            
            return region.Replace("ΠΕΡΙΦΕΡΕΙΑ ", "")
                        .Replace("Περιφερεια ", "")
                        .Trim()
                        .ToUpperInvariant();
        }

        private string NormalizeMunicipalityName(string municipality)
        {
            if (string.IsNullOrEmpty(municipality)) return string.Empty;
            
            return municipality.Trim()
                             .Replace("  ", " ")
                             .ToUpperInvariant();
        }

        private string RemoveMunicipalityPrefix(string municipality)
        {
            if (string.IsNullOrEmpty(municipality)) return string.Empty;
            
            var prefixes = new[] { "ΔΗΜΟΣ ", "Δ. ", "ΔΗΜΟΣ ", "Δημος " };
            
            foreach (var prefix in prefixes)
            {
                if (municipality.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return municipality.Substring(prefix.Length).Trim();
                }
            }
            
            return municipality;
        }

        private List<string> ExtractSearchTerms(string municipality)
        {
            var terms = new List<string>();
            
            if (string.IsNullOrEmpty(municipality)) return terms;
            
            // Remove prefixes and split by common separators
            var cleaned = RemoveMunicipalityPrefix(municipality);
            var parts = cleaned.Split(new[] { " - ", " ", "-" }, StringSplitOptions.RemoveEmptyEntries);
            
            terms.AddRange(parts.Select(p => p.Trim().ToUpperInvariant()));
            
            return terms;
        }

        private double CalculateSimilarityScore(List<string> searchTerms, string target)
        {
            if (!searchTerms.Any() || string.IsNullOrEmpty(target)) return 0;
            
            var targetUpper = target.ToUpperInvariant();
            var matchCount = 0;
            
            foreach (var term in searchTerms)
            {
                if (targetUpper.Contains(term))
                {
                    matchCount++;
                }
            }
            
            return (double)matchCount / searchTerms.Count;
        }

        private string GetLocationKey(FireIncident incident)
        {
            if (!string.IsNullOrEmpty(incident.Municipality) && !string.IsNullOrEmpty(incident.Region))
            {
                return $"{incident.Region}-{incident.Municipality}".ToLowerInvariant();
            }

            if (!string.IsNullOrEmpty(incident.Region))
            {
                return incident.Region.ToLowerInvariant();
            }

            return "unknown";
        }

        private (double Lat, double Lon) GetOffsetCoordinates(string locationKey, double lat, double lon)
        {
            if (!_activeIncidentCoordinates.ContainsKey(locationKey))
            {
                _activeIncidentCoordinates[locationKey] = new List<(double Lat, double Lon)>();
            }

            var existingCoordinates = _activeIncidentCoordinates[locationKey];

            if (existingCoordinates.Count == 0)
            {
                existingCoordinates.Add((lat, lon));
                return (lat, lon);
            }

            double offsetDistance = 0.01 * existingCoordinates.Count; // about 1km per incident
            double angle = Math.PI * 2 * existingCoordinates.Count / 8; // Distribute in a circle

            double offsetLat = lat + offsetDistance * Math.Sin(angle);
            double offsetLon = lon + offsetDistance * Math.Cos(angle);

            existingCoordinates.Add((offsetLat, offsetLon));

            return (offsetLat, offsetLon);
        }
    }

    // Data model for the Greek cities dataset
    public class GreekCityData
    {
        public string Country { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Sub_Region { get; set; } = string.Empty;
        public string Municipality { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public int Population { get; set; }
        
        // Use JsonElement to handle both string and number formats
        [JsonPropertyName("latitude")]
        public JsonElement LatitudeElement { get; set; }
        
        [JsonPropertyName("longitude")]
        public JsonElement LongitudeElement { get; set; }
        
        public bool Has_Geolocation { get; set; }
        public string Settlement_Type { get; set; } = string.Empty;
        
        // Helper methods for parsing
        [JsonIgnore]
        public string? Latitude => GetCoordinateAsString(LatitudeElement);
        
        [JsonIgnore]
        public string? Longitude => GetCoordinateAsString(LongitudeElement);
        
        private static string? GetCoordinateAsString(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                return null;
            
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();
            
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            
            return null;
        }
    }
}
