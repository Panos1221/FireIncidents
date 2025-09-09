// Notification System
class NotificationManager {
    constructor() {
        this.connection = null;
        this.sessionStartTime = null;
        this.notificationSettings = {
            visual: true,
            audio: true,
            enabled: true
        };
        this.audioContext = null;
        this.notificationSound = null;
        this.lastNotificationTime = 0;
        this.notificationQueue = [];
        this.isProcessingQueue = false;

        this.init();
    }

    async init() {
        this.loadSettings();
        await this.initAudio();
        this.setupSignalR();
        this.setupToastContainer();
        this.setSessionStartTime();
        this.setupLanguageListener();
    }

    loadSettings() {
        const saved = localStorage.getItem('notificationSettings');
        if (saved) {
            this.notificationSettings = { ...this.notificationSettings, ...JSON.parse(saved) };
        }
    }

    saveSettings() {
        localStorage.setItem('notificationSettings', JSON.stringify(this.notificationSettings));
    }

    setSessionStartTime() {
        this.sessionStartTime = new Date();
        localStorage.setItem('sessionStartTime', this.sessionStartTime.toISOString());
        console.log('Session start time set:', this.sessionStartTime);
    }

    getSessionStartTime() {
        const stored = localStorage.getItem('sessionStartTime');
        return stored ? new Date(stored) : this.sessionStartTime;
    }

    async initAudio() {
        try {
            // Create audio context for better browser compatibility
            this.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            
            // Create notification sound using Web Audio API
            await this.createNotificationSound();
        } catch (error) {
            console.warn('Audio initialization failed:', error);
            this.notificationSettings.audio = false;
        }
    }

    async createNotificationSound() {
        try {
            // Create a simple notification sound using oscillators
            const duration = 0.3;
            const frequencies = [800, 1000, 800]; // Pleasant notification tone
            
            this.notificationSound = async () => {
                if (!this.audioContext || this.audioContext.state === 'suspended') {
                    await this.audioContext?.resume();
                }
                
                const now = this.audioContext.currentTime;
                
                frequencies.forEach((freq, index) => {
                    const oscillator = this.audioContext.createOscillator();
                    const gainNode = this.audioContext.createGain();
                    
                    oscillator.connect(gainNode);
                    gainNode.connect(this.audioContext.destination);
                    
                    oscillator.frequency.setValueAtTime(freq, now + (index * 0.1));
                    oscillator.type = 'sine';
                    
                    // Envelope for smooth sound
                    gainNode.gain.setValueAtTime(0, now + (index * 0.1));
                    gainNode.gain.linearRampToValueAtTime(0.3, now + (index * 0.1) + 0.05);
                    gainNode.gain.exponentialRampToValueAtTime(0.001, now + (index * 0.1) + duration);
                    
                    oscillator.start(now + (index * 0.1));
                    oscillator.stop(now + (index * 0.1) + duration);
                });
            };
        } catch (error) {
            console.warn('Failed to create notification sound:', error);
        }
    }

    async setupSignalR() {
        try {
            // Check if SignalR is available globally
            if (typeof signalR === 'undefined') {
                console.error('SignalR library not loaded');
                return;
            }
            
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl('/notificationHub')
                .withAutomaticReconnect([0, 2000, 10000, 30000])
                .build();

            this.connection.on('SetSessionStartTime', (serverTime) => {
                console.log('Server set session start time:', serverTime);
            });

            this.connection.on('NewIncidentNotification', (notification) => {
                this.handleNotification(notification);
            });

            this.connection.on('NewWarningNotification', (notification) => {
                this.handleNotification(notification);
            });

            this.connection.onclose((error) => {
                console.log('SignalR connection closed:', error);
            });

            this.connection.onreconnecting((error) => {
                console.log('SignalR reconnecting:', error);
            });

            this.connection.onreconnected((connectionId) => {
                console.log('SignalR reconnected:', connectionId);
            });

            await this.connection.start();
            console.log('SignalR connected successfully');
            
            // Join the notification group
            await this.connection.invoke('JoinNotificationGroup');
            
        } catch (error) {
            console.error('SignalR setup failed:', error);
        }
    }

    setupToastContainer() {
        if (!document.getElementById('toast-container')) {
            const container = document.createElement('div');
            container.id = 'toast-container';
            container.className = 'toast-container';
            document.body.appendChild(container);
        }
    }

    setupLanguageListener() {
        // Listen for language change events
        document.addEventListener('languageChanged', () => {
            this.onLanguageChange();
        });
    }

