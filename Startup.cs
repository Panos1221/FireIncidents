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
            services.AddHttpClient("TwitterScraper");

            services.AddMemoryCache();

            // Register services
            services.AddScoped<FireServiceScraperService>();
            services.AddScoped<GeocodingService>();
            services.AddScoped<TwitterScraperService>();
            services.AddScoped<Warning112Service>();

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