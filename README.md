# Hellenic Fire Service Live Incidents Map

## Overview
This web application provides real-time visualization of active incidents throughout Greece. It scrapes data from the official Greek Fire Service website and displays incidents on an interactive map with filtering capabilities.

## Live Demo
The application is hosted on Azure and can be accessed [here](https://hfcliveincidents-hkcebcfdefgjcuh8.italynorth-01.azurewebsites.net/).

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
1. The application scrapes the Hellenic Fire Service website's incident data
2. It parses HTML to extract incident details including location, status, and category
3. Geocoding service converts location descriptions to map coordinates
4. The map displays incidents with appropriate markers based on type and status
5. The interface allows filtering and detailed viewing of incidents

## Disclaimer
This application is an unofficial tool and is not affiliated with, endorsed by, or connected to the Hellenic Fire Service in any way. It is an independent project that utilizes publicly available data. All incident information comes from the official [Hellenic Fire Service Website](https://museum.fireservice.gr/symvanta/), but this application itself is not an official product of the Greek government or any fire service authority.

The data displayed may not be completely accurate or up-to-date.

## License
This project is provided for educational and informational purposes only.

## Credits
- Hellenic Fire Service for making incident data publicly available
- OpenStreetMap and Nominatim for geocoding services
- Leaflet.js for mapping capabilities
