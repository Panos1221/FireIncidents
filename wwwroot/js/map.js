// Map initialization 
let map;
let markers = [];
let warningMarkers = [];
let allIncidents = [];
let allWarnings112 = [];
let incidentModal;
let markersLayer;
let warningsLayer;
let warningHighlightsLayer;
let fireDistrictsLayer;
let fireDistrictsLabelsLayer;
let fireDistrictsData = null;
let fireDistrictsColors = new Map();
let isInitialLoad = true;

document.addEventListener('DOMContentLoaded', function () {
    initMap();
    initModal();
    setupEventListeners();
    loadIncidents();
    
    // Only load 112 warnings if enabled in configuration
    if (window.appConfig && window.appConfig.show112Warnings) {
        loadWarnings112();
    }

    // Auto-refresh every 5 minutes
    setInterval(() => {
        loadIncidents();
        // Only refresh 112 warnings if enabled in configuration
        if (window.appConfig && window.appConfig.show112Warnings) {
            loadWarnings112();
        }
    }, 5 * 60 * 1000);
});

// Listen for language changes to update the map
document.addEventListener('languageChanged', function () {
    markers.forEach(markerObj => {
        if (markerObj.marker && markerObj.marker.getPopup() && markerObj.marker.getPopup().isOpen()) {
            updateMarkerPopup(markerObj.marker, markerObj.incident);
        }
    });
});

// Leaflet map
function initMap() {
    markersLayer = L.layerGroup();
    warningsLayer = L.layerGroup();
    warningHighlightsLayer = L.layerGroup();
    fireDistrictsLayer = L.layerGroup();
    fireDistrictsLabelsLayer = L.layerGroup();

    // Center map on Greece
    map = L.map('map', {
        zoomControl: false,
        attributionControl: false
    }).setView([38.2, 23.8], 7);

    L.control.zoom({
        position: 'topright'
    }).addTo(map);

    L.control.attribution({
        position: 'bottomright',
        prefix: '<a href="https://leafletjs.com" target="_blank">Leaflet</a>'
    }).addAttribution('© <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a> contributors').addTo(map);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        detectRetina: true
    }).addTo(map);

    // Add layers in order: fire districts (bottom), highlights, markers (middle), 112 warnings (top)
    fireDistrictsLayer.addTo(map);
    fireDistrictsLabelsLayer.addTo(map);
    warningHighlightsLayer.addTo(map);
    markersLayer.addTo(map);
    warningsLayer.addTo(map);
    
    // Add zoom event listener for label visibility
    map.on('zoomend', function() {
        const currentZoom = map.getZoom();
        if (currentZoom >= 12) {
            if (!map.hasLayer(fireDistrictsLabelsLayer)) {
                fireDistrictsLabelsLayer.addTo(map);
            }
        } else {
            if (map.hasLayer(fireDistrictsLabelsLayer)) {
                map.removeLayer(fireDistrictsLabelsLayer);
            }
        }
    });
    
    // Initial check for label visibility on map load
    map.on('ready', function() {
        const currentZoom = map.getZoom();
        if (currentZoom < 12 && map.hasLayer(fireDistrictsLabelsLayer)) {
            map.removeLayer(fireDistrictsLabelsLayer);
        }
    });

    L.control.scale({
        imperial: false,
        position: 'bottomleft'
    }).addTo(map);
}

// Bootstrap modal
function initModal() {
    incidentModal = new bootstrap.Modal(document.getElementById('incidentModal'));
}

// event listeners for filters and refresh button
function setupEventListeners() {
    document.getElementById('refreshBtn').addEventListener('click', function () {
        const refreshBtn = this;
        const originalText = refreshBtn.innerHTML;

        refreshBtn.disabled = true;
        refreshBtn.innerHTML = `<i class="fas fa-spinner fa-spin me-1"></i> <span>${getText('loading')}</span>`;

        const promises = [loadIncidents()];
        
        // Only load 112 warnings if enabled in configuration
        if (window.appConfig && window.appConfig.show112Warnings) {
            promises.push(loadWarnings112());
        }
        
        Promise.all(promises).finally(() => {
            setTimeout(() => {
                refreshBtn.disabled = false;
                refreshBtn.innerHTML = originalText;
            }, 500);
        });
    });

    // Setup status filter checkboxes
    document.getElementById('ongoingCheck').addEventListener('change', filterIncidents);
    document.getElementById('partialControlCheck').addEventListener('change', filterIncidents);
    
    // fire districts toggle
    document.getElementById('fireDistrictsCheck').addEventListener('change', function() {
        if (this.checked) {
            showFireDistricts();
        } else {
            hideFireDistricts();
        }
    });
    
    // Load fire districts data
    loadFireDistricts();
    document.getElementById('fullControlCheck').addEventListener('change', filterIncidents);

    // Setup category filter checkboxes
    document.getElementById('forestFiresCheck').addEventListener('change', filterIncidents);
    document.getElementById('urbanFiresCheck').addEventListener('change', filterIncidents);
    document.getElementById('assistanceCheck').addEventListener('change', filterIncidents);
}

