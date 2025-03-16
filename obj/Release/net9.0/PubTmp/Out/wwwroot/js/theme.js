document.addEventListener('DOMContentLoaded', function () {
    initTheme();
    setupThemeToggle();
});


// Initialize theme from user preference or system preference
function initTheme() {
    // Check local storage
    const savedTheme = localStorage.getItem('theme');

    if (savedTheme) {
        setTheme(savedTheme);
    } else {
        // Check system
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            setTheme('dark');
        } else {
            setTheme('light');
        }
    }

    // Listen for system theme changes
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
        if (!localStorage.getItem('theme')) {
            setTheme(e.matches ? 'dark' : 'light');
        }
    });
}

// Set theme and save
function setTheme(theme) {
    if (theme === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
        localStorage.setItem('theme', 'dark');
        updateThemeToggleUI('dark');
    } else {
        document.documentElement.removeAttribute('data-theme');
        localStorage.setItem('theme', 'light');
        updateThemeToggleUI('light');
    }
}

function updateThemeToggleUI(theme) {
    const themeToggle = document.getElementById('themeToggle');
    if (themeToggle) {
        const icon = themeToggle.querySelector('i');
        if (icon) {
            if (theme === 'dark') {
                icon.className = 'fas fa-sun';
                themeToggle.setAttribute('title', window.translations ? window.translations.getText('lightMode') : 'Light Mode');
            } else {
                icon.className = 'fas fa-moon';
                themeToggle.setAttribute('title', window.translations ? window.translations.getText('darkMode') : 'Dark Mode');
            }
        }
    }
}

// Toggle light and dark theme
function toggleTheme() {
    const currentTheme = localStorage.getItem('theme') || 'light';
    setTheme(currentTheme === 'light' ? 'dark' : 'light');
}

//theme toggle button
function setupThemeToggle() {
    const themeToggle = document.getElementById('themeToggle');
    if (themeToggle) {
        themeToggle.addEventListener('click', toggleTheme);
    }
}

window.themeManager = {
    setTheme,
    toggleTheme,
    getCurrentTheme: () => localStorage.getItem('theme') || 'light'
};