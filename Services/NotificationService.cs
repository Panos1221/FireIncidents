using Microsoft.AspNetCore.SignalR;
using FireIncidents.Hubs;
using FireIncidents.Models;

namespace FireIncidents.Services
{
    public class NotificationService
    {
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<NotificationService> _logger;
        private readonly Dictionary<string, DateTime> _lastIncidentCheck = new();
        private readonly Dictionary<string, DateTime> _lastWarningCheck = new();

        public NotificationService(IHubContext<NotificationHub> hubContext, ILogger<NotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task SendNewIncidentNotification(GeocodedIncident incident)
        {
            try
            {
                var notification = new
                {
                    type = "incident",
                    id = $"incident_{incident.GetHashCode()}",
                    title = GetIncidentTypeDisplayName(incident.Category),
                    message = $"{incident.Location}, {incident.Municipality}",
                    location = new { lat = incident.Latitude, lng = incident.Longitude },
                    category = incident.Category,
                    status = incident.Status,
                    timestamp = DateTime.UtcNow,
                    data = incident
                };

                await _hubContext.Clients.Group("NotificationUsers")
                    .SendAsync("NewIncidentNotification", notification);

                _logger.LogInformation($"Sent new incident notification: {incident.Category} in {incident.Location}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending incident notification");
            }
        }

        public async Task SendNewWarningNotification(GeocodedWarning112 warning)
        {
            try
            {
                var primaryLocation = warning.GeocodedLocations?.FirstOrDefault();
                var locationName = primaryLocation?.LocationName ?? 
                                  warning.Locations?.FirstOrDefault() ?? 
                                  "Unknown location";

                var notification = new
                {
                    type = "warning112",
                    id = warning.Id,
                    title = $"112 Warning - {warning.WarningType}",
                    message = locationName,
                    location = primaryLocation != null ? new { lat = primaryLocation.Latitude, lng = primaryLocation.Longitude } : null,
                    warningType = warning.WarningType,
                    iconType = warning.IconType,
                    timestamp = DateTime.UtcNow,
                    data = warning
                };

                await _hubContext.Clients.Group("NotificationUsers")
                    .SendAsync("NewWarningNotification", notification);

                _logger.LogInformation($"Sent new 112 warning notification: {warning.WarningType} for {locationName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending warning notification");
            }
        }

        public async Task SendTestNotification(string type = "incident")
        {
            try
            {
                object notification;
                string eventName;

                if (type == "warning112")
                {
                    notification = new
                    {
                        type = "warning112",
                        id = "test_warning_" + Guid.NewGuid().ToString("N")[..8],
                        title = "112 Warning - Test",
                        message = "Athens, Attica",
                        location = new { lat = 37.9838, lng = 23.7275 },
                        warningType = "Wildfire",
                        iconType = "red",
                        timestamp = DateTime.UtcNow,
                        data = new { isTest = true }
                    };
                    eventName = "NewWarningNotification";
                }
                else
                {
                    notification = new
                    {
                        type = "incident",
                        id = "test_incident_" + Guid.NewGuid().ToString("N")[..8],
                        title = "Forest Fire",
                        message = "Test Location, Test Municipality",
                        location = new { lat = 38.2466, lng = 21.7359 },
                        category = "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ",
                        status = "ΣΕ ΕΞΕΛΙΞΗ",
                        timestamp = DateTime.UtcNow,
                        data = new { isTest = true }
                    };
                    eventName = "NewIncidentNotification";
                }

                await _hubContext.Clients.Group("NotificationUsers").SendAsync(eventName, notification);

                _logger.LogInformation($"Sent test notification of type: {type}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification");
            }
        }

        private static string GetIncidentTypeDisplayName(string category)
        {
            return category switch
            {
                "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" => "Forest Fire",
                "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" => "Structure Fire",
                "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ" => "Assistance",
                _ => "Fire Incident"
            };
        }

        public void SetLastIncidentCheckTime(string key, DateTime time)
        {
            _lastIncidentCheck[key] = time;
        }

        public DateTime GetLastIncidentCheckTime(string key)
        {
            return _lastIncidentCheck.TryGetValue(key, out var time) ? time : DateTime.MinValue;
        }

        public void SetLastWarningCheckTime(string key, DateTime time)
        {
            _lastWarningCheck[key] = time;
        }

        public DateTime GetLastWarningCheckTime(string key)
        {
            return _lastWarningCheck.TryGetValue(key, out var time) ? time : DateTime.MinValue;
        }
    }
}