// Get translation text
function getText(key) {
    return window.translations ? window.translations.getText(key) : key;
}

// Load incidents from API
async function loadIncidents() {
    try {
        console.log("Fetching incident data from API...");

        document.getElementById('lastUpdated').textContent = getText('loading');
        document.getElementById('lastUpdated').classList.add('loading-text');

        const response = await fetch('/api/incidents');

        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        const responseText = await response.text();
        console.log("Raw API response:", responseText.substring(0, 500) + "...");

        let data;
        try {
            data = JSON.parse(responseText);
        } catch (parseError) {
            console.error("Error parsing JSON:", parseError);
            throw new Error("Invalid JSON response from server");
        }

        allIncidents = data;
        console.log(`Received ${allIncidents.length} incidents`);

        clearMarkers();

        filterIncidents();

        // Update last updated time
        document.getElementById('lastUpdated').classList.remove('loading-text');
        document.getElementById('lastUpdated').textContent = new Date().toLocaleTimeString();

        return data;
    } catch (error) {
        console.error('Error loading incidents:', error);
        document.getElementById('lastUpdated').classList.remove('loading-text');
        document.getElementById('lastUpdated').textContent = getText('error') + ": " + error.message;
        throw error;
    }
}

// Load 112 warnings from API
async function loadWarnings112() {
    try {
        console.log("Fetching 112 warnings from API...");

        const response = await fetch('/api/warnings112');

        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        const responseText = await response.text();
        console.log("Raw 112 warnings API response:", responseText.substring(0, 500) + "...");

        let data;
        try {
            data = JSON.parse(responseText);
        } catch (parseError) {
            console.error("Error parsing 112 warnings JSON:", parseError);
            throw new Error("Invalid JSON response from server");
        }

        allWarnings112 = data;
        console.log(`Received ${allWarnings112.length} 112 warnings`);

        clearWarnings();
        addWarningsToMap(allWarnings112);

        return data;
    } catch (error) {
        console.error('Error loading 112 warnings:', error);
        return [];
    }
}

// Add incidents to the map
function addIncidentsToMap(incidents) {
    if (!incidents || incidents.length === 0) {
        console.warn("No incidents to display on the map");
        return;
    }

    incidents.forEach((incident, index) => {
        // Skip incidents without coordinates
        if (!incident.latitude || !incident.longitude) {
            console.warn(`Incident without coordinates: ${incident.category} - ${incident.location}`);
            return;
        }

        setTimeout(() => {
            try {
                // Get marker image based on incident properties
                let statusCode = getStatusCode(incident.status);
                let categoryCode = getCategoryCode(incident.category);
                let markerImage = `${categoryCode}-${statusCode}.png`;

                console.log(`Creating marker for incident at ${incident.latitude}, ${incident.longitude} with image ${markerImage}`);

                // Create marker with proper icon
                createMarker(incident, markerImage);
            } catch (error) {
                console.error("Error creating marker:", error);
                createMarker(incident, null);
            }
        }, index * 5); // Small delay for each marker
    });
}

// Add 112 warnings to the map
function addWarningsToMap(warnings) {
    if (!warnings || warnings.length === 0) {
        console.warn("No 112 warnings to display on the map");
        return;
    }

    warnings.forEach((warning, index) => {
        // Skip warnings without geocoded locations
        if (!warning.geocodedLocations || warning.geocodedLocations.length === 0) {
            console.warn(`Warning without geocoded locations: ${warning.id}`);
            return;
        }

        setTimeout(() => {
            try {
                createWarningMarkers(warning);
                createWarningHighlights(warning);
            } catch (error) {
                console.error("Error creating warning markers:", error);
            }
        }, index * 10); // Small delay for each warning
    });
}

