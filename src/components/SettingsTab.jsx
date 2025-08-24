import React, { useState, useEffect } from "react";
import { Settings, Save, RefreshCw } from "lucide-react";

function SettingsTab() {
  const [settings, setSettings] = useState({
    autoStart: false,
    minimizeToTray: true,
    checkForUpdates: true,
    autoDownloadUpdates: true,
    silentUpdates: false,
    autoInstallUpdates: true,
    backgroundChecks: true,
    checkInterval: 30, // minutes
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

  // Get current app version on component mount
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
      // Update status events
      window.electronAPI.onUpdateStatus((event, data) => {
        console.log("Update status event:", data);
        if (data.status === "checking") {
          setUpdateStatus((prev) => ({ ...prev, checking: true, error: null }));
        } else if (data.status === "no-update") {
          setUpdateStatus((prev) => ({
            ...prev,
            checking: false,
            updateAvailable: false,
            error: null,
          }));
        }
      });

      // Update available event
      window.electronAPI.onUpdateAvailable((event, data) => {
        console.log("Update available event:", data);
        setUpdateStatus((prev) => ({
          ...prev,
          checking: false,
          updateAvailable: true,
          error: null,
          newVersion: data.version,
          releaseNotes: data.releaseNotes,
        }));
      });

      // Update error event
      window.electronAPI.onUpdateError((event, error) => {
        console.log("Update error event:", error);
        setUpdateStatus((prev) => ({
          ...prev,
          checking: false,
          error: error,
        }));
      });

      // Download progress event
      window.electronAPI.onUpdateDownloadProgress((event, data) => {
        console.log("Download progress event:", data);
        setUpdateStatus((prev) => ({
          ...prev,
          downloading: true,
          downloadProgress: data.percent,
          downloadSpeed: data.speed,
          downloaded: data.downloaded,
          total: data.total,
        }));
      });

      // Download completed event
      window.electronAPI.onUpdateDownloaded((event, data) => {
        console.log("Update downloaded event:", data);
        setUpdateStatus((prev) => ({
          ...prev,
          downloading: false,
          downloadCompleted: true,
          newVersion: data.version,
          releaseNotes: data.releaseNotes,
        }));
      });
    }

    // Cleanup function
    return () => {
      if (window.electronAPI) {
        window.electronAPI.removeAllListeners("update-status");
        window.electronAPI.removeAllListeners("update-available");
        window.electronAPI.removeAllListeners("update-error");
        window.electronAPI.removeAllListeners("update-download-progress");
        window.electronAPI.removeAllListeners("update-downloaded");
      }
    };
  }, []);

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

      // Use the new electron-updater download method
      const downloadResult = await window.electronAPI.downloadUpdate();
      console.log("=== SETTINGS TAB: Download result ===", downloadResult);

      if (!downloadResult.success) {
        setUpdateStatus((prev) => ({
          ...prev,
          error: downloadResult.error || "Failed to download update",
          downloading: false,
        }));
      }
      // Note: Download progress and completion are handled by events
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
        <h1 className="text-3xl font-bold text-gray-900 mb-2">Settings</h1>
        <p className="text-gray-600">Configure your SPT Launcher preferences</p>
        {updateStatus.currentVersion && (
          <div className="mt-2 text-sm text-gray-500">
            <span className="bg-gray-100 px-2 py-1 rounded-md font-mono">
              v{updateStatus.currentVersion}
            </span>
          </div>
        )}
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

            {/* Automation Settings */}
            {settings.checkForUpdates && (
              <div className="space-y-3 p-3 bg-gray-50 rounded-md border border-gray-200">
                <h4 className="font-medium text-sm text-gray-700">
                  Update Automation
                </h4>

                <div className="flex items-center justify-between">
                  <div>
                    <h5 className="text-sm font-medium">
                      Auto-download Updates
                    </h5>
                    <p className="text-xs text-gray-600">
                      Automatically download updates when detected
                    </p>
                  </div>
                  <input
                    type="checkbox"
                    checked={settings.autoDownloadUpdates}
                    onChange={(e) =>
                      handleSettingChange(
                        "autoDownloadUpdates",
                        e.target.checked
                      )
                    }
                    className="rounded border-gray-300"
                  />
                </div>

                <div className="flex items-center justify-between">
                  <div>
                    <h5 className="text-sm font-medium">Silent Updates</h5>
                    <p className="text-xs text-gray-600">
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
                    <h5 className="text-sm font-medium">
                      Auto-install Updates
                    </h5>
                    <p className="text-xs text-gray-600">
                      Automatically install updates after download
                    </p>
                  </div>
                  <input
                    type="checkbox"
                    checked={settings.autoInstallUpdates}
                    onChange={(e) =>
                      handleSettingChange(
                        "autoInstallUpdates",
                        e.target.checked
                      )
                    }
                    className="rounded border-gray-300"
                  />
                </div>

                <div className="flex items-center justify-between">
                  <div>
                    <h5 className="text-sm font-medium">Background Checks</h5>
                    <p className="text-xs text-gray-600">
                      Check for updates while app is running
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

                {settings.backgroundChecks && (
                  <div className="flex items-center justify-between">
                    <div>
                      <h5 className="text-sm font-medium">Check Interval</h5>
                      <p className="text-xs text-gray-600">
                        How often to check for updates (minutes)
                      </p>
                    </div>
                    <select
                      value={settings.checkInterval}
                      onChange={(e) =>
                        handleSettingChange(
                          "checkInterval",
                          parseInt(e.target.value)
                        )
                      }
                      className="px-2 py-1 text-xs border border-gray-300 rounded bg-white"
                    >
                      <option value={15}>15 minutes</option>
                      <option value={30}>30 minutes</option>
                      <option value={60}>1 hour</option>
                      <option value={120}>2 hours</option>
                      <option value={240}>4 hours</option>
                      <option value={480}>8 hours</option>
                    </select>
                  </div>
                )}
              </div>
            )}

            <div className="space-y-3">
              {/* Background Operations Status */}
              {settings.backgroundChecks && (
                <div className="p-3 bg-blue-50 border border-blue-200 rounded-md">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center space-x-2">
                      <div className="w-2 h-2 bg-blue-500 rounded-full animate-pulse"></div>
                      <span className="text-sm font-medium text-blue-800">
                        Background Updates Active
                      </span>
                    </div>
                    <div className="text-right">
                      <div className="text-xs text-blue-600">
                        Checking every {settings.checkInterval} minutes
                      </div>
                      {updateStatus.currentVersion && (
                        <div className="text-xs text-blue-500 font-mono">
                          v{updateStatus.currentVersion}
                        </div>
                      )}
                    </div>
                  </div>
                  {settings.autoDownloadUpdates && (
                    <div className="mt-2 text-xs text-blue-700">
                      ⚡ Auto-download enabled • Updates will be downloaded
                      automatically
                    </div>
                  )}
                </div>
              )}

              {/* Update Status Display */}
              {updateStatus.lastChecked && (
                <div className="text-xs text-gray-500 ml-4">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <span>Last checked: {updateStatus.lastChecked}</span>
                      {settings.backgroundChecks && (
                        <span className="text-blue-600 text-xs">
                          🔄 Background active
                        </span>
                      )}
                    </div>

                                         {/* Version Information */}
                     <div className="flex items-center space-x-2 text-xs">
                       <span className="text-gray-600">Current:</span>
                       <span className="font-mono bg-gray-100 px-1 rounded">
                         v{updateStatus.currentVersion || "Unknown"}
                       </span>
                       {updateStatus.newVersion && (
                         <>
                           <span className="text-gray-400">→</span>
                           <span className="text-green-600">New:</span>
                           <span className="font-mono bg-green-100 px-1 rounded text-green-700">
                             v{updateStatus.newVersion}
                           </span>
                         </>
                       )}
                     </div>

                     {updateStatus.updateAvailable && (
                       <div className="flex items-center space-x-2">
                         <span className="text-green-600 font-medium">
                           • Update available!
                         </span>
                         <button
                           onClick={handleDownloadUpdate}
                           disabled={updateStatus.downloading}
                           className={`px-2 py-1 rounded text-xs transition-colors ${
                             updateStatus.downloading
                               ? "bg-gray-400 cursor-not-allowed text-white"
                               : "bg-blue-600 hover:bg-blue-700 text-white"
                           }`}
                         >
                           {updateStatus.downloading
                             ? "Downloading..."
                             : "Download"}
                         </button>
                       </div>
                     )}
                  </div>
                </div>
              )}

              {updateStatus.downloading && (
                <div className="text-xs text-blue-600 ml-4">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <span>Downloading update...</span>
                      <span className="font-medium">
                        {updateStatus.downloadProgress.toFixed(1)}%
                      </span>
                    </div>

                    {/* Progress Bar */}
                    <div className="w-full bg-blue-200 rounded-full h-2">
                      <div
                        className="bg-blue-600 h-2 rounded-full transition-all duration-300"
                        style={{ width: `${updateStatus.downloadProgress}%` }}
                      />
                    </div>

                    {/* Download Details */}
                    <div className="text-xs text-blue-700 space-y-1">
                      {updateStatus.downloaded && updateStatus.total && (
                        <div className="flex justify-between">
                          <span>Size:</span>
                          <span>
                            {formatBytes(updateStatus.downloaded)} /{" "}
                            {formatBytes(updateStatus.total)}
                          </span>
                        </div>
                      )}
                      {updateStatus.downloadSpeed > 0 && (
                        <div className="flex justify-between">
                          <span>Speed:</span>
                          <span>
                            {formatBytes(updateStatus.downloadSpeed)}/s
                          </span>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              )}

                             {/* Download Completed */}
               {updateStatus.downloadCompleted && !updateStatus.downloading && (
                 <div className="text-xs text-green-600 ml-4">
                   <div className="space-y-2">
                     <div className="flex items-center justify-between">
                       <span>✅ Download completed!</span>
                       <span className="font-medium">
                         v{updateStatus.newVersion}
                       </span>
                     </div>
                     <div className="text-xs text-green-700">
                       Update ready to install
                     </div>
                     {settings.autoInstallUpdates && (
                       <button
                         onClick={async () => {
                           try {
                             await window.electronAPI.installUpdate();
                           } catch (error) {
                             console.error("Installation failed:", error);
                           }
                         }}
                         disabled={updateStatus.installing}
                         className={`px-2 py-1 text-xs rounded transition-colors ${
                           updateStatus.installing
                             ? "bg-gray-400 cursor-not-allowed text-white"
                             : "bg-blue-600 hover:bg-blue-700 text-white"
                         }`}
                       >
                         {updateStatus.installing
                           ? "Installing..."
                           : "Install Now"}
                       </button>
                     )}
                   </div>
                 </div>
               )}

              {/* Installation Progress */}
              {updateStatus.installing && (
                <div className="text-xs text-purple-600 ml-4">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <span>🔧 Installing update...</span>
                      <span className="font-medium">
                        {updateStatus.installProgress.toFixed(1)}%
                      </span>
                    </div>

                    {/* Installation Progress Bar */}
                    <div className="w-full bg-purple-200 rounded-full h-2">
                      <div
                        className="bg-purple-600 h-2 rounded-full transition-all duration-300"
                        style={{ width: `${updateStatus.installProgress}%` }}
                      />
                    </div>
                  </div>
                </div>
              )}

              {/* Installation Completed */}
              {updateStatus.installCompleted && (
                <div className="text-xs text-green-600 ml-4">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between">
                      <span>🎉 Installation completed!</span>
                      <span className="font-medium">Ready to restart</span>
                    </div>
                    <div className="text-xs text-green-700">
                      The update has been installed successfully
                    </div>
                    <div className="text-xs text-green-600">
                      ✅ The app will restart automatically to complete the update.
                    </div>
                  </div>
                </div>
              )}

              {/* Silent Update Notification */}
              {settings.silentUpdates && updateStatus.downloading && (
                <div className="text-xs text-purple-600 ml-4">
                  <div className="flex items-center space-x-2">
                    <div className="w-2 h-2 bg-purple-500 rounded-full animate-pulse"></div>
                    <span>🔄 Silent update in progress...</span>
                    <span className="text-purple-700">
                      {updateStatus.downloadProgress.toFixed(1)}%
                    </span>
                  </div>
                </div>
              )}

              {updateStatus.error && (
                <div className="text-xs text-red-500 ml-4">
                  Error: {updateStatus.error}
                </div>
              )}
            </div>

            {/* Quick Actions */}
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <h4 className="font-medium text-sm text-gray-700">
                  Quick Actions
                </h4>
                {updateStatus.currentVersion && (
                  <span className="text-xs text-gray-500 font-mono">
                    v{updateStatus.currentVersion}
                  </span>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <button
                  onClick={handleManualUpdateCheck}
                  disabled={updateStatus.checking}
                  className={`px-3 py-2 rounded-md text-xs font-medium transition-colors ${
                    updateStatus.checking
                      ? "bg-gray-400 cursor-not-allowed text-white"
                      : "bg-green-600 hover:bg-green-700 text-white"
                  }`}
                >
                  <RefreshCw
                    className={`w-3 h-3 inline mr-1 ${
                      updateStatus.checking ? "animate-spin" : ""
                    }`}
                  />
                  {updateStatus.checking ? "Checking..." : "Check Now"}
                </button>

                                 {updateStatus.updateAvailable && !updateStatus.downloading && (
                   <button
                     onClick={handleDownloadUpdate}
                     className="px-3 py-2 bg-blue-600 hover:bg-blue-700 text-white text-xs font-medium rounded-md transition-colors"
                   >
                     ⬇️ Download Update
                   </button>
                 )}

                {updateStatus.downloadCompleted && (
                  <button
                    onClick={() => {
                      if (updateStatus.downloadedFilePath) {
                        const { shell } = require("electron");
                        shell.showItemInFolder(updateStatus.downloadedFilePath);
                      }
                    }}
                    className="px-3 py-2 bg-green-600 hover:bg-green-700 text-white text-xs font-medium rounded-md transition-colors"
                  >
                    🔧 Install Update
                  </button>
                )}

                {updateStatus.installCompleted && (
                  <div className="text-xs text-green-600">
                    ✅ Update installed successfully! The app will restart automatically.
                  </div>
                )}
              </div>
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

          {/* Version Footer */}
          {updateStatus.currentVersion && (
            <div className="pt-4 border-t border-gray-200">
              <div className="flex items-center justify-center space-x-4 text-xs text-gray-500">
                <span>SPT Launcher</span>
                <span className="font-mono bg-gray-100 px-2 py-1 rounded">
                  v{updateStatus.currentVersion}
                </span>
                <span>•</span>
                <span>Built with Electron</span>
              </div>
            </div>
          )}

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
