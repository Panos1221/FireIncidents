using FireIncidents.Models;
using System.Xml.Serialization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace FireIncidents.Services
{
    public class RssParsingService
    {
        private readonly ILogger<RssParsingService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        
        private const string RSS_CACHE_KEY = "rss_feed_cache";
        private const string RSS_FEED_URL = "https://feeds.livefireincidents.gr/112Greece/rss";
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

        public RssParsingService(
            ILogger<RssParsingService> logger,
            HttpClient httpClient,
            IMemoryCache cache)
        {
            _logger = logger;
            _httpClient = httpClient;
            _cache = cache;
            
            // Configure HttpClient
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "FireIncidents/1.0 (Emergency Services Application)");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<RssItem>> GetRssItemsAsync()
        {
            try
            {
                _logger.LogInformation("Fetching RSS items from feed...");

                // Check cache first
                if (_cache.TryGetValue(RSS_CACHE_KEY, out List<RssItem>? cachedItems) && cachedItems != null)
                {
                    _logger.LogInformation($"Returning {cachedItems.Count} cached RSS items");
                    return cachedItems;
                }

                // Fetch fresh data
                var rssItems = await FetchAndParseRssAsync();
                
                // Cache the results
                _cache.Set(RSS_CACHE_KEY, rssItems, _cacheExpiration);
                
                _logger.LogInformation($"Fetched and cached {rssItems.Count} RSS items");
                return rssItems;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching RSS items");
                return new List<RssItem>();
            }
        }

        private async Task<List<RssItem>> FetchAndParseRssAsync()
        {
            try
            {
                _logger.LogInformation($"Fetching RSS feed from: {RSS_FEED_URL}");

                var response = await _httpClient.GetAsync(RSS_FEED_URL);
                response.EnsureSuccessStatusCode();

                var xmlContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug($"Received XML content: {xmlContent.Length} characters");

                // Parse XML
                var rssFeed = ParseRssXml(xmlContent);
                
                if (rssFeed?.Channel?.Items == null)
                {
                    _logger.LogWarning("RSS feed parsing returned null or empty items");
                    return new List<RssItem>();
                }

                _logger.LogInformation($"Successfully parsed {rssFeed.Channel.Items.Count} RSS items");
                return rssFeed.Channel.Items;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while fetching RSS feed");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout while fetching RSS feed");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while fetching RSS feed");
                throw;
            }
        }

        private RssFeed ParseRssXml(string xmlContent)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(RssFeed));
                
                using var stringReader = new StringReader(xmlContent);
                var rssFeed = (RssFeed)serializer.Deserialize(stringReader);
                
                _logger.LogDebug($"XML deserialization successful. Channel title: {rssFeed?.Channel?.Title}");
                return rssFeed;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "XML deserialization failed - invalid XML structure");
                
                // Log a sample of the XML for debugging
                var sample = xmlContent.Length > 500 ? xmlContent.Substring(0, 500) + "..." : xmlContent;
                _logger.LogDebug($"XML sample: {sample}");
                
                throw new InvalidOperationException("Failed to parse RSS XML", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during XML parsing");
                throw;
            }
        }

        public void ClearCache()
        {
            _cache.Remove(RSS_CACHE_KEY);
            _logger.LogInformation("RSS cache cleared");
        }

        public async Task<RssFeed> GetFullRssFeedAsync()
        {
            try
            {
                _logger.LogInformation("Fetching full RSS feed...");
                
                var response = await _httpClient.GetAsync(RSS_FEED_URL);
                response.EnsureSuccessStatusCode();

                var xmlContent = await response.Content.ReadAsStringAsync();
                return ParseRssXml(xmlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching full RSS feed");
                throw;
            }
        }
    }
}