// Create warning markers and highlights
function createWarningMarkers(warning) {
    // Ensure we have a valid iconType
    const iconType = warning.iconType || 'red';
    const markerImage = `112warning_${iconType}.png`;
    
    if (!warning.iconType) {
        console.warn('Warning missing iconType, using default "red":', warning);
    }
    
    warning.geocodedLocations.forEach(location => {
        if (!location.isGeocoded) return;
        
        try {
            const icon = L.icon({
                iconUrl: `/images/markers/${markerImage}`,
                iconSize: [32, 32],
                iconAnchor: [16, 32],
                popupAnchor: [0, -32],
                shadowUrl: '/images/markers/marker-shadow.png',
                shadowSize: [41, 41],
                shadowAnchor: [13, 41]
            });

            const marker = L.marker([location.latitude, location.longitude], {
                icon: icon,
                title: `112 Warning - ${location.locationName}`,
                riseOnHover: true,
                alt: warning.warningType
            });

            updateWarningPopup(marker, warning, location);
            marker.on('click', () => showWarningDetails(warning));
            marker.addTo(warningsLayer);

            warningMarkers.push({
                marker: marker,
                warning: warning,
                location: location
            });

        } catch (error) {
            console.warn(`Error creating warning marker: ${error.message}`);
        }
    });
}

// Create yellow highlight areas for warnings
function createWarningHighlights(warning) {
    warning.geocodedLocations.forEach(location => {
        if (!location.isGeocoded) return;
        
        try {
            // circular highlight area around the warning location
            const circle = L.circle([location.latitude, location.longitude], {
                color: '#FFD700',
                fillColor: '#FFFF00',
                fillOpacity: 0.2,
                radius: 2000, // 2km radius probably need bigger
                weight: 2,
                className: 'warning-highlight'
            });

            circle.bindTooltip(`⚠️ ${warning.warningType} - ${location.locationName}`, {
                permanent: false,
                direction: 'top',
                className: 'warning-tooltip'
            });

            circle.addTo(warningHighlightsLayer);
        } catch (error) {
            console.warn(`Error creating warning highlight: ${error.message}`);
        }
    });
}

// Update warning popup content
function updateWarningPopup(marker, warning, location) {
    const currentLanguage = getCurrentLanguage();
    const content = warning.getDisplayContent ? warning.getDisplayContent(currentLanguage) : 
                   (currentLanguage === 'el' && warning.greekContent ? warning.greekContent : warning.englishContent);
    
    const shortContent = content ? content.substring(0, 100) + (content.length > 100 ? '...' : '') : '';
    
    const popupContent = `
        <div class="map-popup warning-popup">
            <strong>⚠️ ${warning.warningType || '112 Warning'}</strong><br>
            <strong>${getText('location')}:</strong> ${location.locationName}<br>
            <div class="warning-content">${shortContent}</div>
            <small><em>${getText('clickForDetails')}</em></small>
        </div>
    `;

    marker.bindPopup(popupContent, {
        className: 'custom-popup warning-popup-container',
        closeButton: true,
        autoClose: true,
        closeOnEscapeKey: true,
        maxWidth: 300
    });
}

