# Hellenic Fire Service Live Incidents Map

## Overview
This web application provides real-time visualization of active incidents throughout Greece. It scrapes data from the official Greek Fire Service website and displays incidents on an interactive map with filtering capabilities.

## Live Demo
The application is hosted and can be accessed at:
- [https://livefireincidents.gr](https://livefireincidents.gr)
- [Azure Site](https://hfcliveincidents-hkcebcfdefgjcuh8.italynorth-01.azurewebsites.net/)

The application is currently hosted on both Azure and SmarterASP.NET. This dual-hosting approach was implemented to gain experience with different cloud hosting services and compare their performance characteristics.

### Technical Details
- Data is automatically refreshed every 5 minutes to conserve server resources. You can manually refresh the data using the `Refresh Data (Ανανέωση Δεδομένων)` button.
- To modify the refresh rate, edit the `setInterval` call in the `wwwroot/js/map.js` file (line ~15)
- The application is deployed on SmarterASP.NET

### Azure Free Tier Limitations
Please note that this application is hosted on Azure's Free Tier, which has the following limitations:
- The application automatically sleeps after 20 minutes of inactivity
- Limited to 60 minutes of CPU time per day
- Shared infrastructure with other free tier applications
- Cold starts may take 1-2 minutes when the application has been inactive

Due to these limitations, the site may occasionally be unavailable or slow to load, especially during periods of inactivity. If you encounter the default Azure placeholder page or slow loading times, please wait a few minutes and try again as the application may be waking up from sleep mode.

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

## Learning Journey

This project has been a great learning experience, providing insights into several areas of web development:

### Technical Skills Acquired
- **Web Scraping Challenges**: Learned to handle complex HTML parsing, character encoding issues, and dynamic content extraction
- **Geocoding Implementation**: Developed multi-layered fallback strategies for converting textual addresses to map coordinates

### DevOps & Deployment Insights
- **Multi-Platform Deployment**: Gained experience deploying to both Azure and SmarterASP.NET

## Credits
- Hellenic Fire Service for making incident data publicly available
- OpenStreetMap and Nominatim for geocoding services
- Leaflet.js for mapping capabilities
