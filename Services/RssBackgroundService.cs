using FireIncidents.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FireIncidents.Services
{
    public class RssBackgroundService : BackgroundService
    {
        private readonly ILogger<RssBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _interval;
        


        public RssBackgroundService(
            ILogger<RssBackgroundService> logger,
            IServiceProvider serviceProvider,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _cache = cache;
            _configuration = configuration;
            
            // Get interval from configuration, default to 1 minute
            var intervalMinutes = _configuration.GetValue<int>("BackgroundServices:RssProcessingIntervalMinutes", 1);
            _interval = TimeSpan.FromMinutes(intervalMinutes);
            
            _logger.LogInformation($"RSS Background Service initialized with {intervalMinutes} minute interval");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RSS Background Service started");
            
            // Initial delay to let the application start up
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRssFeedAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RSS background processing");
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
            
            _logger.LogInformation("RSS Background Service stopped");
        }

        private async Task ProcessRssFeedAsync()
        {
            try
            {
                _logger.LogInformation("Starting RSS feed background processing...");
                
                using var scope = _serviceProvider.CreateScope();
                var rssParsingService = scope.ServiceProvider.GetRequiredService<RssParsingService>();
                
                // Fetch fresh RSS data
                var rssItems = await rssParsingService.GetRssItemsAsync();
                
                if (rssItems?.Any() == true)
                {
                    // Cache the processed RSS data with extended expiration for background processing
                _cache.Set(CacheKeyManager.RSS_BACKGROUND_DATA, rssItems, CacheKeyManager.GetBackgroundCacheOptions());
                 _cache.Set(CacheKeyManager.RSS_BACKGROUND_LAST_UPDATE, DateTime.UtcNow, CacheKeyManager.GetBackgroundCacheOptions());
                    
                    _logger.LogInformation($"RSS background processing completed. Cached {rssItems.Count} items");
                }
                else
                {
                    _logger.LogWarning("RSS background processing returned no items");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during RSS background processing");
                throw;
            }
        }
        
        public static List<RssItem>? GetCachedRssItems(IMemoryCache cache)
        {
            return cache.TryGetValue(CacheKeyManager.RSS_BACKGROUND_DATA, out List<RssItem>? cachedItems) ? cachedItems : null;
        }
        
        public static DateTime? GetLastUpdateTime(IMemoryCache cache)
        {
            return cache.TryGetValue(CacheKeyManager.RSS_BACKGROUND_LAST_UPDATE, out DateTime lastUpdate) ? lastUpdate : null;
        }
    }
}