// Show warning details in modal
function showWarningDetails(warning) {
    const modalBody = document.getElementById('incidentModalBody');
    const modalTitle = document.getElementById('incidentModalLabel');
    const currentLanguage = getCurrentLanguage();
    
    modalTitle.textContent = `⚠️ ${warning.warningType || '112 Emergency Warning'}`;
    
    const content = warning.getDisplayContent ? warning.getDisplayContent(currentLanguage) : 
                   (currentLanguage === 'el' && warning.greekContent ? warning.greekContent : warning.englishContent);
    
    const locations = warning.geocodedLocations && warning.geocodedLocations.length > 0 
        ? warning.geocodedLocations.map(l => l.locationName).join(', ')
        : warning.locations ? warning.locations.join(', ') : getText('unknown');

    modalBody.innerHTML = `
        <div class="warning-detail">
            <span class="incident-label">${getText('warningType')}:</span> 
            <span class="warning-type">${warning.warningType || '112 Warning'}</span>
        </div>
        <div class="warning-detail">
            <span class="incident-label">${getText('locations')}:</span> 
            ${locations}
        </div>
        <div class="warning-detail">
            <span class="incident-label">${getText('alertTime')}:</span> 
            ${new Date(warning.tweetDate).toLocaleString()}
        </div>
        <div class="warning-detail">
            <span class="incident-label">${getText('status')}:</span> 
            <span class="warning-status warning-${warning.iconType}">
                ${warning.iconType === 'red' ? getText('activeWarning') : getText('expiredWarning')}
            </span>
        </div>
        <div class="warning-content-full">
            <span class="incident-label">${getText('message')}:</span>
            <div class="warning-text">${content || getText('noContentAvailable')}</div>
        </div>
        ${warning.sourceUrl ? `
        <div class="warning-detail">
            <span class="incident-label">${getText('source')}:</span> 
            <a href="${warning.sourceUrl}" target="_blank" rel="noopener">112Greece Twitter</a>
        </div>
        ` : ''}
    `;

    incidentModal.show();
}

// Clear all warning markers and highlights
function clearWarnings() {
    warningsLayer.clearLayers();
    warningHighlightsLayer.clearLayers();
    warningMarkers = [];
}

// Get current language setting
function getCurrentLanguage() {
    const languageSelector = document.getElementById('languageSelector');
    return languageSelector ? languageSelector.value : 'en';
}

// status code for marker image
function getStatusCode(status) {
    switch (status) {
        case "ΣΕ ΕΞΕΛΙΞΗ": return "ongoing";
        case "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ": return "partial";
        case "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ": return "controlled";
        default: return "unknown";
    }
}

// category code for marker image
function getCategoryCode(category) {
    switch (category) {
        case "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ": return "forest-fire";
        case "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ": return "urban-fire";
        case "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ": return "assistance";
        default: return "unknown";
    }
}

// Create a map marker
function createMarker(incident, markerImage) {
    let marker;

    // Convert coordinates to decimal degrees
    // Handle coordinates that come as large integers (e.g., 370551454 -> 37.0551454)
    let lat = incident.latitude;
    let lng = incident.longitude;
    
    // Convert large integer coordinates to decimal degrees
    if (lat > 1000) {
        const latStr = lat.toString();
        if (latStr.length >= 8) {
            // Insert decimal point after first 2 digits for Greek coordinates
            // 370551454 -> 37.0551454, 40239881 -> 40.239881
            lat = parseFloat(latStr.substring(0, 2) + '.' + latStr.substring(2));
        }
    }
    
    if (lng > 1000) {
        const lngStr = lng.toString();
        if (lngStr.length >= 8) {
            // Insert decimal point after first 2 digits for Greek coordinates
            // 221139316 -> 22.1139316, 232853943 -> 23.2853943
            lng = parseFloat(lngStr.substring(0, 2) + '.' + lngStr.substring(2));
        }
    }

    console.log(`Converting coordinates: ${incident.latitude}, ${incident.longitude} -> ${lat}, ${lng}`);

    try {
        const icon = L.icon({
            iconUrl: `/images/markers/${markerImage}`,
            iconSize: [32, 32],
            iconAnchor: [16, 32],
            popupAnchor: [0, -32],
            shadowUrl: '/images/markers/marker-shadow.png',
            shadowSize: [41, 41],
            shadowAnchor: [13, 41]
        });

        marker = L.marker([lat, lng], {
            icon: icon,
            title: `${incident.category} - ${incident.location || getText('unknown')}`,
            riseOnHover: true, 
            alt: incident.status
        });
    } catch (error) {
        console.warn(`Error creating custom marker: ${error.message}. Using default marker.`);
        marker = L.marker([lat, lng], {
            title: `${incident.category} - ${incident.location || getText('unknown')}`,
            riseOnHover: true
        });
    }

    updateMarkerPopup(marker, incident);

    marker.on('click', () => showIncidentDetails(incident));

    marker.addTo(markersLayer);

    markers.push({
        marker: marker,
        incident: incident
    });

    return marker;
}

