namespace FireIncidents.Models
{
    public class FireIncident
    {
        public string Status { get; set; }
        public string Category { get; set; } 
        public string Region { get; set; }
        public string Municipality { get; set; }
        public string Location { get; set; }
        public string StartDate { get; set; } 
        public string LastUpdate { get; set; }
        
        public string GetMarkerImage()
        {
            string statusCode = Status switch
            {
                "ΣΕ ΕΞΕΛΙΞΗ" => "ongoing",
                "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ" => "partial",
                "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ" => "controlled",
                _ => "unknown"
            };
            
            string categoryCode = Category switch
            {
                "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" => "forest-fire",
                "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" => "urban-fire",
                "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ" => "assistance",
                _ => "unknown"
            };
            
            return $"{categoryCode}-{statusCode}.png";
        }
        
        public string FullAddress => $"{Location}, {Municipality}, {Region}, Greece";
    }
}