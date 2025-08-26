using PuppeteerSharp;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FireIncidents.Services
{
    public class JavaScriptRendererService
    {
        private readonly ILogger<JavaScriptRendererService> _logger;
        private static IBrowser? _browser;
        private static readonly SemaphoreSlim _browserSemaphore = new(1, 1);

        public JavaScriptRendererService(ILogger<JavaScriptRendererService> logger)
        {
            _logger = logger;
        }

        public async Task<string> GetRenderedHtmlAsync(string url, int timeoutSeconds = 30)
        {
            await _browserSemaphore.WaitAsync();
            
            try
            {
                // Initialize browser if not already done
                if (_browser == null || _browser.IsClosed)
                {
                    _logger.LogInformation("Initializing headless browser...");
                    await new BrowserFetcher().DownloadAsync();
                    
                    _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                    {
                        Headless = true,
                        Args = new[]
                        {
                            "--no-sandbox",
                            "--disable-setuid-sandbox",
                            "--disable-dev-shm-usage",
                            "--disable-accelerated-2d-canvas",
                            "--no-first-run",
                            "--no-zygote",
                            "--disable-gpu"
                        }
                    });
                    _logger.LogInformation("✅ Headless browser initialized successfully");
                }

                using var page = await _browser.NewPageAsync();
                
                _logger.LogInformation($"Navigating to: {url}");
                
                // Set a longer timeout for navigation
                await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                    Timeout = timeoutSeconds * 1000
                });

                _logger.LogInformation("Page loaded, waiting for SociableKit widget to render...");

                // Wait for the SociableKit widget to load and render tweets
                try
                {
                    await page.WaitForSelectorAsync(".sk-post-item", new WaitForSelectorOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });
                    _logger.LogInformation("✅ SociableKit tweets detected");
                }
                catch (WaitTaskTimeoutException)
                {
                    _logger.LogWarning("⚠️ Timeout waiting for tweets to load, but continuing...");
                }

                // Additional wait to ensure all tweets are loaded
                await Task.Delay(2000);

                // Get the fully rendered HTML
                var html = await page.GetContentAsync();
                
                _logger.LogInformation($"✅ Retrieved {html.Length} characters of rendered HTML");
                
                // Check if we got any tweets
                var tweetCount = await page.EvaluateExpressionAsync<int>("document.querySelectorAll('.sk-post-item').length");
                _logger.LogInformation($"Found {tweetCount} tweet elements in rendered page");

                return html;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting rendered HTML from {url}");
                throw;
            }
            finally
            {
                _browserSemaphore.Release();
            }
        }

        public async Task DisposeAsync()
        {
            if (_browser != null && !_browser.IsClosed)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
        }
    }
}
