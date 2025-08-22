import React, { useState, useEffect } from "react";
import { Settings, Save, RefreshCw } from "lucide-react";

function SettingsTab() {
  const [settings, setSettings] = useState({
    autoStart: false,
    minimizeToTray: true,
    checkForUpdates: true,
    theme: "system",
  });

  const [updateStatus, setUpdateStatus] = useState({
    checking: false,
    lastChecked: null,
    updateAvailable: false,
    error: null,
    downloading: false,
    downloadProgress: 0,
    downloadSpeed: 0,
  });

  const handleSettingChange = (key, value) => {
    setSettings((prev) => ({ ...prev, [key]: value }));
  };

  const saveSettings = () => {
    localStorage.setItem("appSettings", JSON.stringify(settings));
    // Show success message
  };

  const handleManualUpdateCheck = async () => {
    if (!window.electronAPI?.checkForUpdates) {
      setUpdateStatus((prev) => ({
        ...prev,
        error: "Update system not available",
      }));
      return;
    }

    setUpdateStatus((prev) => ({ ...prev, checking: true, error: null }));

          try {
        console.log("=== SETTINGS TAB: Starting update check ===");
        const result = await window.electronAPI.checkForUpdates();
        console.log("=== SETTINGS TAB: Update check result ===", JSON.stringify(result, null, 2));

        if (result?.success === false) {
          // Handle error from main process
          const errorMsg = result.error || "Update check failed";
          console.error("=== SETTINGS TAB: Update check failed ===", errorMsg);

          // Show error in alert for debugging
          alert(`Update Check Failed:\n\n${errorMsg}`);

          setUpdateStatus((prev) => ({
            ...prev,
            checking: false,
            error: errorMsg,
          }));
        } else {
          // Handle success
          console.log("=== SETTINGS TAB: Update check succeeded ===", JSON.stringify(result, null, 2));
          setUpdateStatus((prev) => ({
            ...prev,
            checking: false,
            lastChecked: new Date().toLocaleTimeString(),
            updateAvailable: result?.updateInfo?.updateAvailable || false,
          }));
        }
    } catch (error) {
      console.error("=== SETTINGS TAB: Update check error ===", error);
      setUpdateStatus((prev) => ({
        ...prev,
        checking: false,
        error: error.message || "Failed to check for updates",
      }));
    }
  };

  // Listen for update events from the main process
  useEffect(() => {
    if (!window.electronAPI?.on) return;

    const handleUpdateAvailable = (event, info) => {
      console.log("=== SETTINGS TAB: Update available event ===", JSON.stringify(info, null, 2));
      setUpdateStatus((prev) => ({
        ...prev,
        updateAvailable: true,
        lastChecked: new Date().toLocaleTimeString(),
      }));
    };

    const handleUpdateDownloaded = (event, info) => {
      console.log("=== SETTINGS TAB: Update downloaded event ===", JSON.stringify(info, null, 2));
      setUpdateStatus((prev) => ({
        ...prev,
        updateAvailable: true,
        lastChecked: new Date().toLocaleTimeString(),
      }));
    };

    const handleUpdateError = (event, error) => {
      console.log("=== SETTINGS TAB: Update error event ===", JSON.stringify(error, null, 2));
      setUpdateStatus((prev) => ({
        ...prev,
        error: error.message || "Update check failed",
        checking: false,
      }));
    };

    const handleUpdateDownloadStarted = (event) => {
      console.log("=== SETTINGS TAB: Download started event ===");
      setUpdateStatus((prev) => ({
        ...prev,
        downloading: true,
        error: null,
      }));
    };

    const handleUpdateDownloadProgress = (event, progress) => {
      console.log("=== SETTINGS TAB: Download progress event ===", progress);
      setUpdateStatus((prev) => ({
        ...prev,
        downloadProgress: progress.percent,
        downloadSpeed: progress.speed,
      }));
    };

    const handleDownloadAttemptStarted = (event) => {
      console.log("=== SETTINGS TAB: Download attempt started event ===");
      setUpdateStatus((prev) => ({
        ...prev,
        downloading: true,
        error: null,
        downloadProgress: 0,
      }));
      
      // Set a timeout to detect stuck downloads
      setTimeout(() => {
        console.log("=== SETTINGS TAB: Download timeout check ===");
        if (updateStatus.downloading && updateStatus.downloadProgress === 0) {
          console.error("=== SETTINGS TAB: Download appears stuck at 0% ===");
          setUpdateStatus((prev) => ({
            ...prev,
            error: "Download timeout - stuck at 0%. Check if latest.yml has correct GitHub URL.",
            downloading: false,
          }));
        }
      }, 15000); // 15 second timeout
    };

    // Set up event listeners
    window.electronAPI.on("update-available", handleUpdateAvailable);
    window.electronAPI.on("update-downloaded", handleUpdateDownloaded);
    window.electronAPI.on("update-error", handleUpdateError);
    window.electronAPI.on(
      "update-download-started",
      handleUpdateDownloadStarted
    );
    window.electronAPI.on(
      "update-download-progress",
      handleUpdateDownloadProgress
    );
    window.electronAPI.on(
      "download-attempt-started",
      handleDownloadAttemptStarted
    );

    // Cleanup
    return () => {
      window.electronAPI.removeListener(
        "update-available",
        handleUpdateAvailable
      );
      window.electronAPI.removeListener(
        "update-downloaded",
        handleUpdateDownloaded
      );
      window.electronAPI.removeListener("update-error", handleUpdateError);
      window.electronAPI.removeListener(
        "update-download-started",
        handleUpdateDownloadStarted
      );
      window.electronAPI.removeListener(
        "update-download-progress",
        handleUpdateDownloadProgress
      );
      window.electronAPI.removeListener(
        "download-attempt-started",
        handleDownloadAttemptStarted
      );
    };
  }, []);

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 mb-2">Settings</h1>
        <p className="text-gray-600">Configure your SPT Launcher preferences</p>
      </div>

      <div className="max-w-2xl mx-auto">
        <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm space-y-6">
          <h2 className="text-xl font-semibold flex items-center space-x-2">
            <Settings className="w-5 h-5" />
            <span>Application Settings</span>
          </h2>

          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-medium">Auto-start with Windows</h3>
                <p className="text-sm text-gray-600">
                  Launch SPT Launcher when Windows starts
                </p>
              </div>
              <input
                type="checkbox"
                checked={settings.autoStart}
                onChange={(e) =>
                  handleSettingChange("autoStart", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-medium">Minimize to System Tray</h3>
                <p className="text-sm text-gray-600">
                  Keep launcher running in background
                </p>
              </div>
              <input
                type="checkbox"
                checked={settings.minimizeToTray}
                onChange={(e) =>
                  handleSettingChange("minimizeToTray", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-medium">Check for Updates</h3>
                <p className="text-sm text-gray-600">
                  Automatically check for launcher updates
                </p>
              </div>
              <input
                type="checkbox"
                checked={settings.checkForUpdates}
                onChange={(e) =>
                  handleSettingChange("checkForUpdates", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="font-medium">Manual Update Check</h3>
                  <p className="text-sm text-gray-600">
                    Check for updates right now
                  </p>
                </div>
                <button
                  onClick={handleManualUpdateCheck}
                  disabled={updateStatus.checking}
                  className={`px-3 py-1.5 rounded-md transition-colors text-sm flex items-center space-x-2 ${
                    updateStatus.checking
                      ? "bg-gray-400 cursor-not-allowed"
                      : "bg-green-600 hover:bg-green-700 text-white"
                  }`}
                >
                  <RefreshCw
                    className={`w-3 h-3 ${
                      updateStatus.checking ? "animate-spin" : ""
                    }`}
                  />
                  <span>
                    {updateStatus.checking ? "Checking..." : "Check Now"}
                  </span>
                </button>
              </div>

              {/* Update Status Display */}
              {updateStatus.lastChecked && (
                <div className="text-xs text-gray-500 ml-4">
                  Last checked: {updateStatus.lastChecked}
                  {updateStatus.updateAvailable && (
                    <span className="text-green-600 font-medium ml-2">
                      • Update available!
                    </span>
                  )}
                </div>
              )}

              {updateStatus.downloading && (
                <div className="text-xs text-blue-600 ml-4">
                  Downloading update...{" "}
                  {updateStatus.downloadProgress.toFixed(1)}%
                  {updateStatus.downloadSpeed > 0 && (
                    <span className="ml-2">
                      (
                      {Math.round(
                        (updateStatus.downloadSpeed / 1024 / 1024) * 100
                      ) / 100}{" "}
                      MB/s)
                    </span>
                  )}
                </div>
              )}

              {updateStatus.error && (
                <div className="text-xs text-red-500 ml-4">
                  Error: {updateStatus.error}
                </div>
              )}
            </div>

            <div className="flex items-center justify-between">
              <div>
                <h3 className="font-medium">Theme</h3>
                <p className="text-sm text-gray-600">
                  Choose your preferred appearance
                </p>
              </div>
              <select
                value={settings.theme}
                onChange={(e) => handleSettingChange("theme", e.target.value)}
                className="px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900"
              >
                <option value="system">System</option>
                <option value="light">Light</option>
                <option value="dark">Dark</option>
              </select>
            </div>
          </div>

          <div className="flex space-x-2 pt-4">
            <button
              onClick={saveSettings}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2"
            >
              <Save className="w-4 h-4" />
              <span>Save Settings</span>
            </button>
            <button
              onClick={() => window.location.reload()}
              className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors flex items-center space-x-2"
            >
              <RefreshCw className="w-4 h-4" />
              <span>Reset to Defaults</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

export default SettingsTab;
