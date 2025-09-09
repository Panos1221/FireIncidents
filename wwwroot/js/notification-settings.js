// Notification Settings UI
class NotificationSettingsUI {
    constructor(notificationManager) {
        this.notificationManager = notificationManager;
        this.isOpen = false;
        this.init();
    }

    init() {
        this.createSettingsButton();
        this.createSettingsPanel();
        this.setupEventListeners();
        this.updateButtonState();
    }

    createSettingsButton() {
        const button = document.createElement('button');
        button.id = 'notificationSettingsToggle';
        button.className = 'notification-settings-toggle';
        button.title = 'Notification Settings';
        button.innerHTML = '<i class="fas fa-bell"></i>';
        
        document.body.appendChild(button);
    }

    createSettingsPanel() {
        const panel = document.createElement('div');
        panel.id = 'notificationSettingsPanel';
        panel.className = 'notification-settings-panel';
        
        panel.innerHTML = `
            <div class="notification-settings-header">
                <h4 data-translate="notificationSettings">
                    <i class="fas fa-bell"></i> Notification Settings
                </h4>
            </div>
            <div class="notification-settings-body">
                <div class="setting-group">
                    <div class="setting-item">
                        <div>
                            <div class="setting-label" data-translate="enableNotifications">Enable Notifications</div>
                            <div class="setting-description" data-translate="enableNotificationsDesc">Turn on/off all notifications</div>
                        </div>
                        <div class="toggle-switch" id="notificationsEnabledToggle">
                        </div>
                    </div>
                </div>
                
                <div class="setting-group">
                    <div class="setting-item">
                        <div>
                            <div class="setting-label" data-translate="visualNotifications">Visual Notifications</div>
                            <div class="setting-description" data-translate="visualNotificationsDesc">Show toast notifications on screen</div>
                        </div>
                        <div class="toggle-switch" id="visualNotificationsToggle">
                        </div>
                    </div>
                    
                    <div class="setting-item">
                        <div>
                            <div class="setting-label" data-translate="audioNotifications">Audio Notifications</div>
                            <div class="setting-description" data-translate="audioNotificationsDesc">Play sound when notifications arrive</div>
                        </div>
                        <div class="toggle-switch" id="audioNotificationsToggle">
                        </div>
                    </div>
                </div>
                
                <div class="setting-group">
                    <div class="setting-label" data-translate="testNotifications" style="margin-bottom: 8px;">Test Notifications</div>
                    <div class="test-buttons">
                        <button class="test-btn" id="testVisualBtn">
                            <i class="fas fa-eye"></i><span data-translate="testVisual">Visual</span>
                        </button>
                        <button class="test-btn" id="testAudioBtn">
                            <i class="fas fa-volume-up"></i><span data-translate="testAudio">Audio</span>
                        </button>
                        <button class="test-btn" id="testFullBtn">
                            <i class="fas fa-bell"></i><span data-translate="testFull">Full</span>
                        </button>
                    </div>
                </div>
            </div>
        `;
        
        document.body.appendChild(panel);
    }

    setupEventListeners() {
        // Toggle button click
        document.getElementById('notificationSettingsToggle').addEventListener('click', () => {
            this.togglePanel();
        });

        // Close panel when clicking outside
        document.addEventListener('click', (e) => {
            const panel = document.getElementById('notificationSettingsPanel');
            const button = document.getElementById('notificationSettingsToggle');
            
            if (this.isOpen && !panel.contains(e.target) && !button.contains(e.target)) {
                this.closePanel();
            }
        });

        // Setting toggles
        this.setupToggle('notificationsEnabledToggle', 'enabled');
        this.setupToggle('visualNotificationsToggle', 'visual');
        this.setupToggle('audioNotificationsToggle', 'audio');

        // Test buttons
        document.getElementById('testVisualBtn').addEventListener('click', async () => {
            await this.testVisualNotification();
        });

        document.getElementById('testAudioBtn').addEventListener('click', async () => {
            await this.testAudioNotification();
        });

        document.getElementById('testFullBtn').addEventListener('click', async () => {
            await this.testFullNotification();
        });

        // Update UI when settings change
        this.updateToggles();
    }