    handleNotification(notification) {
        if (!this.notificationSettings.enabled) {
            return;
        }

        // Check if notification is after session start time
        const notificationTime = new Date(notification.timestamp);
        const sessionStart = this.getSessionStartTime();
        
        if (notificationTime <= sessionStart) {
            console.log('Notification ignored (before session start):', notification);
            return;
        }

        console.log('Processing notification:', notification);
        
        // Add to queue to prevent spam
        this.notificationQueue.push(notification);
        this.processNotificationQueue();
    }

    async processNotificationQueue() {
        if (this.isProcessingQueue || this.notificationQueue.length === 0) {
            return;
        }

        this.isProcessingQueue = true;

        while (this.notificationQueue.length > 0) {
            const notification = this.notificationQueue.shift();
            
            // Prevent notification spam (minimum 2 seconds between notifications)
            const now = Date.now();
            if (now - this.lastNotificationTime < 2000) {
                await new Promise(resolve => setTimeout(resolve, 2000 - (now - this.lastNotificationTime)));
            }
            
            await this.showNotification(notification);
            this.lastNotificationTime = Date.now();
            
            // Small delay between notifications
            await new Promise(resolve => setTimeout(resolve, 500));
        }

        this.isProcessingQueue = false;
    }

    async showNotification(notification) {
        try {
            // Automatically refresh map data when new notification arrives
            // This ensures the incident/warning is visible on the map
            await this.refreshMapData();
            
            // Play audio notification
            if (this.notificationSettings.audio && this.notificationSound) {
                await this.playNotificationSound();
            }

            // Show visual notification
            if (this.notificationSettings.visual) {
                this.showToast(notification);
            }

            // Browser notification (if permission granted)
            this.showBrowserNotification(notification);
            
        } catch (error) {
            console.error('Error showing notification:', error);
        }
    }

    async playNotificationSound() {
        try {
            if (this.notificationSound && this.audioContext) {
                await this.notificationSound();
            }
        } catch (error) {
            console.warn('Failed to play notification sound:', error);
        }
    }