function updateMarkerPopup(marker, incident) {
    const statusClass = getStatusClass(incident.status);

    const popupContent = `
        <div class="map-popup">
            <strong>${incident.category}</strong><br>
            ${incident.location || getText('unknown')}, ${incident.municipality || getText('unknown')}<br>
            <span class="${statusClass}">${incident.status}</span>
        </div>
    `;

    marker.bindPopup(popupContent, {
        className: 'custom-popup',
        closeButton: true,
        autoClose: true,
        closeOnEscapeKey: true
    });
}

// CSS class for incident status
function getStatusClass(status) {
    switch (status) {
        case "ΣΕ ΕΞΕΛΙΞΗ": return "status-ongoing";
        case "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ": return "status-partial";
        case "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ": return "status-controlled";
        default: return "";
    }
}

// Show incident details in modal
function showIncidentDetails(incident) {
    const modalBody = document.getElementById('incidentModalBody');
    const modalTitle = document.getElementById('incidentModalLabel');

    modalTitle.textContent = `${incident.category} - ${incident.location || getText('unknown')}`;

    const statusClass = getStatusClass(incident.status);

    modalBody.innerHTML = `
        <div class="incident-detail">
            <span class="incident-label">${getText('status')}:</span> 
            <span class="${statusClass}">${incident.status}</span>
        </div>
        <div class="incident-detail">
            <span class="incident-label">${getText('region')}:</span> 
            ${incident.region || getText('unknown')}
        </div>
        <div class="incident-detail">
            <span class="incident-label">${getText('municipality')}:</span> 
            ${incident.municipality || getText('unknown')}
        </div>
        <div class="incident-detail">
            <span class="incident-label">${getText('details')}:</span> 
            ${incident.location || getText('unknown')}
        </div>
        <div class="incident-detail">
            <span class="incident-label">${getText('startDate')}:</span> 
            ${incident.startDate || getText('unknown')}
        </div>
        <div class="incident-detail">
            <span class="incident-label">${getText('lastUpdated')}:</span> 
            ${incident.lastUpdate || getText('unknown')}
        </div>
    `;

    incidentModal.show();
}

// Clear all markers from the map
function clearMarkers() {
    markersLayer.clearLayers();
    markers = [];
}

// Filter incidents based on user selections
function filterIncidents() {
    const ongoingChecked = document.getElementById('ongoingCheck').checked;
    const partialControlChecked = document.getElementById('partialControlCheck').checked;
    const fullControlChecked = document.getElementById('fullControlCheck').checked;

    const forestFiresChecked = document.getElementById('forestFiresCheck').checked;
    const urbanFiresChecked = document.getElementById('urbanFiresCheck').checked;
    const assistanceChecked = document.getElementById('assistanceCheck').checked;

    console.log(`Filtering with status: Ongoing=${ongoingChecked}, Partial=${partialControlChecked}, Full=${fullControlChecked}, Forest=${forestFiresChecked}, Urban=${urbanFiresChecked}, Assistance=${assistanceChecked}`);

    clearMarkers();

    // Filter incidents
    const filteredIncidents = allIncidents.filter(incident => {
        // Check status filter
        if (incident.status === "ΣΕ ΕΞΕΛΙΞΗ" && !ongoingChecked) {
            return false;
        }
        if (incident.status === "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ" && !partialControlChecked) {
            return false;
        }
        if (incident.status === "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ" && !fullControlChecked) {
            return false;
        }

        // Check category filters
        if (incident.category === "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" && !forestFiresChecked) {
            return false;
        }
        if (incident.category === "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ" && !urbanFiresChecked) {
            return false;
        }
        if (incident.category === "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ" && !assistanceChecked) {
            return false;
        }

        return true;
    });

    console.log(`Filtered down to ${filteredIncidents.length} incidents`);

    addIncidentsToMap(filteredIncidents);

    updateStatistics(filteredIncidents);

    if (filteredIncidents.length > 0) {
        fitMapToMarkers();

        if (isInitialLoad) {
            isInitialLoad = false;
        }
    }
}

// Fit map to show all markers
function fitMapToMarkers() {
    if (markers.length === 0) return;

    const markerBounds = L.latLngBounds(markers.map(m => m.marker.getLatLng()));
    map.fitBounds(markerBounds, {
        padding: [50, 50],
        maxZoom: 12,
        animate: true,
        duration: 0.5
    });
}

