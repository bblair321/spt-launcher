import React, { useState, useEffect, useCallback } from "react";
import { Play, Square, Server, Save } from "lucide-react";

// Custom hooks
import { useLocalStorage } from "../hooks/useLocalStorage";
import { useProcessMonitor } from "../hooks/useProcessMonitor";

// Utilities
import {
  parseSptDirectory,
  getConfigPath,
  formatPathForDisplay,
} from "../utils/pathUtils";
import {
  getStatusIcon,
  getStatusText,
  getButtonText,
} from "../utils/statusUtils";

// UI Components
import StatusCard from "./ui/StatusCard";
import PathInput from "./ui/PathInput";

function LauncherTab() {
  // State management
  const [launcherPath, setLauncherPath] = useLocalStorage("launcherPath", "");
  const [isLauncherRunning, setIsLauncherRunning] = useState(false);
  const [launcherProcess, setLauncherProcess] = useState(null);
  const [status, setStatus] = useState("idle");

  // Fika configuration state
  const [fikaConfig, setFikaConfig] = useState({
    serverAddress: "",
    serverPort: "6969",
    enableFika: false,
  });
  const [configStatus, setConfigStatus] = useState("idle");
  const [showFikaSettings, setShowFikaSettings] = useState(false);
  const [configPath, setConfigPath] = useState("");

  // Process monitoring
  const handleProcessStop = useCallback(() => {
    setIsLauncherRunning(false);
    setLauncherProcess(null);
    setStatus("stopped");
  }, []);

  useProcessMonitor(launcherProcess?.pid, isLauncherRunning, handleProcessStop);

  // Load Fika configuration on mount
  useEffect(() => {
    loadFikaConfig();
  }, [launcherPath]);

  const loadFikaConfig = async () => {
    if (!window.electronAPI?.getSptConfig) return;

    try {
      const sptDir = parseSptDirectory(launcherPath);
      if (!sptDir) return;

      const result = await window.electronAPI.getSptConfig(sptDir);
      if (result.success && result.config) {
        const config = result.config;
        setFikaConfig({
          serverAddress: config.serverAddress || "",
          serverPort: config.serverPort || "6969",
          enableFika: config.enableFika || false,
        });
        if (result.configPath) {
          setConfigPath(result.configPath);
        }
      }
    } catch (error) {
      console.error("Failed to load Fika config:", error);
    }
  };

  const saveFikaConfig = async () => {
    if (!window.electronAPI?.updateSptConfig) return;

    try {
      setConfigStatus("saving");

      const configData = {
        serverAddress: fikaConfig.serverAddress,
        serverPort: fikaConfig.serverPort,
        enableFika: fikaConfig.enableFika,
      };

      const sptDir = parseSptDirectory(launcherPath);
      if (!sptDir) {
        throw new Error("SPT directory not found");
      }

      const result = await window.electronAPI.updateSptConfig(
        configData,
        sptDir
      );

      if (result.success) {
        setConfigStatus("success");

        // Auto-restart if Fika mode is enabled and launcher is running
        if (configData.enableFika && isLauncherRunning) {
          await restartLauncherForConfig();
        } else {
          setTimeout(() => setConfigStatus("idle"), 2000);
        }
      } else {
        console.error("❌ Backend returned error:", result.error);
        setConfigStatus("error");
        setTimeout(() => setConfigStatus("idle"), 3000);
      }
    } catch (error) {
      console.error("❌ Exception occurred:", error);
      setConfigStatus("error");
      setTimeout(() => setConfigStatus("idle"), 3000);
    }
  };

  const restartLauncherForConfig = async () => {
    try {
      setConfigStatus("restarting");
      console.log(
        "🔄 Fika mode enabled - restarting launcher to apply new configuration..."
      );

      await stopLauncher();
      await new Promise((resolve) => setTimeout(resolve, 1000));
      await launchSPT();

      setConfigStatus("success");
      setTimeout(() => setConfigStatus("idle"), 2000);
    } catch (restartError) {
      console.error("❌ Failed to restart launcher:", restartError);
      setConfigStatus("error");
      setTimeout(() => setConfigStatus("idle"), 3000);
    }
  };

  const handleFikaToggle = async (newValue) => {
    setFikaConfig((prev) => ({ ...prev, enableFika: newValue }));

    // Auto-save when Fika is disabled to revert to default settings
    if (!newValue) {
      try {
        setConfigStatus("saving");

        const configData = {
          serverAddress: fikaConfig.serverAddress,
          serverPort: fikaConfig.serverPort,
          enableFika: false,
        };

        const sptDir = parseSptDirectory(launcherPath);
        if (!sptDir) return;

        const result = await window.electronAPI.updateSptConfig(
          configData,
          sptDir
        );

        if (result.success) {
          setConfigStatus("success");

          // Auto-restart if launcher is running
          if (isLauncherRunning) {
            await restartLauncherForConfig();
          } else {
            setTimeout(() => setConfigStatus("idle"), 2000);
          }
        } else {
          setConfigStatus("error");
          setTimeout(() => setConfigStatus("idle"), 2000);
        }
      } catch (error) {
        console.error("❌ Auto-save failed:", error);
        setConfigStatus("error");
        setTimeout(() => setConfigStatus("idle"), 2000);
      }
    }
  };

  const selectLauncherPath = async () => {
    if (!window.electronAPI) return;

    try {
      const path = await window.electronAPI.selectFile();
      if (path) {
        setLauncherPath(path);
      }
    } catch (error) {
      console.error("Failed to select launcher path:", error);
    }
  };

  const launchSPT = async () => {
    if (!launcherPath) {
      setStatus("error");
      return;
    }

    try {
      setStatus("launching");

      const result = await window.electronAPI.launchProcess(launcherPath);

      if (result.code === 0 && result.pid) {
        setIsLauncherRunning(true);
        setStatus("success");
        setLauncherProcess(result);
      } else {
        console.error("Failed to launch SPT - invalid result:", result);
        setStatus("error");
      }
    } catch (error) {
      console.error("Failed to launch SPT:", error);
      setStatus("error");
    }
  };

  const stopLauncher = async () => {
    if (!launcherProcess?.pid) return;

    try {
      await window.electronAPI.stopProcess(launcherProcess.pid);
    } catch (error) {
      console.error("Failed to stop SPT Launcher:", error);
    }

    setIsLauncherRunning(false);
    setLauncherProcess(null);
    setStatus("stopped");
  };

  const checkLauncherStatus = async () => {
    if (!launcherProcess?.pid) return;

    try {
      const processes = await window.electronAPI.getRunningProcesses();
      const isStillRunning = processes.some(
        (p) => p.pid === launcherProcess.pid
      );

      if (!isStillRunning) {
        setIsLauncherRunning(false);
        setLauncherProcess(null);
        setStatus("stopped");
      }
    } catch (error) {
      console.error("Failed to check launcher status:", error);
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          SPT Launcher
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Launch and manage your SPT-AKI installation
        </p>
      </div>

      {/* Status Card */}
      <StatusCard
        title="Status"
        status={status}
        isRunning={isLauncherRunning}
        onRefresh={checkLauncherStatus}
      />

      {/* Path Configuration */}
      <div className="grid grid-cols-1 gap-6">
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h3 className="text-lg font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Play className="w-5 h-5" />
            <span>SPT Launcher Executable</span>
          </h3>

          <div className="space-y-4">
            <PathInput
              value={launcherPath}
              onChange={setLauncherPath}
              onSelectFile={selectLauncherPath}
              placeholder="e.g., D:\\SPT\\SPT.Launcher.exe"
              label="Launcher Path"
            />

            <div className="flex space-x-2">
              <button
                onClick={launchSPT}
                disabled={!launcherPath || isLauncherRunning}
                className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center justify-center space-x-2"
              >
                <Play className="w-4 h-4" />
                <span>Launch SPT</span>
              </button>
              <button
                onClick={stopLauncher}
                disabled={!isLauncherRunning}
                className="px-4 py-2 bg-red-600 text-white rounded-md hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center justify-center space-x-2"
              >
                <Square className="w-4 h-4" />
                <span>Stop</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Fika Configuration */}
      <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Server className="w-5 h-5" />
            <span>Fika Co-op Configuration</span>
          </h3>
          <button
            onClick={() => setShowFikaSettings(!showFikaSettings)}
            className="px-3 py-1 text-sm bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors"
          >
            {showFikaSettings ? "Hide" : "Configure"}
          </button>
        </div>

        {showFikaSettings && (
          <div className="space-y-4">
            <div className="flex items-center space-x-2">
              <input
                type="checkbox"
                id="enableFika"
                checked={fikaConfig.enableFika}
                onChange={(e) => handleFikaToggle(e.target.checked)}
                className="rounded border-gray-300"
              />
              <label
                htmlFor="enableFika"
                className="text-sm font-medium text-gray-900 dark:text-gray-100"
              >
                Enable Fika Co-op Mode
              </label>
            </div>

            {fikaConfig.enableFika && (
              <>
                <div>
                  <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                    Server Address
                  </label>
                  <input
                    type="text"
                    value={fikaConfig.serverAddress}
                    onChange={(e) =>
                      setFikaConfig((prev) => ({
                        ...prev,
                        serverAddress: e.target.value,
                      }))
                    }
                    placeholder="e.g., 192.168.1.100 or server.example.com"
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                    Server Port
                  </label>
                  <input
                    type="number"
                    value={fikaConfig.serverPort}
                    onChange={(e) =>
                      setFikaConfig((prev) => ({
                        ...prev,
                        serverPort: e.target.value,
                      }))
                    }
                    placeholder="6969"
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                  />
                </div>
              </>
            )}

            <div className="flex items-center space-x-2">
              <button
                onClick={saveFikaConfig}
                disabled={configStatus === "saving"}
                className="px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50 transition-colors flex items-center space-x-2"
              >
                <Save className="w-4 h-4" />
                <span>{getButtonText(configStatus, "Save Configuration")}</span>
              </button>

              <button
                onClick={loadFikaConfig}
                className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
              >
                <span>Reload</span>
              </button>
            </div>

            <div className="text-sm text-gray-600 dark:text-gray-400 bg-gray-50 dark:bg-gray-700 p-3 rounded-md">
              <p className="font-medium mb-1 text-gray-900 dark:text-gray-100">
                Configuration Location:
              </p>
              <p className="font-mono text-xs text-gray-700 dark:text-gray-300">
                {configPath || getConfigPath(parseSptDirectory(launcherPath))}
              </p>
              <p className="mt-2 text-gray-600 dark:text-gray-400">
                This configuration will be applied to your SPT launcher's
                config.json file. When Fika mode is enabled, the launcher will
                connect to the specified server.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default LauncherTab;
