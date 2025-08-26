using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Globalization;

namespace FireIncidents.Services
{
    public class Warning112Service
    {
        private readonly ILogger<Warning112Service> _logger;
        private readonly TwitterScraperService _twitterScraperService;
        private readonly GeocodingService _geocodingService;
        private readonly IMemoryCache _cache;
        
        // Cache keys
        private const string ACTIVE_WARNINGS_CACHE_KEY = "active_112_warnings";
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
                if (_cache.TryGetValue(ACTIVE_WARNINGS_CACHE_KEY, out List<GeocodedWarning112>? cachedWarnings) && cachedWarnings != null)
                {
                    var activeScrapedWarnings = cachedWarnings.Where(w => w.IsActive).ToList();
                    allWarnings.AddRange(activeScrapedWarnings);
                    _logger.LogInformation($"Found {activeScrapedWarnings.Count} cached scraped warnings");
                }
                else
                {
                    // Scrape fresh warnings (use default 7 days for active warnings)
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

        public async Task<List<GeocodedWarning112>> ScrapeAndProcessWarningsAsync(int? daysBack = null)
        {
            try
            {
                var effectiveDaysBack = daysBack ?? 7;
                _logger.LogInformation($"Scraping and processing 112 warnings (last {effectiveDaysBack} days)...");
                
                // Scrape warnings from Twitter with flexible date range
                var rawWarnings = await _twitterScraperService.ScrapeWarningsAsync(effectiveDaysBack);
                
                if (!rawWarnings.Any())
                {
                    _logger.LogWarning($"No warnings found from Twitter scraping in the last {effectiveDaysBack} days");
                    return new List<GeocodedWarning112>();
                }
                
                _logger.LogInformation($"Found {rawWarnings.Count} raw warnings from Twitter");
                
                // Pair English and Greek versions of the same warning
                var pairedWarnings = PairWarningsByContent(rawWarnings);
                
                // Process and geocode the warnings
                var geocodedWarnings = new List<GeocodedWarning112>();
                
                foreach (var warning in pairedWarnings)
                {
                    try
                    {
                        var geocodedWarning = await ProcessWarningAsync(warning);
                        if (geocodedWarning?.HasGeocodedLocations == true)
                        {
                            geocodedWarnings.Add(geocodedWarning);
                        }
                        else
                        {
                            _logger.LogWarning($"Warning could not be processed or geocoded: {warning.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing warning: {warning.Id}");
                    }
                }
                
                _logger.LogInformation($"Successfully processed {geocodedWarnings.Count} geocoded warnings from {rawWarnings.Count} raw warnings");
                
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
                        Locations = englishWarning.Locations,
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
                return warnings;
            }
        }

        private bool AreWarningsSimilar(Warning112 warning1, Warning112 warning2)
        {
            // 112Greece posts Greek and English versions of the same incident within minutes
            // Check if warnings are about the same incident based on:
            // 1. Similar time (within 30 minutes) - 112Greece posts both versions quickly
            // 2. Similar location count (should be roughly the same)
            // 3. Both should be valid 112 activation tweets
            
            var timeDiff = Math.Abs((warning1.TweetDate - warning2.TweetDate).TotalMinutes);
            if (timeDiff > 30)
            {
                _logger.LogDebug($"Time difference too large: {timeDiff:F1} minutes");
                return false;
            }
                
            // Allow some flexibility in location count (hashtags might differ slightly)
            if (Math.Abs(warning1.Locations.Count - warning2.Locations.Count) > 3)
            {
                _logger.LogDebug($"Location count difference too large: {warning1.Locations.Count} vs {warning2.Locations.Count}");
                return false;
            }

            _logger.LogDebug($"Warnings seem similar: {timeDiff:F1} min apart, {warning1.Locations.Count} vs {warning2.Locations.Count} locations");
            return true;
        }

        private async Task<GeocodedWarning112> ProcessWarningAsync(Warning112 warning)
        {
            try
            {
                _logger.LogInformation($"Processing warning {warning.Id}");
                
                // STRATEGY: 112Greece posts 2 tweets per incident (Greek + English)
                // 1. Use Greek content for location extraction (more accurate Greek location names)
                // 2. Keep English content for display (better UX for international users)
                // 3. If only one language available, use what we have
                
                var hasGreek = !string.IsNullOrEmpty(warning.GreekContent);
                var hasEnglish = !string.IsNullOrEmpty(warning.EnglishContent);
                
                if (!hasGreek && !hasEnglish)
                {
                    _logger.LogWarning($"Warning {warning.Id} has no content to process");
                    return null;
                }

                // For location extraction, prefer Greek (more accurate location names)
                var contentForLocationExtraction = hasGreek ? warning.GreekContent : warning.EnglishContent;
                var extractionLanguage = hasGreek ? "Greek" : "English";
                
                _logger.LogInformation($"🔍 Using {extractionLanguage} content for location extraction, " +
                                     $"English: {(hasEnglish ? "available" : "missing")}, " +
                                     $"Greek: {(hasGreek ? "available" : "missing")}");

                // Parse the warning to extract evacuation patterns
                var evacuationInfo = ParseEvacuationPattern(contentForLocationExtraction);
                
                if (!evacuationInfo.DangerZones.Any() && !evacuationInfo.FireLocations.Any())
                {
                    _logger.LogWarning($"No danger zones or fire locations found in warning {warning.Id}");
                    return null;
                }

                // Create geocoded warning
            var geocodedWarning = new GeocodedWarning112
            {
                Id = warning.Id,
                EnglishContent = warning.EnglishContent,
                GreekContent = warning.GreekContent,
                    Locations = evacuationInfo.DangerZones.Concat(evacuationInfo.FireLocations).ToList(),
                TweetDate = warning.TweetDate,
                SourceUrl = warning.SourceUrl,
                CreatedAt = warning.CreatedAt
            };
            
                // Geocode danger zones and fire locations (these are what we show on map)
                var locationsToGeocode = evacuationInfo.DangerZones.Concat(evacuationInfo.FireLocations).Distinct().ToList();
                
                var geocodingTasks = locationsToGeocode.Select(async locationName =>
                {
                    try
                    {
                        _logger.LogInformation($"Starting geocoding for: {locationName}");
                        var geocodedLocation = await GeocodeLocationAsync(locationName, evacuationInfo.RegionalContext);
                        
                        if (geocodedLocation != null)
                        {
                            _logger.LogInformation($"✅ Successfully geocoded '{locationName}' to {geocodedLocation.Latitude:F6}, {geocodedLocation.Longitude:F6}");
                            return geocodedLocation;
                        }
                        else
                        {
                            _logger.LogWarning($"❌ Failed to geocode location: {locationName}");
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"💥 Error geocoding location '{locationName}'");
                        return null;
                    }
                }).ToArray();
                
                // Wait for all geocoding attempts to complete
                var geocodingResults = await Task.WhenAll(geocodingTasks);
                
                // Add successful results to warning
                foreach (var result in geocodingResults.Where(r => r != null))
                {
                    geocodedWarning.GeocodedLocations.Add(result);
                }
                
                // Log evacuation info for debugging
                _logger.LogInformation($"Warning {warning.Id} - Danger zones: [{string.Join(", ", evacuationInfo.DangerZones)}], " +
                                     $"Safe zones: [{string.Join(", ", evacuationInfo.SafeZones)}], " +
                                     $"Fire locations: [{string.Join(", ", evacuationInfo.FireLocations)}], " +
                                     $"Regional context: {evacuationInfo.RegionalContext}");
                
                _logger.LogInformation($"✅ Successfully geocoded {geocodedWarning.GeocodedLocations.Count} out of {locationsToGeocode.Count} locations for warning {warning.Id}");
                
                // For emergency situations, be more tolerant of partial failures
                if (geocodedWarning.GeocodedLocations.Any())
                {
                    var approximateCount = geocodedWarning.GeocodedLocations.Count(l => l.GeocodingSource.Contains("approximation"));
                    var exactCount = geocodedWarning.GeocodedLocations.Count - approximateCount;
                    
                    _logger.LogInformation($"✅ Created warning with {exactCount} exact + {approximateCount} approximate locations");
                return geocodedWarning;
            }
                else
                {
                    _logger.LogError($"❌ No locations were successfully geocoded for warning {warning.Id}");
                    
                    // As a last resort for emergencies, try to geocode the safe zone for evacuation guidance
                    if (evacuationInfo.SafeZones.Any())
                    {
                        _logger.LogInformation($"🚨 EMERGENCY: Attempting to geocode safe zones for evacuation guidance");
                        
                        foreach (var safeZone in evacuationInfo.SafeZones)
        {
            try
            {
                                var safeLocation = await GeocodeLocationAsync(safeZone, evacuationInfo.RegionalContext);
                                if (safeLocation != null)
                                {
                                    // Mark this as a safe zone location for different map treatment
                                    safeLocation.GeocodingSource = $"SAFE ZONE: {safeLocation.GeocodingSource}";
                                    geocodedWarning.GeocodedLocations.Add(safeLocation);
                                    _logger.LogInformation($"🚨 EMERGENCY: Added safe zone '{safeZone}' for evacuation guidance");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to geocode safe zone '{safeZone}'");
                            }
                        }
                        
                        if (geocodedWarning.GeocodedLocations.Any())
                        {
                            _logger.LogInformation($"🚨 EMERGENCY: Created warning with {geocodedWarning.GeocodedLocations.Count} safe zone locations for evacuation guidance");
                            return geocodedWarning;
                        }
                    }
                    
                return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing warning {warning.Id}");
                return null;
            }
        }

        private EvacuationInfo ParseEvacuationPattern(string tweetContent)
        {
            var info = new EvacuationInfo();
            
            try
            {
                _logger.LogInformation($"Parsing evacuation pattern from: {tweetContent.Substring(0, Math.Min(100, tweetContent.Length))}...");
                
                // Determine language
                var isGreek = IsGreekTweet(tweetContent);
                
                // Extract regional context first
                info.RegionalContext = ExtractRegionalContext(tweetContent, isGreek);
                
                // Always try to parse fire location first
                var hasFireLocation = ParseFireLocationPattern(tweetContent, isGreek, info);
                if (hasFireLocation)
                {
                    _logger.LogInformation("Found fire location pattern");
                }
                
                // Then try evacuation instructions
                var hasEvacuationInstruction = ParseEvacuationInstruction(tweetContent, isGreek, info);
                if (hasEvacuationInstruction)
                {
                    _logger.LogInformation("Found evacuation instruction pattern");
                }
                
                // Handle special case: "If you are in the area" refers to fire location
                if (hasEvacuationInstruction && !info.DangerZones.Any() && info.FireLocations.Any())
                {
                    var areaPattern = isGreek ? @"Αν βρίσκεστε στ[ηιοαί]*?\s*περιοχ[ηέάές]*?\s" : @"If you are in(?:\s+the)?\s+area\s";
                    
                    if (Regex.IsMatch(tweetContent, areaPattern, RegexOptions.IgnoreCase))
                    {
                        _logger.LogInformation("'The area' detected - using fire locations as danger zones");
                        info.DangerZones = new List<string>(info.FireLocations);
                        info.FireLocations.Clear(); // Move fire locations to danger zones
                    }
                }
                
                // If still no specific patterns found, use fallback
                if (!hasFireLocation && !hasEvacuationInstruction)
                {
                    _logger.LogWarning("No recognized pattern found, using fallback hashtag extraction");
                    info.DangerZones = ExtractHashtagLocations(tweetContent);
                }
                
                // Filter out regional units from danger zones
                info.DangerZones = FilterOutRegionalUnits(info.DangerZones);
                info.SafeZones = FilterOutRegionalUnits(info.SafeZones);
                info.FireLocations = FilterOutRegionalUnits(info.FireLocations);
                
                _logger.LogInformation($"Final parsing result - Danger: [{string.Join(", ", info.DangerZones)}], " +
                                     $"Safe: [{string.Join(", ", info.SafeZones)}], " +
                                     $"Fire: [{string.Join(", ", info.FireLocations)}], " +
                                     $"Regional: {info.RegionalContext}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing evacuation pattern");
            }
            
            return info;
        }

        private bool ParseEvacuationInstruction(string tweetContent, bool isGreek, EvacuationInfo info)
            {
                try
                {
                // Define evacuation patterns
                var patterns = isGreek ? new[]
                {
                    @"Αν βρίσκεστε στ[ηιοαί]*?\s*περιοχ[ηέάές]*?\s*(.*?)\s*απομακρυνθείτε\s*(?:μέσω\s*(.*?)\s*)?προς\s*(.*?)(?:\s|$|‼️|⚠️)",
                    @"Αν βρίσκεστε στ[ηιοαί]*?\s*(.*?)\s*απομακρυνθείτε\s*(?:μέσω\s*(.*?)\s*)?προς\s*(.*?)(?:\s|$|‼️|⚠️)",
                    @"απομακρυνθείτε\s*(?:μέσω\s*(.*?)\s*)?προς\s*(.*?)(?:\s|$|‼️|⚠️)"
                } : new[]
                {
                    @"If you are in(?:\s+the)?\s+(?:area\s+)?(.*?)\s+move away\s+(?:via\s+(.*?)\s+)?to\s+(.*?)(?:\s|$|‼️|⚠️)",
                    @"If you are in\s+(.*?)\s+move away\s+(?:via\s+(.*?)\s+)?to\s+(.*?)(?:\s|$|‼️|⚠️)",
                    @"move away\s+(?:via\s+(.*?)\s+)?to\s+(.*?)(?:\s|$|‼️|⚠️)"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(tweetContent, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success)
                    {
                        _logger.LogInformation($"Matched evacuation pattern: {pattern}");
                        
                        // Extract parts based on pattern structure
                        if (isGreek)
                        {
                            // For Greek: Group 1 = danger areas, Group 2 = via route (optional), Group 3 = safe zone
                            if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                            {
                                info.DangerZones = ExtractHashtagLocations(match.Groups[1].Value);
                            }
                            if (match.Groups.Count > 3 && !string.IsNullOrEmpty(match.Groups[3].Value))
                            {
                                info.SafeZones = ExtractHashtagLocations(match.Groups[3].Value);
                            }
                            if (match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value))
                            {
                                info.RouteLocations = ExtractHashtagLocations(match.Groups[2].Value);
                        }
                    }
                    else
                    {
                            // For English: Group 1 = danger areas, Group 2 = via route (optional), Group 3 = safe zone
                            if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                            {
                                info.DangerZones = ExtractHashtagLocations(match.Groups[1].Value);
                            }
                            if (match.Groups.Count > 3 && !string.IsNullOrEmpty(match.Groups[3].Value))
                            {
                                info.SafeZones = ExtractHashtagLocations(match.Groups[3].Value);
                            }
                            if (match.Groups.Count > 2 && !string.IsNullOrEmpty(match.Groups[2].Value))
                            {
                                info.RouteLocations = ExtractHashtagLocations(match.Groups[2].Value);
                            }
                        }
                        
                        _logger.LogInformation($"Extracted - Danger: [{string.Join(", ", info.DangerZones)}], " +
                                             $"Safe: [{string.Join(", ", info.SafeZones)}], " +
                                             $"Route: [{string.Join(", ", info.RouteLocations)}]");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing evacuation instruction");
            }
            
            return false;
        }

        private bool ParseFireLocationPattern(string tweetContent, bool isGreek, EvacuationInfo info)
        {
            try
            {
                var firePatterns = isGreek ? new[]
                {
                    @"Δασική πυρκαγιά στην περιοχή\s*(.*?)(?:\s+(?:της\s+Περιφερειακής|‼️|⚠️|$))",
                    @"πυρκαγιά.*?στ[ηιοαί]*?\s*περιοχ[ηέάές]*?\s*(.*?)(?:\s+(?:της\s+Περιφερειακής|‼️|⚠️|$))",
                    @"πυρκαγιά.*?(#[Α-Ωα-ωάέήίόύώA-Za-z0-9_]+)"
                } : new[]
                {
                    @"Wildfire in\s*(.*?)(?:\s+(?:of\s+the\s+regional|‼️|⚠️|$))",
                    @"Fire in\s*(.*?)(?:\s+(?:of\s+the\s+regional|‼️|⚠️|$))",
                    @"(?:Wild)?fire.*?in your area"
                };

                foreach (var pattern in firePatterns)
                {
                    var match = Regex.Match(tweetContent, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        _logger.LogInformation($"Matched fire pattern: {pattern}");
                        _logger.LogInformation($"Fire pattern captured: '{match.Groups[1]?.Value}'");
                        
                        if (match.Groups.Count > 1 && !string.IsNullOrEmpty(match.Groups[1].Value))
                        {
                            var capturedText = match.Groups[1].Value.Trim();
                            info.FireLocations = ExtractHashtagLocations(capturedText);
                            _logger.LogInformation($"Fire locations from captured text '{capturedText}': [{string.Join(", ", info.FireLocations)}]");
                        }
                        else if (pattern.Contains("in your area"))
                        {
                            // For "fire in your area" pattern, look for other hashtags in the tweet
                            info.FireLocations = ExtractHashtagLocations(tweetContent);
                            _logger.LogInformation($"Fire locations from full tweet (area pattern): [{string.Join(", ", info.FireLocations)}]");
                        }
                        
                        _logger.LogInformation($"Final extracted fire locations: [{string.Join(", ", info.FireLocations)}]");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing fire location pattern");
            }
            
            return false;
        }

        private List<string> ExtractHashtagLocations(string text)
        {
            var locations = new List<string>();
            
            try
            {
                // Pattern for hashtags with Greek and Latin characters
                var pattern = @"#([Α-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏA-Za-z0-9_]+)";
                
                var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var hashtag = match.Groups[1].Value;
                    
                    // Filter out common non-location hashtags
                    if (IsLocationHashtag(hashtag))
                    {
                        locations.Add(hashtag);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting hashtag locations");
            }
            
            return locations.Distinct().ToList();
        }

        private bool IsLocationHashtag(string hashtag)
        {
            // Filter out common non-location hashtags
            var nonLocationHashtags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "112", "Fire", "Wildfire", "Emergency", "Evacuation", "Alert", "Warning",
                "Φωτιά", "Εκκένωση", "Κίνδυνος", "Προειδοποίηση", "Δασική", "Πυρκαγιά"
            };
            
            return !nonLocationHashtags.Contains(hashtag) && hashtag.Length > 1;
        }

        private string ExtractRegionalContext(string tweetContent, bool isGreek)
        {
            try
            {
                var patterns = isGreek ? new[]
                {
                    @"της Περιφερειακής Ενότητας #([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+)",
                    @"Περιφερειακής Ενότητας #([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+)",
                    @"#([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+) #([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+ίας|[A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+ανίας)(?:\s|$)" // Pattern like #Location #RegionalUnit
                } : new[]
                {
                    @"of the regional unit of #([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+)",
                    @"regional unit of #([A-Za-zΑ-Ωα-ωάέήίόύώΐΰΆΈΉΊΌΎΏ_]+)",
                    @"#([A-Za-z_]+) #([A-Za-z_]+(?:ia|ania|is|os))(?:\s|$)" // Pattern like #Location #RegionalUnit
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(tweetContent, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var regionalUnit = match.Groups[match.Groups.Count - 1].Value; // Last capture group
                        _logger.LogInformation($"✅ Extracted regional context: {regionalUnit}");
                        return regionalUnit;
                    }
                }

                _logger.LogInformation("❌ No regional context found");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting regional context");
                return null;
            }
        }

        private List<string> FilterOutRegionalUnits(List<string> locations)
        {
            var filtered = new List<string>();
            
            // Use the same regional unit mappings from GetRegionalUnitVariants
            // to dynamically determine what should be filtered out
            foreach (var location in locations)
            {
                var regionalVariants = GetRegionalUnitVariants(location);
                
                // If this location expands to multiple regional variants (meaning it's a regional unit),
                // and it's not just returning itself, then it's likely a regional unit to filter
                if (regionalVariants.Count > 1 && !regionalVariants.All(v => v.Equals(location, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation($"Filtered out regional unit: {location} (has {regionalVariants.Count} variants)");
                }
                else
                {
                    filtered.Add(location);
                }
            }
            
            return filtered;
        }

        private bool IsGreekTweet(string tweetContent)
        {
            return tweetContent.Any(c => c >= '\u0370' && c <= '\u03FF') || 
                   tweetContent.Contains("Ενεργοποίηση") ||
                   tweetContent.Contains("απομακρυνθείτε");
        }

        private async Task<GeocodedWarning112.WarningLocation> GeocodeLocationAsync(string locationName, string regionalContext = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationName))
                    return null;

                _logger.LogInformation($"Geocoding location: '{locationName}'" + 
                    (regionalContext != null ? $" in regional context: {regionalContext}" : ""));
                
                // Use multiple geocoding strategies
                var result = await TryGeocodeWithMultipleStrategies(locationName, regionalContext);
                
                if (result != null)
                {
                    _logger.LogInformation($"Successfully geocoded '{locationName}' to {result.Latitude:F6}, {result.Longitude:F6}");
                    return result;
                }
                
                _logger.LogWarning($"All geocoding strategies failed for: '{locationName}'");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error geocoding location '{locationName}'");
                return null;
            }
        }

        private async Task<GeocodedWarning112.WarningLocation> TryGeocodeWithMultipleStrategies(string locationName, string regionalContext = null)
        {
            var strategies = new List<(string Description, Func<Task<GeocodedWarning112.WarningLocation>> Strategy)>();

            // If we have regional context, prioritize searches within that context
            if (!string.IsNullOrEmpty(regionalContext))
            {
                var regionalVariants = GetRegionalUnitVariants(regionalContext);
                
                foreach (var variant in regionalVariants)
                {
                    strategies.Add(($"Context: {locationName}, {variant}, Greece", 
                        () => GeocodeWithNominatim($"{locationName}, {variant}, Greece", $"Context search: {variant}")));
                    
                    strategies.Add(($"Context village: {locationName} village, {variant}, Greece", 
                        () => GeocodeWithNominatim($"{locationName} village, {variant}, Greece", $"Context village: {variant}")));
                    
                    strategies.Add(($"Context town: {locationName} town, {variant}, Greece", 
                        () => GeocodeWithNominatim($"{locationName} town, {variant}, Greece", $"Context town: {variant}")));
                        
                    strategies.Add(($"Context settlement: {locationName} settlement, {variant}, Greece", 
                        () => GeocodeWithNominatim($"{locationName} settlement, {variant}, Greece", $"Context settlement: {variant}")));
                }
            }

            // Add general strategies with more variations
            strategies.AddRange(new (string, Func<Task<GeocodedWarning112.WarningLocation>>)[]
            {
                ("Direct search", () => GeocodeWithNominatim(locationName, "Direct search")),
                ("Greece context", () => GeocodeWithNominatim($"{locationName}, Greece", "Greece context")),
                ("Village search", () => GeocodeWithNominatim($"{locationName} village, Greece", "Village search")),
                ("Town search", () => GeocodeWithNominatim($"{locationName} town, Greece", "Town search")),
                ("Settlement search", () => GeocodeWithNominatim($"{locationName} settlement, Greece", "Settlement search")),
                ("Municipality search", () => GeocodeWithNominatim($"{locationName} municipality, Greece", "Municipality search")),
                ("Hamlet search", () => GeocodeWithNominatim($"{locationName} hamlet, Greece", "Hamlet search")),
                ("Neighbourhood search", () => GeocodeWithNominatim($"{locationName} neighbourhood, Greece", "Neighbourhood search")),
                ("Greek village", () => GeocodeWithNominatim($"{locationName} χωριό, Ελλάδα", "Greek village search")),
                ("FireIncident fallback", () => GeocodeUsingFireIncidentService(locationName))
            });
            
            // If we have regional context but no results, try approximate location
            if (!string.IsNullOrEmpty(regionalContext))
            {
                strategies.Add(("Regional center approximation", () => GetRegionalCenterApproximation(locationName, regionalContext)));
            }

            GeocodedWarning112.WarningLocation bestResult = null;

            foreach (var (description, strategy) in strategies)
            {
                try
                {
                    _logger.LogInformation($"🔍 Trying: {description}");
                    var result = await strategy();
                    
                    // Validate coordinates are in Greece (rough bounds check)
                    if (result != null && result.Latitude != 0 && result.Longitude != 0 && 
                        result.Latitude >= 34.0 && result.Latitude <= 42.0 && 
                        result.Longitude >= 19.0 && result.Longitude <= 30.0)
                    {
                        _logger.LogInformation($"✅ SUCCESS: {description} → {result.Latitude:F6}, {result.Longitude:F6}");
                        
                        // For context searches, prefer them over general searches
                        if (description.Contains("Context"))
                        {
                            _logger.LogInformation($"🎯 Using context result immediately: {description}");
                            return result;
                        }
                        
                        // Store first valid result as backup
                        if (bestResult == null)
                        {
                            bestResult = result;
                            _logger.LogInformation($"📌 Storing as best result: {description}");
                        }
                    }
                    else if (result != null)
                    {
                        _logger.LogWarning($"❌ Result outside Greece bounds: {description} → {result.Latitude:F6}, {result.Longitude:F6}");
                    }
            }
            catch (Exception ex)
            {
                    _logger.LogError(ex, $"💥 Strategy failed: {description}");
                }
            }
            
            return bestResult;
        }

        private async Task<GeocodedWarning112.WarningLocation> GeocodeWithNominatim(string query, string sourceDescription = "Nominatim")
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "FireIncidents/1.0 (emergency-warnings)");
                
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://nominatim.openstreetmap.org/search?format=json&addressdetails=1&limit=3&countrycodes=gr&q={encodedQuery}";
                
                _logger.LogDebug($"🌐 Nominatim query: {query}");
                
                var response = await httpClient.GetStringAsync(url);
                var results = JsonSerializer.Deserialize<JsonElement[]>(response);
                
                if (results != null && results.Length > 0)
                {
                    var bestResult = results[0];
                    
                    if (bestResult.TryGetProperty("lat", out var latElement) && 
                        bestResult.TryGetProperty("lon", out var lonElement) &&
                        double.TryParse(latElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                        double.TryParse(lonElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                    {
                        string municipality = "";
                        string region = "Greece";
                        
                        if (bestResult.TryGetProperty("address", out var addressElement))
                        {
                            if (addressElement.TryGetProperty("municipality", out var munElement))
                                municipality = munElement.GetString() ?? "";
                            else if (addressElement.TryGetProperty("city", out var cityElement))
                                municipality = cityElement.GetString() ?? "";
                            else if (addressElement.TryGetProperty("town", out var townElement))
                                municipality = townElement.GetString() ?? "";
                            
                            if (addressElement.TryGetProperty("state", out var stateElement))
                                region = stateElement.GetString() ?? "Greece";
                        }
                        
                        return new GeocodedWarning112.WarningLocation
                        {
                            LocationName = query.Split(',')[0].Trim(),
                            Latitude = lat,
                            Longitude = lon,
                            Municipality = municipality,
                            Region = region,
                            GeocodingSource = sourceDescription
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Nominatim search failed for '{query}'");
            }
            
            return null;
        }

        private async Task<GeocodedWarning112.WarningLocation> GeocodeUsingFireIncidentService(string locationName)
        {
            try
            {
                _logger.LogDebug($"🔥 Trying FireIncident service for: {locationName}");
                
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
                
                // Reject default/fallback coordinates for life-saving accuracy
                var isDefaultCoordinates = (Math.Abs(geocodedIncident.Latitude - 38.2) < 0.1 && Math.Abs(geocodedIncident.Longitude - 23.8) < 0.1) ||
                                          (geocodedIncident.Latitude == 0 && geocodedIncident.Longitude == 0);
                
                if (geocodedIncident.IsGeocoded && !isDefaultCoordinates)
                {
                    _logger.LogInformation($"🔥 FireIncident service found accurate coordinates: {geocodedIncident.Latitude:F6}, {geocodedIncident.Longitude:F6}");
                    return new GeocodedWarning112.WarningLocation
                    {
                        LocationName = locationName,
                        Latitude = geocodedIncident.Latitude,
                        Longitude = geocodedIncident.Longitude,
                        Municipality = geocodedIncident.Municipality ?? "",
                        Region = geocodedIncident.Region ?? "Greece",
                        GeocodingSource = $"FireIncident service: {geocodedIncident.GeocodingSource}"
                    };
                }
                else if (isDefaultCoordinates)
                {
                    _logger.LogWarning($"🔥 FireIncident service returned default coordinates for '{locationName}' - rejecting for safety");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"FireIncident service failed for '{locationName}'");
            }
            
            return null;
        }

        private async Task<GeocodedWarning112.WarningLocation> GetRegionalCenterApproximation(string locationName, string regionalContext)
        {
            try
            {
                _logger.LogInformation($"🎯 Attempting regional center approximation for '{locationName}' in {regionalContext}");
                
                // Try to geocode the regional unit center and use that as an approximation
                var regionalVariants = GetRegionalUnitVariants(regionalContext);
                
                foreach (var variant in regionalVariants)
                {
                    var result = await GeocodeWithNominatim($"{variant}, Greece", $"Regional center: {variant}");
                    if (result != null && result.Latitude >= 34.0 && result.Latitude <= 42.0 && 
                        result.Longitude >= 19.0 && result.Longitude <= 30.0)
                    {
                        _logger.LogInformation($"🎯 Using regional center approximation: {variant} → {result.Latitude:F6}, {result.Longitude:F6}");
                        
                        // Create approximation with clear indication
                        return new GeocodedWarning112.WarningLocation
                        {
                            LocationName = locationName,
                            Latitude = result.Latitude,
                            Longitude = result.Longitude,
                            Municipality = result.Municipality,
                            Region = result.Region,
                            GeocodingSource = $"Regional approximation: {locationName} ≈ {variant} center (EMERGENCY FALLBACK)"
                        };
                    }
                }
                
                _logger.LogWarning($"🎯 Regional center approximation failed for {regionalContext}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in regional center approximation for '{locationName}'");
                return null;
            }
        }

        private List<string> GetRegionalUnitVariants(string regionalUnit)
        {
            var variants = new List<string>();
            
            // Regional unit translations (Greek ↔ English)
            var regionalUnitMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // East Macedonia & Thrace
                { "Evros",        new[] { "Evros", "Έβρου", "Έβρος" } },
                { "Έβρου",        new[] { "Evros", "Έβρου", "Έβρος" } },
                { "Rhodope",      new[] { "Rhodope", "Rodopi", "Ροδόπης", "Ροδόπη" } },
                { "Ροδόπης",      new[] { "Rhodope", "Rodopi", "Ροδόπης", "Ροδόπη" } },
                { "Xanthi",       new[] { "Xanthi", "Ξάνθης", "Ξάνθη" } },
                { "Ξάνθης",       new[] { "Xanthi", "Ξάνθης", "Ξάνθη" } },
                { "Drama",        new[] { "Drama", "Δράμας", "Δράμα" } },
                { "Δράμας",       new[] { "Drama", "Δράμας", "Δράμα" } },
                { "Kavala",       new[] { "Kavala", "Καβάλας" } },
                { "Καβάλας",      new[] { "Kavala", "Καβάλας" } },
                { "Thasos",       new[] { "Thasos", "Thassos", "Θάσου", "Θάσος" } },
                { "Θάσου",        new[] { "Thasos", "Thassos", "Θάσου", "Θάσος" } },

                // Central Macedonia
                { "Thessaloniki", new[] { "Thessaloniki", "Salonika", "Θεσσαλονίκης", "Θεσσαλονίκη" } },
                { "Θεσσαλονίκης", new[] { "Thessaloniki", "Salonika", "Θεσσαλονίκης", "Θεσσαλονίκη" } },
                { "Imathia",      new[] { "Imathia", "Ημαθίας" } },
                { "Ημαθίας",      new[] { "Imathia", "Ημαθίας" } },
                { "Pella",        new[] { "Pella", "Πέλλας" } },
                { "Πέλλας",       new[] { "Pella", "Πέλλας" } },
                { "Kilkis",       new[] { "Kilkis", "Κιλκίς" } },
                { "Κιλκίς",       new[] { "Kilkis", "Κιλκίς" } },
                { "Pieria",       new[] { "Pieria", "Πιερίας" } },
                { "Πιερίας",      new[] { "Pieria", "Πιερίας" } },
                { "Serres",       new[] { "Serres", "Σερρών" } },
                { "Σερρών",       new[] { "Serres", "Σερρών" } },
                { "Chalkidiki",   new[] { "Chalkidiki", "Halkidiki", "Χαλκιδικής" } },
                { "Χαλκιδικής",   new[] { "Chalkidiki", "Halkidiki", "Χαλκιδικής" } },

                // West Macedonia
                { "Kozani",       new[] { "Kozani", "Κοζάνης" } },
                { "Κοζάνης",      new[] { "Kozani", "Κοζάνης" } },
                { "Grevena",      new[] { "Grevena", "Γρεβενών" } },
                { "Γρεβενών",     new[] { "Grevena", "Γρεβενών" } },
                { "Kastoria",     new[] { "Kastoria", "Καστοριάς" } },
                { "Καστοριάς",    new[] { "Kastoria", "Καστοριάς" } },
                { "Florina",      new[] { "Florina", "Φλώρινας" } },
                { "Φλώρινας",     new[] { "Florina", "Φλώρινας" } },

                // Epirus
                { "Ioannina",     new[] { "Ioannina", "Ιωαννίνων", "Γιάννενα" } },
                { "Ιωαννίνων",    new[] { "Ioannina", "Ιωαννίνων", "Γιάννενα" } },
                { "Thesprotia",   new[] { "Thesprotia", "Θεσπρωτίας" } },
                { "Θεσπρωτίας",   new[] { "Thesprotia", "Θεσπρωτίας" } },
                { "Preveza",      new[] { "Preveza", "Πρέβεζας", "Πρεβέζης", "Prevezza" } },
                { "Πρέβεζας",     new[] { "Preveza", "Πρέβεζας", "Πρεβέζης", "Prevezza" } },
                { "Πρεβέζης",     new[] { "Preveza", "Πρέβεζας", "Πρεβέζης", "Prevezza" } },
                { "Arta",         new[] { "Arta", "Άρτας" } },
                { "Άρτας",        new[] { "Arta", "Άρτας" } },

                // Thessaly
                { "Larissa",      new[] { "Larissa", "Larisa", "Λάρισας" } },
                { "Λάρισας",      new[] { "Larissa", "Larisa", "Λάρισας" } },
                { "Trikala",      new[] { "Trikala", "Τρικάλων" } },
                { "Τρικάλων",     new[] { "Trikala", "Τρικάλων" } },
                { "Karditsa",     new[] { "Karditsa", "Καρδίτσας" } },
                { "Καρδίτσας",    new[] { "Karditsa", "Καρδίτσας" } },
                { "Magnesia",     new[] { "Magnesia", "Magnisia", "Μαγνησίας" } },
                { "Μαγνησίας",    new[] { "Magnesia", "Magnisia", "Μαγνησίας" } },

                // Ionian Islands
                { "Corfu",        new[] { "Corfu", "Kerkyra", "Κέρκυρας" } },
                { "Κέρκυρας",     new[] { "Corfu", "Kerkyra", "Κέρκυρας" } },
                { "Zakynthos",    new[] { "Zakynthos", "Zakinthos", "Zante", "Ζακύνθου" } },
                { "Ζακύνθου",     new[] { "Zakynthos", "Zakinthos", "Zante", "Ζακύνθου" } },
                { "Kefalonia",    new[] { "Kefalonia", "Kefallonia", "Cephalonia", "Κεφαλονιάς", "Κεφαλληνίας" } },
                { "Κεφαλονιάς",   new[] { "Kefalonia", "Kefallonia", "Cephalonia", "Κεφαλονιάς", "Κεφαλληνίας" } },
                { "Lefkada",      new[] { "Lefkada", "Lefkas", "Λευκάδας" } },
                { "Λευκάδας",     new[] { "Lefkada", "Lefkas", "Λευκάδας" } },

                // Western Greece
                { "Aetolia-Acarnania", new[] { "Aetolia-Acarnania", "Aitoloakarnania", "Etoloakarnania", "Αιτωλοακαρνανίας" } },
                { "Αιτωλοακαρνανίας",  new[] { "Aetolia-Acarnania", "Aitoloakarnania", "Etoloakarnania", "Αιτωλοακαρνανίας" } },
                { "Aitoloakarnania", new[] { "Aetolia-Acarnania", "Aitoloakarnania", "Etoloakarnania", "Αιτωλοακαρνανίας" } },
                { "Achaia",       new[] { "Achaia", "Achaea", "Αχαΐας", "Αχαίας" } },
                { "Αχαΐας",       new[] { "Achaia", "Achaea", "Αχαΐας", "Αχαίας" } },
                { "Elis",         new[] { "Elis", "Ilia", "Eleia", "Ηλείας", "Ήλιδα" } },
                { "Ηλείας",       new[] { "Elis", "Ilia", "Eleia", "Ηλείας", "Ήλιδα" } },
                { "Ilia",         new[] { "Ilia", "Elis", "Eleia", "Ηλείας", "Ήλιδα" } },

                // Central Greece
                { "Phthiotis",    new[] { "Phthiotis", "Fthiotida", "Φθιώτιδας" } },
                { "Φθιώτιδας",    new[] { "Phthiotis", "Fthiotida", "Φθιώτιδας" } },
                { "Evrytania",    new[] { "Evrytania", "Ευρυτανίας" } },
                { "Ευρυτανίας",   new[] { "Evrytania", "Ευρυτανίας" } },
                { "Phocis",       new[] { "Phocis", "Fokida", "Φωκίδας" } },
                { "Φωκίδας",      new[] { "Phocis", "Fokida", "Φωκίδας" } },
                { "Boeotia",      new[] { "Boeotia", "Viotia", "Βοιωτίας" } },
                { "Βοιωτίας",     new[] { "Boeotia", "Viotia", "Βοιωτίας" } },
                { "Euboea",       new[] { "Euboea", "Evia", "Εύβοιας" } },
                { "Εύβοιας",      new[] { "Euboea", "Evia", "Εύβοιας" } },

                // Attica
                { "Attica",       new[] { "Attica", "Αττικής", "Athens" } },
                { "Αττικής",      new[] { "Attica", "Αττικής", "Athens" } },

                // Peloponnese
                { "Argolis",     new[] { "Argolis", "Argolida", "Αργολίδας" } },
                { "Αργολίδας",   new[] { "Argolis", "Argolida", "Αργολίδας" } },
                { "Arcadia",     new[] { "Arcadia", "Arkadia", "Αρκαδίας" } },
                { "Αρκαδίας",    new[] { "Arcadia", "Arkadia", "Αρκαδίας" } },
                { "Corinthia",   new[] { "Corinthia", "Korinthia", "Κορινθίας" } },
                { "Κορινθίας",   new[] { "Corinthia", "Korinthia", "Κορινθίας" } },
                { "Laconia",     new[] { "Laconia", "Lakonia", "Λακωνίας" } },
                { "Λακωνίας",    new[] { "Laconia", "Lakonia", "Λακωνίας" } },
                { "Messenia",    new[] { "Messenia", "Messinia", "Μεσσηνίας" } },
                { "Μεσσηνίας",   new[] { "Messenia", "Messinia", "Μεσσηνίας" } },

                // North Aegean
                { "Lesbos",      new[] { "Lesbos", "Lesvos", "Λέσβου", "Μυτιλήνη" } },
                { "Λέσβου",      new[] { "Lesbos", "Lesvos", "Λέσβου", "Μυτιλήνη" } },
                { "Chios",       new[] { "Chios", "Khios", "Χίου" } },
                { "Χίου",        new[] { "Chios", "Khios", "Χίου" } },
                { "Samos",       new[] { "Samos", "Σάμου" } },
                { "Σάμου",       new[] { "Samos", "Σάμου" } },

                // South Aegean
                { "Rhodes",      new[] { "Rhodes", "Ρόδου", "Rodos" } },
                { "Ρόδου",       new[] { "Rhodes", "Ρόδου", "Rodos" } },

                // Crete
                { "Chania",      new[] { "Chania", "Hania", "Χανίων" } },
                { "Χανίων",      new[] { "Chania", "Hania", "Χανίων" } },
                { "Rethymno",    new[] { "Rethymno", "Rethymnon", "Ρεθύμνου" } },
                { "Ρεθύμνου",    new[] { "Rethymno", "Rethymnon", "Ρεθύμνου" } },
                { "Heraklion",   new[] { "Heraklion", "Iraklion", "Iraklio", "Ηρακλείου" } },
                { "Ηρακλείου",   new[] { "Heraklion", "Iraklion", "Iraklio", "Ηρακλείου" } },
                { "Lasithi",     new[] { "Lasithi", "Lassithi", "Λασιθίου" } },
                { "Λασιθίου",    new[] { "Lasithi", "Lassithi", "Λασιθίου" } }
            };

            // Add the original term
            variants.Add(regionalUnit);
            
            // Look for mapped variants
            if (regionalUnitMappings.TryGetValue(regionalUnit, out var mappedVariants))
            {
                variants.AddRange(mappedVariants);
                _logger.LogInformation($"🗺️ Found {mappedVariants.Length} variants for '{regionalUnit}': [{string.Join(", ", mappedVariants)}]");
            }
            else
            {
                _logger.LogInformation($"🔍 No specific variants found for '{regionalUnit}', using original only");
            }

            return variants.Distinct().ToList();
        }

        // Helper classes
        private class EvacuationInfo
        {
            public List<string> DangerZones { get; set; } = new List<string>();
            public List<string> SafeZones { get; set; } = new List<string>();
            public List<string> RouteLocations { get; set; } = new List<string>();
            public List<string> FireLocations { get; set; } = new List<string>();
            public string? RegionalContext { get; set; }
        }

        public async Task<List<GeocodedWarning112>> GetWarningsForLocationAsync(string? region = null, string? municipality = null)
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

        public async Task<GeocodedWarning112?> CreateWarningFromTweetContentAsync(string tweetContent)
        {
            try
            {
                _logger.LogInformation($"Creating warning from tweet content: {tweetContent.Substring(0, Math.Min(100, tweetContent.Length))}...");

                // Validate tweet content is a 112 activation
                if (!IsValid112Tweet(tweetContent))
                {
                    _logger.LogWarning("Tweet content is not a valid 112 activation tweet");
                    return null;
                }

                // Create basic warning object
                var warning = new Warning112
                {
                    Id = Guid.NewGuid().ToString(),
                    TweetDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    SourceUrl = "test://content",
                    Locations = new List<string>()
                };

                // Determine language and set content
                bool isGreek = IsGreekTweet(tweetContent);
                if (isGreek)
                {
                    warning.GreekContent = tweetContent;
                    }
                    else
                    {
                    warning.EnglishContent = tweetContent;
                }

                // Process the warning
                var geocodedWarning = await ProcessWarningAsync(warning);
                
                if (geocodedWarning == null || !geocodedWarning.HasGeocodedLocations)
                {
                    _logger.LogWarning("Processing failed for tweet content");
                    return null;
                }

                // Store the test warning
                AddTestWarning(geocodedWarning);

                _logger.LogInformation($"Successfully created warning from tweet content with {geocodedWarning.GeocodedLocations.Count} geocoded locations");
                return geocodedWarning;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating warning from tweet content");
                return null;
            }
        }

        private bool IsValid112Tweet(string tweetText)
        {
            // Check if tweet contains 112 activation markers
            var activationPatterns = new[]
            {
                "Activation 1⃣1⃣2⃣",
                "Activation 1️⃣1️⃣2️⃣",
                "Activation 112",
                "Ενεργοποίηση 1⃣1⃣2⃣",
                "Ενεργοποίηση 1️⃣1️⃣2️⃣",
                "Ενεργοποίηση 112"
            };
            
            return activationPatterns.Any(pattern => tweetText.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private List<GeocodedWarning112> GetTestWarnings()
        {
            try
            {
                if (_cache.TryGetValue(TEST_WARNINGS_CACHE_KEY, out List<GeocodedWarning112>? testWarnings) && testWarnings != null)
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
