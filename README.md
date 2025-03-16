# Greek Fire Incidents Map

## Overview
This web application provides real-time visualization of active fire incidents throughout Greece. It scrapes data from the official Greek Fire Service website and displays incidents on an interactive map with filtering capabilities.

## Features
- **Real-time Data**: Scrapes and displays current fire incidents from the Greek Fire Service
- **Interactive Map**: Visualizes incidents with location-specific markers
- **Incident Categorization**: Distinguishes between forest fires, urban fires, and assistance incidents
- **Status Filtering**: Filter incidents by status (In Progress, Partial Control, Full Control)
- **Incident Details**: Click markers to view detailed information about each incident
- **Statistics**: View summary statistics about current incidents
- **Multilingual Support**: Available in both Greek and English
- **Dark/Light Mode**: Toggle between light and dark themes

## Technical Implementation
- **Backend**: ASP.NET Core 9.0
- **Frontend**: HTML, CSS, JavaScript, Bootstrap
- **Mapping**: Leaflet.js
- **Data Source**: Web scraping using HtmlAgilityPack
- **Geocoding**: OpenStreetMap's Nominatim API with multiple fallback strategies
- **Caching**: Memory caching for improved performance

## Setup and Installation
1. Ensure you have .NET 9.0 SDK installed
2. Clone the repository
3. Open in Visual Studio or your preferred IDE
4. Restore NuGet packages
5. Build and run the application

## How It Works
1. The application scrapes the Greek Fire Service website's incident data
2. It parses HTML to extract incident details including location, status, and category
3. Geocoding service converts location descriptions to map coordinates
4. The map displays incidents with appropriate markers based on type and status
5. The interface allows filtering and detailed viewing of incidents

## Configuration
- Configure API endpoints in `appsettings.json`
- Adjust geocoding parameters in the `GeocodingService.cs` file
- Customize visual aspects using the site.css file

## Logging
- Comprehensive logging with rotation is implemented
- Logs are stored in the `Logs` directory
- Current day's logs are cleared on application restart
- Previous days' logs are retained

## Disclaimer
This application is an unofficial tool and is not affiliated with, endorsed by, or connected to the Hellenic Fire Service in any way. It is an independent project that utilizes publicly available data. All incident information comes from the official Greek Fire Service website, but this application itself is not an official product of the Greek government or any fire service authority.

The data displayed may not be completely accurate or up-to-date. For official information about fire incidents in Greece, please refer to the official website of the Hellenic Fire Service.

## License
This project is provided for educational and informational purposes only.

## Credits
- Greek Fire Service for making incident data publicly available
- OpenStreetMap and Nominatim for geocoding services
- Leaflet.js for mapping capabilities
