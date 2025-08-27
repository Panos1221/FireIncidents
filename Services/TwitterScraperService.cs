using FireIncidents.Models;
using System.Text.RegularExpressions;
using System.Text;
using System.Web;
using System.Text.Json;

namespace FireIncidents.Services
{
    public class TwitterScraperService
    {
        private readonly ILogger<TwitterScraperService> _logger;
        private readonly HttpClient _httpClient;
        // RSS.app JSON feed URL for 112 Greece Twitter feed
        private readonly string _rssAppFeedUrl = "https://rss.app/feeds/v1.1/bcgxFnQk0mKozyCv.json";
        

        
        public TwitterScraperService(ILogger<TwitterScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("TwitterScraper");
            // HttpClient is now configured in Startup.cs with automatic decompression
            // Additional headers can be added here if needed
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "FireIncidents/1.0");
        }

        public async Task<List<Warning112>> ScrapeWarningsAsync(int daysBack = 7)
        {
            var warnings = new List<Warning112>();
            
            try
            {
                _logger.LogInformation($"Starting to scrape 112Greece warnings from RSS.app feed (last {daysBack} days)...");
                
                string jsonContent = await GetRssAppFeedAsync();
                
                if (string.IsNullOrEmpty(jsonContent))
                {
                    _logger.LogWarning("No JSON content retrieved from RSS.app feed");
                    return warnings;
                }

                // for debugging purposes, save the JSON to a file
                var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "debug_rss_feed.json");
                await File.WriteAllTextAsync(debugPath, jsonContent, Encoding.UTF8);
                _logger.LogInformation($"Saved RSS feed JSON to {debugPath} for debugging");
                
                warnings = ParseWarningsFromJson(jsonContent, daysBack);
                
                _logger.LogInformation($"Successfully scraped {warnings.Count} 112 warnings");
                
                foreach (var warning in warnings)
                {
                    _logger.LogInformation($"Warning: {warning.WarningType} - {warning.Locations.Count} locations - {warning.TweetDate}");
                }
                
                return warnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while scraping 112Greece warnings");
                return warnings;
            }
        }

        private async Task<string> GetRssAppFeedAsync()
        {
            try
            {
                _logger.LogInformation($"🚀 Fetching RSS.app JSON feed from: {_rssAppFeedUrl}");
                
                var response = await _httpClient.GetAsync(_rssAppFeedUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to fetch RSS.app feed. Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    return string.Empty;
                }
                
                var jsonContent = await response.Content.ReadAsStringAsync();
                
                _logger.LogInformation($"✅ Retrieved {jsonContent.Length} characters of JSON content");
                _logger.LogDebug($"Content preview: {jsonContent.Substring(0, Math.Min(200, jsonContent.Length))}...");
                
                return jsonContent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting RSS.app feed JSON");
                return string.Empty;
            }
        }

        private List<Warning112> ParseWarningsFromJson(string jsonContent, int daysBack = 7)
        {
            var warnings = new List<Warning112>();
            
            try
            {
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;
                
                if (!root.TryGetProperty("items", out var itemsElement))
                {
                    _logger.LogWarning("No 'items' property found in RSS.app JSON feed");
                    return warnings;
                }
                
                var items = itemsElement.EnumerateArray().ToList();
                _logger.LogInformation($"Found {items.Count} items in RSS.app feed");
                
                foreach (var item in items)
                {
                    try
                    {
                        var warning = ParseSingleJsonItem(item, daysBack);
                        if (warning != null)
                        {
                            warnings.Add(warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing individual JSON item");
                    }
                }
                
                // Remove duplicates based on content similarity
                warnings = RemoveDuplicateWarnings(warnings);
                
                return warnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing warnings from JSON");
                return warnings;
            }
        }

        private Warning112? ParseSingleJsonItem(JsonElement item, int daysBack = 7)
        {
            try
            {
                // Extract tweet text from content_text field
                if (!item.TryGetProperty("content_text", out var contentTextElement))
                {
                    return null;
                }
                
                string tweetText = contentTextElement.GetString() ?? string.Empty;
                
                if (string.IsNullOrWhiteSpace(tweetText))
                {
                    return null;
                }
                
                _logger.LogDebug($"Examining tweet text: {tweetText.Substring(0, Math.Min(100, tweetText.Length))}...");
                
                // Check if this is a 112 activation tweet
                if (!IsValid112Tweet(tweetText))
                {
                    return null;
                }
                
                _logger.LogInformation("Found valid 112 activation tweet");
                
                // Determine if this is English or Greek version
                bool isGreek = IsGreekTweet(tweetText);
                
                var tweetDate = ExtractTweetDateFromJson(item);
                
                // Skip tweets older than specified days back
                if (DateTime.UtcNow - tweetDate > TimeSpan.FromDays(daysBack))
                {
                    _logger.LogInformation($"Skipping old tweet from {tweetDate:yyyy-MM-dd HH:mm} (older than {daysBack} days)");
                    return null;
                }
                
                _logger.LogInformation($"✅ Including tweet from {tweetDate:yyyy-MM-dd HH:mm} (within {daysBack} days)");
                
                var warning = new Warning112
                {
                    TweetDate = tweetDate,
                    SourceUrl = ExtractTweetUrlFromJson(item)
                };
                
                if (isGreek)
                {
                    warning.GreekContent = CleanTweetText(tweetText);
                    warning.Locations = ExtractLocationsFromGreekTweet(tweetText);
                }
                else
                {
                    warning.EnglishContent = CleanTweetText(tweetText);
                    warning.Locations = ExtractLocationsFromEnglishTweet(tweetText);
                }
                
                if (!warning.Locations.Any())
                {
                    _logger.LogWarning("No locations found in 112 tweet, skipping");
                    return null;
                }
                
                _logger.LogInformation($"Parsed 112 warning with {warning.Locations.Count} locations: {string.Join(", ", warning.Locations)}");
                
                return warning;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing single JSON item");
                return null;
            }
        }

        private bool IsValid112Tweet(string tweetText)
        {
            if (string.IsNullOrWhiteSpace(tweetText))
                return false;
                
            // Check for 112 activation pattern or general emergency indicators
            return (tweetText.Contains("⚠️") && 
                   (tweetText.Contains("Activation 1⃣1⃣2⃣") || tweetText.Contains("Ενεργοποίηση 1⃣1⃣2⃣"))) ||
                   (tweetText.Contains("🆘") && 
                   (tweetText.Contains("Wildfire") || tweetText.Contains("Δασική πυρκαγιά"))) ||
                   (tweetText.Contains("‼️") && 
                   (tweetText.Contains("move away") || tweetText.Contains("απομακρυνθείτε")));
        }

        private bool IsGreekTweet(string tweetText)
        {
            // Check for Greek characters or specific Greek words
            return tweetText.Contains("Ενεργοποίηση") || 
                   tweetText.Contains("Δασική") || 
                   tweetText.Contains("περιοχή") ||
                   Regex.IsMatch(tweetText, @"[\u0370-\u03FF\u1F00-\u1FFF]"); // Greek Unicode ranges
        }

        private string CleanTweetText(string tweetText)
        {
            if (string.IsNullOrWhiteSpace(tweetText))
                return string.Empty;
                
            // Remove extra whitespace and normalize
            tweetText = Regex.Replace(tweetText, @"\s+", " ").Trim();
            
            return tweetText;
        }

        private List<string> ExtractLocationsFromEnglishTweet(string tweetText)
        {
            var locations = new List<string>();
            
            try
            {
                // Extract hashtags that represent locations
                var hashtagMatches = Regex.Matches(tweetText, @"#([A-Za-z\u0370-\u03FF\u1F00-\u1FFF]+)", RegexOptions.IgnoreCase);
                
                foreach (Match match in hashtagMatches)
                {
                    string location = match.Groups[1].Value;
                    
                    // Filter out non-location hashtags
                    if (!IsLocationHashtag(location))
                        continue;
                        
                    locations.Add(location);
                }
                
                _logger.LogDebug($"Extracted {locations.Count} locations from English tweet: {string.Join(", ", locations)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting locations from English tweet");
            }
            
            return locations;
        }

        private List<string> ExtractLocationsFromGreekTweet(string tweetText)
        {
            var locations = new List<string>();
            
            try
            {
                // Extract hashtags that represent locations
                var hashtagMatches = Regex.Matches(tweetText, @"#([Α-Ωα-ωάέήίόύώA-Za-z]+)", RegexOptions.IgnoreCase);
                
                foreach (Match match in hashtagMatches)
                {
                    string location = match.Groups[1].Value;
                    
                    // Filter out non-location hashtags
                    if (!IsLocationHashtag(location))
                        continue;
                        
                    locations.Add(location);
                }
                
                _logger.LogDebug($"Extracted {locations.Count} locations from Greek tweet: {string.Join(", ", locations)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting locations from Greek tweet");
            }
            
            return locations;
        }

        private bool IsLocationHashtag(string hashtag)
        {
            if (string.IsNullOrWhiteSpace(hashtag))
                return false;
                
            // Filter out common non-location hashtags
            var excludedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "112", "112Greece", "Fire", "Wildfire", "Emergency", "Alert", 
                "Φωτιά", "Πυρκαγιά", "Έκτακτο", "Συναγερμός"
            };
            
            return !excludedTags.Contains(hashtag) && hashtag.Length >= 3;
        }

        private DateTime ExtractTweetDateFromJson(JsonElement item)
        {
            try
            {
                // Look for date_published field
                if (item.TryGetProperty("date_published", out var dateElement))
                {
                    var dateString = dateElement.GetString();
                    if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out DateTime parsedDate))
                    {
                        return parsedDate.ToUniversalTime();
                    }
                }
                
                _logger.LogWarning("Could not extract tweet date from JSON, using current time");
                return DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tweet date from JSON");
                return DateTime.UtcNow;
            }
        }

        private string ExtractTweetUrlFromJson(JsonElement item)
        {
            try
            {
                // Look for url field
                if (item.TryGetProperty("url", out var urlElement))
                {
                    var url = urlElement.GetString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
                
                // Fallback: look for external_url field
                if (item.TryGetProperty("external_url", out var externalUrlElement))
                {
                    var externalUrl = externalUrlElement.GetString();
                    if (!string.IsNullOrEmpty(externalUrl))
                    {
                        return externalUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tweet URL from JSON");
            }
            
            return "https://twitter.com/112Greece";
        }

        private List<Warning112> RemoveDuplicateWarnings(List<Warning112> warnings)
        {
            var uniqueWarnings = new List<Warning112>();
            var seenContent = new HashSet<string>();
            
            foreach (var warning in warnings)
            {
                string contentKey = (warning.EnglishContent ?? warning.GreekContent ?? "").Trim();
                
                if (!string.IsNullOrEmpty(contentKey) && !seenContent.Contains(contentKey))
                {
                    seenContent.Add(contentKey);
                    uniqueWarnings.Add(warning);
                }
            }
            
            _logger.LogInformation($"Removed {warnings.Count - uniqueWarnings.Count} duplicate warnings");
            
            return uniqueWarnings;
        }
    }
}
