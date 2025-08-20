import React, { useState, useEffect } from "react";
import {
  Play,
  Square,
  FileText,
  RefreshCw,
  AlertCircle,
  CheckCircle,
  Clock,
  Settings,
  Server,
  Save,
} from "lucide-react";
import path from "path-browserify";

function LauncherTab() {
  const [launcherPath, setLauncherPath] = useState("");
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

  // Load saved launcher path from localStorage
  useEffect(() => {
    const savedLauncherPath = localStorage.getItem("launcherPath");
    if (savedLauncherPath) setLauncherPath(savedLauncherPath);

    // Load Fika configuration
    loadFikaConfig();
  }, []);

  // Monitor launcher process status
  useEffect(() => {
    if (!isLauncherRunning || !launcherProcess) return;

    const checkProcessStatus = async () => {
      try {
        // Check if the process is still running
        const processes = await window.electronAPI.getRunningProcesses();
        const isStillRunning = processes.some(
          (p) => p.pid === launcherProcess.pid
        );

        if (!isStillRunning) {
          // Process has stopped, update state
          setIsLauncherRunning(false);
          setLauncherProcess(null);
          setStatus("stopped");
        }
      } catch (error) {
        console.error("Failed to check process status:", error);
      }
    };

    // Check every 2 seconds
    const interval = setInterval(checkProcessStatus, 2000);

    return () => clearInterval(interval);
  }, [isLauncherRunning, launcherProcess]);

  const loadFikaConfig = async () => {
    if (window.electronAPI && window.electronAPI.getSptConfig) {
      try {
        // Get the SPT path from localStorage or use the current launcherPath
        const sptPath = launcherPath || localStorage.getItem("launcherPath");

        // Fix Windows path parsing issue with path-browserify
        let sptDir = null;
        if (sptPath) {
          // Handle Windows paths properly
          if (sptPath.includes("\\")) {
            // Windows path - split by backslash and remove the last part (filename)
            const parts = sptPath.split("\\");
            parts.pop(); // Remove filename
            sptDir = parts.join("\\");
          } else {
            // Unix path - use path.dirname
            sptDir = path.dirname(sptPath);
          }
        }

        const result = await window.electronAPI.getSptConfig(sptDir);
        if (result.success && result.config) {
          const config = result.config;
          setFikaConfig({
            serverAddress: config.serverAddress || "",
            serverPort: config.serverPort || "6969",
            enableFika: config.enableFika || false,
          });
          // Store the actual config path
          if (result.configPath) {
            setConfigPath(result.configPath);
          }
        }
      } catch (error) {
        console.error("Failed to load Fika config:", error);
      }
    }
  };

  const saveFikaConfig = async () => {
    if (window.electronAPI && window.electronAPI.updateSptConfig) {
      try {
        setConfigStatus("saving");

        const configData = {
          serverAddress: fikaConfig.serverAddress,
          serverPort: fikaConfig.serverPort,
          enableFika: fikaConfig.enableFika,
        };

        // Get the SPT path from localStorage or use the current launcherPath
        const sptPath = launcherPath || localStorage.getItem("launcherPath");

        // Fix Windows path parsing issue with path-browserify
        let sptDir = null;
        if (sptPath) {
          // Handle Windows paths properly
          if (sptPath.includes("\\")) {
            // Windows path - split by backslash and remove the last part (filename)
            const parts = sptPath.split("\\");
            parts.pop(); // Remove filename
            sptDir = parts.join("\\");
          } else {
            // Unix path - use path.dirname
            sptDir = path.dirname(sptPath);
          }
        }

        console.log("🔍 Debug: SPT Path:", sptPath);
        console.log("🔍 Debug: SPT Directory:", sptDir);
        console.log("🔍 Debug: Config Data:", configData);

        const result = await window.electronAPI.updateSptConfig(
          configData,
          sptDir
        );

        console.log("🔍 Debug: Result:", result);

        if (result.success) {
          setConfigStatus("success");

          // If Fika mode is being enabled and launcher is running, restart it to apply new config
          if (configData.enableFika && isLauncherRunning) {
            setConfigStatus("restarting");
            console.log(
              "🔄 Fika mode enabled - restarting launcher to apply new configuration..."
            );

            try {
              // Stop the current launcher
              await stopLauncher();

              // Wait a moment for the process to fully stop
              await new Promise((resolve) => setTimeout(resolve, 1000));

              // Restart the launcher
              await launchSPT();

              setConfigStatus("success");
              setTimeout(() => setConfigStatus("idle"), 2000);
            } catch (restartError) {
              console.error("❌ Failed to restart launcher:", restartError);
              setConfigStatus("error");
              setTimeout(() => setConfigStatus("idle"), 3000);
            }
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
    } else {
      console.error("❌ electronAPI or updateSptConfig not available");
      setConfigStatus("error");
      setTimeout(() => setConfigStatus("idle"), 3000);
    }
  };

  const selectLauncherPath = async () => {
    if (window.electronAPI) {
      try {
        const path = await window.electronAPI.selectFile();
        if (path) {
          setLauncherPath(path);
          localStorage.setItem("launcherPath", path);
        }
      } catch (error) {
        console.error("Failed to select launcher path:", error);
      }
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
    if (launcherProcess && launcherProcess.pid) {
      try {
        await window.electronAPI.stopProcess(launcherProcess.pid);
      } catch (error) {
        console.error("Failed to stop SPT Launcher:", error);
      }

      setIsLauncherRunning(false);
      setLauncherProcess(null);
      setStatus("stopped");
    }
  };

  const checkLauncherStatus = async () => {
    if (!launcherProcess || !launcherProcess.pid) return;

    try {
      // Check if the process is still running
      const processes = await window.electronAPI.getRunningProcesses();
      const isStillRunning = processes.some(
        (p) => p.pid === launcherProcess.pid
      );

      if (!isStillRunning) {
        // Process has stopped, update state
        setIsLauncherRunning(false);
        setLauncherProcess(null);
        setStatus("stopped");
      }
    } catch (error) {
      console.error("Failed to check launcher status:", error);
    }
  };

  const getStatusIcon = () => {
    switch (status) {
      case "success":
        return <CheckCircle className="w-5 h-5 text-green-500" />;
      case "error":
        return <AlertCircle className="w-5 h-5 text-red-500" />;
      case "launching":
        return <RefreshCw className="w-5 h-5 text-blue-500 animate-spin" />;
      case "stopped":
        return <Clock className="w-5 h-5 text-gray-500" />;
      case "restarting":
        return <RefreshCw className="w-5 h-5 text-orange-500 animate-spin" />;
      default:
        return <Clock className="w-5 h-5 text-gray-400" />;
    }
  };

  const getStatusText = () => {
    switch (status) {
      case "success":
        return "Ready";
      case "error":
        return "Error occurred";
      case "launching":
        return "Launching...";
      case "stopped":
        return "Stopped";
      case "restarting":
        return "Restarting...";
      default:
        return "Idle";
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 mb-2">SPT Launcher</h1>
        <p className="text-gray-600">
          Launch and manage your SPT-AKI installation
        </p>
      </div>

      {/* Status Card */}
      <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-xl font-semibold">Status</h2>
          <div className="flex items-center space-x-2">
            {getStatusIcon()}
            <span className="font-medium">{getStatusText()}</span>
            <button
              onClick={checkLauncherStatus}
              className="ml-2 p-1 text-gray-500 hover:text-gray-700 transition-colors"
              title="Refresh status"
            >
              <RefreshCw className="w-4 h-4" />
            </button>
          </div>
        </div>

        <div className="flex items-center space-x-2">
          <div
            className={`w-3 h-3 rounded-full ${
              isLauncherRunning ? "bg-green-500" : "bg-gray-400"
            }`}
          ></div>
          <span>Launcher: {isLauncherRunning ? "Running" : "Stopped"}</span>
        </div>
      </div>

      {/* Path Configuration */}
      <div className="grid grid-cols-1 gap-6">
        {/* Launcher Path */}
        <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm">
          <h3 className="text-lg font-semibold mb-4 flex items-center space-x-2">
            <Play className="w-5 h-5" />
            <span>SPT Launcher Executable</span>
          </h3>

          <div className="space-y-4">
            <div className="flex space-x-2">
              <input
                type="text"
                value={launcherPath}
                onChange={(e) => setLauncherPath(e.target.value)}
                placeholder="e.g., D:\\SPT\\SPT.Launcher.exe"
                className="flex-1 px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900"
              />
              <button
                onClick={selectLauncherPath}
                className="px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 rounded-md transition-colors"
              >
                <FileText className="w-4 h-4" />
              </button>
            </div>

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
      <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center space-x-2">
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
                onChange={async (e) => {
                  const newValue = e.target.checked;
                  setFikaConfig((prev) => ({
                    ...prev,
                    enableFika: newValue,
                  }));

                                     // Auto-save when Fika is disabled to revert to default settings
                   if (!newValue) {
                     try {
                       setConfigStatus("saving");

                       const configData = {
                         serverAddress: fikaConfig.serverAddress,
                         serverPort: fikaConfig.serverPort,
                         enableFika: false,
                       };

                       // Get the SPT path from localStorage or use the current launcherPath
                       const sptPath =
                         launcherPath || localStorage.getItem("launcherPath");

                       // Fix Windows path parsing issue with path-browserify
                       let sptDir = null;
                       if (sptPath) {
                         // Handle Windows paths properly
                         if (sptPath.includes("\\")) {
                           // Windows path - split by backslash and remove the last part (filename)
                           const parts = sptPath.split("\\");
                           parts.pop(); // Remove the filename
                           sptDir = parts.join("\\");
                         } else {
                           // Unix path - use path.dirname
                           sptDir = path.dirname(sptPath);
                         }
                       }

                       const result = await window.electronAPI.updateSptConfig(
                         configData,
                         sptDir
                       );

                       if (result.success) {
                         setConfigStatus("success");
                         
                         // If launcher is running, restart it to apply the reverted default settings
                         if (isLauncherRunning) {
                           setConfigStatus("restarting");
                           console.log("🔄 Fika mode disabled - restarting launcher to apply default SPT settings...");
                           
                           try {
                             // Stop the current launcher
                             await stopLauncher();
                             
                             // Wait a moment for the process to fully stop
                             await new Promise(resolve => setTimeout(resolve, 1000));
                             
                             // Restart the launcher
                             await launchSPT();
                             
                             setConfigStatus("success");
                             setTimeout(() => setConfigStatus("idle"), 2000);
                           } catch (restartError) {
                             console.error("❌ Failed to restart launcher:", restartError);
                             setConfigStatus("error");
                             setTimeout(() => setConfigStatus("idle"), 3000);
                           }
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
                       setTimeout(() => setConfigStatus("idle"), 3000);
                     }
                   }
                }}
                className="rounded border-gray-300"
              />
              <label htmlFor="enableFika" className="text-sm font-medium">
                Enable Fika Co-op Mode
              </label>
            </div>

            {fikaConfig.enableFika && (
              <>
                <div>
                  <label className="block text-sm font-medium mb-2">
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
                    className="w-full px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium mb-2">
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
                    className="w-full px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900"
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
                <span>
                  {configStatus === "saving"
                    ? "Saving..."
                    : configStatus === "restarting"
                    ? "Restarting..."
                    : configStatus === "success"
                    ? "Saved!"
                    : configStatus === "error"
                    ? "Error"
                    : "Save Configuration"}
                </span>
              </button>

              <button
                onClick={loadFikaConfig}
                className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
              >
                <RefreshCw className="w-4 h-4" />
                <span>Reload</span>
              </button>
            </div>

            <div className="text-sm text-gray-600 bg-gray-50 p-3 rounded-md">
              <p className="font-medium mb-1">Configuration Location:</p>
              <p className="font-mono text-xs">
                {configPath ||
                  (launcherPath
                    ? `${path.dirname(launcherPath)}\\config.json`
                    : "SPT Installation Folder\\config.json")}
              </p>
              <p className="mt-2">
                This configuration will be applied to your SPT launcher's
                config.json file. When Fika mode is enabled, the launcher will
                connect to the specified server.
              </p>
            </div>
          </div>
        )}
      </div>

      {/* Quick Actions */}
      <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm">
        <h3 className="text-lg font-semibold mb-4">Quick Actions</h3>
        <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
          <button
            onClick={() => setLauncherPath("")}
            className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
          >
            Clear Launcher Executable
          </button>
          <button
            onClick={() => {
              localStorage.removeItem("launcherPath");
              setLauncherPath("");
            }}
            className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
          >
            Reset Launcher Path
          </button>
          <button
            onClick={() => window.location.reload()}
            className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
          >
            Refresh
          </button>
        </div>
      </div>
    </div>
  );
}

export default LauncherTab;
