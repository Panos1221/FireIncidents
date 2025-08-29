using System.Text.Json;
using FireIncidents.Models;

namespace FireIncidents.Services
{
    public class AlertsStoreService
    {
        private readonly ILogger<AlertsStoreService> _logger;
        private readonly string _storePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public AlertsStoreService(ILogger<AlertsStoreService> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _storePath = Path.Combine(dataDir, "alerts.json");
        }

        public async Task<List<Alert>> GetAlertsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_storePath))
                {
                    return new List<Alert>();
                }
                await using var fs = File.Open(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var alerts = await JsonSerializer.DeserializeAsync<List<Alert>>(fs, _jsonOptions) ?? new List<Alert>();
                return alerts.OrderByDescending(a => a.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read alerts from store");
                return new List<Alert>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<Alert> AddAlertAsync(Alert alert)
        {
            await _lock.WaitAsync();
            try
            {
                var alerts = new List<Alert>();
                if (File.Exists(_storePath))
                {
                    await using var rfs = File.Open(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    alerts = await JsonSerializer.DeserializeAsync<List<Alert>>(rfs, _jsonOptions) ?? new List<Alert>();
                }

                alerts.Add(alert);
                // Keep only latest 500 to avoid unbounded growth
                alerts = alerts.OrderByDescending(a => a.Timestamp).Take(500).ToList();

                await using var wfs = File.Open(_storePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(wfs, alerts, _jsonOptions);

                return alert;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write alert to store");
                throw;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}