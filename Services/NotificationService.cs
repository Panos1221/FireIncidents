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
            await SendNewIncidentNotification(incident, DateTime.UtcNow);
        }

        public async Task SendNewIncidentNotification(GeocodedIncident incident, DateTime incidentOccurredAt)
        {
            try
            {
                var notification = new
                {
                    type = "incident",
                    id = $"incident_{incident.GetHashCode()}",
                    title = incident.Category, // Send raw category for frontend translation
                    message = $"{incident.Location}, {incident.Municipality}",
                    location = new { lat = incident.Latitude, lng = incident.Longitude },
                    category = incident.Category,
                    status = incident.Status,
                    timestamp = incidentOccurredAt,
                    data = incident
                };

                // Get eligible connection IDs (users who joined before the incident occurred)
                var eligibleConnectionIds = GetEligibleConnectionIds(incidentOccurredAt);
                
                if (eligibleConnectionIds.Any())
                {
                    await _hubContext.Clients.Clients(eligibleConnectionIds)
                        .SendAsync("NewIncidentNotification", notification);

                    _logger.LogInformation($"Sent new incident notification to {eligibleConnectionIds.Count} eligible users: {incident.Category} in {incident.Location}");
                }
                else
                {
                    _logger.LogInformation($"No eligible users found for incident notification: {incident.Category} in {incident.Location}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending incident notification");
            }
        }

        public async Task SendNewWarningNotification(GeocodedWarning112 warning)
        {
            await SendNewWarningNotification(warning, warning.TweetDate);
        }

        public async Task SendNewWarningNotification(GeocodedWarning112 warning, DateTime warningOccurredAt)
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
                    timestamp = warningOccurredAt,
                    data = warning
                };

                // Get eligible connection IDs (users who joined before the warning occurred)
                var eligibleConnectionIds = GetEligibleConnectionIds(warningOccurredAt);
                
                if (eligibleConnectionIds.Any())
                {
                    await _hubContext.Clients.Clients(eligibleConnectionIds)
                        .SendAsync("NewWarningNotification", notification);

                    _logger.LogInformation($"Sent new 112 warning notification to {eligibleConnectionIds.Count} eligible users: {warning.WarningType} for {locationName}");
                }
                else
                {
                    _logger.LogInformation($"No eligible users found for warning notification: {warning.WarningType} for {locationName}");
                }
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
                        title = "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ", // Send raw category for frontend translation
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

        /// <summary>
        /// Gets the connection IDs of users who joined before the specified event time
        /// (i.e., users who should receive notifications for events that occurred after they joined)
        /// </summary>
        /// <param name="eventOccurredAt">The timestamp when the event occurred</param>
        /// <returns>List of connection IDs eligible to receive the notification</returns>
        private List<string> GetEligibleConnectionIds(DateTime eventOccurredAt)
        {
            var eligibleConnections = new List<string>();
            var allConnectionStartTimes = NotificationHub.GetAllConnectionSessionStartTimes();

            foreach (var kvp in allConnectionStartTimes)
            {
                var connectionId = kvp.Key;
                var sessionStartTime = kvp.Value;

                // Only send notification if the user joined BEFORE the event occurred
                // This ensures users only get notifications for events that happen AFTER they join
                if (sessionStartTime < eventOccurredAt)
                {
                    eligibleConnections.Add(connectionId);
                }
            }

            return eligibleConnections;
        }
    }
}

