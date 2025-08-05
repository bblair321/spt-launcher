// Update Manager for SPT-AKI Launcher
class UpdateManager {
  constructor() {
    this.updateState = {
      is_checking: false,
      is_downloading: false,
      progress: 0.0,
      current_version: "Loading...",
      latest_version: null,
      update_available: false,
      last_check: null,
      error_message: null,
    };

    this.updateCheckInterval = null;
    this.autoCheckEnabled = true;
    this.checkInterval = 24 * 60 * 60 * 1000; // 24 hours

    this.init();
  }

  init() {
    this.setupUpdateUI();
    this.setupEventListeners();
    this.loadUpdateSettings();
    this.loadCurrentVersion();
    this.startAutoCheck();
  }

  setupUpdateUI() {
    // Create update notification element
    const updateNotification = document.createElement("div");
    updateNotification.id = "update-notification";
    updateNotification.className = "update-notification hidden";
    updateNotification.innerHTML = `
            <div class="update-content">
                <div class="update-header">
                    <span class="update-icon">🔄</span>
                    <span class="update-title">Update Available</span>
                    <button class="update-close" onclick="updateManager.hideNotification()">×</button>
                </div>
                <div class="update-body">
                    <p>Version <span id="latest-version"></span> is available!</p>
                    <div class="update-actions">
                        <button id="download-update" class="btn btn-primary">Download Update</button>
                        <button id="view-release-notes" class="btn btn-secondary">Release Notes</button>
                        <button id="skip-update" class="btn btn-tertiary">Skip</button>
                    </div>
                </div>
                <div class="update-progress hidden">
                    <div class="progress-bar">
                        <div class="progress-fill"></div>
                    </div>
                    <div class="progress-text">Downloading... <span class="progress-percentage">0%</span></div>
                </div>
            </div>
        `;

    // Add to body
    document.body.appendChild(updateNotification);

    // Add update status to system status section
    const systemStatus = document.getElementById("system-status");
    if (systemStatus) {
      const updateStatus = document.createElement("div");
      updateStatus.id = "update-status";
      updateStatus.className = "status-item";
      updateStatus.innerHTML = `
                <span class="status-label">Updates:</span>
                <span class="status-value" id="update-status-value">Checking...</span>
                <button id="check-updates-btn" class="btn btn-small">Check</button>
            `;
      systemStatus.appendChild(updateStatus);
    }
  }

  setupEventListeners() {
    // Check updates button
    const checkUpdatesBtn = document.getElementById("check-updates-btn");
    if (checkUpdatesBtn) {
      checkUpdatesBtn.addEventListener("click", () => this.checkForUpdates());
    }

    // Update notification buttons
    document.addEventListener("click", (e) => {
      if (e.target.id === "download-update") {
        this.downloadUpdate();
      } else if (e.target.id === "view-release-notes") {
        this.showReleaseNotes();
      } else if (e.target.id === "skip-update") {
        this.skipUpdate();
      }
    });

    // Listen for update state changes from backend
    if (window.__TAURI__) {
      window.__TAURI__.event.listen("update-state-changed", (event) => {
        this.updateState = event.payload;
        this.updateUI();
      });
    }
  }

  async checkForUpdates() {
    try {
      this.setCheckingState(true);

      const result = await safeInvoke("check_for_updates");

      if (result && result.version) {
        this.updateState.update_available = true;
        this.updateState.latest_version = result.version;
        this.updateState.latest_update_info = result; // Store the full update info
        this.showNotification(result);
      } else {
        this.updateState.update_available = false;
        this.updateState.error_message = result || "No update available";
        this.updateStatus("Up to date");
      }
    } catch (error) {
      this.updateState.error_message = error.toString();
      this.updateStatus("Check failed");
    } finally {
      this.setCheckingState(false);
    }
  }

