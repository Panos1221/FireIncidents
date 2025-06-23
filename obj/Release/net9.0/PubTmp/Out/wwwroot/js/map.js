// Map initialization 
let map;
let markers = [];
let allIncidents = [];
let incidentModal;
let markersLayer;
let isInitialLoad = true;

document.addEventListener('DOMContentLoaded', function () {
    initMap();
    initModal();
    setupEventListeners();
    loadIncidents();

    // Auto-refresh every 5 minutes
    setInterval(loadIncidents, 5 * 60 * 1000);
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

    markersLayer.addTo(map);

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

        loadIncidents().finally(() => {
            setTimeout(() => {
                refreshBtn.disabled = false;
                refreshBtn.innerHTML = originalText;
            }, 500);
        });
    });

    // Setup status filter checkboxes
    document.getElementById('ongoingCheck').addEventListener('change', filterIncidents);
    document.getElementById('partialControlCheck').addEventListener('change', filterIncidents);
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

        marker = L.marker([incident.latitude, incident.longitude], {
            icon: icon,
            title: `${incident.category} - ${incident.location || getText('unknown')}`,
            riseOnHover: true, 
            alt: incident.status
        });
    } catch (error) {
        console.warn(`Error creating custom marker: ${error.message}. Using default marker.`);
        marker = L.marker([incident.latitude, incident.longitude], {
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