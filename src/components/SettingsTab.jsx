import React, { useState, useEffect } from "react";
import { Settings, Save, RefreshCw, Sun, Moon, Monitor } from "lucide-react";
import { useTheme } from "../hooks/useTheme";

function SettingsTab() {
  const { theme, changeTheme } = useTheme();
  
  const [settings, setSettings] = useState({
    autoStart: false,
    minimizeToTray: true,
    checkForUpdates: true,
    autoDownloadUpdates: true,
    silentUpdates: false,
    autoInstallUpdates: true,
    backgroundChecks: true,
  });

  const [saveFeedback, setSaveFeedback] = useState({
    show: false,
    message: "",
    type: "success", // "success" or "error"
  });

  const [updateStatus, setUpdateStatus] = useState({
    checking: false,
    lastChecked: null,
    updateAvailable: false,
    error: null,
    downloading: false,
    downloadProgress: 0,
    downloadSpeed: 0,
    downloaded: 0,
    total: 0,
    downloadCompleted: false,
    newVersion: null,
    releaseNotes: null,
    installing: false,
    installProgress: 0,
    installCompleted: false,
    currentVersion: null,
  });

  // Load saved settings from localStorage
  useEffect(() => {
    const savedSettings = localStorage.getItem("appSettings");
    if (savedSettings) {
      try {
        const parsedSettings = JSON.parse(savedSettings);
        setSettings(parsedSettings);
      } catch (error) {
        console.error("Failed to parse saved settings:", error);
      }
    }
  }, []);

  // Get current app version and set up event listeners
  useEffect(() => {
    const getVersion = async () => {
      try {
        if (window.electronAPI?.getAppVersion) {
          const version = await window.electronAPI.getAppVersion();
          setUpdateStatus((prev) => ({ ...prev, currentVersion: version }));
        }
      } catch (error) {
        console.error("Failed to get app version:", error);
      }
    };
    getVersion();

    // Set up electron-updater event listeners
    if (window.electronAPI) {
      const eventHandlers = {
        "update-status": (event, data) => {
          console.log("Update status event:", data);
          if (data.status === "checking") {
            setUpdateStatus((prev) => ({
              ...prev,
              checking: true,
              error: null,
            }));
          } else if (data.status === "no-update") {
            setUpdateStatus((prev) => ({
              ...prev,
              checking: false,
              updateAvailable: false,
              error: null,
            }));
          }
        },
        "update-available": (event, data) => {
          console.log("Update available event:", data);
          setUpdateStatus((prev) => ({
            ...prev,
            checking: false,
            updateAvailable: true,
            error: null,
            newVersion: data.version,
            releaseNotes: data.releaseNotes,
          }));
        },
        "update-error": (event, error) => {
          console.log("Update error event:", error);
          setUpdateStatus((prev) => ({
            ...prev,
            checking: false,
            error: error,
          }));
        },
        "update-download-progress": (event, data) => {
          console.log("Download progress event:", data);
          setUpdateStatus((prev) => ({
            ...prev,
            downloading: true,
            downloadProgress: data.percent,
            downloadSpeed: data.speed,
            downloaded: data.downloaded,
            total: data.total,
          }));
        },
        "update-downloaded": (event, data) => {
          console.log("Update downloaded event:", data);
          setUpdateStatus((prev) => ({
            ...prev,
            downloading: false,
            downloadCompleted: true,
            newVersion: data.version,
            releaseNotes: data.releaseNotes,
          }));
        },
      };

      // Register all event handlers
      Object.entries(eventHandlers).forEach(([event, handler]) => {
        window.electronAPI[
          `on${
            event.charAt(0).toUpperCase() +
            event.slice(1).replace(/-([a-z])/g, (g) => g[1].toUpperCase())
          }`
        ](handler);
      });

      // Cleanup function
      return () => {
        if (window.electronAPI) {
          Object.keys(eventHandlers).forEach((event) => {
            window.electronAPI.removeAllListeners(event);
          });
        }
      };
    }
  }, []);

  const handleSettingChange = (key, value) => {
    setSettings((prev) => ({ ...prev, [key]: value }));
  };

  const saveSettings = () => {
    try {
      localStorage.setItem("appSettings", JSON.stringify(settings));
      console.log("Settings saved successfully:", settings);
      setSaveFeedback({
        show: true,
        message: "Settings saved successfully!",
        type: "success",
      });
      // Hide feedback after 3 seconds
      setTimeout(() => {
        setSaveFeedback({ show: false, message: "", type: "success" });
      }, 3000);
    } catch (error) {
      console.error("Failed to save settings:", error);
      setSaveFeedback({
        show: true,
        message: "Failed to save settings",
        type: "error",
      });
      // Hide feedback after 3 seconds
      setTimeout(() => {
        setSaveFeedback({ show: false, message: "", type: "success" });
      }, 3000);
    }
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
      console.log("=== SETTINGS TAB: Update check result ===", result);

      if (result?.success === false) {
        setUpdateStatus((prev) => ({
          ...prev,
          checking: false,
          error: result.error || "Update check failed",
        }));
      } else {
        setUpdateStatus((prev) => ({
          ...prev,
          checking: false,
          lastChecked: new Date().toLocaleTimeString(),
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

  const handleDownloadUpdate = async () => {
    console.log("=== SETTINGS TAB: Download update button clicked ===");
    try {
      setUpdateStatus((prev) => ({
        ...prev,
        downloading: true,
        error: null,
        downloadProgress: 0,
      }));

      const downloadResult = await window.electronAPI.downloadUpdate();
      console.log("=== SETTINGS TAB: Download result ===", downloadResult);

      if (!downloadResult.success) {
        setUpdateStatus((prev) => ({
          ...prev,
          error: downloadResult.error || "Failed to download update",
          downloading: false,
        }));
      }
    } catch (error) {
      console.error("=== SETTINGS TAB: Download error ===", error);
      setUpdateStatus((prev) => ({
        ...prev,
        error: error.message || "Error downloading update",
        downloading: false,
      }));
    }
  };

  // Auto-install handler for when download completes
  const handleAutoInstall = async () => {
    if (settings.autoInstallUpdates && updateStatus.downloadCompleted) {
      console.log("=== SETTINGS TAB: Auto-installing update ===");
      try {
        await window.electronAPI.installUpdate();
      } catch (error) {
        console.error("Auto-installation failed:", error);
        setUpdateStatus((prev) => ({
          ...prev,
          error: "Auto-installation failed: " + error.message,
        }));
      }
    }
  };

  // Watch for download completion to trigger auto-install
  useEffect(() => {
    if (updateStatus.downloadCompleted && settings.autoInstallUpdates) {
      handleAutoInstall();
    }
  }, [updateStatus.downloadCompleted, settings.autoInstallUpdates]);

  const formatBytes = (bytes) => {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
  };

  return (
    <div className="space-y-6">
             <div className="text-center">
         <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">Settings</h1>
         <p className="text-gray-600 dark:text-gray-400">Configure your SPT Launcher preferences</p>
         {updateStatus.currentVersion && (
           <div className="mt-2 text-sm text-gray-500 dark:text-gray-400">
             <span className="bg-gray-100 dark:bg-gray-700 px-2 py-1 rounded-md font-mono">
               v{updateStatus.currentVersion}
             </span>
           </div>
         )}
       </div>

      <div className="max-w-2xl mx-auto">
        {/* Application Settings Section */}
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm space-y-6">
          <h2 className="text-xl font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Settings className="w-5 h-5" />
            <span>Application Settings</span>
          </h2>

          <div className="space-y-4">
                         <div className="flex items-center justify-between">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Auto-start with Windows</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
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
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Minimize to System Tray</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
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
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Check for Updates</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
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
                <div>
                  <h3 className="font-medium text-gray-900 dark:text-gray-100">Theme</h3>
                  <p className="text-sm text-gray-600 dark:text-gray-400">
                    Choose your preferred appearance
                  </p>
                </div>
               <div className="grid grid-cols-3 gap-3">
                 <button
                   onClick={() => changeTheme('light')}
                   className={`p-4 rounded-lg border-2 transition-all ${
                     theme === 'light'
                       ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20'
                       : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500'
                   }`}
                 >
                   <Sun className="w-6 h-6 mx-auto mb-2 text-yellow-600" />
                   <span className="text-sm font-medium">Light</span>
                 </button>
                 
                 <button
                   onClick={() => changeTheme('dark')}
                   className={`p-4 rounded-lg border-2 transition-all ${
                     theme === 'dark'
                       ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20'
                       : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500'
                   }`}
                 >
                   <Moon className="w-6 h-6 mx-auto mb-2 text-blue-600" />
                   <span className="text-sm font-medium">Dark</span>
                 </button>
                 
                 <button
                   onClick={() => changeTheme('system')}
                   className={`p-4 rounded-lg border-2 transition-all ${
                     theme === 'system'
                       ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20'
                       : 'border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500'
                   }`}
                 >
                   <Monitor className="w-6 h-6 mx-auto mb-2 text-gray-600" />
                   <span className="text-sm font-medium">System</span>
                 </button>
               </div>
             </div>
          </div>

          {/* Save Feedback */}
          {saveFeedback.show && (
            <div className={`p-3 rounded-md ${
              saveFeedback.type === "success" 
                ? "bg-green-100 dark:bg-green-900/20 border border-green-300 dark:border-green-700" 
                : "bg-red-100 dark:bg-red-900/20 border border-red-300 dark:border-red-700"
            }`}>
              <p className={`text-sm ${
                saveFeedback.type === "success" 
                  ? "text-green-700 dark:text-green-300" 
                  : "text-red-700 dark:text-red-300"
              }`}>
                {saveFeedback.message}
              </p>
            </div>
          )}

          <div className="flex justify-end">
            <button
              onClick={saveSettings}
              className="flex items-center space-x-2 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
            >
              <Save className="w-4 h-4" />
              <span>Save Settings</span>
            </button>
          </div>
        </div>

                                   {/* Update Management Section */}
          <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm space-y-6">
            <h2 className="text-xl font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
              <RefreshCw className="w-5 h-5" />
              <span>Update Management</span>
            </h2>

          {/* Update Automation Settings */}
          <div className="space-y-4">
                         <div className="flex items-center justify-between">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Auto-download Updates</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
                   Automatically download updates when available
                 </p>
               </div>
              <input
                type="checkbox"
                checked={settings.autoDownloadUpdates}
                onChange={(e) =>
                  handleSettingChange("autoDownloadUpdates", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

                         <div className="flex items-center justify-between">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Silent Updates</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
                   Download updates in background without notifications
                 </p>
               </div>
              <input
                type="checkbox"
                checked={settings.silentUpdates}
                onChange={(e) =>
                  handleSettingChange("silentUpdates", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

                         <div className="flex items-center justify-between">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Auto-install Updates</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
                   Automatically install updates after download
                 </p>
               </div>
              <input
                type="checkbox"
                checked={settings.autoInstallUpdates}
                onChange={(e) =>
                  handleSettingChange("autoInstallUpdates", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>

                         <div className="flex items-center justify-between">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Background Update Checks</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">
                   Check for updates while launcher is running
                 </p>
               </div>
              <input
                type="checkbox"
                checked={settings.backgroundChecks}
                onChange={(e) =>
                  handleSettingChange("backgroundChecks", e.target.checked)
                }
                className="rounded border-gray-300"
              />
            </div>
          </div>

          {/* Manual Update Controls */}
          <div className="border-t pt-4">
                         <div className="flex items-center justify-between mb-4">
               <div>
                 <h3 className="font-medium text-gray-900 dark:text-gray-100">Manual Update Check</h3>
                 <p className="text-sm text-gray-600 dark:text-gray-400">Check for updates now</p>
               </div>
              <button
                onClick={handleManualUpdateCheck}
                disabled={updateStatus.checking}
                className="flex items-center space-x-2 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                <RefreshCw
                  className={`w-4 h-4 ${
                    updateStatus.checking ? "animate-spin" : ""
                  }`}
                />
                <span>
                  {updateStatus.checking ? "Checking..." : "Check for Updates"}
                </span>
              </button>
            </div>

            {updateStatus.lastChecked && (
              <p className="text-sm text-gray-500">
                Last checked: {updateStatus.lastChecked}
              </p>
            )}

            {updateStatus.error && (
              <div className="mt-2 p-3 bg-red-100 border border-red-300 rounded-md">
                <p className="text-sm text-red-700">
                  Update Check Failed: {updateStatus.error}
                </p>
              </div>
            )}
          </div>

          {/* Update Status Display */}
          {updateStatus.updateAvailable && (
            <div className="border-t pt-4">
              <div className="bg-blue-50 border border-blue-200 rounded-md p-4">
                <div className="flex items-start justify-between">
                  <div className="flex-1">
                    <h3 className="font-medium text-blue-900 mb-2">
                      Update Available!
                    </h3>
                    <div className="space-y-2 text-sm text-blue-800">
                      <div>
                        <span className="font-medium">Current:</span> v
                        {updateStatus.currentVersion}
                        <span className="mx-2">→</span>
                        <span className="font-medium">New:</span> v
                        {updateStatus.newVersion}
                      </div>
                      {updateStatus.releaseNotes && (
                        <div>
                          <span className="font-medium">Release Notes:</span>{" "}
                          {updateStatus.releaseNotes}
                        </div>
                      )}
                    </div>
                  </div>
                  <button
                    onClick={handleDownloadUpdate}
                    disabled={updateStatus.downloading}
                    className="ml-4 px-4 py-2 bg-blue-600 text-white text-sm rounded-md hover:bg-blue-700 disabled:opacity-50 transition-colors"
                  >
                    {updateStatus.downloading
                      ? "Downloading..."
                      : "Download Update"}
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Download Progress */}
          {updateStatus.downloading && (
            <div className="border-t pt-4">
              <div className="space-y-3">
                <div className="flex justify-between text-sm">
                  <span>Downloading update...</span>
                  <span>{Math.round(updateStatus.downloadProgress)}%</span>
                </div>
                <div className="w-full bg-gray-200 rounded-full h-2">
                  <div
                    className="bg-blue-600 h-2 rounded-full transition-all duration-300"
                    style={{ width: `${updateStatus.downloadProgress}%` }}
                  ></div>
                </div>
                <div className="flex justify-between text-xs text-gray-500">
                  <span>{formatBytes(updateStatus.downloaded)}</span>
                  <span>{formatBytes(updateStatus.total)}</span>
                  <span>{formatBytes(updateStatus.downloadSpeed)}/s</span>
                </div>
              </div>
            </div>
          )}

          {/* Download Completed */}
          {updateStatus.downloadCompleted && (
            <div className="border-t pt-4">
              <div className="bg-green-50 border border-green-200 rounded-md p-4">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <h3 className="font-medium text-green-900 mb-1">
                      ✅ Update Downloaded Successfully!
                    </h3>
                    <p className="text-sm text-green-700">
                      Version {updateStatus.newVersion} is ready to install
                    </p>
                  </div>
                  <button
                    onClick={() => window.electronAPI.installUpdate()}
                    className="ml-4 px-4 py-2 bg-green-600 text-white text-sm rounded-md hover:bg-green-700 transition-colors"
                  >
                    🔧 Install Update
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Installation Progress */}
          {updateStatus.installing && (
            <div className="border-t pt-4">
              <div className="space-y-3">
                <div className="flex justify-between text-sm">
                  <span>Installing update...</span>
                  <span>{updateStatus.installProgress}%</span>
                </div>
                <div className="w-full bg-gray-200 rounded-full h-2">
                  <div
                    className="bg-green-600 h-2 rounded-full transition-all duration-300"
                    style={{ width: `${updateStatus.installProgress}%` }}
                  ></div>
                </div>
              </div>
            </div>
          )}

          {/* Installation Completed */}
          {updateStatus.installCompleted && (
            <div className="border-t pt-4">
              <div className="bg-green-50 border border-green-200 rounded-md p-4">
                <div className="text-center">
                  <h3 className="font-medium text-green-900 mb-2">
                    🎉 Update Installation Complete!
                  </h3>
                  <p className="text-sm text-green-700 mb-3">
                    The app will restart automatically to complete the update.
                  </p>
                  <div className="text-xs text-green-600">
                    ✅ The app will restart automatically to complete the
                    update.
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default SettingsTab;