  async downloadUpdate() {
    try {
      this.setDownloadingState(true);
      this.showProgress();

      // Use the stored update info from the latest check
      if (
        !this.updateState.latest_update_info ||
        !this.updateState.latest_update_info.version
      ) {
        const errorMsg =
          "No update information available. Please check for updates first.";
        throw new Error(errorMsg);
      }

      // Pass parameters as an object with named properties
      const result = await safeInvoke("download_update", {
        downloadUrl: this.updateState.latest_update_info.download_url,
        version: this.updateState.latest_update_info.version,
      });

      if (result && result.includes("downloaded successfully")) {
        this.showInstallPrompt();
      } else {
        throw new Error(result || "Download failed");
      }
    } catch (error) {
      this.showError(`Download failed: ${error.message}`);
    } finally {
      this.setDownloadingState(false);
      this.hideProgress();
    }
  }

  async installUpdate() {
    try {
      const result = await safeInvoke("install_update");

      // Clear the update state since we're installing
      await safeInvoke("clear_update_state");
      this.hideNotification();

      // Show a message that the installer is launching
      this.showInstallMessage(
        "Update installer is launching. The application will close shortly."
      );
    } catch (error) {
      this.showError(`Install failed: ${error.message}`);
    }
  }

  async skipUpdate() {
    try {
      await safeInvoke("skip_update");
      this.hideNotification();
      this.updateStatus("Update skipped");
    } catch (error) {
      console.error("Skip failed:", error);
    }
  }

  async showReleaseNotes() {
    try {
      const releaseNotes = await safeInvoke(
        "get_release_notes",
        this.updateState.latest_version
      );
      this.showReleaseNotesModal(releaseNotes);
    } catch (error) {
      console.error("Failed to load release notes:", error);
      this.showError("Failed to load release notes");
    }
  }

  showNotification(updateInfo) {
    const notification = document.getElementById("update-notification");
    const latestVersion = document.getElementById("latest-version");

    if (latestVersion) {
      latestVersion.textContent = updateInfo.version;
    }

    notification.classList.remove("hidden");
    this.updateStatus(`Update available: ${updateInfo.version}`);

    // Auto-hide after 30 seconds if not interacted with
    setTimeout(() => {
      if (notification.classList.contains("hidden")) return;
      this.hideNotification();
    }, 30000);
  }

  hideNotification() {
    const notification = document.getElementById("update-notification");
    notification.classList.add("hidden");
  }

  showProgress() {
    const progress = document.querySelector(".update-progress");
    if (progress) {
      progress.classList.remove("hidden");
    }
  }

  hideProgress() {
    const progress = document.querySelector(".update-progress");
    if (progress) {
      progress.classList.add("hidden");
    }
  }

  updateProgress(percentage) {
    const progressFill = document.querySelector(".progress-fill");
    const progressText = document.querySelector(".progress-percentage");

    if (progressFill) {
      progressFill.style.width = `${percentage}%`;
    }

    if (progressText) {
      progressText.textContent = `${Math.round(percentage)}%`;
    }
  }

  showInstallPrompt() {
    const notification = document.getElementById("update-notification");
    const updateBody = notification.querySelector(".update-body");

    updateBody.innerHTML = `
            <p>Update downloaded successfully!</p>
            <div class="update-actions">
                <button id="install-update" class="btn btn-primary">Install & Restart</button>
                <button id="install-later" class="btn btn-secondary">Install Later</button>
            </div>
        `;

    // Add event listeners for new buttons
    document
      .getElementById("install-update")
      .addEventListener("click", () => this.installUpdate());
    document
      .getElementById("install-later")
      .addEventListener("click", () => this.hideNotification());
  }

  showReleaseNotesModal(notes) {
    const modal = document.createElement("div");
    modal.className = "modal-overlay";
    modal.innerHTML = `
            <div class="modal-content">
                <div class="modal-header">
                    <h3>Release Notes - ${this.updateState.latest_version}</h3>
                    <button class="modal-close" onclick="this.closest('.modal-overlay').remove()">×</button>
                </div>
                <div class="modal-body">
                    <pre class="release-notes">${notes}</pre>
                </div>
            </div>
        `;

    document.body.appendChild(modal);

    // Close on overlay click
    modal.addEventListener("click", (e) => {
      if (e.target === modal) {
        modal.remove();
      }
    });
  }

