const translations = {
    en: {
        // General
        "appTitle": "Hellenic Fire Service Live Incidents Map",
        "refresh": "Refresh Data",
        "loading": "Loading...",
        "lastUpdated": "Last Updated",
        "error": "Error",
        "close": "Close",
        
        // Filters
        "filters": "Filters",
        "status": "Status",
        "category": "Category",
        "all": "All",
        "inProgress": "In Progress",
        "partialControl": "Partial Control",
        "fullControl": "Full Control",
        
        // Categories
        "forestFires": "Forest Fires",
        "urbanFires": "Urban Fires",
        "assistance": "Assistance",
        
        // Legend
        "legend": "Legend",
        "forestFireInProgress": "Forest Fire - In Progress",
        "forestFirePartial": "Forest Fire - Partial Control",
        "forestFireControlled": "Forest Fire - Full Control",
        "urbanFireInProgress": "Urban Fire - In Progress",
        "urbanFirePartial": "Urban Fire - Partial Control",
        "urbanFireControlled": "Urban Fire - Full Control",
        "assistanceInProgress": "Assistance - In Progress",
        "assistancePartial": "Assistance - Partial Control",
        "assistanceControlled": "Assistance - Full Control",
        
        // Statistics
        "statistics": "Statistics",
        "totalIncidents": "Total Incidents",
        "forestFireCount": "Forest Fires",
        "urbanFireCount": "Urban Fires",
        "assistanceCount": "Assistance",
        
        // Incident details
        "incidentDetails": "Incident Details",
        "region": "Region",
        "municipality": "Municipality",
        "details": "Details",
        "location": "Location",
        "startDate": "Start Date",
        "unknown": "Unknown",
        
        // Footer
        "footer": "Hellenic Fire Service Live Incidents Map",
        
        // Theme
        "darkMode": "Dark Mode",
        "lightMode": "Light Mode",
        
        // Language
        "language": "Language",
        "english": "English",
        "greek": "Greek"
    },
    el: {
        // General
        "appTitle": "Συμβάντα Επικράτειας του Πυροσβεστικού Σώματος Ελλάδος",
        "refresh": "Ανανέωση Δεδομένων",
        "loading": "Ενημέρωση...",
        "lastUpdated": "Τελευταία Ενημέρωση",
        "error": "Σφάλμα",
        "close": "Κλείσιμο",
        
        // Filters
        "filters": "Φίλτρα",
        "status": "Κατάσταση",
        "category": "Κατηγορία",
        "all": "Όλα",
        "inProgress": "ΣΕ ΕΞΕΛΙΞΗ",
        "partialControl": "ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ",
        "fullControl": "ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ",
        
        // Categories
        "forestFires": "ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ",
        "urbanFires": "ΑΣΤΙΚΕΣ ΠΥΡΚΑΓΙΕΣ",
        "assistance": "ΠΑΡΟΧΕΣ ΒΟΗΘΕΙΑΣ",
        
        // Legend
        "legend": "Υπόμνημα",
        "forestFireInProgress": "Δασική Πυρκαγιά - Σε Εξέλιξη",
        "forestFirePartial": "Δασική Πυρκαγιά - Μερικός Έλεγχος",
        "forestFireControlled": "Δασική Πυρκαγιά - Πλήρης Έλεγχος",
        "urbanFireInProgress": "Αστική Πυρκαγιά - Σε Εξέλιξη",
        "urbanFirePartial": "Αστική Πυρκαγιά - Μερικός Έλεγχος",
        "urbanFireControlled": "Αστική Πυρκαγιά - Πλήρης Έλεγχος",
        "assistanceInProgress": "Παροχή Βοήθειας - Σε Εξέλιξη",
        "assistancePartial": "Παροχή Βοήθειας - Μερικός Έλεγχος",
        "assistanceControlled": "Παροχή Βοήθειας - Πλήρης Έλεγχος",
        
        // Statistics
        "statistics": "Στατιστικά",
        "totalIncidents": "Συνολικά Περιστατικά",
        "forestFireCount": "Δασικές Πυρκαγιές",
        "urbanFireCount": "Αστικές Πυρκαγιές",
        "assistanceCount": "Παροχές Βοήθειας",
        
        // Incident details
        "incidentDetails": "Λεπτομέρειες Περιστατικού",
        "region": "Περιφέρεια",
        "municipality": "Δήμος",
        "details": "Λεπτομέρειες",
        "location": "Τοποθεσία",
        "startDate": "Ημερομηνία Έναρξης",
        "unknown": "Άγνωστο",
        
        // Footer
        "footer": "Χάρτης Ενεργών Συμβάντων - Δεδομένα από: Πυροσβεστικό Σώμα Ελλάδος",
        
        // Theme
        "darkMode": "Σκούρο Θέμα",
        "lightMode": "Φωτεινό Θέμα",
        
        // Language
        "language": "Γλώσσα",
        "english": "Αγγλικά",
        "greek": "Ελληνικά"
    }
};

