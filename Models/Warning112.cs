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
        public bool IsActive => DateTime.UtcNow < TweetDate.ToUniversalTime().AddHours(24);
        public string IconType => DateTime.UtcNow < TweetDate.ToUniversalTime().AddHours(12) ? "red" : "yellow";
        public string SourceUrl { get; set; }

        // Extract warning type from content more to be added as needed. To do: add smoke warning which is common 
        public string WarningType
        {
            get
            {
                if (string.IsNullOrEmpty(EnglishContent) && string.IsNullOrEmpty(GreekContent))
                    return "General Warning";

                var contentToCheck = $"{EnglishContent} {GreekContent}".ToLower();

                // Wildfire warnings (English and Greek)
                if (contentToCheck.Contains("wildfire") ||
                    contentToCheck.Contains("wild fire") ||
                    contentToCheck.Contains("πυρκαγιά") ||
                    contentToCheck.Contains("φωτιά") ||
                    contentToCheck.Contains("δασική πυρκαγιά") ||
                    contentToCheck.Contains("πυρκαγιάς"))
                    return "Wildfire Warning";

                // Evacuation warnings (English and Greek)
                if (contentToCheck.Contains("evacuation") ||
                    contentToCheck.Contains("evacuate") ||
                    contentToCheck.Contains("εκκένωση") ||
                    contentToCheck.Contains("εκκενώστε") ||
                    contentToCheck.Contains("εκκενώσετε") ||
                    contentToCheck.Contains("απομάκρυνση"))
                    return "Evacuation Warning";

                // Flood warnings (English and Greek)
                if (contentToCheck.Contains("flood") ||
                    contentToCheck.Contains("πλημμύρα") ||
                    contentToCheck.Contains("πλημμύρες") ||
                    contentToCheck.Contains("κατακλυσμός"))
                    return "Flood Warning";

                // Smoke warnings (English and Greek)
                if (contentToCheck.Contains("smoke") ||
                    contentToCheck.Contains("καπνός") ||
                    contentToCheck.Contains("καπνού") ||
                    contentToCheck.Contains("καπνό") ||
                    contentToCheck.Contains("καπνοί"))
                    return "Smoke Warning";

                return "Emergency Warning";
            }
        }

        // Get display content based on language preference
        public string GetDisplayContent(string language = "en")
        {
            if (language?.ToLower() == "el" && !string.IsNullOrEmpty(GreekContent))
                return GreekContent;

            // Fallback to Greek content if English is not available
            return !string.IsNullOrEmpty(EnglishContent) ? EnglishContent : GreekContent;
        }

        // Get icon filename based on current time (red `12 hours, yellow after for 12 more)
        public string GetIconFilename()
        {
            return $"112warning_{IconType}.png";
        }
    }
}