    setupToggle(toggleId, settingKey) {
        const toggle = document.getElementById(toggleId);
        
        toggle.addEventListener('click', () => {
            const newValue = !this.notificationManager.notificationSettings[settingKey];
            
            // Special handling for audio toggle - request permission first
            if (settingKey === 'audio' && newValue) {
                this.requestAudioPermission().then((granted) => {
                    if (granted) {
                        this.updateSetting(settingKey, newValue);
                        this.updateToggle(toggleId, newValue);
                    }
                });
            } else {
                this.updateSetting(settingKey, newValue);
                this.updateToggle(toggleId, newValue);
            }
            
            this.updateButtonState();
        });
    }

    updateSetting(key, value) {
        const newSettings = { [key]: value };
        this.notificationManager.updateSettings(newSettings);
    }

    updateToggle(toggleId, isActive) {
        const toggle = document.getElementById(toggleId);
        if (isActive) {
            toggle.classList.add('active');
        } else {
            toggle.classList.remove('active');
        }
    }

    updateToggles() {
        const settings = this.notificationManager.notificationSettings;
        this.updateToggle('notificationsEnabledToggle', settings.enabled);
        this.updateToggle('visualNotificationsToggle', settings.visual);
        this.updateToggle('audioNotificationsToggle', settings.audio);
    }

    updateButtonState() {
        const button = document.getElementById('notificationSettingsToggle');
        const settings = this.notificationManager.notificationSettings;
        
        if (settings.enabled) {
            button.classList.remove('disabled');
            button.querySelector('i').className = 'fas fa-bell';
        } else {
            button.classList.add('disabled');
            button.querySelector('i').className = 'fas fa-bell-slash';
        }
    }

    async requestAudioPermission() {
        try {
            // Try to resume audio context to test if audio is available
            if (this.notificationManager.audioContext) {
                await this.notificationManager.audioContext.resume();
                return this.notificationManager.audioContext.state === 'running';
            }
            return false;
        } catch (error) {
            console.warn('Audio permission request failed:', error);
            alert('Audio notifications require user interaction. Please try the audio test button.');
            return false;
        }
    }

    togglePanel() {
        if (this.isOpen) {
            this.closePanel();
        } else {
            this.openPanel();
        }
    }

    openPanel() {
        const panel = document.getElementById('notificationSettingsPanel');
        panel.classList.add('show');
        this.isOpen = true;
        
        // Update toggles to reflect current state
        this.updateToggles();
        
        // Request browser notification permission if not already granted
        if ('Notification' in window && Notification.permission === 'default') {
            this.notificationManager.requestNotificationPermission();
        }
    }

    closePanel() {
        const panel = document.getElementById('notificationSettingsPanel');
        panel.classList.remove('show');
        this.isOpen = false;
    }

    async testVisualNotification() {
        try {
            await this.notificationManager.testVisualNotification();
        } catch (error) {
            console.error('Visual test failed:', error);
        }
    }

    async testAudioNotification() {
        try {
            await this.notificationManager.testAudioNotification();
        } catch (error) {
            console.error('Audio test failed:', error);
            alert('Audio test failed. Please check your browser audio settings.');
        }
    }

    async testFullNotification() {
        try {
            await this.notificationManager.testFullNotification();
        } catch (error) {
            console.error('Full notification test failed:', error);
        }
    }
}

// Initialize notification settings UI when notification manager is ready
document.addEventListener('DOMContentLoaded', function() {
    // Wait for notification manager to be available
    const initSettingsUI = () => {
        if (window.notificationManager) {
            window.notificationSettingsUI = new NotificationSettingsUI(window.notificationManager);
        } else {
            setTimeout(initSettingsUI, 200);
        }
    };
    
    // Start checking after a delay to ensure scripts are loaded
    setTimeout(initSettingsUI, 200);
});

