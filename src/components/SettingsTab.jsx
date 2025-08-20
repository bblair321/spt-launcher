import React, { useState } from "react";
import { Settings, Save, RefreshCw } from "lucide-react";

function SettingsTab() {
  const [settings, setSettings] = useState({
    autoStart: false,
    minimizeToTray: true,
    checkForUpdates: true,
    theme: "system",
  });

  const handleSettingChange = (key, value) => {
    setSettings((prev) => ({ ...prev, [key]: value }));
  };

  const saveSettings = () => {
    localStorage.setItem("appSettings", JSON.stringify(settings));
    // Show success message
  };

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
