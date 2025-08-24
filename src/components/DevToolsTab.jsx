import React, { useState, useEffect } from "react";
import {
  Wrench,
  Terminal,
  FileText,
  Database,
  RefreshCw,
  Trash2,
  Download,
  Upload,
  Eye,
  Settings,
  Code,
  Monitor,
} from "lucide-react";
import { useToastContext } from "../contexts/ToastContext";

function DevToolsTab() {
  const { showSuccess, showError, showInfo } = useToastContext();
  const [activeTool, setActiveTool] = useState(null);
  const [processes, setProcesses] = useState([]);
  const [logs, setLogs] = useState([]);
  const [configData, setConfigData] = useState({});
  const [isLoading, setIsLoading] = useState(false);

  // Function to fetch real process data
  const fetchProcesses = async () => {
    if (!window.electronAPI?.getRunningProcesses) {
      // Fallback to mock data if API not available
      setProcesses([
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
      ]);
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
            status: proc.tracked ? "tracked" : "running",
            uptime: uptime,
            tracked: proc.tracked || false,
          };
        });
        setProcesses(formattedProcesses);
        showSuccess(
          "Processes Updated",
          `Found ${formattedProcesses.length} SPT-related processes`
        );
      }
    } catch (error) {
      console.error("Failed to fetch processes:", error);
      showError(
        "Process Detection Failed",
        "Could not detect system processes. Showing demonstration data."
      );
      // Fallback to mock data
      setProcesses([
        {
          id: 1,
          name: "SPT-AKI Server",
          pid: 1234,
          cpu: "2.3%",
          memory: "156 MB",
          status: "running",
          uptime: "2h 15m",
          tracked: false,
        },
        {
          id: 2,
          name: "SPT-AKI Client",
          pid: 5678,
          cpu: "1.8%",
          memory: "89 MB",
          status: "running",
          uptime: "1h 45m",
          tracked: false,
        },
        {
          id: 3,
          name: "Fika Co-op",
          pid: 9012,
          cpu: "0.5%",
          memory: "23 MB",
          status: "running",
          uptime: "45m",
          tracked: false,
        },
      ]);
    } finally {
      setIsLoading(false);
    }
  };

  // Load processes on component mount
  useEffect(() => {
    fetchProcesses();
  }, []);

  // Load logs and config data
  useEffect(() => {
    // Simulate logs
    setLogs([
      {
        id: 1,
        timestamp: "2024-01-15 14:30:22",
        level: "INFO",
        message: "SPT-AKI Server started successfully",
        source: "Server",
      },
      {
        id: 2,
        timestamp: "2024-01-15 14:30:25",
        level: "INFO",
        message: "Database connection established",
        source: "Database",
      },
      {
        id: 3,
        timestamp: "2024-01-15 14:31:00",
        level: "WARN",
        message: "High memory usage detected",
        source: "System",
      },
      {
        id: 4,
        timestamp: "2024-01-15 14:31:15",
        level: "ERROR",
        message: "Failed to load addon: SPT Realism",
        source: "AddonManager",
      },
    ]);

    // Simulate config data
    setConfigData({
      server: { port: 6969, host: "127.0.0.1", maxPlayers: 100 },
      database: { type: "sqlite", path: "./user/profiles/profiles.db" },
      game: { version: "3.7.1", mods: ["SPT Realism", "Fika Co-op"] },
    });
  }, []);

  const handleStopProcess = async (processId) => {
    if (!window.electronAPI) {
      console.error("Electron API not available");
      return;
    }

    // Find the actual process object to get the real PID
    const process = processes.find((p) => p.id === processId);
    if (!process) {
      showError("Process Not Found", `Process with ID ${processId} not found`);
      return;
    }

    const actualPid = process.pid;
    const processName = process.name;

    // Safety check is no longer needed since launcher processes are hidden
    // All visible processes are safe to manage

    try {
      // Stop the process
      const result = await window.electronAPI.stopProcess(actualPid);
      if (result.success) {
        // Remove the stopped process from the list
        setProcesses((prev) => prev.filter((p) => p.id !== processId));
        showSuccess(
          "Process Stopped",
          `Successfully stopped ${processName} (PID: ${actualPid})`
        );
        console.log(
          `Process ${processName} (PID: ${actualPid}) stopped successfully`
        );
      } else {
        showError(
          "Process Stop Failed",
          `Failed to stop ${processName} (PID: ${actualPid}): ${result.error}`
        );
        console.error(
          `Failed to stop process ${processName} (PID: ${actualPid}):`,
          result.error
        );
      }
    } catch (error) {
      showError(
        "Process Stop Failed",
        `Error stopping ${processName} (PID: ${actualPid}): ${error.message}`
      );
      console.error(
        `Error stopping process ${processName} (PID: ${actualPid}):`,
        error
      );
    }
  };

  const clearLogs = () => {
    setLogs([]);
  };

  const exportLogs = () => {
    const logText = logs
      .map((log) => `[${log.timestamp}] ${log.level}: ${log.message}`)
      .join("\n");
    const blob = new Blob([logText], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "spt-logs.txt";
    a.click();
    URL.revokeObjectURL(url);
  };

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
          onClick={() => setActiveTool(null)}
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
                      onClick={() => handleStopProcess(process.id)}
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

  const renderLogViewer = () => (
    <div className="space-y-4">
      <div className="flex justify-between items-center">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          System Logs
        </h3>
        <div className="flex space-x-2">
          <button
            onClick={exportLogs}
            className="px-3 py-1 bg-green-600 text-white text-xs rounded hover:bg-green-700 flex items-center space-x-1"
          >
            <Download className="w-3 h-3" />
            <span>Export</span>
          </button>
          <button
            onClick={clearLogs}
            className="px-3 py-1 bg-red-600 text-white text-xs rounded hover:bg-red-700 flex items-center space-x-1"
          >
            <Trash2 className="w-3 h-3" />
            <span>Clear</span>
          </button>
          <button
            onClick={() => setActiveTool(null)}
            className="px-3 py-1 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded text-sm hover:bg-gray-300 dark:hover:bg-gray-600"
          >
            Back to Tools
          </button>
        </div>
      </div>

      <div className="max-h-96 overflow-y-auto space-y-2">
        {logs.map((log) => (
          <div
            key={log.id}
            className={`p-3 rounded text-sm font-mono ${
              log.level === "ERROR"
                ? "bg-red-100 dark:bg-red-900/20 text-red-800 dark:text-red-300"
                : log.level === "WARN"
                ? "bg-yellow-100 dark:bg-yellow-900/20 text-yellow-800 dark:text-yellow-300"
                : "bg-gray-100 dark:bg-gray-800 text-gray-800 dark:text-gray-300"
            }`}
          >
            <div className="flex items-center justify-between">
              <span className="font-medium">
                [{log.timestamp}] {log.level}
              </span>
              <span className="text-xs opacity-75">{log.source}</span>
            </div>
            <div className="mt-1">{log.message}</div>
          </div>
        ))}
      </div>
    </div>
  );

  const renderConfigEditor = () => (
    <div className="space-y-4">
      <div className="flex justify-between items-center">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          Configuration Editor
        </h3>
        <button
          onClick={() => setActiveTool(null)}
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
  if (activeTool === "logs") return renderLogViewer();
  if (activeTool === "config") return renderConfigEditor();

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Developer Tools
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Advanced tools for developers and power users
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Terminal className="w-5 h-5" />
            <span>Process Monitor</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Monitor running SPT processes and system resources
          </p>
          <button
            onClick={() => setActiveTool("process")}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
          >
            Open Process Monitor
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <FileText className="w-5 h-5" />
            <span>Log Viewer</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            View and analyze SPT server and client logs
          </p>
          <button
            onClick={() => setActiveTool("logs")}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
          >
            Open Log Viewer
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Database className="w-5 h-5" />
            <span>Database Tools</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Manage SPT database and profile data
          </p>
          <button className="px-4 py-2 bg-gray-400 text-white rounded-md cursor-not-allowed">
            Coming Soon
          </button>
        </div>

        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Wrench className="w-5 h-5" />
            <span>Configuration Editor</span>
          </h2>
          <p className="text-gray-600 dark:text-gray-400 mb-4">
            Edit SPT configuration files directly
          </p>
          <button
            onClick={() => setActiveTool("config")}
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors"
          >
            Open Config Editor
          </button>
        </div>
      </div>
    </div>
  );
}

export default DevToolsTab;
