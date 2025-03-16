namespace FireIncidents.Models
{
    public class GeocodedIncident : FireIncident
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsGeocoded => Latitude != 0 && Longitude != 0;
        public string GeocodingSource { get; set; } = "Unknown";
    }
}