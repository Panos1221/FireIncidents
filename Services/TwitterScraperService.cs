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
        
        private readonly string _widget112Url = "https://widgets.sociablekit.com/twitter-feed/iframe/25590424";
        
        // Direct feed (if iframe doesn't work)
        // private readonly string _widget112Url = "https://widgets.sociablekit.com/twitter-feed/25590424";
        
        // Last option: Scrape my own page if nothing works.
        // private readonly string _widget112Url = "http://dev.livefireincidents.gr";
        

        
        public TwitterScraperService(ILogger<TwitterScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("TwitterScraper");
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            
            // Configure headers to mimic browser behavior
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        }

        public async Task<List<Warning112>> ScrapeWarningsAsync()
        {
            var warnings = new List<Warning112>();
            
            try
            {
                _logger.LogInformation("Starting to scrape 112Greece warnings from SociableKit widget...");
                
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
                
                warnings = ParseWarningsFromHtml(html);
                
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
                _logger.LogInformation($"Fetching widget HTML from: {_widget112Url}");
                
                var response = await _httpClient.GetAsync(_widget112Url);
                
                _logger.LogInformation($"Widget response status: {response.StatusCode}");
                _logger.LogInformation($"Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))}");
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to fetch widget. Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Error response body: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                    return null;
                }
                
                var contentBytes = await response.Content.ReadAsByteArrayAsync();
                var content = Encoding.UTF8.GetString(contentBytes);
                
                _logger.LogDebug($"Retrieved {content.Length} characters from widget");
                
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching widget HTML");
                return null;
            }
        }

        private List<Warning112> ParseWarningsFromHtml(string html)
        {
            var warnings = new List<Warning112>();
            
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Look for tweet containers in the SociableKit widget
                // The exact selectors may need adjustment based on the actual widget structure
                var tweetNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sk-ww-twitter-feed-item')] | //div[contains(@class, 'tweet')] | //article | //div[contains(@data-testid, 'tweet')]");
                
                if (tweetNodes == null || !tweetNodes.Any())
                {
                    _logger.LogWarning("No tweet nodes found in widget HTML. Trying alternative selectors...");
                    _logger.LogInformation($"HTML sample (first 1000 chars): {html.Substring(0, Math.Min(1000, html.Length))}");
                    
                    // Try alternative selectors
                    tweetNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'sk-ww')] | //div[contains(text(), '⚠️')] | //*[contains(text(), 'Activation')]");
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
                        var warning = ParseSingleTweet(tweetNode);
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

        private Warning112 ParseSingleTweet(HtmlNode tweetNode)
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
                
                // Skip tweets older than 7 days should lower it?
                if (DateTime.UtcNow - tweetDate > TimeSpan.FromDays(7))
                {
                    _logger.LogInformation($"Skipping old tweet from {tweetDate} (older than 7 days)");
                    return null;
                }
                
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
            // Try multiple selectors to extract tweet text
            var textSelectors = new[]
            {
                ".//div[contains(@class, 'tweet-text')] | .//p | .//div[contains(@class, 'text')] | .//span[contains(@class, 'text')]",
                ".//div[contains(@data-testid, 'tweetText')]",
                ".//div[contains(@class, 'sk-ww-twitter-feed-item-text')]"
            };
            
            foreach (var selector in textSelectors)
            {
                var textNode = tweetNode.SelectSingleNode(selector);
                if (textNode != null)
                {
                    string text = HttpUtility.HtmlDecode(textNode.InnerText?.Trim());
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
            
            // Fallback: get all text from the node
            string fallbackText = HttpUtility.HtmlDecode(tweetNode.InnerText?.Trim());
            return fallbackText;
        }

        private bool IsValid112Tweet(string tweetText)
        {
            if (string.IsNullOrWhiteSpace(tweetText))
                return false;
                
            // Check for 112 activation pattern
            return tweetText.Contains("⚠️") && 
                   (tweetText.Contains("Activation 1⃣1⃣2⃣") || tweetText.Contains("Ενεργοποίηση 1⃣1⃣2⃣"));
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
                // Try to find timestamp in various formats
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
            return DateTime.UtcNow;
        }

        private string ExtractTweetUrl(HtmlNode tweetNode)
        {
            try
            {
                // Try to find the tweet URL
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
