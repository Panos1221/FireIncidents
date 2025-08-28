using FireIncidents.Services;
using FireIncidents.Logging;
using System.Text;

namespace FireIncidents
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            services.AddHttpClient("FireService");
            services.AddHttpClient("Nominatim");
            
            // Configure TwitterScraper HttpClient with automatic decompression
            services.AddHttpClient("TwitterScraper", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Add("User-Agent", 
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", 
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            });

            services.AddMemoryCache();

            // Register services
            services.AddScoped<FireServiceScraperService>();
            services.AddScoped<GeocodingService>();
            services.AddScoped<JavaScriptRendererService>();
            services.AddScoped<TwitterScraperService>();
            services.AddScoped<Warning112Service>();
            services.AddSingleton<AlertsStoreService>();

            // Configure logging
            services.AddLogging(logging =>
            {
                logging.AddConfiguration(Configuration.GetSection("Logging"));
                logging.AddConsole();
                logging.AddDebug();
                logging.AddFile(Configuration); // custom file logger
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            // Set console encoding to UTF-8
            Console.OutputEncoding = Encoding.UTF8;

            ClearLogs();

            // Log application startup
            var logger = loggerFactory.CreateLogger<Startup>();
            logger.LogInformation("Application starting up");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                logger.LogInformation("Running in Development environment");
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
                logger.LogInformation("Running in Production environment");
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            logger.LogInformation("Application configuration complete");
        }

        private void ClearLogs()
        {
            try
            {
                string logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");

                if (!Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                    return;
                }

                // Get current date to identify today's log file
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                string currentDayLogPattern = $"fire-incidents-{today}";

                // Delete current day's log files
                foreach (var file in Directory.GetFiles(logsDirectory))
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);

                        // Check log date
                        if (fileName.StartsWith(currentDayLogPattern))
                        {
                            File.Delete(file);
                            Console.WriteLine($"Cleared log file: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not delete log file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error clearing logs: {ex.Message}");
            }
        }
    }
}