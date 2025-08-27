import React, { useState, useEffect, useCallback, useMemo, memo } from "react";
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
import {
  safeElectronCall,
  isElectronFunctionAvailable,
} from "../utils/electronUtils";

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

  // Memoized SPT directory to prevent recalculation
  const sptDirectory = useMemo(() => {
    return parseSptDirectory(launcherPath);
  }, [launcherPath]);

  // Load Fika configuration on mount
  useEffect(() => {
    loadFikaConfig();
  }, [sptDirectory]);

  const loadFikaConfig = useCallback(async () => {
    if (!sptDirectory) return;

    const result = await safeElectronCall(
      "getSptConfig",
      () => window.electronAPI.getSptConfig(sptDirectory),
      "Failed to load Fika configuration"
    );

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
    } else if (result.isElectronError) {
      console.warn("Running in browser mode - Fika config not available");
    }
  }, [sptDirectory]);

  const saveFikaConfig = useCallback(async () => {
    if (!sptDirectory) {
      throw new Error("SPT directory not found");
    }

    setConfigStatus("saving");

    const configData = {
      serverAddress: fikaConfig.serverAddress,
      serverPort: fikaConfig.serverPort,
      enableFika: fikaConfig.enableFika,
    };

    const result = await safeElectronCall(
      "updateSptConfig",
      () => window.electronAPI.updateSptConfig(configData, sptDirectory),
      "Failed to save Fika configuration"
    );

    if (result.success) {
      setConfigStatus("saved");
      setTimeout(() => setConfigStatus("idle"), 2000);
    } else {
      setConfigStatus("error");
      if (result.isElectronError) {
        console.warn("Running in browser mode - config save not available");
        // In browser mode, simulate success
        setConfigStatus("saved");
        setTimeout(() => setConfigStatus("idle"), 2000);
      } else {
        throw new Error(result.error || "Failed to save configuration");
      }
    }
  }, [fikaConfig, sptDirectory]);

  // Memoized Fika configuration handlers
  const handleFikaToggle = useCallback((enabled) => {
    setFikaConfig((prev) => ({ ...prev, enableFika: enabled }));
  }, []);

  const handleFikaConfigChange = useCallback((field, value) => {
    setFikaConfig((prev) => ({ ...prev, [field]: value }));
  }, []);

  // Memoized button states and text
  const buttonStates = useMemo(() => {
    const isDisabled = !launcherPath.trim();
    const buttonText = getButtonText(status, isLauncherRunning);
    const buttonIcon = isLauncherRunning ? Square : Play;

    return { isDisabled, buttonText, buttonIcon };
  }, [launcherPath, status, isLauncherRunning]);

  // Memoized status display
  const statusDisplay = useMemo(() => {
    const statusIcon = getStatusIcon(status, isLauncherRunning);
    const statusText = getStatusText(status, isLauncherRunning);

    return { statusIcon, statusText };
  }, [status, isLauncherRunning]);

  // Memoized form validation
  const isFormValid = useMemo(() => {
    if (!fikaConfig.enableFika) return true;
    return fikaConfig.serverAddress.trim() && fikaConfig.serverPort.trim();
  }, [fikaConfig]);

  const selectLauncherPath = useCallback(async () => {
    const result = await safeElectronCall(
      "selectFile",
      () => window.electronAPI.selectFile(),
      "Failed to select launcher path"
    );

    if (result.success && result) {
      setLauncherPath(result);
    } else if (result.isElectronError) {
      console.warn("Running in browser mode - file selection not available");
    }
  }, []);

  const launchSPT = useCallback(async () => {
    if (!launcherPath) {
      setStatus("error");
      return;
    }

    setStatus("launching");

    const result = await safeElectronCall(
      "launchProcess",
      () => window.electronAPI.launchProcess(launcherPath),
      "Failed to launch SPT"
    );

    if (result.success && result.code === 0 && result.pid) {
      setIsLauncherRunning(true);
      setStatus("success");
      setLauncherProcess(result);
    } else if (result.isElectronError) {
      console.warn("Running in browser mode - process launch not available");
      setStatus("error");
    } else {
      console.error("Failed to launch SPT - invalid result:", result);
      setStatus("error");
    }
  }, [launcherPath]);

  const stopLauncher = useCallback(async () => {
    if (!launcherProcess?.pid) return;

    await safeElectronCall(
      "stopProcess",
      () => window.electronAPI.stopProcess(launcherProcess.pid),
      "Failed to stop SPT Launcher"
    );

    setIsLauncherRunning(false);
    setLauncherProcess(null);
    setStatus("stopped");
  }, [launcherProcess?.pid]);

  const checkLauncherStatus = useCallback(async () => {
    if (!launcherProcess?.pid) return;

    const result = await safeElectronCall(
      "getRunningProcesses",
      () => window.electronAPI.getRunningProcesses(),
      "Failed to check launcher status"
    );

    if (result.success && Array.isArray(result)) {
      const isStillRunning = result.some((p) => p.pid === launcherProcess.pid);

      if (!isStillRunning) {
        setIsLauncherRunning(false);
        setLauncherProcess(null);
        setStatus("stopped");
      }
    } else if (result.isElectronError) {
      console.warn(
        "Running in browser mode - process monitoring not available"
      );
    }
  }, [launcherProcess?.pid]);

  // Memoized button text for save button
  const saveButtonText = useMemo(() => {
    switch (configStatus) {
      case "saving":
        return "Saving...";
      case "saved":
        return "Saved!";
      case "error":
        return "Error";
      default:
        return "Save Configuration";
    }
  }, [configStatus]);

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Header */}
      <div className="text-center">
        <h1 className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          SPT Launcher
        </h1>
        <p className="text-sm sm:text-base text-gray-600 dark:text-gray-400 px-2">
          Launch and manage your SPT-AKI installation
        </p>
      </div>

      {/* Status Card */}
      <div className="px-2 sm:px-0">
        <StatusCard
          title="Status"
          status={status}
          isRunning={isLauncherRunning}
          onRefresh={checkLauncherStatus}
        />
      </div>

      {/* Path Configuration */}
      <div className="grid grid-cols-1 gap-4 sm:gap-6 px-2 sm:px-0">
        <div className="bg-white dark:bg-gray-800 p-4 sm:p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h3 className="text-base sm:text-lg font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Play className="w-4 h-4 sm:w-5 sm:h-5" />
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

            <div className="flex flex-col sm:flex-row gap-2 sm:space-x-2">
              <button
                onClick={launchSPT}
                disabled={buttonStates.isDisabled}
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
      <div className="bg-white dark:bg-gray-800 p-4 sm:p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm px-2 sm:px-0">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 sm:gap-0 mb-4">
          <h3 className="text-base sm:text-lg font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Server className="w-4 h-4 sm:w-5 sm:h-5" />
            <span>Fika Co-op Configuration</span>
          </h3>
          <button
            onClick={() => setShowFikaSettings(!showFikaSettings)}
            className="px-3 py-1 text-sm bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors self-start sm:self-auto"
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
                className="rounded border-gray-300 w-4 h-4"
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
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                      Server Address
                    </label>
                    <input
                      type="text"
                      value={fikaConfig.serverAddress}
                      onChange={(e) =>
                        handleFikaConfigChange("serverAddress", e.target.value)
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
                        handleFikaConfigChange("serverPort", e.target.value)
                      }
                      placeholder="6969"
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    />
                  </div>
                </div>
              </>
            )}

            <div className="flex flex-col sm:flex-row gap-2 sm:space-x-2">
              <button
                onClick={saveFikaConfig}
                disabled={configStatus === "saving"}
                className="px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50 transition-colors flex items-center justify-center space-x-2"
              >
                <Save className="w-4 h-4" />
                <span>{saveButtonText}</span>
              </button>

              <button
                onClick={loadFikaConfig}
                className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors flex items-center justify-center space-x-2"
              >
                <span>Reload</span>
              </button>
            </div>

            <div className="text-sm text-gray-600 dark:text-gray-400 bg-gray-50 dark:bg-gray-700 p-3 rounded-md">
              <p className="font-medium mb-1 text-gray-900 dark:text-gray-100">
                Configuration Location:
              </p>
              <p className="font-mono text-xs text-gray-700 dark:text-gray-300">
                {configPath || getConfigPath(sptDirectory)}
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

export default memo(LauncherTab);
