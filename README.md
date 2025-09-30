# Hellenic Fire Service Live Incidents Map

Real‑time fire incidents across Greece, including 112 warnings.

## Live Demo
- https://livefireincidents.gr

> Previously hosted on Azure and SmarterASP.NET. Today the app runs on a Raspberry Pi.

## Overview
This project visualizes active incidents from the official Hellenic Fire Service and surfaces 112 warnings on an interactive map.

![Fire Incidents Features](https://github.com/Panos1221/FireIncidents/raw/main/fireincidents-features.png)

## Highlights
- Real‑time incidents from the Hellenic Fire Service.
- 112 warnings via self‑hosted Nitter, displayed in a custom widget.
- Location extraction and geocoding to place warnings and incidents on the map.
- Interactive map with categories and status filters.
- Fire station district boundaries overlay (GeoJSON) from official data via http://geodata.gov.gr/.
- Live statistics.
- Notifications about new Incidents using SignalR.
- Multilingual (Greek & English) and Dark/Light mode.

## 112 Warnings
- Nitter is hosted on a Raspberry Pi and monitors the official 112 account (@112Greece).
- Tweets are parsed to extract locations from posts/hashtags.
- Extracted locations are geocoded and shown on the map as 112 warnings.

## Incident Geocoding
- Incidents are retrieved from the official [Hellenic Fire Service Website](https://museum.fireservice.gr/symvanta/).
- Each incident card is parsed to extract details such as location, status, and category.
- Geocoding service is then used to place the extracted location on the map using first the Greek Dataset (Information about it on Credits section). If no match is found, Nominatim is used for direct search.
- Incidents are plotted on the map at municipality level (approximate location, not exact address since exact address is not provided).

## Official Boundaries Overlay
- Fire station boundaries are based on official open data from http://geodata.gov.gr/.
- These boundaries are rendered as a GeoJSON overlay on the map to provide clear district context around incidents and 112 warnings.

## Hosting & Cost
- Self‑hosted on a Raspberry Pi for low running costs and full control.
- Previously deployed to Azure and SmarterASP.NET for experimentation and comparison but the cost to keep the application hosted there is big.

## Disclaimer
This application is an unofficial tool and is not affiliated with, endorsed by, or connected to the Hellenic Fire Service in any way. It is an independent project that utilizes publicly available data. All incident information comes from the official [Hellenic Fire Service Website](https://museum.fireservice.gr/symvanta/), but this application itself is not an official product of the Greek government or any fire service authority.

The data displayed may not be completely accurate or up-to-date.

## Credits
- Hellenic Fire Service for making incident data publicly available
- OpenStreetMap and Nominatim for geocoding services
- Leaflet for mapping capabilities
- Nitter for monitoring capabilities of accounts.
- Aris Papaprodromou for his dataset about Greek Cities & Villages which is used for geocoding https://github.com/arispapapro/Greek-Cities-and-Villages-Geolocation-Dataset?utm_source=chatgpt.com

## License
This project is provided for educational and informational purposes only.