// Update statistics display
function updateStatistics(incidents) {
    const totalIncidents = incidents.length;
    const forestFireCount = incidents.filter(i => i.category === "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ").length;
    const urbanFireCount = incidents.filter(i => i.category === "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ").length;
    const assistanceCount = incidents.filter(i => i.category === "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ").length;
    
    animateCounter('totalIncidents', totalIncidents);
    animateCounter('forestFireCount', forestFireCount);
    animateCounter('urbanFireCount', urbanFireCount);
    animateCounter('assistanceCount', assistanceCount);
    
    // Update warnings count only if 112 warnings are enabled and element exists
    if (window.appConfig && window.appConfig.show112Warnings) {
        const warningsCount = allWarnings112.length;
        const warningsElement = document.getElementById('warningsCount');
        if (warningsElement) {
            animateCounter('warningsCount', warningsCount);
        }
    }
}

function animateCounter(elementId, targetValue) {
    const element = document.getElementById(elementId);
    const currentValue = parseInt(element.textContent) || 0;
    const duration = 750; // Animation duration in ms
    const step = 25; // Update every 25ms

    if (Math.abs(targetValue - currentValue) > 5) {
        let current = currentValue;
        const increment = (targetValue - currentValue) / (duration / step);

        const timer = setInterval(() => {
            current += increment;

            if ((increment > 0 && current >= targetValue) ||
                (increment < 0 && current <= targetValue)) {
                clearInterval(timer);
                element.textContent = targetValue;
            } else {
                element.textContent = Math.round(current);
            }
        }, step);
    } else {
        element.textContent = targetValue;
    }
}

// Fire Districts Functions
async function loadFireDistricts() {
    try {
        console.log('Loading fire department districts...');
        const response = await fetch('/api/fire-districts');
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        fireDistrictsData = await response.json();
        console.log('Fire districts data loaded:', fireDistrictsData);
        
        // Generate colors for each district
        generateDistrictColors();
        
        // Enable toggle button
        const toggleButton = document.getElementById('fireDistrictsCheck');
        toggleButton.disabled = false;
        
        console.log('Fire districts loaded successfully');
    } catch (error) {
        console.error('Error loading fire districts:', error);
        // toggle disabled on error
    }
}

function generateDistrictColors() {
    if (!fireDistrictsData || !fireDistrictsData.features) {
        return;
    }
    
    const districts = fireDistrictsData.features;

    const colorPalette = [
    // Reds & Oranges
    '#FF0000', '#FF4500', '#FF6347', '#FF7F50', '#FF8C00',
    '#FFA500', '#FFD700', '#FFB347',

    // Yellows & Warm tones
    '#FFFF00', '#F0E68C', '#FFDAB9', '#FFE4B5', '#F5DEB3',

    // Greens
    '#32CD32', '#00FF00', '#7CFC00', '#98FB98', '#00FA9A',
    '#2E8B57', '#3CB371', '#228B22', '#ADFF2F',

    // Cyans & Teals
    '#00CED1', '#20B2AA', '#40E0D0', '#48D1CC', '#5F9EA0',

    // Blues
    '#1E90FF', '#4169E1', '#0000FF', '#6495ED', '#87CEEB',
    '#4682B4', '#00BFFF', '#7B68EE',

    // Purples & Violets
    '#8A2BE2', '#9932CC', '#BA55D3', '#DA70D6', '#9400D3',
    '#EE82EE', '#DDA0DD', '#C71585',

    // Pinks
    '#FF69B4', '#FF1493', '#DB7093', '#FFC0CB', '#FFB6C1',

    // Browns & Neutrals
    '#8B4513', '#A0522D', '#CD853F', '#D2691E', '#DEB887',
    '#BC8F8F', '#BDB76B', '#808000'
    ];
    
    // Shuffle the palette
    const shuffledPalette = [...colorPalette];
    for (let i = shuffledPalette.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [shuffledPalette[i], shuffledPalette[j]] = [shuffledPalette[j], shuffledPalette[i]];
    }
    
    districts.forEach((district, index) => {
        const stationName = district.properties.PYR_YPIRES;
        const color = shuffledPalette[index % shuffledPalette.length];
        fireDistrictsColors.set(stationName, color);
    });
}

// Coordinate transformation cache for performance
const coordinateCache = new Map();

