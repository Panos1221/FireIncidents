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
        private readonly RssParsingService _rssParsingService;
        private readonly UnifiedGeocodingService _unifiedGeocodingService;
        private readonly IMemoryCache _cache;
        private readonly GreekDatasetGeocodingService _greekDatasetService;

        // Cache keys managed by CacheKeyManager
        
        // Geocoding cache
        private static readonly Dictionary<string, List<GeocodedWarning112.WarningLocation>> _geocodingCache = new();
        private static readonly object _cacheLock = new object();
        
        // Rate limiting for Nominatim API calls
        private static readonly SemaphoreSlim _nominatimRateLimiter = new SemaphoreSlim(1, 1); // Only 1 concurrent request
        private static DateTime _lastNominatimRequest = DateTime.MinValue;
        private static readonly TimeSpan _nominatimDelay = TimeSpan.FromMilliseconds(1100); // 1.1 second delay between requests

        // Time-based constants
        private readonly TimeSpan _warningActiveTime = TimeSpan.FromHours(24);
        private readonly TimeSpan _redIconTime = TimeSpan.FromHours(12);

        public Warning112Service(
            ILogger<Warning112Service> logger,
            RssParsingService rssParsingService,
            UnifiedGeocodingService unifiedGeocodingService,
            IMemoryCache cache,
            GreekDatasetGeocodingService greekDatasetService)
        {
            _logger = logger;
            _rssParsingService = rssParsingService;
            _unifiedGeocodingService = unifiedGeocodingService;
            _cache = cache;
            _greekDatasetService = greekDatasetService;
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

                // First, try to get warnings from background service cache
                var backgroundWarnings = Warning112BackgroundService.GetCachedWarnings(_cache);
                if (backgroundWarnings?.Any() == true)
                {
                    var activeBackgroundWarnings = backgroundWarnings.Where(w => w.IsActive).ToList();
                    allWarnings.AddRange(activeBackgroundWarnings);
                    _logger.LogInformation($"Found {activeBackgroundWarnings.Count} background processed warnings");
                }
                else
                {
                    // Fallback to regular cache
                    if (_cache.TryGetValue(CacheKeyManager.ACTIVE_WARNINGS_CACHE, out List<GeocodedWarning112>? cachedWarnings) && cachedWarnings != null)
                    {
                        var activeScrapedWarnings = cachedWarnings.Where(w => w.IsActive).ToList();
                        allWarnings.AddRange(activeScrapedWarnings);
                        _logger.LogInformation($"Found {activeScrapedWarnings.Count} cached scraped warnings");
                    }
                    else
                    {
                        // Last resort: Parse fresh warnings from RSS feed
                        var parsedWarnings = await ParseAndProcessWarningsAsync();

                        // Cache the results for 5 minutes
                        _cache.Set(CacheKeyManager.ACTIVE_WARNINGS_CACHE, parsedWarnings, CacheKeyManager.GetRegularCacheOptions());

                        var activeParsedWarnings = parsedWarnings.Where(w => w.IsActive).ToList();
                        allWarnings.AddRange(activeParsedWarnings);
                        _logger.LogInformation($"Found {activeParsedWarnings.Count} fresh RSS warnings");
                    }
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

        public async Task<List<GeocodedWarning112>> ParseAndProcessWarningsAsync(int? daysBack = null)
        {
            try
            {
                var effectiveDaysBack = daysBack ?? 7;
                _logger.LogInformation($"Parsing and processing 112 warnings from RSS feed (last {effectiveDaysBack} days)...");

                // Get RSS items from the feed
                var rssItems = await _rssParsingService.GetRssItemsAsync();
                
                // Convert RSS items to Warning112 objects
                var rawWarnings = ConvertRssItemsToWarnings(rssItems, effectiveDaysBack);

                if (!rawWarnings.Any())
                {
                    _logger.LogWarning($"No warnings found from Twitter scraping in the last {effectiveDaysBack} days");
                    return new List<GeocodedWarning112>();
                }

                _logger.LogInformation($"Found {rawWarnings.Count} raw warnings from RSS feed");

                // Process and geocode the warnings (no pairing needed since each RSS item already contains both languages)
                var geocodedWarnings = new List<GeocodedWarning112>();

                foreach (var warning in rawWarnings)
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

                _logger.LogInformation($"Successfully processed {geocodedWarnings.Count} geocoded warnings from {rawWarnings.Count} RSS items");

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
            // 1. Similar time (within 3 minutes) - 112Greece posts both versions quickly its instant usually
            // 2. Similar location count (should be roughly the same)
            // 3. Both should be valid 112 activation tweets

            var timeDiff = Math.Abs((warning1.TweetDate - warning2.TweetDate).TotalMinutes);
            if (timeDiff > 3)
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

        public async Task<GeocodedWarning112> ProcessWarningAsync(Warning112 warning)
        {
            try
            {
                _logger.LogInformation($"Processing warning {warning.Id}");

                // STRATEGY: 112Greece posts 2 tweets per incident (Greek + English)
                // 1. Use Greek content for location extraction (more accurate Greek location names using our dataset)
                // 2. Keep English content for display
                // 3. If only one language available, use what we have

                var hasGreek = !string.IsNullOrEmpty(warning.GreekContent);
                var hasEnglish = !string.IsNullOrEmpty(warning.EnglishContent);

                if (!hasGreek && !hasEnglish)
                {
                    _logger.LogWarning($"Warning {warning.Id} has no content to process");
                    return null;
                }

                // For location extraction, prefer Greek
                var contentForLocationExtraction = hasGreek ? warning.GreekContent : warning.EnglishContent;
                var extractionLanguage = hasGreek ? "Greek" : "English";

                _logger.LogInformation($"🔍 Using {extractionLanguage} content for location extraction, " +
                                     $"English: {(hasEnglish ? "available" : "missing")}, " +
                                     $"Greek: {(hasGreek ? "available" : "missing")}");

                // Parse the warning to extract evacuation patterns
                var evacuationInfo = await ParseEvacuationPatternAsync(contentForLocationExtraction);

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

                // Process locations with parallel processing while maintaining proximity analysis
                await ProcessLocationsWithParallelGeocodingAsync(locationsToGeocode, evacuationInfo.RegionalContext, geocodedWarning);

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
                                var safeLocation = await GeocodeLocationAsync(safeZone, evacuationInfo.RegionalContext, geocodedWarning.GeocodedLocations.ToList());
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

        private async Task<EvacuationInfo> ParseEvacuationPatternAsync(string tweetContent)
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

                // Filter out regional units and administrative contexts from all location types
                info.DangerZones = FilterOutRegionalUnits(info.DangerZones);
                info.SafeZones = FilterOutRegionalUnits(info.SafeZones);
                info.FireLocations = FilterOutRegionalUnits(info.FireLocations);

                // Apply intelligent administrative filtering to fire locations
                if (info.FireLocations.Any())
                {
                    info.FireLocations = await FilterAdministrativeContextsAsync(info.FireLocations, info.RegionalContext);
                }

                // Apply intelligent administrative filtering to danger zones
                if (info.DangerZones.Any())
                {
                    info.DangerZones = await FilterAdministrativeContextsAsync(info.DangerZones, info.RegionalContext);
                }

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
                    // Enhanced patterns that better capture the structure
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
                            var extractedLocations = ExtractHashtagLocations(capturedText);
                            
                            _logger.LogInformation($"Raw extracted locations from '{capturedText}': [{string.Join(", ", extractedLocations)}]");
                            
                            // Apply smart contextual filtering to distinguish fire locations from administrative context
                            info.FireLocations = ApplyContextualLocationFiltering(extractedLocations, tweetContent);
                            
                            _logger.LogInformation($"Fire locations after contextual filtering: [{string.Join(", ", info.FireLocations)}]");
                        }
                        else if (pattern.Contains("in your area"))
                        {
                            // For "fire in your area" pattern, look for other hashtags in the tweet
                            var allLocations = ExtractHashtagLocations(tweetContent);
                            info.FireLocations = ApplyContextualLocationFiltering(allLocations, tweetContent);
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

        /// <summary>
        /// Applies contextual filtering to distinguish specific fire locations from administrative context
        /// Uses linguistic and positional cues within the tweet content
        /// </summary>
        private List<string> ApplyContextualLocationFiltering(List<string> locations, string tweetContent)
        {
            if (!locations.Any()) return locations;

            var filteredLocations = new List<string>();
            
            _logger.LogInformation($"🔍 Applying contextual filtering to {locations.Count} locations based on tweet structure");

            foreach (var location in locations)
            {
                var shouldInclude = true;
                var reasonsToExclude = new List<string>();

                // Rule 1: Check if location appears in genitive form (administrative context)
                if (IsGenitiveForm(location))
                {
                    reasonsToExclude.Add("genitive form suggests administrative context");
                    shouldInclude = false;
                }

                // Rule 2: Check positional context - locations appearing after certain phrases are often administrative
                var administrativeContextPatterns = new[]
                {
                    @"της\s+Περιφερειακής\s+Ενότητας.*?" + Regex.Escape(location),
                    @"Δήμου\s.*?" + Regex.Escape(location),
                    @"Municipality\s+of.*?" + Regex.Escape(location)
                };

                foreach (var pattern in administrativeContextPatterns)
                {
                    if (Regex.IsMatch(tweetContent, pattern, RegexOptions.IgnoreCase))
                    {
                        reasonsToExclude.Add("appears in administrative context phrase");
                        shouldInclude = false;
                        break;
                    }
                }

                // Rule 3: Check if location appears as the last hashtag in a sequence (often administrative context)
                var hashtagPattern = @"#([Α-Ωα-ωάέήίόύώA-Za-z0-9_]+)";
                var hashtags = Regex.Matches(tweetContent, hashtagPattern)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();

                if (hashtags.Count > 1 && hashtags.Last().Equals(location, StringComparison.OrdinalIgnoreCase))
                {
                    // Last hashtag in a sequence is often administrative context
                    // But only if it follows the genitive pattern
                    if (IsGenitiveForm(location))
                    {
                        reasonsToExclude.Add("last hashtag in sequence with genitive form");
                        shouldInclude = false;
                    }
                }

                if (shouldInclude)
                {
                    filteredLocations.Add(location);
                    _logger.LogInformation($"✅ Keeping location: {location}");
                }
                else
                {
                    _logger.LogInformation($"🏛️ Filtering out administrative context: {location} ({string.Join(", ", reasonsToExclude)})");
                }
            }

            // Fallback: If we filtered everything out, keep the first location as emergency fallback
            if (!filteredLocations.Any() && locations.Any())
            {
                var fallback = locations.First();
                filteredLocations.Add(fallback);
                _logger.LogInformation($"🚨 Emergency fallback: Keeping first location {fallback} (all locations were filtered)");
            }

            return filteredLocations;
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

        /// <summary>
        /// Intelligently filters locations to distinguish between specific fire locations and administrative contexts
        /// Uses the Greek dataset to identify municipalities and administrative areas dynamically
        /// </summary>
        private async Task<List<string>> FilterAdministrativeContextsAsync(List<string> locations, string? regionalContext = null)
        {
            if (!locations.Any()) return locations;

            var filtered = new List<string>();
            var administrativeAreas = new List<string>();

            _logger.LogInformation($"🔍 Analyzing {locations.Count} locations to filter administrative contexts: [{string.Join(", ", locations)}]");

            foreach (var location in locations)
            {
                var isAdministrative = await IsAdministrativeAreaAsync(location, regionalContext);
                if (isAdministrative)
                {
                    administrativeAreas.Add(location);
                    _logger.LogInformation($"🏛️ Identified administrative area: {location}");
                }
                else
                {
                    filtered.Add(location);
                    _logger.LogInformation($"🎯 Kept as specific location: {location}");
                }
            }

            // Special case: If we filtered out everything, keep the most specific location
            if (!filtered.Any() && administrativeAreas.Any())
            {
                var mostSpecific = await GetMostSpecificLocationAsync(administrativeAreas, regionalContext);
                if (mostSpecific != null)
                {
                    filtered.Add(mostSpecific);
                    _logger.LogInformation($"🚨 Emergency fallback: Using most specific administrative area as location: {mostSpecific}");
                }
            }

            _logger.LogInformation($"✅ Filtering result: {filtered.Count} specific locations, {administrativeAreas.Count} administrative areas filtered out");
            
            return filtered;
        }

        /// <summary>
        /// Determines if a location is an administrative area (municipality, regional unit, etc.) vs a specific settlement
        /// </summary>
        private async Task<bool> IsAdministrativeAreaAsync(string locationName, string? regionalContext = null)
        {
            try
            {
                // Check if it's a known regional unit
                var regionalVariants = GetRegionalUnitVariants(locationName);
                if (regionalVariants.Count > 1 && !regionalVariants.All(v => v.Equals(locationName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true; // It's a regional unit
                }

                // Use Greek dataset to check if this location appears as a municipality name
                // but not as a settlement name (or appears as both but primarily as municipality)
                var isMunicipality = await CheckIfMunicipalityAsync(locationName, regionalContext);
                var isSettlement = await CheckIfSettlementAsync(locationName, regionalContext);

                // If it appears as municipality but not as settlement, it's administrative
                if (isMunicipality && !isSettlement)
                {
                    return true;
                }

                // If it appears as both, use additional heuristics
                if (isMunicipality && isSettlement)
                {
                    // Check population and settlement type to determine primary role
                    var municipalityWeight = await GetMunicipalityWeightAsync(locationName, regionalContext);
                    var settlementWeight = await GetSettlementWeightAsync(locationName, regionalContext);
                    
                    return municipalityWeight > settlementWeight;
                }

                // Check for genitive/possessive forms that indicate administrative context
                // Greek genitive endings that suggest "of [place]" meaning administrative context
                if (IsGenitiveForm(locationName))
                {
                    _logger.LogInformation($"📝 Detected genitive form: {locationName} (likely administrative context)");
                    return true;
                }

                return false; // Default to specific location if unclear
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if {locationName} is administrative area");
                return false; // Default to keeping the location if we can't determine
            }
        }

        /// <summary>
        /// Checks if a location name is in genitive form, indicating administrative context
        /// </summary>
        private bool IsGenitiveForm(string locationName)
        {
            // Common Greek genitive endings for place names
            var genitiveEndings = new[]
            {
                "ων", "ών", "ης", "ής", "ας", "άς", "ου", "ού", 
                "ιας", "ίας", "εως", "έως", "ανίας", "ανίας"
            };

            return genitiveEndings.Any(ending => locationName.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the most specific location from a list of administrative areas
        /// </summary>
        private async Task<string?> GetMostSpecificLocationAsync(List<string> administrativeAreas, string? regionalContext = null)
        {
            // Prefer settlements over municipalities, municipalities over regional units
            foreach (var area in administrativeAreas)
            {
                if (await CheckIfSettlementAsync(area, regionalContext))
                {
                    return area; // Settlement is most specific
                }
            }

            foreach (var area in administrativeAreas)
            {
                if (await CheckIfMunicipalityAsync(area, regionalContext))
                {
                    return area; // Municipality is next most specific
                }
            }

            // Return first regional unit as fallback
            return administrativeAreas.FirstOrDefault();
        }

        /// <summary>
        /// Checks if location appears as a municipality in the Greek dataset
        /// </summary>
        private async Task<bool> CheckIfMunicipalityAsync(string locationName, string? regionalContext = null)
        {
            // This would require accessing the Greek dataset
            // For now, we'll use a simplified approach based on known patterns and geocoding results
            try
            {
                // Try geocoding as municipality
                var result = await GeocodeWithNominatim($"{locationName} municipality, Greece", "Municipality check");
                return result != null && !string.IsNullOrEmpty(result.Municipality) && 
                       result.Municipality.Contains(locationName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if location appears as a settlement in the Greek dataset
        /// </summary>
        private async Task<bool> CheckIfSettlementAsync(string locationName, string? regionalContext = null)
        {
            try
            {
                // Try geocoding as village/settlement
                var result = await GeocodeWithNominatim($"{locationName} village, Greece", "Settlement check");
                return result != null && result.Latitude != 0 && result.Longitude != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets weight score for municipality role (higher = more likely to be administrative)
        /// </summary>
        private async Task<int> GetMunicipalityWeightAsync(string locationName, string? regionalContext = null)
        {
            // Simplified scoring - in a full implementation, this would query the dataset
            return IsGenitiveForm(locationName) ? 10 : 5;
        }

        /// <summary>
        /// Gets weight score for settlement role (higher = more likely to be specific location)
        /// </summary>
        private async Task<int> GetSettlementWeightAsync(string locationName, string? regionalContext = null)
        {
            // Simplified scoring - in a full implementation, this would query the dataset
            return IsGenitiveForm(locationName) ? 2 : 8;
        }

        private bool IsGreekTweet(string tweetContent)
        {
            return tweetContent.Any(c => c >= '\u0370' && c <= '\u03FF') ||
                   tweetContent.Contains("Ενεργοποίηση") ||
                   tweetContent.Contains("απομακρυνθείτε");
        }

        private async Task<GeocodedWarning112.WarningLocation> GeocodeLocationAsync(string locationName, string regionalContext = null, List<GeocodedWarning112.WarningLocation> existingLocations = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(locationName))
                    return null;

                _logger.LogInformation($"Geocoding location: '{locationName}'" +
                    (regionalContext != null ? $" in regional context: {regionalContext}" : "") +
                    (existingLocations?.Any() == true ? $" with {existingLocations.Count} existing locations for proximity analysis" : ""));

                // Use multiple geocoding strategies
                var result = await TryGeocodeWithMultipleStrategies(locationName, regionalContext, existingLocations);

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

        private async Task<GeocodedWarning112.WarningLocation> TryGeocodeWithMultipleStrategies(string locationName, string regionalContext = null, List<GeocodedWarning112.WarningLocation> existingLocations = null)
        {
            var strategies = new List<(string Description, Func<Task<GeocodedWarning112.WarningLocation>> Strategy, int Priority)>();

            // If we have regional context, prioritize searches within that context
            if (!string.IsNullOrEmpty(regionalContext))
            {
                var regionalVariants = GetRegionalUnitVariants(regionalContext);

                foreach (var variant in regionalVariants)
                {
                    // Prioritize more specific context searches that are likely to disambiguate locations
                    strategies.Add(($"Context: {locationName}, {variant}, Greece",
                        () => GeocodeWithNominatim($"{locationName}, {variant}, Greece", $"Context search: {variant}"), 120));

                    strategies.Add(($"Context village: {locationName} village, {variant}, Greece",
                        () => GeocodeWithNominatim($"{locationName} village, {variant}, Greece", $"Context village: {variant}"), 110));

                    strategies.Add(($"Context town: {locationName} town, {variant}, Greece",
                        () => GeocodeWithNominatim($"{locationName} town, {variant}, Greece", $"Context town: {variant}"), 110));

                    strategies.Add(($"Context settlement: {locationName} settlement, {variant}, Greece",
                        () => GeocodeWithNominatim($"{locationName} settlement, {variant}, Greece", $"Context settlement: {variant}"), 105));
                }
            }

            // Add general strategies with lower priority to avoid wrong matches
            strategies.AddRange(new (string, Func<Task<GeocodedWarning112.WarningLocation>>, int)[]
            {
                ("Direct search", () => GeocodeWithNominatim(locationName, "Direct search"), 60),
                ("Greece context", () => GeocodeWithNominatim($"{locationName}, Greece", "Greece context"), 55),
                ("Village search", () => GeocodeWithNominatim($"{locationName} village, Greece", "Village search"), 50),
                ("Town search", () => GeocodeWithNominatim($"{locationName} town, Greece", "Town search"), 50),
                ("Settlement search", () => GeocodeWithNominatim($"{locationName} settlement, Greece", "Settlement search"), 45),
                ("Greek village", () => GeocodeWithNominatim($"{locationName} χωριό, Ελλάδα", "Greek village search"), 40),
                ("Municipality search", () => GeocodeWithNominatim($"{locationName} municipality, Greece", "Municipality search"), 35),
                ("Hamlet search", () => GeocodeWithNominatim($"{locationName} hamlet, Greece", "Hamlet search"), 30),
                ("Neighbourhood search", () => GeocodeWithNominatim($"{locationName} neighbourhood, Greece", "Neighbourhood search"), 30),
                ("FireIncident fallback", () => GeocodeUsingFireIncidentService(locationName), 25)
            });

            // If we have regional context but no results, try approximate location
            if (!string.IsNullOrEmpty(regionalContext))
            {
                strategies.Add(("Regional center approximation", () => GetRegionalCenterApproximation(locationName, regionalContext), 10));
            }

            var candidateResults = new List<(GeocodedWarning112.WarningLocation Result, string Description, int Priority, int Score)>();

            // Tiered approach: Try high-priority strategies first, then progressively lower priority
            var contextStrategies = strategies.Where(s => s.Description.Contains("Context") && s.Priority >= 100).OrderByDescending(s => s.Priority).ToList();
            var generalStrategies = strategies.Where(s => !s.Description.Contains("Context") && !s.Description.Contains("FireIncident") && !s.Description.Contains("approximation") && s.Priority >= 40).OrderByDescending(s => s.Priority).ToList();
            var fallbackStrategies = strategies.Where(s => s.Description.Contains("FireIncident") || s.Description.Contains("approximation") || s.Priority < 40).OrderByDescending(s => s.Priority).ToList();
            
            // Tier 1: Context-based strategies (highest priority)
            if (contextStrategies.Any())
            {
                _logger.LogInformation($"🎯 Tier 1: Trying {contextStrategies.Count} context-based strategies");
                foreach (var (description, strategy, priority) in contextStrategies)
                {
                    var earlyTermination = await TryStrategy(description, strategy, priority, candidateResults, regionalContext, existingLocations, locationName);
                    if (earlyTermination) // Score >= 180, return immediately
                    {
                        var bestResult = candidateResults.OrderByDescending(c => c.Score).First();
                        return bestResult.Result;
                    }
                    
                    // Check if we have a good enough result to skip remaining tiers
                    if (candidateResults.Any() && candidateResults.Max(c => c.Score) >= 150)
                    {
                        var bestResult = candidateResults.OrderByDescending(c => c.Score).First();
                        _logger.LogInformation($"🏆 High confidence context result found (Score: {bestResult.Score}), skipping remaining tiers");
                        return bestResult.Result;
                    }
                }
            }
            
            // Tier 2: General strategies (only if no good context results)
            if (!candidateResults.Any() || candidateResults.Max(c => c.Score) < 120)
            {
                _logger.LogInformation($"🔍 Tier 2: Trying {Math.Min(generalStrategies.Count, 3)} general strategies"); // Limit to top 3
                foreach (var (description, strategy, priority) in generalStrategies.Take(3)) // Limit API calls
                {
                    var earlyTermination = await TryStrategy(description, strategy, priority, candidateResults, regionalContext, existingLocations, locationName);
                    if (earlyTermination) // Score >= 180, return immediately
                    {
                        var bestResult = candidateResults.OrderByDescending(c => c.Score).First();
                        return bestResult.Result;
                    }
                    
                    // Check if we have a good enough result to skip fallbacks
                    if (candidateResults.Any() && candidateResults.Max(c => c.Score) >= 130)
                    {
                        var bestResult = candidateResults.OrderByDescending(c => c.Score).First();
                        _logger.LogInformation($"🏆 Good confidence general result found (Score: {bestResult.Score}), skipping fallbacks");
                        return bestResult.Result;
                    }
                }
            }
            
            // Tier 3: Fallback strategies (only if no acceptable results)
            if (!candidateResults.Any() || candidateResults.Max(c => c.Score) < 80)
            {
                _logger.LogInformation($"🚨 Tier 3: Trying {fallbackStrategies.Count} fallback strategies");
                foreach (var (description, strategy, priority) in fallbackStrategies)
                {
                    var earlyTermination = await TryStrategy(description, strategy, priority, candidateResults, regionalContext, existingLocations, locationName);
                    if (earlyTermination) // Score >= 180, return immediately
                    {
                        var bestResult = candidateResults.OrderByDescending(c => c.Score).First();
                        return bestResult.Result;
                    }
                }
            }

            // Select the best result based on score
            if (candidateResults.Any())
            {
                var bestCandidate = candidateResults.OrderByDescending(c => c.Score).First();
                _logger.LogInformation($"🏆 Selected best result: {bestCandidate.Description} (Score: {bestCandidate.Score}, Priority: {bestCandidate.Priority})");
                
                // Log all candidates for debugging
                foreach (var candidate in candidateResults.OrderByDescending(c => c.Score))
                {
                    _logger.LogInformation($"📊 Candidate: {candidate.Description} - Score: {candidate.Score}, Priority: {candidate.Priority}");
                }
                
                return bestCandidate.Result;
            }

            return null;
        }

        private async Task<bool> TryStrategy(string description, Func<Task<GeocodedWarning112.WarningLocation>> strategy, int priority, 
            List<(GeocodedWarning112.WarningLocation Result, string Description, int Priority, int Score)> candidateResults, 
            string regionalContext, List<GeocodedWarning112.WarningLocation> existingLocations, string locationName)
        {
            try
            {
                _logger.LogInformation($"🔍 Trying: {description} (Priority: {priority})");
                
                // Extract query from the strategy description for multiple candidate search
                string query = "";
                if (description.StartsWith("Context:"))
                {
                    var parts = description.Split("Context: ")[1];
                    query = parts;
                }
                else if (description.Contains("search"))
                {
                    // Extract the query pattern from common search types
                    if (description == "Direct search") query = locationName;
                    else if (description == "Greece context") query = $"{locationName}, Greece";
                    else if (description == "Village search") query = $"{locationName} village, Greece";
                    else if (description == "Town search") query = $"{locationName} town, Greece";
                    else if (description == "Settlement search") query = $"{locationName} settlement, Greece";
                    else if (description == "Municipality search") query = $"{locationName} municipality, Greece";
                    else if (description == "Hamlet search") query = $"{locationName} hamlet, Greece";
                    else if (description == "Neighbourhood search") query = $"{locationName} neighbourhood, Greece";
                    else if (description == "Greek village") query = $"{locationName} χωριό, Ελλάδα";
                }
                
                if (!string.IsNullOrEmpty(query))
                {
                    var candidates = await GeocodeWithNominatimMultiple(query, description);
                    foreach (var candidate in candidates)
                    {
                        var score = CalculateGeocodingScore(candidate, regionalContext, description, priority, existingLocations);
                        candidateResults.Add((candidate, description, priority, score));
                        
                        _logger.LogInformation($"✅ CANDIDATE: {description} → {candidate.Latitude:F6}, {candidate.Longitude:F6} (Score: {score})");
                        
                        // Early termination for very high confidence results
                        if (score >= 180)
                        {
                            _logger.LogInformation($"🎯 High confidence result found (Score: {score}), terminating search early");
                            return true; // Signal early termination
                        }
                    }
                    return candidates.Any();
                }
                else
                {
                    // Fallback to single result for complex strategies
                    var result = await strategy();
                    if (result != null && result.Latitude != 0 && result.Longitude != 0 &&
                        result.Latitude >= 34.0 && result.Latitude <= 42.0 &&
                        result.Longitude >= 19.0 && result.Longitude <= 30.0)
                    {
                        var score = CalculateGeocodingScore(result, regionalContext, description, priority, existingLocations);
                        candidateResults.Add((result, description, priority, score));
                        
                        _logger.LogInformation($"✅ SUCCESS: {description} → {result.Latitude:F6}, {result.Longitude:F6} (Score: {score})");
                        
                        // Early termination for very high confidence results
                        if (score >= 180)
                        {
                            _logger.LogInformation($"🎯 High confidence result found (Score: {score}), terminating search early");
                            return true; // Signal early termination
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"💥 Strategy failed: {description}");
            }
            return false;
        }

        private int CalculateGeocodingScore(GeocodedWarning112.WarningLocation result, string regionalContext, string description, int basePriority, List<GeocodedWarning112.WarningLocation> existingLocations = null)
        {
            var score = basePriority;
            
            // Bonus for regional context match
            if (!string.IsNullOrEmpty(regionalContext) && !string.IsNullOrEmpty(result.Region))
            {
                var regionalVariants = GetRegionalUnitVariants(regionalContext);
                var regionMatches = regionalVariants.Any(variant => 
                    result.Region.Contains(variant, StringComparison.OrdinalIgnoreCase) ||
                    result.Municipality.Contains(variant, StringComparison.OrdinalIgnoreCase));
                    
                if (regionMatches)
                {
                    score += 50;
                    _logger.LogDebug($"🎯 Regional context match bonus: +50 (Region: {result.Region}, Municipality: {result.Municipality})");
                }
            }
            
            // Municipality consistency bonus: prefer locations in the same municipality as other warning locations
            // This is now calculated BEFORE proximity to give it higher priority for disambiguation
            if (existingLocations != null && existingLocations.Any() && !string.IsNullOrEmpty(result.Municipality))
            {
                var sameMusicipalityCount = existingLocations.Count(loc => 
                    !string.IsNullOrEmpty(loc.Municipality) && 
                    loc.Municipality.Equals(result.Municipality, StringComparison.OrdinalIgnoreCase));
                    
                if (sameMusicipalityCount > 0)
                {
                    // Increased bonus for municipality consistency - this is critical for disambiguation
                    var municipalityBonus = sameMusicipalityCount * 40; // Increased from 15 to 40
                    score += municipalityBonus;
                    _logger.LogDebug($"🏛️ Municipality consistency bonus: +{municipalityBonus} ({sameMusicipalityCount} locations in {result.Municipality})");
                }
            }
            
            // Proximity bonus: locations in the same warning should be geographically close
            if (existingLocations != null && existingLocations.Any())
            {
                var proximityBonus = CalculateProximityBonus(result, existingLocations);
                score += proximityBonus;
                if (proximityBonus > 0)
                {
                    _logger.LogDebug($"📍 Proximity bonus: +{proximityBonus} (close to other warning locations)");
                }
            }
            
            // Bonus for context-based searches
            if (description.Contains("Context"))
            {
                score += 30;
            }
            
            // Penalty for approximations
            if (description.Contains("approximation") || result.GeocodingSource.Contains("approximation"))
            {
                score -= 20;
            }
            
            // Bonus for having detailed municipality information
            if (!string.IsNullOrEmpty(result.Municipality) && result.Municipality != "Greece")
            {
                score += 10;
            }
            
            // Penalty for very generic region names
            if (result.Region == "Greece" || string.IsNullOrEmpty(result.Region))
            {
                score -= 5;
            }
            
            return score;
        }
        
        private int CalculateProximityBonus(GeocodedWarning112.WarningLocation candidate, List<GeocodedWarning112.WarningLocation> existingLocations)
        {
            if (!existingLocations.Any()) return 0;
            
            var distances = existingLocations.Select(existing => CalculateDistance(candidate.Latitude, candidate.Longitude, existing.Latitude, existing.Longitude)).ToList();
            var averageDistance = distances.Average();
            var minDistance = distances.Min();
            
            // Enhanced proximity bonus with stronger penalties for distant locations
            if (minDistance <= 5) // Within 5km - very close
                return 35; // Increased from 25
            else if (minDistance <= 15) // Within 15km - close
                return 25; // Increased from 15
            else if (minDistance <= 30) // Within 30km - nearby
                return 15; // Increased from 10
            else if (averageDistance <= 50) // Average distance within 50km - same region
                return 8; // Increased from 5
            else if (minDistance > 200) // Very far away - likely wrong location
                return -30; // Strong penalty for very distant locations
            else
                return 0; // Too far, no bonus
        }
        
        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversine formula for calculating distance between two points on Earth
            const double R = 6371; // Earth's radius in kilometers
            
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                    
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            
            return R * c;
        }
        
        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180;
        }

        private async Task<List<GeocodedWarning112.WarningLocation>> GeocodeWithNominatimMultiple(string query, string sourceDescription = "Nominatim")
        {
            // Check cache first
            lock (_cacheLock)
            {
                if (_geocodingCache.TryGetValue(query, out var cachedResults))
                {
                    _logger.LogDebug($"🎯 Cache hit for query: {query} ({cachedResults.Count} results)");
                    return cachedResults.Select(r => new GeocodedWarning112.WarningLocation
                    {
                        LocationName = r.LocationName,
                        Latitude = r.Latitude,
                        Longitude = r.Longitude,
                        Municipality = r.Municipality,
                        Region = r.Region,
                        GeocodingSource = r.GeocodingSource
                    }).ToList();
                }
            }
            
            var candidates = new List<GeocodedWarning112.WarningLocation>();
            
            try
            {
                // Apply rate limiting for Nominatim API
                await _nominatimRateLimiter.WaitAsync();
                string response = null;
            try
            {
                // Ensure minimum delay between requests
                var timeSinceLastRequest = DateTime.UtcNow - _lastNominatimRequest;
                if (timeSinceLastRequest < _nominatimDelay)
                {
                    var delayNeeded = _nominatimDelay - timeSinceLastRequest;
                    _logger.LogDebug($"⏱️ Rate limiting: waiting {delayNeeded.TotalMilliseconds:F0}ms before Nominatim request");
                    await Task.Delay(delayNeeded);
                }
                
                _lastNominatimRequest = DateTime.UtcNow;
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10); // Set reasonable timeout
                httpClient.DefaultRequestHeaders.Add("User-Agent", "FireIncidents/1.0 (emergency-warnings)");

                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://nominatim.openstreetmap.org/search?format=json&addressdetails=1&limit=5&countrycodes=gr&q={encodedQuery}";

                _logger.LogDebug($"🌐 Nominatim query: {query}");

                response = await httpClient.GetStringAsync(url);
            }
            finally
            {
                _nominatimRateLimiter.Release();
            }
            
            if (response == null)
                return candidates;
                
            var results = JsonSerializer.Deserialize<JsonElement[]>(response);

                if (results != null && results.Length > 0)
                {
                    _logger.LogDebug($"🌐 Nominatim returned {results.Length} results for '{query}'");
                    
                    foreach (var result in results)
                    {
                        if (result.TryGetProperty("lat", out var latElement) &&
                            result.TryGetProperty("lon", out var lonElement) &&
                            double.TryParse(latElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                            double.TryParse(lonElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                        {
                            // Validate coordinates are within Greece bounds
                            if (lat < 34 || lat > 42 || lon < 19 || lon > 30)
                                continue;
                                
                            string municipality = "";
                            string region = "Greece";
                            string displayName = "";

                            if (result.TryGetProperty("address", out var addressElement))
                            {
                                if (addressElement.TryGetProperty("municipality", out var munElement))
                                    municipality = munElement.GetString() ?? "";
                                else if (addressElement.TryGetProperty("city", out var cityElement))
                                    municipality = cityElement.GetString() ?? "";
                                else if (addressElement.TryGetProperty("town", out var townElement))
                                    municipality = townElement.GetString() ?? "";
                                else if (addressElement.TryGetProperty("village", out var villageElement))
                                    municipality = villageElement.GetString() ?? "";

                                if (addressElement.TryGetProperty("state", out var stateElement))
                                    region = stateElement.GetString() ?? "Greece";
                            }
                            
                            if (result.TryGetProperty("display_name", out var displayElement))
                                displayName = displayElement.GetString() ?? "";

                            _logger.LogDebug($"🌐 Candidate: {lat:F6}, {lon:F6} - Municipality: {municipality}, Region: {region}, Display: {displayName}");

                            candidates.Add(new GeocodedWarning112.WarningLocation
                            {
                                LocationName = query.Split(',')[0].Trim(),
                                Latitude = lat,
                                Longitude = lon,
                                Municipality = municipality,
                                Region = region,
                                GeocodingSource = sourceDescription
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Nominatim search failed for '{query}'");
            }

            // Cache successful results
            if (candidates.Any())
            {
                lock (_cacheLock)
                {
                    _geocodingCache[query] = candidates.ToList();
                    _logger.LogDebug($"🎯 Cached {candidates.Count} results for query: {query}");
                }
            }

            return candidates;
        }
        
        private async Task<GeocodedWarning112.WarningLocation> GeocodeWithNominatim(string query, string sourceDescription = "Nominatim")
        {
            var candidates = await GeocodeWithNominatimMultiple(query, sourceDescription);
            return candidates.FirstOrDefault();
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

                var geocodedIncident = await _unifiedGeocodingService.GeocodeIncidentAsync(tempIncident);

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
            _cache.Remove(CacheKeyManager.ACTIVE_WARNINGS_CACHE);
            _cache.Remove(CacheKeyManager.TEST_WARNINGS_CACHE);
            _logger.LogInformation("Cleared 112 warnings cache");
        }

        public void AddTestWarning(GeocodedWarning112 warning)
        {
            try
            {
                var testWarnings = GetTestWarnings();
                testWarnings.Add(warning);

                // Store test warnings for 1 hour
                _cache.Set(CacheKeyManager.TEST_WARNINGS_CACHE, testWarnings, CacheKeyManager.GetTestCacheOptions());

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
                if (_cache.TryGetValue(CacheKeyManager.TEST_WARNINGS_CACHE, out List<GeocodedWarning112>? testWarnings) && testWarnings != null)
                {
                    // Filter out expired test warnings
                    var activeTestWarnings = testWarnings.Where(w => w.IsActive).ToList();

                    // Update cache if we filtered out expired warnings
                    if (activeTestWarnings.Count != testWarnings.Count)
                    {
                        _cache.Set(CacheKeyManager.TEST_WARNINGS_CACHE, activeTestWarnings, CacheKeyManager.GetTestCacheOptions());
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
            // First, try to get statistics from background service cache
            var backgroundStats = Warning112BackgroundService.GetCachedStatistics(_cache);
            if (backgroundStats != null)
            {
                _logger.LogInformation("Retrieved statistics from background service cache");
                return backgroundStats;
            }

            // Fallback to generating statistics from active warnings
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

        private async Task ProcessLocationsWithParallelGeocodingAsync(List<string> locationsToGeocode, string? regionalContext, GeocodedWarning112 geocodedWarning)
        {
            const int maxConcurrency = 3; // Limit concurrent API calls to avoid rate limiting
            const int batchSize = 2; // Process in small batches to maintain some proximity analysis

            _logger.LogInformation($"Starting parallel geocoding for {locationsToGeocode.Count} locations with max concurrency: {maxConcurrency}");

            // Process locations in batches to balance speed and proximity analysis
            for (int i = 0; i < locationsToGeocode.Count; i += batchSize)
            {
                var batch = locationsToGeocode.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation($"Processing batch {(i / batchSize) + 1}: [{string.Join(", ", batch)}]");

                // Create semaphore to limit concurrent operations
                using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

                // Process batch in parallel
                var tasks = batch.Select(async locationName =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        _logger.LogInformation($"Starting geocoding for: {locationName}");
                        
                        // Pass existing geocoded locations for proximity analysis
                        var existingLocations = geocodedWarning.GeocodedLocations.ToList();
                        var geocodedLocation = await GeocodeLocationAsync(locationName, regionalContext, existingLocations);

                        if (geocodedLocation != null)
                        {
                            _logger.LogInformation($"✅ Successfully geocoded '{locationName}' to {geocodedLocation.Latitude:F6}, {geocodedLocation.Longitude:F6}");
                            
                            // Thread-safe addition to the collection
                            lock (geocodedWarning.GeocodedLocations)
                            {
                                geocodedWarning.GeocodedLocations.Add(geocodedLocation);
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"❌ Failed to geocode location: {locationName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"💥 Error geocoding location '{locationName}'");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Wait for current batch to complete before starting next batch
                await Task.WhenAll(tasks);
                
                _logger.LogInformation($"Completed batch {(i / batchSize) + 1}, total geocoded so far: {geocodedWarning.GeocodedLocations.Count}");
            }

            _logger.LogInformation($"✅ Parallel geocoding completed. Successfully geocoded {geocodedWarning.GeocodedLocations.Count} out of {locationsToGeocode.Count} locations");
        }

        private List<Warning112> ConvertRssItemsToWarnings(List<RssItem> rssItems, int daysBack)
        {
            try
            {
                _logger.LogInformation($"Converting {rssItems.Count} RSS items to Warning112 objects...");
                
                var warnings = new List<Warning112>();
                var cutoffDate = DateTime.UtcNow.AddDays(-daysBack);
                
                foreach (var item in rssItems)
                {
                    try
                    {
                        // Skip items older than the cutoff date
                        if (item.PubDate < cutoffDate)
                        {
                            _logger.LogDebug($"Skipping RSS item from {item.PubDate:yyyy-MM-dd HH:mm} (older than {daysBack} days)");
                            continue;
                        }
                        
                        // Create Warning112 object from RSS item with both Greek and English content
                        var fullContent = $"{item.Title}\n{item.Description}";
                        var warning = new Warning112
                        {
                            Id = GenerateWarningId(item),
                            EnglishContent = ExtractEnglishContentFromMixed(fullContent),
                            GreekContent = ExtractGreekContentFromMixed(fullContent),
                            Locations = ExtractLocationsFromRssItem(item),
                            TweetDate = item.PubDate,
                            SourceUrl = item.Link ?? "https://feeds.livefireincidents.gr/112Greece/rss",
                            CreatedAt = DateTime.UtcNow
                        };
                        
                        // Only add warnings that have content and locations
                        if (!string.IsNullOrEmpty(warning.EnglishContent) || !string.IsNullOrEmpty(warning.GreekContent))
                        {
                            warnings.Add(warning);
                            _logger.LogDebug($"Converted RSS item to warning: {warning.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error converting RSS item to warning: {item.Title}");
                    }
                }
                
                _logger.LogInformation($"Successfully converted {warnings.Count} RSS items to Warning112 objects");
                return warnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting RSS items to warnings");
                return new List<Warning112>();
            }
        }
        
        private string GenerateWarningId(RssItem item)
        {
            // Generate a unique ID based on the RSS item content and date
            var content = $"{item.Title}_{item.PubDate:yyyyMMddHHmm}";
            return $"rss_{content.GetHashCode():X8}";
        }
        
        private string ExtractEnglishContentFromMixed(string content)
        {
            // Split content by common separators and extract English parts
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var englishLines = new List<string>();
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && ContainsLatinCharacters(trimmedLine))
                {
                    englishLines.Add(trimmedLine);
                }
            }
            
            return englishLines.Any() ? string.Join("\n", englishLines) : string.Empty;
        }
        
        private string ExtractGreekContentFromMixed(string content)
        {
            // Split content by common separators and extract Greek parts
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var greekLines = new List<string>();
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (!string.IsNullOrEmpty(trimmedLine) && ContainsGreekCharacters(trimmedLine))
                {
                    greekLines.Add(trimmedLine);
                }
            }
            
            return greekLines.Any() ? string.Join("\n", greekLines) : string.Empty;
        }
        
        private bool ContainsLatinCharacters(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
        }
        
        private bool ContainsGreekCharacters(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Any(c => c >= 'Α' && c <= 'ω');
        }
        
        private List<string> ExtractLocationsFromRssItem(RssItem item)
        {
            var locations = new List<string>();
            
            try
            {
                // Extract locations from both title and description
                var content = $"{item.Title} {item.Description}";
                
                var locationMatches = Regex.Matches(content, @"[Α-Ωα-ωA-Za-z\s]+(?=\s|$|,|\.|!|\?)", RegexOptions.IgnoreCase);
                
                foreach (Match match in locationMatches)
                {
                    var location = match.Value.Trim();
                    if (location.Length > 2 && !locations.Contains(location))
                    {
                        locations.Add(location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error extracting locations from RSS item: {item.Title}");
            }
            
            return locations;
        }
    }
}
