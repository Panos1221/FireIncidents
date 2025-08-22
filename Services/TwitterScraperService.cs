using HtmlAgilityPack;
using FireIncidents.Models;
using System.Text.RegularExpressions;
using System.Text;
using System.Web;

namespace FireIncidents.Services
{
    public class TwitterScraperService
    {
        private readonly ILogger<TwitterScraperService> _logger;
        private readonly HttpClient _httpClient;
        private readonly JavaScriptRendererService _jsRenderer;
        
        // Use the live Twitter widget page - JavaScript renderer will execute JS to get rendered content
        private readonly string _widget112Url = "https://dev.livefireincidents.gr/twitter";
        
        // SociableKit URLs don't work without JavaScript execution:
        // private readonly string _widget112Url = "https://widgets.sociablekit.com/twitter-feed/iframe/25590789";
        
        // Previous SociableKit URLs (different embed ID):
        // private readonly string _widget112Url = "https://widgets.sociablekit.com/twitter-feed/25590424";        
        // private readonly string _widget112Url = "https://widgets.sociablekit.com/twitter-feed/iframe/25590424";
        

        
        public TwitterScraperService(ILogger<TwitterScraperService> logger, IHttpClientFactory httpClientFactory, JavaScriptRendererService jsRenderer)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("TwitterScraper");
            _jsRenderer = jsRenderer;
            // HttpClient is now configured in Startup.cs with automatic decompression
            // Additional headers can be added here if needed
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        }

