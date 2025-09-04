using Microsoft.Extensions.Caching.Memory;

namespace FireIncidents.Services
{
    /// <summary>
    /// Centralized cache key management to prevent conflicts between services
    /// </summary>
    public static class CacheKeyManager
    {
        // Background Services Cache Keys
        public const string RSS_BACKGROUND_DATA = "rss_background_processed_data";
        public const string RSS_BACKGROUND_LAST_UPDATE = "rss_last_update_time";
        public const string WARNING112_BACKGROUND_DATA = "warning112_background_processed_data";
        public const string WARNING112_BACKGROUND_LAST_UPDATE = "warning112_last_update_time";
        public const string WARNING112_BACKGROUND_STATISTICS = "warning112_statistics";
        
        // Regular Service Cache Keys
        public const string ACTIVE_WARNINGS_CACHE = "active_112_warnings";
        public const string TEST_WARNINGS_CACHE = "test_112_warnings";
        public const string RSS_FEED_CACHE = "rss_feed_cache";
        public const string DATASET_CACHE = "greek_dataset_cache";
        public const string MUNICIPALITY_INDEX_CACHE = "municipality_index_cache";
        public const string FIRE_DISTRICTS_CACHE = "fire_districts_cache";
        
        // Cache Expiration Times
        public static readonly TimeSpan BACKGROUND_CACHE_EXPIRATION = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan REGULAR_CACHE_EXPIRATION = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan TEST_CACHE_EXPIRATION = TimeSpan.FromHours(1);
        
        /// <summary>
        /// Clear all background service caches
        /// </summary>
        public static void ClearBackgroundCaches(IMemoryCache cache)
        {
            cache.Remove(RSS_BACKGROUND_DATA);
            cache.Remove(RSS_BACKGROUND_LAST_UPDATE);
            cache.Remove(WARNING112_BACKGROUND_DATA);
            cache.Remove(WARNING112_BACKGROUND_LAST_UPDATE);
            cache.Remove(WARNING112_BACKGROUND_STATISTICS);
        }
        
        /// <summary>
        /// Clear all regular service caches
        /// </summary>
        public static void ClearRegularCaches(IMemoryCache cache)
        {
            cache.Remove(ACTIVE_WARNINGS_CACHE);
            cache.Remove(TEST_WARNINGS_CACHE);
            cache.Remove(RSS_FEED_CACHE);
        }
        
        /// <summary>
        /// Get cache options for background services
        /// </summary>
        public static MemoryCacheEntryOptions GetBackgroundCacheOptions()
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = BACKGROUND_CACHE_EXPIRATION,
                Priority = CacheItemPriority.High
            };
        }
        
        /// <summary>
        /// Get cache options for regular services
        /// </summary>
        public static MemoryCacheEntryOptions GetRegularCacheOptions()
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = REGULAR_CACHE_EXPIRATION,
                Priority = CacheItemPriority.Normal
            };
        }
        
        /// <summary>
        /// Get cache options for test data
        /// </summary>
        public static MemoryCacheEntryOptions GetTestCacheOptions()
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TEST_CACHE_EXPIRATION,
                Priority = CacheItemPriority.Low
            };
        }
    }
}