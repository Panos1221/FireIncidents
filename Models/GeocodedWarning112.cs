namespace FireIncidents.Models
{
    public class GeocodedWarning112 : Warning112
    {
        public List<WarningLocation> GeocodedLocations { get; set; } = new List<WarningLocation>();
        public bool HasGeocodedLocations => GeocodedLocations.Any(l => l.IsGeocoded);
        
        public double? PrimaryLatitude => GeocodedLocations.FirstOrDefault(l => l.IsGeocoded)?.Latitude;
        public double? PrimaryLongitude => GeocodedLocations.FirstOrDefault(l => l.IsGeocoded)?.Longitude;
        
        public class WarningLocation
        {
            public string LocationName { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public bool IsGeocoded => Latitude != 0 && Longitude != 0;
            public string GeocodingSource { get; set; } = "Unknown";
            public string Municipality { get; set; }
            public string Region { get; set; }
        }
    }
}
