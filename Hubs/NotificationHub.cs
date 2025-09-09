using Microsoft.AspNetCore.SignalR;
using FireIncidents.Models;

namespace FireIncidents.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(ILogger<NotificationHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"Client connected: {Context.ConnectionId}");
            
            // Store the user's session start time when they connect
            await Groups.AddToGroupAsync(Context.ConnectionId, "NotificationUsers");
            await Clients.Caller.SendAsync("SetSessionStartTime", DateTime.UtcNow);
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation($"Client disconnected: {Context.ConnectionId}");
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
            // Store the session start time for this connection if needed
            // For now, we'll rely on client-side storage
        }
    }
}

