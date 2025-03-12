// Map initialization and incident data handling
let map;
let markers = [];
let allIncidents = [];
let incidentModal;

// Initialize the map when the document is loaded
document.addEventListener('DOMContentLoaded', function () {
    initMap();
    initModal();
    setupEventListeners();
    loadIncidents();

    // Auto-refresh data every 2 minutes
    setInterval(loadIncidents, 2 * 60 * 1000);
});

// Initialize the Leaflet map
function initMap() {
    // Center map on Greece
    map = L.map('map').setView([38.2, 23.8], 7);

    // Add OpenStreetMap tile layer
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        maxZoom: 19
    }).addTo(map);
}

// Initialize the Bootstrap modal
function initModal() {
    // Modal is initialized when needed
    incidentModal = new bootstrap.Modal(document.getElementById('incidentModal'));
}

// Set up event listeners for filters and refresh button
function setupEventListeners() {
    document.getElementById('refreshBtn').addEventListener('click', loadIncidents);

    document.getElementById('statusFilter').addEventListener('change', filterIncidents);

    document.getElementById('forestFiresCheck').addEventListener('change', filterIncidents);
    document.getElementById('urbanFiresCheck').addEventListener('change', filterIncidents);
    document.getElementById('assistanceCheck').addEventListener('change', filterIncidents);
}

// Load incidents from API
async function loadIncidents() {
    try {
        console.log("Fetching incident data from API...");

        // Show loading indicator
        document.getElementById('lastUpdated').textContent = "Loading...";

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

        // Add markers for the incidents
        addIncidentsToMap(allIncidents);

        // Update statistics
        updateStatistics(allIncidents);

        // Update last updated time
        document.getElementById('lastUpdated').textContent = new Date().toLocaleTimeString();
    } catch (error) {
        console.error('Error loading incidents:', error);
        document.getElementById('lastUpdated').textContent = "Error: " + error.message;
    }
}

// Add incidents to the map
function addIncidentsToMap(incidents) {
    if (!incidents || incidents.length === 0) {
        console.warn("No incidents to display on the map");
        return;
    }

    incidents.forEach(incident => {
        // Skip incidents without coordinates
        if (!incident.latitude || !incident.longitude) {
            console.warn(`Incident without coordinates: ${incident.category} - ${incident.location}`);
            return;
        }

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
            popupAnchor: [0, -32]
        });

        // Create marker with custom icon
        marker = L.marker([incident.latitude, incident.longitude], {
            icon: icon,
            title: `${incident.category} - ${incident.location}`
        });
    } catch (error) {
        console.warn(`Error creating custom marker: ${error.message}. Using default marker.`);
        // Create default marker
        marker = L.marker([incident.latitude, incident.longitude], {
            title: `${incident.category} - ${incident.location}`
        });
    }

    // Add marker to map
    marker.addTo(map);

    // Add popup with basic info
    marker.bindPopup(`
        <strong>${incident.category}</strong><br>
        ${incident.location || "Unknown location"}, ${incident.municipality || "Unknown municipality"}<br>
        <em>${incident.status}</em>
    `);

    // Add click listener to show details modal
    marker.on('click', () => showIncidentDetails(incident));

    // Add to markers array for later filtering
    markers.push({
        marker: marker,
        incident: incident
    });
}

// Show incident details in modal
function showIncidentDetails(incident) {
    const modalBody = document.getElementById('incidentModalBody');
    const modalTitle = document.getElementById('incidentModalLabel');

    // Set modal title
    modalTitle.textContent = `${incident.category} - ${incident.location || "Unknown location"}`;

    // Create modal content
    modalBody.innerHTML = `
        <div class="incident-detail">
            <span class="incident-label">Status:</span> ${incident.status}
        </div>
        <div class="incident-detail">
            <span class="incident-label">Region:</span> ${incident.region || "Unknown"}
        </div>
        <div class="incident-detail">
            <span class="incident-label">Municipality:</span> ${incident.municipality || "Unknown"}
        </div>
        <div class="incident-detail">
            <span class="incident-label">Details:</span> ${incident.location || "Unknown"}
        </div>
        <div class="incident-detail">
            <span class="incident-label">Start Date:</span> ${incident.startDate || "Unknown"}
        </div>
        <div class="incident-detail">
            <span class="incident-label">Last Update:</span> ${incident.lastUpdate || "Unknown"}
        </div>
    `;

    // Show the modal
    incidentModal.show();
}

// Clear all markers from the map
function clearMarkers() {
    markers.forEach(markerObj => {
        map.removeLayer(markerObj.marker);
    });
    markers = [];
}

// Filter incidents based on user selections
function filterIncidents() {
    const statusFilter = document.getElementById('statusFilter').value;
    const forestFiresChecked = document.getElementById('forestFiresCheck').checked;
    const urbanFiresChecked = document.getElementById('urbanFiresCheck').checked;
    const assistanceChecked = document.getElementById('assistanceCheck').checked;

    console.log(`Filtering with status: ${statusFilter || 'All'}, Forest: ${forestFiresChecked}, Urban: ${urbanFiresChecked}, Assistance: ${assistanceChecked}`);

    // Clear existing markers
    clearMarkers();

    // Filter incidents
    const filteredIncidents = allIncidents.filter(incident => {
        // Check status filter
        if (statusFilter && incident.status !== statusFilter) {
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
}

// Update statistics display
function updateStatistics(incidents) {
    const totalIncidents = incidents.length;
    const forestFireCount = incidents.filter(i => i.category === "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ").length;
    const urbanFireCount = incidents.filter(i => i.category === "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ").length;
    const assistanceCount = incidents.filter(i => i.category === "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ").length;

    document.getElementById('totalIncidents').textContent = totalIncidents;
    document.getElementById('forestFireCount').textContent = forestFireCount;
    document.getElementById('urbanFireCount').textContent = urbanFireCount;
    document.getElementById('assistanceCount').textContent = assistanceCount;
}