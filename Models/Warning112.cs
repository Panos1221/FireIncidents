namespace FireIncidents.Models
{
    public class Warning112
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string EnglishContent { get; set; }
        public string GreekContent { get; set; }
        public List<string> Locations { get; set; } = new List<string>();
        public DateTime TweetDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive => DateTime.UtcNow < TweetDate.AddHours(24);
        public string IconType => DateTime.UtcNow < TweetDate.AddHours(12) ? "red" : "yellow";
        public string SourceUrl { get; set; }

        // Extract warning type from content more to be added as needed. To do: add smoke warning which is common 
        public string WarningType
        {
            get
            {
                if (string.IsNullOrEmpty(EnglishContent))
                    return "General Warning";
                    
                if (EnglishContent.Contains("Wildfire", StringComparison.OrdinalIgnoreCase) ||
                    EnglishContent.Contains("Wild fire", StringComparison.OrdinalIgnoreCase))
                    return "Wildfire Warning";
                    
                if (EnglishContent.Contains("evacuation", StringComparison.OrdinalIgnoreCase) ||
                    EnglishContent.Contains("evacuate", StringComparison.OrdinalIgnoreCase))
                    return "Evacuation Warning";
                    
                if (EnglishContent.Contains("flood", StringComparison.OrdinalIgnoreCase))
                    return "Flood Warning";
                    
                return "Emergency Warning";
            }
        }
        
        // Get display content based on language preference
        public string GetDisplayContent(string language = "en")
        {
            return language?.ToLower() == "el" && !string.IsNullOrEmpty(GreekContent) 
                ? GreekContent 
                : EnglishContent;
        }

        // Get icon filename based on current time (red `12 hours, yellow after for 12 more)
        public string GetIconFilename()
        {
            return $"112warning_{IconType}.png";
        }
    }
}