        public async Task<List<Warning112>> ScrapeWarningsAsync(int daysBack = 7)
        {
            var warnings = new List<Warning112>();
            
            try
            {
                _logger.LogInformation($"Starting to scrape 112Greece warnings from SociableKit widget (last {daysBack} days)...");
                
                string html = await GetWidgetHtmlAsync();
                
                if (string.IsNullOrEmpty(html))
                {
                    _logger.LogWarning("No HTML content retrieved from widget");
                    return warnings;
                }

                // for debugging purposes, save the HTML to a file
                var debugPath = Path.Combine(Directory.GetCurrentDirectory(), "debug_twitter_widget.html");
                await File.WriteAllTextAsync(debugPath, html, Encoding.UTF8);
                _logger.LogInformation($"Saved widget HTML to {debugPath} for debugging");
                
                warnings = ParseWarningsFromHtml(html, daysBack);
                
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

        private async Task<string> GetWidgetHtmlAsync()
        {
            try
            {
                _logger.LogInformation($"🚀 Using JavaScript renderer to get fully rendered content from: {_widget112Url}");
                
                // Use JavaScript renderer to execute the SociableKit widget and get rendered content
                var renderedHtml = await _jsRenderer.GetRenderedHtmlAsync(_widget112Url, timeoutSeconds: 45);
                
                _logger.LogInformation($"✅ Retrieved {renderedHtml.Length} characters of rendered HTML");
                _logger.LogDebug($"Content preview: {renderedHtml.Substring(0, Math.Min(200, renderedHtml.Length))}...");
                
                return renderedHtml;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting rendered widget HTML");
                
                // Fallback to direct HTTP request (will get empty container but won't crash)
                _logger.LogWarning("⚠️ Falling back to direct HTTP request (may not contain tweets)");
                try
                {
                    var response = await _httpClient.GetAsync(_widget112Url);
                    if (response.IsSuccessStatusCode)
                    {
                        var fallbackContent = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"Fallback: Retrieved {fallbackContent.Length} characters");
                        return fallbackContent;
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback HTTP request also failed");
                }
                
                return string.Empty;
            }
        }

        private List<Warning112> ParseWarningsFromHtml(string html, int daysBack = 7)
        {
            var warnings = new List<Warning112>();
            
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Look for tweet containers - specifically target the sk-post-item class from the real HTML structure
                var tweetNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sk-post-item')]");
                
                if (tweetNodes == null || !tweetNodes.Any())
                {
                    _logger.LogWarning("No tweet nodes found in widget HTML. Trying alternative selectors...");
                    _logger.LogInformation($"HTML sample (first 1000 chars): {html.Substring(0, Math.Min(1000, html.Length))}");
                    
                    // Try alternative selectors for SociableKit content and general emergency indicators
                    tweetNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sk-ww')] | //div[contains(@class, 'sk-post')] | //div[contains(text(), '⚠️')] | //*[contains(text(), 'Activation')] | //*[contains(text(), 'Ενεργοποίηση')]");
                }
                
                if (tweetNodes == null || !tweetNodes.Any())
                {
                    _logger.LogWarning("No tweet content found with any selector");
                    _logger.LogInformation($"Full HTML content: {html}");
                    return warnings;
                }
                
                _logger.LogInformation($"Found {tweetNodes.Count} potential tweet nodes");
                
                foreach (var tweetNode in tweetNodes)
                {
                    try
                    {
                        var warning = ParseSingleTweet(tweetNode, daysBack);
                        if (warning != null)
                        {
                            warnings.Add(warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error parsing individual tweet node");
                    }
                }
                
                // Remove duplicates based on content similarity
                warnings = RemoveDuplicateWarnings(warnings);
                
                return warnings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing warnings from HTML");
                return warnings;
            }
        }

        private Warning112? ParseSingleTweet(HtmlNode tweetNode, int daysBack = 7)
        {
            try
            {
                string tweetText = ExtractTweetText(tweetNode);
                
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
                
                var tweetDate = ExtractTweetDate(tweetNode);
                
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
                    SourceUrl = ExtractTweetUrl(tweetNode)
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
                _logger.LogError(ex, "Error parsing single tweet");
                return null;
            }
        }

        private string ExtractTweetText(HtmlNode tweetNode)
        {
            // Target the 'sk-post-body-full' div for the main tweet content (from real HTML structure)
            var textNode = tweetNode.SelectSingleNode(".//div[contains(@class, 'sk-post-body-full')]");
            if (textNode != null)
            {
                // Decode HTML entities and clean up text (e.g., remove emoji images, extra spaces)
                string text = HttpUtility.HtmlDecode(textNode.InnerHtml); // Use InnerHtml to preserve structure for cleaning
                
                // Remove image tags for emojis, keeping their alt text if available
                text = Regex.Replace(text, @"<img[^>]*alt=""([^""]*)""[^>]*>", "$1");
                text = Regex.Replace(text, @"<img[^>]*>", ""); // Remove any remaining img tags
                
                // Replace <br> with newlines
                text = Regex.Replace(text, @"<br\s*\/?>", "\n", RegexOptions.IgnoreCase);
                
                // Remove other HTML tags like <span>, <div>, <a>, <p> but keep their inner text
                text = Regex.Replace(text, @"<[^>]*>", "");
                
                return text.Trim();
            }
            
            // Fallback: try other common selectors
            var textSelectors = new[]
            {
                ".//div[@class='tweet-text']",                    // Our test data structure
                ".//div[contains(@class, 'tweet-text')] | .//p | .//div[contains(@class, 'text')] | .//span[contains(@class, 'text')]",
                ".//div[contains(@data-testid, 'tweetText')]",
                ".//div[contains(@class, 'sk-ww-twitter-feed-item-text')]"  // Other SociableKit structure
            };
            
            foreach (var selector in textSelectors)
            {
                var fallbackNode = tweetNode.SelectSingleNode(selector);
                if (fallbackNode != null)
                {
                    string? text = HttpUtility.HtmlDecode(fallbackNode.InnerText?.Trim());
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            
            // Final fallback: get all text from the node
            string? fallbackText = HttpUtility.HtmlDecode(tweetNode.InnerText?.Trim());
            return fallbackText ?? string.Empty;
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

        private DateTime ExtractTweetDate(HtmlNode tweetNode)
        {
            try
            {
                // Target the 'sk-post-dateposted' p element for date (from real HTML structure)
                var dateNode = tweetNode.SelectSingleNode(".//p[contains(@class, 'sk-post-dateposted')]");
                if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var date))
                {
                    return date;
                }
                
                // Fallback: try to find timestamp in various formats
                var timeNode = tweetNode.SelectSingleNode(".//time | .//span[contains(@class, 'time')] | .//div[contains(@class, 'date')]");
                
                if (timeNode != null)
                {
                    string timeText = timeNode.GetAttributeValue("datetime", "") ?? timeNode.InnerText;
                    
                    if (DateTime.TryParse(timeText, out DateTime parsedDate))
                    {
                        return parsedDate;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tweet date");
            }
            
            // Fallback to current time
            _logger.LogWarning("Could not extract tweet date, defaulting to UtcNow.");
            return DateTime.UtcNow;
        }

        private string ExtractTweetUrl(HtmlNode tweetNode)
        {
            try
            {
                // Target the 'sk-post-link' div with an a tag for the tweet URL (from real HTML structure)
                var urlNode = tweetNode.SelectSingleNode(".//div[contains(@class, 'sk-post-link')]/a");
                if (urlNode != null)
                {
                    return urlNode.GetAttributeValue("href", "");
                }
                
                // Fallback: try to find any tweet URL
                var linkNode = tweetNode.SelectSingleNode(".//a[contains(@href, 'twitter.com') or contains(@href, 'x.com')]");
                
                if (linkNode != null)
                {
                    return linkNode.GetAttributeValue("href", "");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting tweet URL");
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
