using Microsoft.AspNetCore.SignalR;
using FireIncidents.Models;
using System.Collections.Concurrent;

namespace FireIncidents.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly ILogger<NotificationHub> _logger;
        private static readonly ConcurrentDictionary<string, DateTime> _connectionSessionStartTimes = new();

        public NotificationHub(ILogger<NotificationHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            
            // Store the user's session start time when they connect
            var sessionStartTime = DateTime.UtcNow;
            _connectionSessionStartTimes.TryAdd(Context.ConnectionId, sessionStartTime);
            
            await Groups.AddToGroupAsync(Context.ConnectionId, "NotificationUsers");
            await Clients.Caller.SendAsync("SetSessionStartTime", sessionStartTime);
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
            
            // Remove the session start time when user disconnects
            _connectionSessionStartTimes.TryRemove(Context.ConnectionId, out _);
            
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "NotificationUsers");
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinNotificationGroup()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "NotificationUsers");
            _logger.LogInformation($"Client {Context.ConnectionId} joined notification group");
        }

        public async Task LeaveNotificationGroup()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "NotificationUsers");
            _logger.LogInformation($"Client {Context.ConnectionId} left notification group");
        }

        public async Task UpdateSessionStartTime(DateTime sessionStartTime)
        {
            _logger.LogInformation($"Client {Context.ConnectionId} updated session start time to {sessionStartTime}");
            _connectionSessionStartTimes.AddOrUpdate(Context.ConnectionId, sessionStartTime, (key, oldValue) => sessionStartTime);
        }

        /// <summary>
        /// Get the session start time for a specific connection
        /// </summary>
        /// <param name="connectionId">The connection ID</param>
        /// <returns>The session start time, or null if connection not found</returns>
        public static DateTime? GetConnectionSessionStartTime(string connectionId)
        {
            return _connectionSessionStartTimes.TryGetValue(connectionId, out var startTime) ? startTime : null;
        }

        /// <summary>
        /// Get all active connection session start times
        /// </summary>
        /// <returns>Dictionary of connection IDs and their session start times</returns>
        public static Dictionary<string, DateTime> GetAllConnectionSessionStartTimes()
        {
            return new Dictionary<string, DateTime>(_connectionSessionStartTimes);
        }
    }
}

