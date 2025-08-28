using System.Text.Json.Serialization;

namespace FireIncidents.Models
{
    public class Alert
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Url { get; set; }
        public string? AlertType { get; set; } // e.g., evacuation, info, warning
        public string? Source { get; set; } // e.g., twitter
    }

    public class AlertUpdateRequest
    {
        public string Text { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string? Url { get; set; }
        public string? AlertType { get; set; }
        public string? Source { get; set; }
    }
}