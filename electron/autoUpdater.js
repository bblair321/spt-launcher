const { autoUpdater } = require("electron-updater");
const { ipcMain, dialog } = require("electron");

class AutoUpdateManager {
  constructor() {
    this.mainWindow = null;
    this.setupAutoUpdater();
    this.setupIpcHandlers();
  }

  setMainWindow(window) {
    this.mainWindow = window;
  }

  setupAutoUpdater() {
    // Configure auto-updater
    autoUpdater.autoDownload = false; // Let user decide
    autoUpdater.autoInstallOnAppQuit = true;

    // Set update server configuration for GitHub releases
    console.log("Using GitHub provider for updates");
    autoUpdater.setFeedURL({
      provider: "github",
      owner: "bblair321",
      repo: "spt-launcher",
      private: false,
      releaseType: "release"
    });

    // Standard configuration
    autoUpdater.allowPrerelease = false;
    autoUpdater.allowDowngrade = false;

    // Update available event
    autoUpdater.on("update-available", (info) => {
      console.log("Update available:", info);
      this.showUpdateAvailableDialog(info);
    });

    // No update available event
    autoUpdater.on("update-not-available", (info) => {
      console.log("No update available:", info);
    });

    // Download started event
    autoUpdater.on("download-started", () => {
      console.log("Download started");
      if (this.mainWindow) {
        this.mainWindow.webContents.send("update-download-started");
      }
    });

    // Download progress event
    autoUpdater.on("download-progress", (progressObj) => {
      console.log(`Download progress: ${progressObj.percent.toFixed(1)}%`);
      if (this.mainWindow) {
        this.mainWindow.webContents.send("update-download-progress", {
          speed: progressObj.bytesPerSecond,
          percent: progressObj.percent,
          transferred: progressObj.transferred,
          total: progressObj.total,
        });
      }
    });

    // Update downloaded event
    autoUpdater.on("update-downloaded", (info) => {
      this.showUpdateReadyDialog(info);
    });

    // Error handling
    autoUpdater.on("error", (err) => {
      console.error("Auto-updater error:", err.message);
      if (this.mainWindow) {
        this.mainWindow.webContents.send("update-error", err.message);
      }
    });
  }

  setupIpcHandlers() {
    // Check for updates
    ipcMain.handle("check-for-updates", async () => {
      try {
        console.log("Checking for updates...");
        const result = await autoUpdater.checkForUpdates();
        console.log("Update check completed:", result);
        return { success: true, updateInfo: result };
      } catch (error) {
        console.error("Update check failed:", error.message);
        return { success: false, error: error.message };
      }
    });

    // Download update
    ipcMain.handle("download-update", async () => {
      try {
        await autoUpdater.downloadUpdate();
        return { success: true };
      } catch (error) {
        return { success: false, error: error.message };
      }
    });

    // Install update
    ipcMain.handle("install-update", async () => {
      try {
        autoUpdater.quitAndInstall();
        return { success: true };
      } catch (error) {
        return { success: false, error: error.message };
      }
    });
  }

  showUpdateAvailableDialog(info) {
    if (!this.mainWindow) return;

    dialog
      .showMessageBox(this.mainWindow, {
        type: "info",
        title: "Update Available",
        message: `Version ${info.version} is available!`,
        detail: `Current version: ${require("electron").app.getVersion()}\nNew version: ${
          info.version
        }\n\nWould you like to download this update?`,
        buttons: ["Download Now", "Remind Me Later"],
        defaultId: 0,
        cancelId: 1,
      })
      .then((result) => {
        if (result.response === 0) { // Download Now
          console.log("Starting update download...");
          if (this.mainWindow) {
            this.mainWindow.webContents.send("update-available", info);
          }
          autoUpdater.downloadUpdate();
        }
      });
  }

  showUpdateReadyDialog(info) {
    if (!this.mainWindow) return;

    dialog
      .showMessageBox(this.mainWindow, {
        type: "info",
        title: "Update Ready",
        message: "Update downloaded successfully!",
        detail: `Version ${info.version} is ready to install.\n\nThe application will restart to complete the update.`,
        buttons: ["Restart Now", "Install Later"],
        defaultId: 0,
      })
      .then((result) => {
        if (result.response === 0) {
          autoUpdater.quitAndInstall();
        }
      });
  }

  // Start checking for updates
  startUpdateCheck() {
    // Check for updates every 10 minutes
    setInterval(() => {
      autoUpdater.checkForUpdates();
    }, 10 * 60 * 1000);

    // Initial check after 30 seconds
    setTimeout(() => {
      autoUpdater.checkForUpdates();
    }, 30000);
  }
}

module.exports = AutoUpdateManager;