    showToast(notification) {
        const toast = this.createToastElement(notification);
        const container = document.getElementById('toast-container');
        
        container.appendChild(toast);
        
        // Trigger animation
        setTimeout(() => toast.classList.add('show'), 10);
        
        // Auto-dismiss after 8 seconds
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 300);
        }, 8000);
    }

    createToastElement(notification) {
        const toast = document.createElement('div');
        toast.className = `notification-toast ${notification.type}`;
        
        const icon = this.getNotificationIcon(notification);
        const urgencyClass = this.getUrgencyClass(notification);
        
        // Translate the notification title based on type
        let translatedTitle = notification.title;
        if (notification.type === 'incident' && window.translations) {
            translatedTitle = window.translations.translateIncidentCategory(notification.title);
        } else if (notification.type === 'warning112' && window.translations) {
            translatedTitle = window.translations.translateWarningType(notification.warningType || notification.title);
        }
        
        toast.innerHTML = `
            <div class="toast-header ${urgencyClass}">
                <i class="${icon}"></i>
                <strong>${translatedTitle}</strong>
                <button type="button" class="toast-close" onclick="this.parentNode.parentNode.remove()">
                    <i class="fas fa-times"></i>
                </button>
            </div>
            <div class="toast-body">
                <div class="toast-message">${notification.message}</div>
                <div class="toast-time">${new Date().toLocaleTimeString()}</div>
            </div>
        `;
        
        // Add click handler to focus on location
        if (notification.location) {
            toast.style.cursor = 'pointer';
            toast.addEventListener('click', (e) => {
                if (!e.target.closest('.toast-close')) {
                    this.focusOnLocation(notification.location);
                    toast.remove();
                }
            });
        }
        
        return toast;
    }

    getNotificationIcon(notification) {
        switch (notification.type) {
            case 'warning112':
                return 'fas fa-exclamation-triangle';
            case 'incident':
                return 'fas fa-fire';
            default:
                return 'fas fa-bell';
        }
    }

    getUrgencyClass(notification) {
        if (notification.type === 'warning112') {
            return notification.iconType === 'red' ? 'urgent' : 'warning';
        }
        if (notification.type === 'incident') {
            return notification.status === 'ΣΕ ΕΞΕΛΙΞΗ' ? 'urgent' : 'info';
        }
        return 'info';
    }

    async focusOnLocation(location) {
        if (window.map && location.lat && location.lng) {
            // First, trigger a map refresh to ensure the incident is loaded
            await this.refreshMapData();
            
            // Then focus on the location
            window.map.setView([location.lat, location.lng], 13);
            console.log('Focused map on:', location);
        }
    }

    async refreshMapData() {
        try {
            // Check if the map refresh function exists and call it
            if (typeof window.loadIncidents === 'function') {
                await window.loadIncidents();
            }
            
            // Also refresh 112 warnings if available
            if (typeof window.loadWarnings112 === 'function') {
                await window.loadWarnings112();
            }
            
            // Trigger filter update to display new data
            if (typeof window.filterIncidents === 'function') {
                await window.filterIncidents();
            }
            
            console.log('Map data refreshed due to new notification');
        } catch (error) {
            console.warn('Failed to refresh map data:', error);
        }
    }

    showBrowserNotification(notification) {
        if ('Notification' in window && Notification.permission === 'granted') {
            try {
                // Translate the notification title based on type
                let translatedTitle = notification.title;
                if (notification.type === 'incident' && window.translations) {
                    translatedTitle = window.translations.translateIncidentCategory(notification.title);
                } else if (notification.type === 'warning112' && window.translations) {
                    translatedTitle = window.translations.translateWarningType(notification.warningType || notification.title);
                }

                const browserNotification = new Notification(translatedTitle, {
                    body: notification.message,
                    icon: '/images/fire-dept-icon.png',
                    tag: notification.id,
                    requireInteraction: true
                });

                browserNotification.onclick = () => {
                    window.focus();
                    if (notification.location) {
                        this.focusOnLocation(notification.location);
                    }
                    browserNotification.close();
                };

                // Auto-close after 10 seconds
                setTimeout(() => browserNotification.close(), 10000);
            } catch (error) {
                console.warn('Browser notification failed:', error);
            }
        }
    }

    async requestNotificationPermission() {
        if ('Notification' in window && Notification.permission === 'default') {
            const permission = await Notification.requestPermission();
            console.log('Notification permission:', permission);
            return permission === 'granted';
        }
        return Notification.permission === 'granted';
    }

    // Settings management
    updateSettings(newSettings) {
        this.notificationSettings = { ...this.notificationSettings, ...newSettings };
        this.saveSettings();
        
        // Update UI if settings panel exists
        this.updateSettingsUI();
    }

    updateSettingsUI() {
        const enabledToggle = document.getElementById('notifications-enabled');
        const visualToggle = document.getElementById('notifications-visual');
        const audioToggle = document.getElementById('notifications-audio');
        
        if (enabledToggle) enabledToggle.checked = this.notificationSettings.enabled;
        if (visualToggle) visualToggle.checked = this.notificationSettings.visual;
        if (audioToggle) audioToggle.checked = this.notificationSettings.audio;
    }

    // Test methods
    async testVisualNotification() {
        const testNotification = {
            type: 'incident',
            id: 'test-visual-' + Date.now(),
            title: 'ΔΑΣΙΚΕΣ ΠΥΡΚΑΓΙΕΣ', // Use raw category for translation
            message: 'This is a test visual notification',
            location: { lat: 38.2466, lng: 21.7359 },
            timestamp: new Date().toISOString()
        };
        
        this.showToast(testNotification);
    }

    async testAudioNotification() {
        try {
            if (this.audioContext && this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }
            await this.playNotificationSound();
            console.log('Audio test completed');
        } catch (error) {
            console.error('Audio test failed:', error);
            alert('Audio test failed. Please check your browser audio settings.');
        }
    }

    async testFullNotification() {
        const testNotification = {
            type: 'warning112',
            id: 'test-full-' + Date.now(),
            title: '112 Warning - Test',
            message: 'Test Location, Test Area',
            location: { lat: 37.9838, lng: 23.7275 },
            warningType: 'Wildfire',
            iconType: 'red',
            timestamp: new Date().toISOString()
        };
        
        await this.showNotification(testNotification);
    }

    // Handle language changes
    onLanguageChange() {
        // Update any existing toast notifications
        const existingToasts = document.querySelectorAll('.notification-toast');
        existingToasts.forEach(toast => {
            // remove existing toasts when language changes
            // New notifications will use the new language
            toast.classList.remove('show');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 300);
        });
    }

    // Cleanup
    destroy() {
        if (this.connection) {
            this.connection.stop();
        }
        if (this.audioContext) {
            this.audioContext.close();
        }
    }
}

// Initialize notification manager when DOM is ready
let notificationManager;

document.addEventListener('DOMContentLoaded', function() {
    // Wait a bit for all scripts to load
    setTimeout(() => {
        notificationManager = new NotificationManager();
        window.notificationManager = notificationManager;
    }, 100);
});

// Export for global access
window.NotificationManager = NotificationManager;

