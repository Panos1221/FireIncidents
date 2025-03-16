// Map initialization and incident data handling with translations support
let map;
let markers = [];
let allIncidents = [];
let incidentModal;
let markersLayer;
let isInitialLoad = true;

// Initialize the map when the document is loaded
document.addEventListener('DOMContentLoaded', function () {
    initMap();
    initModal();
    setupEventListeners();
    loadIncidents();

    // Auto-refresh data every 5 minutes
    setInterval(loadIncidents, 5 * 60 * 1000);
});

// Listen for language changes to update the map
document.addEventListener('languageChanged', function () {
    // Update any visible popups
    markers.forEach(markerObj => {
        if (markerObj.marker && markerObj.marker.getPopup() && markerObj.marker.getPopup().isOpen()) {
            updateMarkerPopup(markerObj.marker, markerObj.incident);
        }
    });
});

// Initialize the Leaflet map with better styling
function initMap() {
    // Create a markers layer group to manage all markers
    markersLayer = L.layerGroup();

    // Center map on Greece
    map = L.map('map', {
        zoomControl: false,
        attributionControl: false
    }).setView([38.2, 23.8], 7);

    // Custom zoom control on the right side
    L.control.zoom({
        position: 'topright'
    }).addTo(map);

    // Add attribution with custom styling
    L.control.attribution({
        position: 'bottomright',
        prefix: '<a href="https://leafletjs.com" target="_blank">Leaflet</a>'
    }).addAttribution('© <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a> contributors').addTo(map);

    // Add OpenStreetMap tile layer with retina support for sharper display
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        detectRetina: true
    }).addTo(map);

    // Add the markers layer to the map
    markersLayer.addTo(map);

    // Add scale control
    L.control.scale({
        imperial: false,
        position: 'bottomleft'
    }).addTo(map);
}

// Initialize the Bootstrap modal
function initModal() {
    incidentModal = new bootstrap.Modal(document.getElementById('incidentModal'));
}

// Set up event listeners for filters and refresh button
function setupEventListeners() {
    document.getElementById('refreshBtn').addEventListener('click', function () {
        // Add loading animation to button
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

// Get translation text helper function
function getText(key) {
    return window.translations ? window.translations.getText(key) : key;
}

// Load incidents from API
async function loadIncidents() {
    try {
        console.log("Fetching incident data from API...");

        // Show loading indicator
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

        // Clear existing markers
        clearMarkers();

        // Apply initial filtering rather than showing all incidents
        // This ensures we respect the checkbox states on initial load
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

    // Stagger the addition of markers for a smooth animation effect
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
                // Create fallback marker
                createMarker(incident, null);
            }
        }, index * 5); // Small delay for each marker
    });
}

// Get status code for marker image
function getStatusCode(status) {
    switch (status) {
        case "ΣΕ ΕΞΕΛΙΞΗ": return "ongoing";
        case "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ": return "partial";
        case "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ": return "controlled";
        default: return "unknown";
    }
}

// Get category code for marker image
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
        // Create custom icon
        const icon = L.icon({
            iconUrl: `/images/markers/${markerImage}`,
            iconSize: [32, 32],
            iconAnchor: [16, 32],
            popupAnchor: [0, -32],
            // Add shadow
            shadowUrl: '/images/markers/marker-shadow.png',
            shadowSize: [41, 41],
            shadowAnchor: [13, 41]
        });

        // Create marker with custom icon
        marker = L.marker([incident.latitude, incident.longitude], {
            icon: icon,
            title: `${incident.category} - ${incident.location || getText('unknown')}`,
            riseOnHover: true, // Raise marker on hover
            alt: incident.status
        });
    } catch (error) {
        console.warn(`Error creating custom marker: ${error.message}. Using default marker.`);
        // Create default marker
        marker = L.marker([incident.latitude, incident.longitude], {
            title: `${incident.category} - ${incident.location || getText('unknown')}`,
            riseOnHover: true
        });
    }

    // Add popup with basic info
    updateMarkerPopup(marker, incident);

    // Add click listener to show details modal
    marker.on('click', () => showIncidentDetails(incident));

    // Add to markers layer
    marker.addTo(markersLayer);

    // Add to markers array for later filtering
    markers.push({
        marker: marker,
        incident: incident
    });

    return marker;
}

// Update marker popup with current language
function updateMarkerPopup(marker, incident) {
    // Get status class for styling
    const statusClass = getStatusClass(incident.status);

    // Create popup content with translations
    const popupContent = `
        <div class="map-popup">
            <strong>${incident.category}</strong><br>
            ${incident.location || getText('unknown')}, ${incident.municipality || getText('unknown')}<br>
            <span class="${statusClass}">${incident.status}</span>
        </div>
    `;

    // Update popup
    marker.bindPopup(popupContent, {
        className: 'custom-popup',
        closeButton: true,
        autoClose: true,
        closeOnEscapeKey: true
    });
}

// Get CSS class for incident status
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

    // Set modal title
    modalTitle.textContent = `${incident.category} - ${incident.location || getText('unknown')}`;

    // Get status class for styling
    const statusClass = getStatusClass(incident.status);

    // Create modal content with translations
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

    // Show the modal
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

    // Clear existing markers
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

    // Add filtered incidents to map
    addIncidentsToMap(filteredIncidents);

    // Update statistics for filtered incidents
    updateStatistics(filteredIncidents);

    // Fit map to show all filtered markers
    if (filteredIncidents.length > 0) {
        fitMapToMarkers();

        // If this is initial load, mark it as complete after fitting the map
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

// Update statistics display with animations
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

// Animate counter from current to target value
function animateCounter(elementId, targetValue) {
    const element = document.getElementById(elementId);
    const currentValue = parseInt(element.textContent) || 0;
    const duration = 750; // Animation duration in ms
    const step = 25; // Update every 25ms

    // If the difference is significant, animate
    if (Math.abs(targetValue - currentValue) > 5) {
        let current = currentValue;
        const increment = (targetValue - currentValue) / (duration / step);

        const timer = setInterval(() => {
            current += increment;

            // Check if we've reached the target (or close enough)
            if ((increment > 0 && current >= targetValue) ||
                (increment < 0 && current <= targetValue)) {
                clearInterval(timer);
                element.textContent = targetValue;
            } else {
                element.textContent = Math.round(current);
            }
        }, step);
    } else {
        // Small difference, just set the value
        element.textContent = targetValue;
    }
}