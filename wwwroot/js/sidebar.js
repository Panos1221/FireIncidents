class IncidentsSidebar {
    constructor() {
        this.sidebar = document.getElementById('incidentsSidebar');
        this.sidebarToggle = document.getElementById('sidebarToggle');
        this.closeSidebar = document.getElementById('closeSidebar');
        this.sidebarContent = document.getElementById('sidebarContent');
        this.isOpen = false;
        this.incidents = [];
        this.filteredIncidents = [];
        this.activeFilters = {
            categories: ['ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ', 'ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ', 'ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ'],
            statuses: ['ΣΕ ΕΞΕΛΙΞΗ', 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ'] // Default: all except Full Control like index page
        };
        
        this.initializeEventListeners();
        this.initializeFilters();
    }

    initializeEventListeners() {
        // Toggle sidebar
        this.sidebarToggle?.addEventListener('click', () => this.toggleSidebar());
        this.closeSidebar?.addEventListener('click', () => this.closeSidebarPanel());
        
        // Close sidebar when clicking outside
        document.addEventListener('click', (e) => {
            if (this.isOpen && !this.sidebar.contains(e.target) && !this.sidebarToggle.contains(e.target)) {
                this.closeSidebarPanel();
            }
        });
        
        // Close sidebar on escape key
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.isOpen) {
                this.closeSidebarPanel();
            }
        });
    }

    initializeFilters() {
        // Add event listeners to filter checkboxes
        document.addEventListener('change', (e) => {
            if (e.target.classList.contains('sidebar-filter-checkbox')) {
                const filterType = e.target.getAttribute('data-filter-type');
                const filterValue = e.target.value;
                
                if (filterType === 'category') {
                    if (e.target.checked) {
                        if (!this.activeFilters.categories.includes(filterValue)) {
                            this.activeFilters.categories.push(filterValue);
                        }
                    } else {
                        this.activeFilters.categories = this.activeFilters.categories.filter(cat => cat !== filterValue);
                    }
                    // Sync with main page category filters
                    this.syncMainPageCategoryFilter(filterValue, e.target.checked);
                } else if (filterType === 'status') {
                    if (e.target.checked) {
                        if (!this.activeFilters.statuses.includes(filterValue)) {
                            this.activeFilters.statuses.push(filterValue);
                        }
                    } else {
                        this.activeFilters.statuses = this.activeFilters.statuses.filter(status => status !== filterValue);
                    }
                    // Sync with main page status filters
                    this.syncMainPageStatusFilter(filterValue, e.target.checked);
                }
                
                // Apply filters and re-render
                this.applyFiltersToIncidents();
                this.renderIncidents();
            }
        });
    }

    toggleSidebar() {
        if (this.isOpen) {
            this.closeSidebarPanel();
        } else {
            this.openSidebarPanel();
        }
    }

    openSidebarPanel() {
        this.sidebar.classList.add('open');
        this.isOpen = true;
        
        // Update toggle button icon
        const icon = this.sidebarToggle.querySelector('i');
        if (icon) {
            icon.className = 'fas fa-times';
        }
        
        // Load incidents if not already loaded
        if (this.incidents.length === 0) {
            this.loadIncidents();
        }
    }

    closeSidebarPanel() {
        this.sidebar.classList.remove('open');
        this.isOpen = false;
        
        // Update toggle button icon
        const icon = this.sidebarToggle.querySelector('i');
        if (icon) {
            icon.className = 'fas fa-list';
        }
    }

    async loadIncidents() {
        try {
            this.showLoading();
            
            const response = await fetch('/api/incidents');
            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }
            
            this.incidents = await response.json();
            this.applyFiltersToIncidents();
            this.renderIncidents();
        } catch (error) {
            console.error('Error loading incidents:', error);
            this.showError('Failed to load incidents. Please try again.');
        }
    }

    showLoading() {
        this.sidebarContent.innerHTML = `
            <div class="text-center py-4">
                <div class="spinner-border text-primary" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <p class="mt-2" data-translate="loadingIncidents">Loading incidents...</p>
            </div>
        `;
    }

    showError(message) {
        const retryText = window.translations && window.translations.getText ? 
            window.translations.getText('refresh') : 'Retry';
        
        this.sidebarContent.innerHTML = `
            <div class="text-center py-4">
                <i class="fas fa-exclamation-triangle text-warning mb-3" style="font-size: 2rem;"></i>
                <p class="text-muted">${message}</p>
                <button class="btn btn-primary btn-sm" onclick="incidentsSidebar.loadIncidents()">
                    <i class="fas fa-sync-alt me-1"></i> ${retryText}
                </button>
            </div>
        `;
    }

    applyFiltersToIncidents() {
        this.filteredIncidents = this.incidents.filter(incident => {
            const categoryMatch = this.activeFilters.categories.length === 0 || this.activeFilters.categories.includes(incident.category);
            const statusMatch = this.activeFilters.statuses.length === 0 || this.activeFilters.statuses.includes(incident.status);
            return categoryMatch && statusMatch;
        });
    }

    // Synchronization methods for main page filters
    syncMainPageCategoryFilter(category, checked) {
        const categoryMap = {
            'ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ': 'forestFiresCheck',
            'ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ': 'urbanFiresCheck',
            'ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ': 'assistanceCheck'
        };
        
        const checkboxId = categoryMap[category];
        if (checkboxId) {
            const checkbox = document.getElementById(checkboxId);
            if (checkbox && checkbox.checked !== checked) {
                checkbox.checked = checked;
                // Trigger the main page filter update
                if (window.filterIncidents) {
                    window.filterIncidents();
                }
            }
        }
    }

    syncMainPageStatusFilter(status, checked) {
        const statusMap = {
            'ΣΕ ΕΞΕΛΙΞΗ': 'ongoingCheck',
            'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ': 'partialControlCheck',
            'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ': 'fullControlCheck'
        };
        
        const checkboxId = statusMap[status];
        if (checkboxId) {
            const checkbox = document.getElementById(checkboxId);
            if (checkbox && checkbox.checked !== checked) {
                checkbox.checked = checked;
                // Trigger the main page filter update
                if (window.filterIncidents) {
                    window.filterIncidents();
                }
            }
        }
    }

    // Method to sync from main page to sidebar
    syncFromMainPage() {
        // Update categories based on main page checkboxes
        this.activeFilters.categories = [];
        if (document.getElementById('forestFiresCheck')?.checked) {
            this.activeFilters.categories.push('ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ');
        }
        if (document.getElementById('urbanFiresCheck')?.checked) {
            this.activeFilters.categories.push('ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ');
        }
        if (document.getElementById('assistanceCheck')?.checked) {
            this.activeFilters.categories.push('ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ');
        }

        // Update statuses based on main page checkboxes
        this.activeFilters.statuses = [];
        if (document.getElementById('ongoingCheck')?.checked) {
            this.activeFilters.statuses.push('ΣΕ ΕΞΕΛΙΞΗ');
        }
        if (document.getElementById('partialControlCheck')?.checked) {
            this.activeFilters.statuses.push('ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ');
        }
        if (document.getElementById('fullControlCheck')?.checked) {
            this.activeFilters.statuses.push('ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ');
        }

        // Update sidebar checkboxes to match
        this.updateSidebarCheckboxes();
        this.applyFiltersToIncidents();
        this.renderIncidents();
    }

    updateSidebarCheckboxes() {
        // Update category checkboxes
        const categoryCheckboxes = {
            'ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ': document.querySelector('.sidebar-filter-checkbox[value="ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ"]'),
            'ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ': document.querySelector('.sidebar-filter-checkbox[value="ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ"]'),
            'ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ': document.querySelector('.sidebar-filter-checkbox[value="ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ"]')
        };

        Object.entries(categoryCheckboxes).forEach(([category, checkbox]) => {
            if (checkbox) {
                checkbox.checked = this.activeFilters.categories.includes(category);
            }
        });

        // Update status checkboxes
        const statusCheckboxes = {
            'ΣΕ ΕΞΕΛΙΞΗ': document.querySelector('.sidebar-filter-checkbox[value="ΣΕ ΕΞΕΛΙΞΗ"]'),
            'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ': document.querySelector('.sidebar-filter-checkbox[value="ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ"]'),
            'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ': document.querySelector('.sidebar-filter-checkbox[value="ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ"]')
        };

        Object.entries(statusCheckboxes).forEach(([status, checkbox]) => {
            if (checkbox) {
                checkbox.checked = this.activeFilters.statuses.includes(status);
            }
        });
    }

    renderIncidents() {
        const incidentsToRender = this.filteredIncidents.length > 0 ? this.filteredIncidents : 
                                 (this.incidents.length > 0 ? this.incidents : []);
        
        if (!incidentsToRender || incidentsToRender.length === 0) {
            const hasActiveFilters = this.activeFilters.categories.length < 3 || this.activeFilters.statuses.length < 3;
            const noResultsMessage = hasActiveFilters ? 
                'No incidents match the selected filters' : 'No incidents found';
            this.sidebarContent.innerHTML = `
                <div class="text-center py-4">
                    <i class="fas fa-info-circle text-info mb-3" style="font-size: 2rem;"></i>
                    <p class="text-muted" data-translate="noIncidents">${noResultsMessage}</p>
                </div>
            `;
            // Update translations after rendering
            if (window.translations && window.translations.updatePageTranslations) {
                window.translations.updatePageTranslations();
            }
            return;
        }

        const incidentsHtml = incidentsToRender.map(incident => this.createIncidentCard(incident)).join('');
        this.sidebarContent.innerHTML = `
            <div class="mb-3 sidebar-info-text">
                <small class="text-muted">
                    <i class="fas fa-info-circle me-1"></i>
                    <span data-translate="clickToNavigate">Click on an incident to navigate to its location</span>
                </small>
            </div>
            ${incidentsHtml}
        `;
        
        // Update translations after rendering
        if (window.translations && window.translations.updatePageTranslations) {
            window.translations.updatePageTranslations();
        }
    }

    createIncidentCard(incident) {
        const statusClass = this.getStatusClass(incident.status);
        const statusText = this.getStatusText(incident.status);
        const categoryIcon = this.getCategoryIcon(incident.category);
        const markerImage = incident.getMarkerImage ? incident.getMarkerImage() : this.getMarkerImage(incident);
        
        return `
            <div class="incident-card" onclick="incidentsSidebar.navigateToIncident(${incident.latitude}, ${incident.longitude})" data-lat="${incident.latitude}" data-lng="${incident.longitude}">
                <div class="incident-card-header">
                    <img src="/images/markers/${markerImage}" alt="${incident.category}" class="incident-icon" onerror="this.src='/images/markers/forest-fire-ongoing.png'">
                    <h6 class="incident-title">${incident.category}</h6>
                </div>
                <div class="incident-card-status ${statusClass}">
                    ${statusText}
                </div>
                <div class="incident-card-location">
                    <i class="fas fa-map-marker-alt me-1"></i>
                    ${incident.location}, ${incident.municipality}
                </div>
                <div class="incident-card-location">
                    <i class="fas fa-map me-1"></i>
                    ${incident.region}
                </div>
                ${incident.startDate ? `
                    <div class="incident-card-time">
                        <i class="fas fa-clock me-1"></i>
                        Started: ${incident.startDate}
                    </div>
                ` : ''}
                ${incident.lastUpdate ? `
                    <div class="incident-card-time">
                        <i class="fas fa-sync-alt me-1"></i>
                        Updated: ${incident.lastUpdate}
                    </div>
                ` : ''}
            </div>
        `;
    }

    getStatusClass(status) {
        switch (status) {
            case 'ΣΕ ΕΞΕΛΙΞΗ':
                return 'ongoing';
            case 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ':
                return 'partial';
            case 'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ':
                return 'controlled';
            default:
                return 'ongoing';
        }
    }

    getStatusText(status) {
        // Use the existing translation system
        if (window.translations && window.translations.getText) {
            switch (status) {
                case 'ΣΕ ΕΞΕΛΙΞΗ':
                    return window.translations.getText('inProgress');
                case 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ':
                    return window.translations.getText('partialControl');
                case 'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ':
                    return window.translations.getText('fullControl');
                default:
                    return status;
            }
        } else {
            // Fallback if translations not loaded
            switch (status) {
                case 'ΣΕ ΕΞΕΛΙΞΗ':
                    return 'In Progress';
                case 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ':
                    return 'Partial Control';
                case 'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ':
                    return 'Full Control';
                default:
                    return status;
            }
        }
    }

    getCategoryIcon(category) {
        switch (category) {
            case 'ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ':
                return 'fas fa-tree';
            case 'ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ':
                return 'fas fa-building';
            case 'ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ':
                return 'fas fa-hands-helping';
            default:
                return 'fas fa-fire';
        }
    }

    getMarkerImage(incident) {
        const statusCode = incident.status === 'ΣΕ ΕΞΕΛΙΞΗ' ? 'ongoing' :
                          incident.status === 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ' ? 'partial' :
                          incident.status === 'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ' ? 'controlled' : 'ongoing';
        
        const categoryCode = incident.category === 'ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ' ? 'forest-fire' :
                            incident.category === 'ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ' ? 'urban-fire' :
                            incident.category === 'ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ' ? 'assistance' : 'forest-fire';
        
        return `${categoryCode}-${statusCode}.png`;
    }

    navigateToIncident(lat, lng) {
        // Wait for map to be available with retry mechanism
        const attemptNavigation = (retries = 0) => {
            if (typeof window.map !== 'undefined' && window.map && typeof window.map.setView === 'function') {
                // Center the map on the incident location
                window.map.setView([lat, lng], 12);      
                this.closeSidebarPanel();        
                this.showNavigationFeedback();
            } else if (retries < 10) {
                // Retry after a short delay (max 10 retries = 5 seconds)
                setTimeout(() => attemptNavigation(retries + 1), 500);
            } else {
                console.error('Map not available for navigation after retries');
                this.showNavigationError();
            }
        };
        
        attemptNavigation();
    }

    showNavigationFeedback() {
        // Create a temporary notification
        const notification = document.createElement('div');
        notification.className = 'alert alert-success position-fixed';
        notification.style.cssText = 'top: 20px; left: 50%; transform: translateX(-50%); z-index: 9999; opacity: 0; transition: opacity 0.3s ease;';
        
        const message = window.translations && window.translations.getText ? 
            window.translations.getText('navigatedToIncidentLocation') : 'Navigated to incident location'; 
        
        notification.innerHTML = `<i class="fas fa-check-circle me-2"></i>${message}`;
        
        document.body.appendChild(notification);
        
        // Fade in
        setTimeout(() => notification.style.opacity = '1', 100);
        
        // Fade out and remove
        setTimeout(() => {
            notification.style.opacity = '0';
            setTimeout(() => notification.remove(), 300);
        }, 1000);
    }

    showNavigationError() {
        // Create a temporary error notification
        const notification = document.createElement('div');
        notification.className = 'alert alert-danger position-fixed';
        notification.style.cssText = 'top: 20px; left: 50%; transform: translateX(-50%); z-index: 9999; opacity: 0; transition: opacity 0.3s ease;';
        
        const errorMessage = window.translations && window.translations.getText ? 
            window.translations.getText('mapNotAvailable') : 'Map not available. Please try again later.'; 
        
        notification.innerHTML = `<i class="fas fa-exclamation-triangle me-2"></i>${errorMessage}`;
        
        document.body.appendChild(notification);   
        setTimeout(() => notification.style.opacity = '1', 100);  
        setTimeout(() => {
            notification.style.opacity = '0';
            setTimeout(() => notification.remove(), 300);
        }, 2000);
    }

    // Method to refresh incidents
    refreshIncidents() {
        this.incidents = [];
        this.loadIncidents();
    }

    // Method to filter incidents based on current map filters
    applyFilters(filters) {
        console.log('Applying filters to sidebar:', filters);
        this.loadIncidents();
    }
}

// Initialize the sidebar when the DOM is loaded
let incidentsSidebar;
document.addEventListener('DOMContentLoaded', function() {
    incidentsSidebar = new IncidentsSidebar();
    // Export for global access after initialization
    window.incidentsSidebar = incidentsSidebar;
});