// Current lang
let currentLanguage = localStorage.getItem('preferredLanguage') || 'gr';

// change language
function setLanguage(lang) {
    if (translations[lang]) {
        currentLanguage = lang;
        localStorage.setItem('preferredLanguage', lang);
        updatePageTranslations();
    }
}

// get translation
function getText(key) {
    return translations[currentLanguage][key] || translations['en'][key] || key;
}

function updatePageTranslations() {
    document.querySelectorAll('[data-translate]').forEach(element => {
        const key = element.getAttribute('data-translate');
        if (key) {
            if (element.hasAttribute('data-translate-placeholder')) {
                element.placeholder = getText(key);
            } 
            else if (element.hasAttribute('data-translate-attribute')) {
                const attr = element.getAttribute('data-translate-attribute');
                element.setAttribute(attr, getText(key));
            }
            else {
                element.innerText = getText(key);
            }
        }
    });

    document.title = getText('appTitle');
    
    updateSelectOptions();
    
    document.dispatchEvent(new CustomEvent('languageChanged', { detail: { language: currentLanguage } }));
}


// Update select options
function updateSelectOptions() {
    const statusFilter = document.getElementById('statusFilter');
    if (statusFilter) {

        const currentValue = statusFilter.value;
        
        statusFilter.innerHTML = '';
        
        const allOption = document.createElement('option');
        allOption.value = '';
        allOption.textContent = getText('all');
        statusFilter.appendChild(allOption);
        
        const statuses = [
            { value: 'ΣΕ ΕΞΕΛΙΞΗ', key: 'inProgress' },
            { value: 'ΜΕΡΙΚΟΣ ΕΛΕΓΧΟΣ', key: 'partialControl' },
            { value: 'ΠΛΗΡΗΣ ΕΛΕΓΧΟΣ', key: 'fullControl' }
        ];
        
        statuses.forEach(status => {
            const option = document.createElement('option');
            option.value = status.value;
            option.textContent = getText(status.key);
            statusFilter.appendChild(option);
        });
        
        statusFilter.value = currentValue;
    }
}

document.addEventListener('DOMContentLoaded', function() {
    // browser language and set initial lang
    if (!localStorage.getItem('preferredLanguage')) {
        const browserLang = navigator.language.split('-')[0];
        if (translations[browserLang]) {
            currentLanguage = browserLang;
            localStorage.setItem('preferredLanguage', browserLang);
        }
    }
    
    updatePageTranslations();
    
    // language selector
    const languageSelector = document.getElementById('languageSelector');
    if (languageSelector) {
        languageSelector.value = currentLanguage;
        languageSelector.addEventListener('change', function() {
            setLanguage(this.value);
        });
    }
});

window.translations = {
    getText,
    setLanguage,
    getCurrentLanguage: () => currentLanguage,
    updatePageTranslations
};