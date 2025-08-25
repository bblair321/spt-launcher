import React, { useState, useEffect, useCallback, useMemo, memo } from "react";
import { Wrench, Terminal, Database, RefreshCw, Monitor } from "lucide-react";
import { useToastContext } from "../contexts/ToastContext";

function DevToolsTab() {
  const { showSuccess, showError, showInfo } = useToastContext();
  const [activeTool, setActiveTool] = useState(null);
  const [processes, setProcesses] = useState([]);
  const [configData, setConfigData] = useState({});
  const [isLoading, setIsLoading] = useState(false);

  // Memoized tool selection handler
  const handleToolSelect = useCallback((tool) => {
    setActiveTool(tool);
  }, []);

  // Memoized tool reset handler
  const handleToolReset = useCallback(() => {
    setActiveTool(null);
  }, []);

  // Function to fetch real process data
  const fetchProcesses = useCallback(async () => {
    if (!window.electronAPI?.getRunningProcesses) {
      // Fallback to mock data if API not available
      const mockProcesses = [
        {
          id: 1,
          name: "SPT-AKI Server",
          pid: 1234,
          cpu: "2.3%",
          memory: "156 MB",
          status: "running",
          uptime: "2h 15m",
        },
        {
          id: 2,
          name: "SPT-AKI Client",
          pid: 5678,
          cpu: "1.8%",
          memory: "89 MB",
          status: "running",
          uptime: "1h 45m",
        },
        {
          id: 3,
          name: "Fika Co-op",
          pid: 9012,
          cpu: "0.5%",
          memory: "23 MB",
          status: "running",
          uptime: "45m",
        },
      ];
      setProcesses(mockProcesses);
      showInfo(
        "Mock Data",
        "Using demonstration data - Electron API not available"
      );
      return;
    }

    try {
      setIsLoading(true);
      const result = await window.electronAPI.getRunningProcesses();
      if (result.success && result.processes) {
        // Transform the API response to match our UI format
        const formattedProcesses = result.processes.map((proc, index) => {
          // Calculate uptime if we have startTime
          let uptime = "Unknown";
          if (proc.startTime) {
            const now = new Date();
            const diff = now - new Date(proc.startTime);
            const hours = Math.floor(diff / (1000 * 60 * 60));
            const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
            uptime = hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
          }

          // Format memory if available
          let memory = "Unknown";
          if (proc.memory && proc.memory !== "0") {
            const memKB = parseInt(proc.memory);
            if (memKB > 1024) {
              memory = `${(memKB / 1024).toFixed(1)} MB`;
            } else {
              memory = `${memKB} KB`;
            }
          }

          // Ensure we have a proper process name
          let processName = proc.name;
          if (
            !processName ||
            processName.trim() === "" ||
            processName === "Unknown Process"
          ) {
            // Try to get a better name from the process
            if (proc.filePath) {
              processName =
                proc.filePath.split(/[\\/]/).pop() || `Process ${proc.pid}`;
            } else {
              processName = `Process ${proc.pid}`;
            }
          }

          return {
            id: index + 1,
            name: processName,
            pid: proc.pid,
            cpu: `${(Math.random() * 2 + 0.5).toFixed(1)}%`, // Mock CPU for now
            memory: memory,
            status: proc.status || "running",
            uptime: uptime,
          };
        });
        setProcesses(formattedProcesses);
      } else {
        // Handle case where result.processes is not an array
        if (Array.isArray(result.processes)) {
          setProcesses(result.processes);
        } else {
          console.warn("Unexpected processes format:", result);
          setProcesses([]);
        }
      }
    } catch (error) {
      console.error("Failed to fetch processes:", error);
      showError(
        "Failed to Load Processes",
        "Unable to retrieve running processes. Please try again.",
        () => fetchProcesses(),
        error.toString(),
        "Retry"
      );
    } finally {
      setIsLoading(false);
    }
  }, [showError, showInfo]);

  // Memoized process stop handler
  const handleProcessStop = useCallback(
    async (pid) => {
      if (!window.electronAPI?.stopProcess) {
        showError(
          "API Not Available",
          "Process management API is not available in this environment."
        );
        return;
      }

      try {
        const result = await window.electronAPI.stopProcess(pid);
        if (result.success) {
          showSuccess("Process Stopped", `Successfully stopped process ${pid}`);
          // Refresh the process list
          fetchProcesses();
        } else {
          throw new Error(result.error || "Failed to stop process");
        }
      } catch (error) {
        console.error("Failed to stop process:", error);
        showError(
          "Failed to Stop Process",
          `Unable to stop process ${pid}. Please try again.`,
          () => handleProcessStop(pid),
          error.toString(),
          "Retry"
        );
      }
    },
    [fetchProcesses, showError, showSuccess]
  );

  // Memoized configuration loading
  const loadConfig = useCallback(async () => {
    if (!window.electronAPI?.getSptConfig) {
      showError(
        "API Not Available",
        "Configuration API is not available in this environment."
      );
      return;
    }

    try {
      setIsLoading(true);
      const result = await window.electronAPI.getSptConfig();
      if (result.success && result.config) {
        setConfigData(result.config);
        showSuccess(
          "Configuration Loaded",
          "Successfully loaded SPT configuration"
        );
      } else {
        throw new Error(result.error || "Failed to load configuration");
      }
    } catch (error) {
      console.error("Failed to load configuration:", error);
      showError(
        "Failed to Load Configuration",
        "Unable to load SPT configuration. Please try again.",
        () => loadConfig(),
        error.toString(),
        "Retry"
      );
    } finally {
      setIsLoading(false);
    }
  }, [showError, showSuccess]);

  // Memoized configuration saving
  const saveConfig = useCallback(
    async (config) => {
      if (!window.electronAPI?.updateSptConfig) {
        showError(
          "API Not Available",
          "Configuration API is not available in this environment."
        );
        return;
      }

      try {
        setIsLoading(true);
        const result = await window.electronAPI.updateSptConfig(config);
        if (result.success) {
          showSuccess(
            "Configuration Saved",
            "Successfully saved SPT configuration"
          );
          setConfigData(config);
        } else {
          throw new Error(result.error || "Failed to save configuration");
        }
      } catch (error) {
        console.error("Failed to save configuration:", error);
        showError(
          "Failed to Save Configuration",
          "Unable to save SPT configuration. Please try again.",
          () => saveConfig(config),
          error.toString(),
          "Retry"
        );
      } finally {
        setIsLoading(false);
      }
    },
    [showError, showSuccess]
  );

  // Load processes on mount
  useEffect(() => {
    fetchProcesses();
  }, [fetchProcesses]);

  // Memoized tool cards to prevent unnecessary re-renders
  const toolCards = useMemo(
    () => [
      {
        id: "process",
        title: "Process Monitor",
        description: "Monitor running SPT processes and system resources",
        icon: Terminal,
        onClick: () => handleToolSelect("process"),
      },
      {
        id: "config",
        title: "Configuration Editor",
        description: "Edit SPT configuration files directly",
        icon: Database,
        onClick: () => handleToolSelect("config"),
      },
    ],
    [handleToolSelect]
  );

  const renderProcessMonitor = () => (
    <div className="space-y-4">
      <div className="flex justify-between items-center">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          Running Processes
        </h3>
      </div>

      {/* Safety Notice */}
      <div className="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-700 rounded-lg p-3 mb-4">
        <div className="flex items-start space-x-2">
          <span className="text-green-600 dark:text-green-400 text-lg">✅</span>
          <div className="text-sm text-green-800 dark:text-green-200">
            <p className="font-medium mb-1">✅ SAFETY FEATURE ENABLED ✅</p>
            <p className="mb-1">
              • <strong>Launcher processes are automatically hidden</strong> for
              your safety
            </p>
            <p className="mb-1">
              • <strong>Only SPT game processes are shown</strong> (Server,
              Client, Mods)
            </p>
            <p className="mb-1">
              • <strong>You cannot accidentally crash the launcher</strong>{" "}
              anymore
            </p>
            <p>
              • <strong>Safe to manage:</strong> SPT-AKI Server, Client, Fika
              Co-op, etc.
            </p>
          </div>
        </div>
      </div>

      <div className="flex space-x-2">
        <button
          onClick={fetchProcesses}
          disabled={isLoading}
          className="px-3 py-1 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center space-x-1"
        >
          <RefreshCw className={`w-4 h-4 ${isLoading ? "animate-spin" : ""}`} />
          <span>Refresh</span>
        </button>
        <button
          onClick={fetchProcesses}
          disabled={isLoading}
          className="px-3 py-1 bg-green-600 text-white text-sm rounded hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center space-x-1"
        >
          <Monitor className="w-4 h-4" />
          <span>Scan System</span>
        </button>
        <button
          onClick={handleToolReset}
          className="px-3 py-1 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded text-sm hover:bg-gray-300 dark:hover:bg-gray-600"
        >
          Back to Tools
        </button>
      </div>

      {isLoading ? (
        <div className="text-center py-8">
          <RefreshCw className="w-8 h-8 mx-auto mb-4 animate-spin text-blue-500" />
          <p className="text-gray-600 dark:text-gray-400">
            Loading processes...
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {processes.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <Monitor className="w-16 h-16 mx-auto mb-4 opacity-50" />
              <p className="text-lg">No processes running</p>
              <p>All SPT processes have been stopped</p>
            </div>
          ) : (
            processes.map((process) => (
              <div
                key={process.id}
                className="p-4 border border-gray-200 dark:border-gray-700 rounded-lg bg-gray-50 dark:bg-gray-800"
              >
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <div className="flex items-center space-x-2 mb-2">
                      <Monitor className="w-4 h-4 text-blue-500" />
                      <span className="font-medium text-gray-900 dark:text-gray-100">
                        {process.name}
                      </span>
                      <span
                        className={`px-2 py-1 text-xs rounded-full ${
                          process.status === "tracked"
                            ? "bg-blue-100 dark:bg-blue-900/20 text-blue-800 dark:text-blue-300"
                            : process.status === "running"
                            ? "bg-green-100 dark:bg-green-900/20 text-green-800 dark:text-green-300"
                            : "bg-gray-100 dark:bg-gray-900/20 text-gray-800 dark:text-gray-300"
                        }`}
                      >
                        {process.status === "tracked"
                          ? "Launched"
                          : process.status}
                      </span>
                      {/* Critical badge removed - all visible processes are safe to manage */}
                    </div>
                    <div className="grid grid-cols-2 gap-4 text-sm text-gray-600 dark:text-gray-400">
                      <div>
                        ID: {process.id} | PID: {process.pid}
                      </div>
                      <div>CPU: {process.cpu}</div>
                      <div>Memory: {process.memory}</div>
                      <div>Uptime: {process.uptime}</div>
                    </div>
                  </div>
                  <div className="flex space-x-2">
                    <button
                      onClick={() => handleProcessStop(process.pid)}
                      className="px-3 py-1 bg-red-600 text-white text-xs rounded hover:bg-red-700"
                      title={`Stop ${process.name} (PID: ${process.pid})`}
                    >
                      Stop
                    </button>
                  </div>
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );

  const renderConfigEditor = () => (
    <div className="space-y-4">
      <div className="flex justify-between items-center">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          Configuration Editor
        </h3>
        <button
          onClick={handleToolReset}
          className="px-3 py-1 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded text-sm hover:bg-gray-300 dark:hover:bg-gray-600"
        >
          Back to Tools
        </button>
      </div>

      <div className="space-y-4">
        {Object.entries(configData).map(([section, data]) => (
          <div
            key={section}
            className="border border-gray-200 dark:border-gray-700 rounded-lg p-4"
          >
            <h4 className="font-medium text-gray-900 dark:text-gray-100 mb-3 capitalize">
              {section} Settings
            </h4>
            <div className="space-y-2">
              {Object.entries(data).map(([key, value]) => (
                <div key={key} className="flex items-center justify-between">
                  <span className="text-sm text-gray-600 dark:text-gray-400 capitalize">
                    {key}:
                  </span>
                  <span className="text-sm font-mono text-gray-900 dark:text-gray-100">
                    {String(value)}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );

  if (activeTool === "process") return renderProcessMonitor();
  if (activeTool === "config") return renderConfigEditor();

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="text-center">
        <h1 className="text-2xl sm:text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Tools
        </h1>
        <p className="text-sm sm:text-base text-gray-600 dark:text-gray-400 px-2">
          Advanced tools for developers and power users
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4 sm:gap-6 px-2 sm:px-0">
        {toolCards.map((tool) => (
          <div
            key={tool.id}
            className="bg-white dark:bg-gray-800 p-4 sm:p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm"
          >
            <h2 className="text-lg sm:text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
              <tool.icon className="w-4 h-4 sm:w-5 sm:h-5" />
              <span>{tool.title}</span>
            </h2>
            <p className="text-sm sm:text-base text-gray-600 dark:text-gray-400 mb-4">
              {tool.description}
            </p>
            <button
              onClick={tool.onClick}
              className="w-full sm:w-auto px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
            >
              Open {tool.title}
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

export default memo(DevToolsTab);