function getCachedCoordinate(x, y) {
    const key = `${x},${y}`;
    if (coordinateCache.has(key)) {
        return coordinateCache.get(key);
    }
    
    const transformed = proj4('EPSG:2100', 'EPSG:4326', [x, y]);
    const result = [transformed[1], transformed[0]];
    coordinateCache.set(key, result);
    return result;
}

function showFireDistricts() {
    if (!fireDistrictsData || !fireDistrictsData.features) {
        console.warn('No fire districts data available');
        return;
    }
    
    console.log(`Processing ${fireDistrictsData.features.length} fire districts...`);
    
    // loading indicator
    showDistrictsLoadingIndicator(true);
    
    // Clear coordinate cache
    coordinateCache.clear();
    
    // Greek Grid EPSG:2100 for coordinates system
    if (!proj4.defs('EPSG:2100')) {
        proj4.defs('EPSG:2100', '+proj=tmerc +lat_0=0 +lon_0=24 +k=0.9996 +x_0=500000 +y_0=0 +ellps=GRS80 +towgs84=-199.87,74.79,246.62,0,0,0,0 +units=m +no_defs');
    }
    
    let successCount = 0;
    let errorCount = 0;
    const districts = fireDistrictsData.features;
    const chunkSize = 10; // How many districts we process each time by default 10 
    let currentIndex = 0;
    
    function processChunk() {
        const endIndex = Math.min(currentIndex + chunkSize, districts.length);
        
        for (let index = currentIndex; index < endIndex; index++) {
            const district = districts[index];
        try {
            const stationName = district.properties.PYR_YPIRES || `District ${index + 1}`;
            // validate station name we got above
            if (!stationName || stationName.trim() === '') {
                errorCount++;
                return;
            }

            // geometry type - support for both Polygon and MultiPolygon
             if (district.geometry.type !== 'Polygon' && district.geometry.type !== 'MultiPolygon') {
                 errorCount++;
                 return;
             }

             const color = fireDistrictsColors.get(stationName) || '#3388ff';
             
             // Convert coordinates from Greek Grid (EPSG:2100) to WGS84 using proj4js
             // Handle both Polygon and MultiPolygon geometries
             let polygonsToProcess = [];
             
             if (district.geometry.type === 'Polygon') {
                 polygonsToProcess = [district.geometry.coordinates[0]]; // Single polygon outer ring
             } else if (district.geometry.type === 'MultiPolygon') {
                 // Process all polygons in MultiPolygon
                 polygonsToProcess = district.geometry.coordinates.map(polygon => polygon[0]); // All outer rings
             }
             
             // Process each polygon separately
             polygonsToProcess.forEach((rawCoordinates, polygonIndex) => {
                 // Validate coordinates exist and have data)
                 if (!rawCoordinates || rawCoordinates.length < 3) {
                     return;
                 }
                 
                 const coordinates = rawCoordinates.map((coord, coordIndex) => {
                try {
                    // Validate input coordinates
                    if (!Array.isArray(coord) || coord.length !== 2) {
                        return null;
                    }
                    
                    const [x, y] = coord;
                    
                    // Check if coordinates are finite numbers
                    if (!isFinite(x) || !isFinite(y) || x === null || y === null || x === undefined || y === undefined) {
                        return null;
                    }
                    
                    // Use cached coordinate transformation
                    const result = getCachedCoordinate(x, y);
                    
                    // Validate transformed coordinates (based on default central Greece roughly 34-42°N, 19-30°E)
                    if (isNaN(result[0]) || isNaN(result[1]) || 
                        result[0] < 30 || result[0] > 45 || 
                        result[1] < 15 || result[1] > 35) {
                        return null;
                    }
                    
                    return result;
                } catch (coordError) {
                    return null;
                }
                 }).filter(coord => coord !== null);
                 
                 // Validate coordinates for this polygon
                 if (coordinates.length === 0) {
                     return; // Skip this polygon
                 }
                 
                 const polygon = L.polygon(coordinates, {
                     color: color,
                     weight: 2,
                     opacity: 0.8,
                     fillColor: color,
                     fillOpacity: 0.2
                 });
                 
                 // popup py name
                 const popupText = `<strong>${stationName}</strong>`;
                 polygon.bindPopup(popupText);
                 
                 fireDistrictsLayer.addLayer(polygon);
             });
             
             // label at the center of all polygons for this district
             if (polygonsToProcess.length > 0) {
                 // Calculate center from all valid polygons
                 let allBounds = null;
                 polygonsToProcess.forEach(rawCoordinates => {
                     if (rawCoordinates && rawCoordinates.length >= 3) {
                         const coords = rawCoordinates.map(coord => {
                             if (Array.isArray(coord) && coord.length === 2) {
                                 const [x, y] = coord;
                                 if (isFinite(x) && isFinite(y)) {
                                     return getCachedCoordinate(x, y);
                                 }
                             }
                             return null;
                         }).filter(coord => coord !== null);
                         
                         if (coords.length > 0) {
                             const tempPolygon = L.polygon(coords);
                             const bounds = tempPolygon.getBounds();
                             if (!allBounds) {
                                 allBounds = bounds;
                             } else {
                                 allBounds.extend(bounds);
                             }
                         }
                     }
                 });
                 
                 if (allBounds) {
                     const center = allBounds.getCenter();
                     const label = L.marker(center, {
                         icon: L.divIcon({
                             className: 'fire-district-label',
                             html: `<div style="background: rgba(255,255,255,0.9); padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: bold; color: #333; border: 1px solid #ff6b35; box-shadow: 0 1px 2px rgba(0,0,0,0.3); white-space: nowrap; text-shadow: 1px 1px 1px rgba(255,255,255,0.8);">${stationName}</div>`,
                             iconSize: [100, 16],
                             iconAnchor: [50, 8]
                         })
                     });
                     // Only add label if zoom level is appropriate
                     const currentZoom = map.getZoom();
                     if (currentZoom >= 12) {
                         fireDistrictsLabelsLayer.addLayer(label);
                     }
                 }
             }
             
             successCount++;
            
        } catch (error) {
            console.error(`Error processing district ${index}:`, error);
            errorCount++;
        }
        }
        
        currentIndex = endIndex;
        
        // Update progress
        const progress = Math.round((currentIndex / districts.length) * 100);
        updateDistrictsProgress(progress);
        
        // Continue processing if there are more districts
        if (currentIndex < districts.length) {
            setTimeout(processChunk, 10);
        } else {
            // completed operation
            console.log(`Fire districts processing complete: ${successCount} successful, ${errorCount} errors`);
            showDistrictsLoadingIndicator(false);
        }
    }
    
    processChunk();
}