  showError(message) {
    const notification = document.getElementById("update-notification");
    const updateBody = notification.querySelector(".update-body");

    updateBody.innerHTML = `
            <p class="error-message">${message}</p>
            <div class="update-actions">
                <button onclick="updateManager.hideNotification()" class="btn btn-secondary">Close</button>
            </div>
        `;
  }

  showInstallMessage(message) {
    const notification = document.getElementById("update-notification");
    const updateBody = notification.querySelector(".update-body");

    updateBody.innerHTML = `
            <p class="install-message">${message}</p>
            <div class="update-actions">
                <button onclick="updateManager.hideNotification()" class="btn btn-secondary">Close</button>
            </div>
        `;
  }

  setCheckingState(checking) {
    this.updateState.is_checking = checking;
    this.updateUI();
  }

  setDownloadingState(downloading) {
    this.updateState.is_downloading = downloading;
    this.updateUI();
  }

  updateUI() {
    // Update status display
    this.updateStatus();

    // Update check button state
    const checkBtn = document.getElementById("check-updates-btn");
    if (checkBtn) {
      checkBtn.disabled = this.updateState.is_checking;
      checkBtn.textContent = this.updateState.is_checking
        ? "Checking..."
        : "Check";
    }

    // Update progress if downloading
    if (this.updateState.is_downloading) {
      this.updateProgress(this.updateState.progress);
    }
  }

  updateStatus(message = null) {
    const statusElement = document.getElementById("update-status-value");
    if (!statusElement) return;

    if (message) {
      statusElement.textContent = message;
      return;
    }

    if (this.updateState.is_checking) {
      statusElement.textContent = "Checking...";
    } else if (this.updateState.update_available) {
      statusElement.textContent = `Update available: ${this.updateState.latest_version}`;
    } else if (this.updateState.error_message) {
      statusElement.textContent = "Check failed";
    } else {
      statusElement.textContent = "Up to date";
    }
  }

  startAutoCheck() {
    if (!this.autoCheckEnabled) return;

    // Check on startup
    setTimeout(() => {
      this.checkForUpdates();
    }, 5000); // Wait 5 seconds after startup

    // Set up periodic checks
    this.updateCheckInterval = setInterval(() => {
      this.checkForUpdates();
    }, this.checkInterval);
  }

  stopAutoCheck() {
    if (this.updateCheckInterval) {
      clearInterval(this.updateCheckInterval);
      this.updateCheckInterval = null;
    }
  }

  loadUpdateSettings() {
    try {
      const settings = localStorage.getItem("updateSettings");
      if (settings) {
        const parsed = JSON.parse(settings);
        this.autoCheckEnabled = parsed.autoCheckEnabled !== false;
        this.checkInterval = parsed.checkInterval || this.checkInterval;
      }
    } catch (error) {
      console.error("Failed to load update settings:", error);
    }
  }

  saveUpdateSettings() {
    try {
      const settings = {
        autoCheckEnabled: this.autoCheckEnabled,
        checkInterval: this.checkInterval,
      };
      localStorage.setItem("updateSettings", JSON.stringify(settings));
    } catch (error) {
      console.error("Failed to save update settings:", error);
    }
  }

  async loadCurrentVersion() {
    try {
      if (window.__TAURI__) {
        const currentVersion = await safeInvoke("get_current_version");
        this.updateState.current_version = currentVersion;
        this.updateUI();
      }
    } catch (error) {
      console.error("Failed to load current version:", error);
      this.updateState.current_version = "Unknown";
    }
  }
}

// Initialize update manager when DOM is ready
let updateManager;
document.addEventListener("DOMContentLoaded", () => {
  updateManager = new UpdateManager();
});

// Export for global access
window.updateManager = updateManager;
