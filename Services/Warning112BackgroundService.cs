using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FireIncidents.Services
{
    public class Warning112BackgroundService : BackgroundService
    {
        private readonly ILogger<Warning112BackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _interval;
        


        public Warning112BackgroundService(
            ILogger<Warning112BackgroundService> logger,
            IServiceProvider serviceProvider,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _cache = cache;
            _configuration = configuration;
            
            // Get interval from configuration, default to 1 minute
            var intervalMinutes = _configuration.GetValue<int>("BackgroundServices:Warning112ProcessingIntervalMinutes", 1);
            _interval = TimeSpan.FromMinutes(intervalMinutes);
            
            _logger.LogInformation($"Warning112 Background Service initialized with {intervalMinutes} minute interval");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Warning112 Background Service started");
            
            // Initial delay to let the application start up
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessWarningsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Warning112 background processing");
                }
                
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
            }
            
            _logger.LogInformation("Warning112 Background Service stopped");
        }

        private async Task ProcessWarningsAsync()
        {
            try
            {
                _logger.LogInformation("Starting Warning112 background processing...");
                
                using var scope = _serviceProvider.CreateScope();
                var rssParsingService = scope.ServiceProvider.GetRequiredService<RssParsingService>();
                var unifiedGeocodingService = scope.ServiceProvider.GetRequiredService<UnifiedGeocodingService>();
                
                // Get RSS items (either from background cache or fresh)
                var rssItems = RssBackgroundService.GetCachedRssItems(_cache) ?? await rssParsingService.GetRssItemsAsync();
                
                if (rssItems?.Any() != true)
                {
                    _logger.LogWarning("No RSS items available for Warning112 processing");
                    return;
                }
                
                // Process warnings using the optimized approach
                var processedWarnings = await ProcessWarningsFromRssItems(rssItems, unifiedGeocodingService);
                
                if (processedWarnings?.Any() == true)
                {
                    // Cache the processed warnings with extended expiration
                    _cache.Set(CacheKeyManager.WARNING112_BACKGROUND_DATA, processedWarnings, CacheKeyManager.GetBackgroundCacheOptions());
                     _cache.Set(CacheKeyManager.WARNING112_BACKGROUND_LAST_UPDATE, DateTime.UtcNow, CacheKeyManager.GetBackgroundCacheOptions());
                     
                     // Generate and cache statistics
                     var statistics = GenerateStatistics(processedWarnings);
                     _cache.Set(CacheKeyManager.WARNING112_BACKGROUND_STATISTICS, statistics, CacheKeyManager.GetBackgroundCacheOptions());
                    
                    _logger.LogInformation($"Warning112 background processing completed. Cached {processedWarnings.Count} warnings");
                }
                else
                {
                    _logger.LogWarning("Warning112 background processing returned no warnings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Warning112 background processing");
                throw;
            }
        }
        
        private async Task<List<GeocodedWarning112>> ProcessWarningsFromRssItems(List<RssItem> rssItems, UnifiedGeocodingService geocodingService)
        {
            try
            {
                var warnings = new List<GeocodedWarning112>();
                var cutoffDate = DateTime.UtcNow.AddDays(-7); // Process last 7 days
                
                foreach (var item in rssItems.Where(i => i.PubDate >= cutoffDate))
                {
                    try
                    {
                        // Convert RSS item to Warning112
                        var warning = ConvertRssItemToWarning(item);
                        
                        if (warning != null && (!string.IsNullOrEmpty(warning.EnglishContent) || !string.IsNullOrEmpty(warning.GreekContent)))
                        {
                            // Process the warning using unified geocoding
                            var geocodedWarning = await ProcessSingleWarningAsync(warning, geocodingService);
                            
                            if (geocodedWarning != null)
                            {
                                warnings.Add(geocodedWarning);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing individual RSS item: {item.Title}");
                    }
                }
                
                return warnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing warnings from RSS items");
                return new List<GeocodedWarning112>();
            }
        }
        
        private Warning112 ConvertRssItemToWarning(RssItem item)
        {
            // Extract both Greek and English content from the same RSS item
            var fullContent = $"{item.Title}\n{item.Description}";
            
            return new Warning112
            {
                Id = GenerateWarningId(item),
                EnglishContent = ExtractEnglishContentFromMixed(fullContent),
                GreekContent = ExtractGreekContentFromMixed(fullContent),
                Locations = ExtractLocationsFromRssItem(item),
                TweetDate = item.PubDate,
                SourceUrl = item.Link ?? "https://feeds.livefireincidents.gr/112Greece/rss",
                CreatedAt = DateTime.UtcNow
            };
        }
        
        private string GenerateWarningId(RssItem item)
        {
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
            var content = $"{item.Title} {item.Description}".ToLower();
            
            // Extract Greek municipalities and regions from content
            var greekLocationPatterns = new[]
            {
                @"\b(αθήνα|θεσσαλονίκη|πάτρα|ηράκλειο|λάρισα|βόλος|ιωάννινα|καβάλα|σέρρες|κομοτηνή|ξάνθη|δράμα|κιλκίς|φλώρινα|καστοριά|γρεβενά|κοζάνη|βέροια|κατερίνη|πιερία|χαλκιδική|αγρίνιο|μεσολόγγι|πρέβεζα|άρτα|καρδίτσα|τρίκαλα|καλαμπάκα|καρπενήσι|λαμία|χαλκίδα|λιβαδειά|θήβα|αμφισσα|καλαμάτα|σπάρτη|τρίπολη|άργος|ναύπλιο|κόρινθος|αίγιο|πύργος|ζάκυνθος|κεφαλονιά|λευκάδα|κέρκυρα|ρόδος|κως|σάμος|μυτιλήνη|χίος|σύρος|νάξος|πάρος|σαντορίνη|κρήτη|χανιά|ρέθυμνο|αγιος νικόλαος|σητεία|ιεράπετρα)\b",
                @"\b(αττική|μακεδονία|θράκη|ήπειρος|θεσσαλία|στερεά ελλάδα|πελοπόννησος|ιόνια νησιά|αιγαίο|κρήτη|δωδεκάνησα|κυκλάδες|βόρειο αιγαίο|νότιο αιγαίο)\b"
            };
            
            foreach (var pattern in greekLocationPatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var location = match.Value.Trim();
                    if (!string.IsNullOrEmpty(location) && !locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                    {
                        locations.Add(location);
                    }
                }
            }
            
            // If no specific locations found, try to extract any capitalized words that might be locations
            if (!locations.Any())
            {
                var words = content.Split(new[] { ' ', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    if (word.Length > 3 && char.IsUpper(word[0]) && ContainsGreekCharacters(word))
                    {
                        locations.Add(word);
                    }
                }
            }
            
            return locations.Take(5).ToList(); // Limit to 5 locations to avoid too many API calls
        }
        
        private async Task<GeocodedWarning112?> ProcessSingleWarningAsync(Warning112 warning, UnifiedGeocodingService geocodingService)
        {
            try
            {
                // Use the Warning112Service to properly process and geocode the warning
                using var scope = _serviceProvider.CreateScope();
                var warning112Service = scope.ServiceProvider.GetRequiredService<Warning112Service>();
                
                // Use the full processing logic from Warning112Service
                var geocodedWarning = await warning112Service.ProcessWarningAsync(warning);
                
                if (geocodedWarning?.HasGeocodedLocations == true)
                {
                    _logger.LogInformation($"Successfully processed warning {warning.Id} with {geocodedWarning.GeocodedLocations.Count} geocoded locations");
                    return geocodedWarning;
                }
                else
                {
                    _logger.LogWarning($"Warning {warning.Id} could not be geocoded or has no valid locations");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing warning {warning.Id}");
                return null;
            }
        }
        
        private Dictionary<string, int> GenerateStatistics(List<GeocodedWarning112> warnings)
        {
            var activeWarnings = warnings.Where(w => w.IsActive).ToList();
            
            return new Dictionary<string, int>
            {
                ["total_warnings"] = warnings.Count,
                ["active_warnings"] = activeWarnings.Count,
                ["warnings_last_24h"] = warnings.Count(w => w.TweetDate >= DateTime.UtcNow.AddDays(-1)),
                ["warnings_last_week"] = warnings.Count(w => w.TweetDate >= DateTime.UtcNow.AddDays(-7))
            };
        }
        
        public static List<GeocodedWarning112>? GetCachedWarnings(IMemoryCache cache)
        {
            return cache.TryGetValue(CacheKeyManager.WARNING112_BACKGROUND_DATA, out List<GeocodedWarning112>? cachedWarnings) ? cachedWarnings : null;
        }
        
        public static Dictionary<string, int>? GetCachedStatistics(IMemoryCache cache)
        {
            return cache.TryGetValue(CacheKeyManager.WARNING112_BACKGROUND_STATISTICS, out Dictionary<string, int>? cachedStats) ? cachedStats : null;
        }
        
        public static DateTime? GetLastUpdateTime(IMemoryCache cache)
        {
            return cache.TryGetValue(CacheKeyManager.WARNING112_BACKGROUND_LAST_UPDATE, out DateTime lastUpdate) ? lastUpdate : null;
        }
    }
}