function hideFireDistricts() {
    console.log('Hiding fire districts from map');
    fireDistrictsLayer.clearLayers();
    fireDistrictsLabelsLayer.clearLayers();
    showDistrictsLoadingIndicator(false);
}

// Loading indicator functions
function showDistrictsLoadingIndicator(show) {
    let indicator = document.getElementById('districts-loading-indicator');
    
    if (show) {
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'districts-loading-indicator';
            indicator.innerHTML = `
                <div style="position: fixed; top: 50%; left: 50%; transform: translate(-50%, -50%); 
                           background: rgba(255,255,255,0.95); padding: 20px; border-radius: 8px; 
                           box-shadow: 0 4px 12px rgba(0,0,0,0.3); z-index: 10000; text-align: center;
                           border: 2px solid #ff6b35;">
                    <div style="font-size: 16px; font-weight: bold; color: #333; margin-bottom: 10px;" data-translate="loadingfiredistricts">${getText('loadingfiredistricts')}</div>
                    <div style="width: 200px; height: 6px; background: #f0f0f0; border-radius: 3px; overflow: hidden;">
                        <div id="districts-progress-bar" style="width: 0%; height: 100%; background: #ff6b35; transition: width 0.3s ease;"></div>
                    </div>
                    <div id="districts-progress-text" style="font-size: 12px; color: #666; margin-top: 5px;">0%</div>
                </div>
            `;
            document.body.appendChild(indicator);
            
            // Apply translations to the newly created element
            if (window.translations && window.translations.updatePageTranslations) {
                window.translations.updatePageTranslations();
            }
        }
        indicator.style.display = 'block';
    } else {
        if (indicator) {
            indicator.style.display = 'none';
        }
    }
}

function updateDistrictsProgress(percentage) {
    const progressBar = document.getElementById('districts-progress-bar');
    const progressText = document.getElementById('districts-progress-text');
    
    if (progressBar) {
        progressBar.style.width = percentage + '%';
    }
    if (progressText) {
        progressText.textContent = percentage + '%';
    }
}