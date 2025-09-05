import React, {
  useState,
  useEffect,
  useCallback,
  useMemo,
  useRef,
  memo,
} from "react";
import {
  Wrench,
  Terminal,
  Database,
  RefreshCw,
  Monitor,
  Save,
  Copy,
  RotateCcw,
  AlertTriangle,
  CheckCircle,
  ChevronDown,
  ChevronUp,
  FileText,
  Search,
  Settings,
  Server,
  Gamepad2,
  Shield,
  X,
} from "lucide-react";
import { useToastContext } from "../contexts/ToastContext";
import { useLauncher } from "../contexts/LauncherContext";

function DevToolsTab() {
  const { showSuccess, showError, showInfo } = useToastContext();
  const { launcherPath, fikaConfig, setFikaConfig } = useLauncher();
  const [activeTool, setActiveTool] = useState(null);
  const [processes, setProcesses] = useState([]);
  const [configData, setConfigData] = useState({});
  const [isLoading, setIsLoading] = useState(false);

  // Configuration editor state
  const [configEditor, setConfigEditor] = useState({
    rawConfig: "",
    parsedConfig: {},
    hasChanges: false,
    isValid: true,
    error: null,
    configPath: "",
    selectedConfig: null,
  });
  const [availableConfigs, setAvailableConfigs] = useState([]);
  const [backups, setBackups] = useState([]);
  const [showBackupManager, setShowBackupManager] = useState(false);
  const [selectedServer, setSelectedServer] = useState(null);
  const [availableServers, setAvailableServers] = useState([]);
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedFolder, setSelectedFolder] = useState(null);
  const [viewMode, setViewMode] = useState("folders"); // "folders" or "files"
  const [showSearch, setShowSearch] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState([]);
  const [currentSearchIndex, setCurrentSearchIndex] = useState(0);
  const textareaRef = useRef(null);
  const searchInputRef = useRef(null);
  const searchTimeoutRef = useRef(null);

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
      if (result && result.success && Array.isArray(result.processes)) {
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
      } else if (result && Array.isArray(result.processes)) {
        // Fallback for backward compatibility
        setProcesses(result.processes);
      } else {
        console.warn("Unexpected processes format");
        setProcesses([]);
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
        if (result) {
          showSuccess("Process Stopped", `Successfully stopped process ${pid}`);
          // Refresh the process list
          fetchProcesses();
        } else {
          throw new Error(result?.error || "Failed to stop process");
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

  // Configuration editor functions
  const loadAvailableServers = useCallback(() => {
    const savedServers = localStorage.getItem("sptServers");
    if (savedServers) {
      const servers = JSON.parse(savedServers);
      const localServers = servers.filter(
        (server) => server.serverType === "local" && server.path
      );
      setAvailableServers(localServers);

      // Auto-select first server if none selected
      if (localServers.length > 0 && !selectedServer) {
        setSelectedServer(localServers[0]);
      }
    } else {
      setAvailableServers([]);
      setSelectedServer(null);
    }
  }, [selectedServer]);

  const loadAvailableConfigs = useCallback(async () => {
    if (!selectedServer) {
      showError("No Server Selected", "Please select a server first.");
      return;
    }

    // Extract directory from executable path (remove the .exe file)
    const sptDirectory = selectedServer.path
      .replace(/\\[^\\]*\.exe$/, "")
      .replace(/\/[^\/]*$/, "");

    try {
      const result = await window.electronAPI?.listSptConfigs?.(sptDirectory);

      if (result?.success) {
        setAvailableConfigs(result.configs || []);
        if (result.message) {
          showSuccess("Directory Created", result.message);
        } else {
          showSuccess(
            "Configurations Loaded",
            `Found ${result.configs?.length || 0} configuration files`
          );
        }
      } else {
        throw new Error(result?.error || "Failed to load configurations");
      }
    } catch (error) {
      console.error("Failed to load configurations:", error);
      showError("Load Failed", error.message);
    }
  }, [selectedServer, showError, showSuccess]);

  const loadConfigFile = useCallback(
    async (configFile) => {
      try {
        setIsLoading(true);
        const result = await window.electronAPI.readSptConfig(configFile.path);

        if (result?.success && result.config) {
          const rawConfig = JSON.stringify(result.config, null, 2);
          setConfigEditor((prev) => ({
            ...prev,
            rawConfig,
            parsedConfig: result.config,
            hasChanges: false,
            isValid: true,
            error: null,
            configPath: result.configPath || configFile.path,
            selectedConfig: configFile,
          }));
          showSuccess(
            "Configuration Loaded",
            `Loaded ${configFile.name} successfully`,
            3000 // 3 second duration
          );
        } else {
          throw new Error(result?.error || "Failed to load configuration");
        }
      } catch (error) {
        console.error("Failed to load configuration:", error);
        showError("Load Failed", error.message);
      } finally {
        setIsLoading(false);
      }
    },
    [showError, showSuccess]
  );

  const validateConfig = useCallback((configText) => {
    try {
      const parsed = JSON.parse(configText);
      return { isValid: true, config: parsed, error: null };
    } catch (error) {
      return { isValid: false, config: null, error: error.message };
    }
  }, []);

  const handleConfigChange = useCallback(
    (newConfig) => {
      const validation = validateConfig(newConfig);
      setConfigEditor((prev) => ({
        ...prev,
        rawConfig: newConfig,
        parsedConfig: validation.config || prev.parsedConfig,
        hasChanges: true,
        isValid: validation.isValid,
        error: validation.error,
      }));
    },
    [validateConfig]
  );

  const saveConfigEditor = useCallback(async () => {
    // Get SPT directory from configured servers
    const savedServers = localStorage.getItem("sptServers");
    if (!savedServers) {
      showError(
        "No Servers Configured",
        "Please configure at least one server in the Servers tab first."
      );
      return;
    }

    const servers = JSON.parse(savedServers);
    const localServers = servers.filter(
      (server) => server.serverType === "local" && server.path
    );

    if (localServers.length === 0) {
      showError(
        "No Local Servers",
        "Please configure at least one local server in the Servers tab first."
      );
      return;
    }

    if (!configEditor.isValid) {
      showError(
        "Invalid Configuration",
        "Please fix JSON syntax errors before saving."
      );
      return;
    }

    // If no config path is set (new config), create one
    let configPath = configEditor.configPath;
    if (!configPath) {
      const sptPath = localServers[0].path;
      const sptDirectory = sptPath
        .replace(/\\[^\\]*\.exe$/, "")
        .replace(/\/[^\/]*$/, "");
      const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
      configPath = `${sptDirectory}\\SPT_Data\\Server\\config-${timestamp}.json`;
    }

    try {
      setIsLoading(true);
      const result = await window.electronAPI.saveSptConfig(
        configEditor.parsedConfig,
        configPath
      );

      if (result?.success) {
        setConfigEditor((prev) => ({
          ...prev,
          hasChanges: false,
          configPath: configPath,
        }));
        showSuccess(
          "Configuration Saved",
          result.message || "Configuration saved successfully."
        );

        // Refresh the available configs list
        loadAvailableConfigs();

        // Update Fika config in context if it changed
        if (configEditor.parsedConfig.IsDevMode !== undefined) {
          setFikaConfig((prev) => ({
            ...prev,
            enableFika: configEditor.parsedConfig.IsDevMode === true,
          }));
        }
      } else {
        throw new Error(result?.error || "Failed to save configuration");
      }
    } catch (error) {
      console.error("Failed to save configuration:", error);
      showError("Save Failed", "Failed to save SPT configuration.");
    } finally {
      setIsLoading(false);
    }
  }, [
    configEditor,
    showSuccess,
    showError,
    setFikaConfig,
    loadAvailableConfigs,
  ]);

  const resetConfigEditor = useCallback(() => {
    setConfigEditor((prev) => ({
      ...prev,
      rawConfig: JSON.stringify(prev.parsedConfig, null, 2),
      hasChanges: false,
      isValid: true,
      error: null,
    }));
    showInfo(
      "Configuration Reset",
      "Configuration has been reset to the last saved state."
    );
  }, [showInfo]);

  // Backup functions
  const createBackup = useCallback(async () => {
    if (!launcherPath) {
      showError(
        "No SPT Directory",
        "Please set your SPT installation directory first."
      );
      return;
    }

    try {
      const result = await window.electronAPI?.backupSptConfig?.(launcherPath);

      if (result?.success) {
        showSuccess("Backup Created", result.message);
        // Reload backups to show the new one
        await loadBackups();
      } else {
        showError("Backup Failed", result?.error || "Failed to create backup.");
      }
    } catch (error) {
      console.error("Failed to create backup:", error);
      showError("Backup Failed", "Failed to create backup.");
    }
  }, [launcherPath, showSuccess, showError]);

  const loadBackups = useCallback(async () => {
    if (!launcherPath) return;

    try {
      const result = await window.electronAPI?.listConfigBackups?.(
        launcherPath
      );

      if (result?.success) {
        setBackups(result.backups || []);
      } else {
        console.error("Failed to load backups:", result?.error);
      }
    } catch (error) {
      console.error("Failed to load backups:", error);
    }
  }, [launcherPath]);

  // Load backups when component mounts or launcher path changes
  useEffect(() => {
    if (launcherPath) {
      loadBackups();
    }
  }, [launcherPath, loadBackups]);

  const restoreBackup = useCallback(
    async (backupPath) => {
      if (!launcherPath) {
        showError(
          "No SPT Directory",
          "Please set your SPT installation directory first."
        );
        return;
      }

      try {
        const result = await window.electronAPI?.restoreSptConfig?.(
          backupPath,
          launcherPath
        );

        if (result?.success) {
          showSuccess("Configuration Restored", result.message);
          setShowBackupManager(false);
          // Refresh available configs list
          await loadAvailableConfigs();
          // Reload the current configuration if one is selected
          if (configEditor.selectedConfig) {
            await loadConfigFile(configEditor.selectedConfig);
          }
        } else {
          showError(
            "Restore Failed",
            result?.error || "Failed to restore configuration."
          );
        }
      } catch (error) {
        console.error("Failed to restore configuration:", error);
        showError("Restore Failed", "Failed to restore configuration.");
      }
    },
    [launcherPath, showSuccess, showError]
  );

  // Memoized configuration loading (legacy)
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
      if (result && result.config) {
        setConfigData(result.config);
        showSuccess(
          "Configuration Loaded",
          "Successfully loaded SPT configuration"
        );
      } else {
        throw new Error(result?.error || "Failed to load configuration");
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
        if (result) {
          showSuccess(
            "Configuration Saved",
            "Successfully saved SPT configuration"
          );
          setConfigData(config);
        } else {
          throw new Error(result?.error || "Failed to save configuration");
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

  // Load available servers on mount
  useEffect(() => {
    loadAvailableServers();
  }, [loadAvailableServers]);

  // Group configurations by folder
  const configsByFolder = useMemo(() => {
    const folders = {};

    availableConfigs.forEach((config) => {
      const folderName = config.directory || "Root";
      if (!folders[folderName]) {
        folders[folderName] = [];
      }
      folders[folderName].push(config);
    });

    // Sort folders by priority and files within each folder
    const sortedFolders = {};
    const folderOrder = ["database", "configs", "launcher", "Server", "Root"];

    folderOrder.forEach((folder) => {
      if (folders[folder]) {
        sortedFolders[folder] = folders[folder].sort((a, b) =>
          a.name.localeCompare(b.name)
        );
      }
    });

    // Add any remaining folders
    Object.keys(folders).forEach((folder) => {
      if (!sortedFolders[folder]) {
        sortedFolders[folder] = folders[folder].sort((a, b) =>
          a.name.localeCompare(b.name)
        );
      }
    });

    return sortedFolders;
  }, [availableConfigs]);

  // Get current view data based on view mode
  const currentViewData = useMemo(() => {
    if (viewMode === "folders") {
      // Filter folders based on search
      if (searchTerm) {
        const filteredFolders = {};
        Object.entries(configsByFolder).forEach(([folderName, configs]) => {
          const matchingConfigs = configs.filter(
            (config) =>
              config.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
              folderName.toLowerCase().includes(searchTerm.toLowerCase())
          );
          if (matchingConfigs.length > 0) {
            filteredFolders[folderName] = matchingConfigs;
          }
        });
        return filteredFolders;
      }
      return configsByFolder;
    } else {
      // File view - show all files with search filter
      let configs = availableConfigs;

      // Apply search filter
      if (searchTerm) {
        configs = configs.filter(
          (config) =>
            config.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
            config.directory?.toLowerCase().includes(searchTerm.toLowerCase())
        );
      }

      return configs;
    }
  }, [viewMode, configsByFolder, availableConfigs, searchTerm]);

  // Get files for selected folder
  const selectedFolderFiles = useMemo(() => {
    if (selectedFolder && configsByFolder[selectedFolder]) {
      return configsByFolder[selectedFolder];
    }
    return [];
  }, [selectedFolder, configsByFolder]);

  // Search functionality for JSON editor
  const performSearch = useCallback(
    (query) => {
      if (!query.trim()) {
        setSearchResults([]);
        setCurrentSearchIndex(0);
        return;
      }

      const text = configEditor.rawConfig;
      const results = [];
      const searchTerm = query.toLowerCase();
      let index = 0;

      while (index < text.length) {
        const foundIndex = text.toLowerCase().indexOf(searchTerm, index);
        if (foundIndex === -1) break;

        results.push({
          index: foundIndex,
          length: query.length,
          text: text.substring(foundIndex, foundIndex + query.length),
        });

        index = foundIndex + 1;
      }

      setSearchResults(results);
      setCurrentSearchIndex(0);
    },
    [configEditor.rawConfig]
  );

  const handleSearch = useCallback(
    (query) => {
      setSearchQuery(query);
      performSearch(query);
    },
    [performSearch]
  );

  const goToNextResult = useCallback(() => {
    if (searchResults.length === 0) return;
    setCurrentSearchIndex((prev) => (prev + 1) % searchResults.length);
  }, [searchResults.length]);

  const goToPreviousResult = useCallback(() => {
    if (searchResults.length === 0) return;
    setCurrentSearchIndex(
      (prev) => (prev - 1 + searchResults.length) % searchResults.length
    );
  }, [searchResults.length]);

  const closeSearch = useCallback(() => {
    setShowSearch(false);
    setSearchQuery("");
    setSearchResults([]);
    setCurrentSearchIndex(0);
  }, []);

  // Scroll to current search result
  useEffect(() => {
    if (searchResults.length > 0 && textareaRef.current) {
      const currentResult = searchResults[currentSearchIndex];
      if (currentResult) {
        // Calculate line number and scroll to it
        const textBeforeResult = configEditor.rawConfig.substring(
          0,
          currentResult.index
        );
        const lineNumber = textBeforeResult.split("\n").length - 1;

        // Scroll to the line (approximate)
        const lineHeight = 20; // Approximate line height
        const scrollTop = lineNumber * lineHeight;

        textareaRef.current.scrollTop = scrollTop;

        // Focus the textarea first, then select the text
        textareaRef.current.focus();

        // Use setTimeout to ensure focus happens before selection
        setTimeout(() => {
          if (textareaRef.current) {
            textareaRef.current.setSelectionRange(
              currentResult.index,
              currentResult.index + currentResult.length
            );
          }
        }, 10);
      }
    }
  }, [currentSearchIndex, searchResults, configEditor.rawConfig]);

  // Ensure search input maintains focus when search is open
  useEffect(() => {
    if (showSearch && searchInputRef.current) {
      searchInputRef.current.focus();
    }
  }, [showSearch]);

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

  const renderConfigEditor = () => {
    return (
      <div className="space-y-6">
        {/* Header */}
        <div className="flex justify-between items-center">
          <div>
            <h3 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              SPT-AKI Configuration Editor
            </h3>
            <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
              Manage SPT-AKI server configuration files
            </p>
          </div>
          <button
            onClick={handleToolReset}
            className="px-3 py-1 bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 rounded text-sm hover:bg-gray-300 dark:hover:bg-gray-600"
          >
            Back to Tools
          </button>
        </div>

        {/* Server Selection */}
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <div className="flex justify-between items-center mb-4">
            <h4 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              Select Server
            </h4>
            <button
              onClick={loadAvailableServers}
              className="flex items-center space-x-2 px-3 py-1 text-sm bg-gray-600 text-white rounded hover:bg-gray-700 transition-colors"
            >
              <RefreshCw className="w-4 h-4" />
              <span>Refresh Servers</span>
            </button>
          </div>

          {availableServers.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <Server className="w-12 h-12 mx-auto mb-2 opacity-50" />
              <p>No local servers configured</p>
              <p className="text-sm">
                Configure servers in the Servers tab first
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              {availableServers.map((server) => (
                <div
                  key={server.id}
                  className={`p-3 border rounded-lg cursor-pointer transition-colors ${
                    selectedServer?.id === server.id
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20"
                      : "border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500"
                  }`}
                  onClick={() => setSelectedServer(server)}
                >
                  <div className="flex items-center space-x-2 mb-1">
                    <Server className="w-4 h-4 text-gray-500" />
                    <span className="font-medium text-sm">{server.name}</span>
                  </div>
                  <div className="text-xs text-gray-500 dark:text-gray-400">
                    {server.path}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Available Configurations */}
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <div className="flex justify-between items-center mb-4">
            <div className="flex items-center space-x-4">
              <h4 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                {viewMode === "folders"
                  ? selectedFolder
                    ? `📁 ${selectedFolder} (${selectedFolderFiles.length})`
                    : `Available Folders (${
                        Object.keys(currentViewData).length
                      })`
                  : `Available Configurations (${
                      Array.isArray(currentViewData)
                        ? currentViewData.length
                        : Object.values(currentViewData).flat().length
                    })`}
              </h4>

              {/* View Mode Toggle */}
              <div className="flex bg-gray-200 dark:bg-gray-700 rounded-md p-1">
                <button
                  onClick={() => {
                    setViewMode("folders");
                    setSelectedFolder(null);
                  }}
                  className={`px-3 py-1 text-sm rounded-md transition-colors ${
                    viewMode === "folders"
                      ? "bg-white dark:bg-gray-600 text-gray-900 dark:text-gray-100 shadow-sm"
                      : "text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100"
                  }`}
                >
                  📁 Folders
                </button>
                <button
                  onClick={() => {
                    setViewMode("files");
                    setSelectedFolder(null);
                  }}
                  className={`px-3 py-1 text-sm rounded-md transition-colors ${
                    viewMode === "files"
                      ? "bg-white dark:bg-gray-600 text-gray-900 dark:text-gray-100 shadow-sm"
                      : "text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-100"
                  }`}
                >
                  📄 Files
                </button>
              </div>
            </div>

            <div className="flex items-center space-x-2">
              {selectedFolder && (
                <button
                  onClick={() => setSelectedFolder(null)}
                  className="flex items-center space-x-1 px-3 py-1 text-sm bg-gray-600 text-white rounded hover:bg-gray-700 transition-colors"
                >
                  <span>← Back</span>
                </button>
              )}
              <button
                onClick={loadAvailableConfigs}
                disabled={isLoading || !selectedServer}
                className="flex items-center space-x-2 px-3 py-1 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <RefreshCw
                  className={`w-4 h-4 ${isLoading ? "animate-spin" : ""}`}
                />
                <span>Refresh</span>
              </button>
            </div>
          </div>

          {/* Search Controls */}
          <div className="mb-6">
            <div className="relative">
              <input
                type="text"
                placeholder="Search configurations..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full px-4 py-2 pl-10 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <FileText className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
            </div>
          </div>

          {availableConfigs.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <FileText className="w-12 h-12 mx-auto mb-2 opacity-50" />
              <p>No configuration files found</p>
              <p className="text-sm">
                The SPT_Data/Server directory is empty or doesn't exist. Try
                clicking the refresh button.
              </p>
            </div>
          ) : viewMode === "folders" ? (
            // Folder View
            selectedFolder ? (
              // Show files in selected folder
              selectedFolderFiles.length === 0 ? (
                <div className="text-center py-8 text-gray-500 dark:text-gray-400">
                  <FileText className="w-12 h-12 mx-auto mb-2 opacity-50" />
                  <p>No files in this folder</p>
                </div>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                  {selectedFolderFiles.map((config, index) => (
                    <div
                      key={index}
                      className={`p-3 border rounded-lg cursor-pointer transition-colors ${
                        configEditor.selectedConfig?.name === config.name
                          ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20"
                          : "border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500"
                      }`}
                      onClick={() => loadConfigFile(config)}
                    >
                      <div className="flex items-center space-x-2 mb-2">
                        <FileText className="w-4 h-4 text-gray-500" />
                        <span className="font-medium text-sm">
                          {config.name}
                        </span>
                      </div>
                      <div className="text-xs text-gray-500 dark:text-gray-400">
                        <div>Size: {(config.size / 1024).toFixed(1)} KB</div>
                        <div>
                          Modified:{" "}
                          {new Date(config.modified).toLocaleDateString()}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )
            ) : // Show folders
            Object.keys(currentViewData).length === 0 ? (
              <div className="text-center py-8 text-gray-500 dark:text-gray-400">
                <FileText className="w-12 h-12 mx-auto mb-2 opacity-50" />
                <p>No folders match your search</p>
                <p className="text-sm">Try adjusting your search criteria</p>
              </div>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {Object.entries(currentViewData).map(
                  ([folderName, configs]) => (
                    <div
                      key={folderName}
                      className="p-4 border border-gray-200 dark:border-gray-600 rounded-lg cursor-pointer transition-colors hover:border-gray-300 dark:hover:border-gray-500 hover:bg-gray-50 dark:hover:bg-gray-700/50"
                      onClick={() => setSelectedFolder(folderName)}
                    >
                      <div className="flex items-center space-x-3 mb-2">
                        <div className="w-8 h-8 bg-blue-100 dark:bg-blue-900/30 rounded-lg flex items-center justify-center">
                          <FileText className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                        </div>
                        <div>
                          <h5 className="font-medium text-sm text-gray-900 dark:text-gray-100">
                            {folderName === "database"
                              ? "📊 Database"
                              : folderName === "configs"
                              ? "⚙️ Server Configs"
                              : folderName === "launcher"
                              ? "🚀 Launcher"
                              : folderName === "Server"
                              ? "🖥️ Server"
                              : `📁 ${folderName}`}
                          </h5>
                          <p className="text-xs text-gray-500 dark:text-gray-400">
                            {configs.length} file
                            {configs.length !== 1 ? "s" : ""}
                          </p>
                        </div>
                      </div>
                      <div className="text-xs text-gray-500 dark:text-gray-400">
                        <div>Click to browse files</div>
                      </div>
                    </div>
                  )
                )}
              </div>
            )
          ) : // File View (category mode)
          Array.isArray(currentViewData) && currentViewData.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <FileText className="w-12 h-12 mx-auto mb-2 opacity-50" />
              <p>No configurations match your filter</p>
              <p className="text-sm">
                Try adjusting your search or filter criteria
              </p>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
              {(Array.isArray(currentViewData)
                ? currentViewData
                : Object.values(currentViewData).flat()
              ).map((config, index) => (
                <div
                  key={index}
                  className={`p-3 border rounded-lg cursor-pointer transition-colors ${
                    configEditor.selectedConfig?.name === config.name
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20"
                      : "border-gray-200 dark:border-gray-600 hover:border-gray-300 dark:hover:border-gray-500"
                  }`}
                  onClick={() => loadConfigFile(config)}
                >
                  <div className="flex items-center space-x-2 mb-2">
                    <FileText className="w-4 h-4 text-gray-500" />
                    <span className="font-medium text-sm">{config.name}</span>
                  </div>
                  <div className="text-xs text-gray-500 dark:text-gray-400">
                    <div>Size: {(config.size / 1024).toFixed(1)} KB</div>
                    <div>
                      Modified: {new Date(config.modified).toLocaleDateString()}
                    </div>
                    {config.directory && (
                      <div
                        className={`${
                          config.directory === "database"
                            ? "text-green-600 dark:text-green-400 font-medium"
                            : config.directory === "configs"
                            ? "text-blue-600 dark:text-blue-400 font-medium"
                            : config.directory === "launcher"
                            ? "text-orange-600 dark:text-orange-400"
                            : "text-gray-600 dark:text-gray-400"
                        }`}
                      >
                        📁{" "}
                        {config.directory === "database"
                          ? "Server Database"
                          : config.directory === "configs"
                          ? "Server Configs"
                          : config.directory === "launcher"
                          ? "Launcher Config"
                          : config.directory}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Action Buttons */}
        <div className="flex flex-wrap gap-2">
          <button
            onClick={saveConfigEditor}
            disabled={
              isLoading ||
              !configEditor.hasChanges ||
              !configEditor.isValid ||
              !launcherPath ||
              !configEditor.selectedConfig
            }
            className="flex items-center space-x-2 px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <Save className="w-4 h-4" />
            <span>{isLoading ? "Saving..." : "Save Config"}</span>
          </button>

          <button
            onClick={createBackup}
            disabled={!launcherPath}
            className="flex items-center space-x-2 px-4 py-2 bg-orange-600 text-white rounded-md hover:bg-orange-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <Copy className="w-4 h-4" />
            <span>Backup</span>
          </button>

          <button
            onClick={() => setShowBackupManager(!showBackupManager)}
            disabled={!launcherPath}
            className="flex items-center space-x-2 px-4 py-2 bg-teal-600 text-white rounded-md hover:bg-teal-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <FileText className="w-4 h-4" />
            <span>Manage Backups</span>
          </button>
        </div>

        {/* Backup Manager */}
        {showBackupManager && (
          <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
            <h4 className="text-lg font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100 mb-4">
              <FileText className="w-5 h-5" />
              <span>Backup Manager</span>
            </h4>

            <div className="space-y-4">
              <div className="flex justify-between items-center">
                <p className="text-sm text-gray-600 dark:text-gray-400">
                  Manage your configuration backups
                </p>
                <button
                  onClick={loadBackups}
                  className="flex items-center space-x-2 px-3 py-1 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 transition-colors"
                >
                  <RefreshCw className="w-4 h-4" />
                  <span>Refresh</span>
                </button>
              </div>

              {backups.length === 0 ? (
                <div className="text-center py-8 text-gray-500 dark:text-gray-400">
                  <FileText className="w-12 h-12 mx-auto mb-2 opacity-50" />
                  <p>No backups found</p>
                  <p className="text-sm">Create a backup to get started</p>
                </div>
              ) : (
                <div className="space-y-2 max-h-64 overflow-y-auto">
                  {backups.map((backup, index) => (
                    <div
                      key={index}
                      className="flex items-center justify-between p-3 bg-gray-50 dark:bg-gray-700 rounded-lg"
                    >
                      <div className="flex-1">
                        <div className="flex items-center space-x-2">
                          <FileText className="w-4 h-4 text-gray-500" />
                          <span className="font-medium text-sm">
                            {backup.name}
                          </span>
                        </div>
                        <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                          Created: {new Date(backup.created).toLocaleString()}
                          <span className="mx-2">•</span>
                          Size: {(backup.size / 1024).toFixed(1)} KB
                        </div>
                      </div>
                      <button
                        onClick={() => restoreBackup(backup.path)}
                        className="px-3 py-1 text-sm bg-green-600 text-white rounded hover:bg-green-700 transition-colors"
                      >
                        Restore
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {/* JSON Editor */}
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <div>
              <h4 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
                Configuration JSON
              </h4>
              {configEditor.selectedConfig && (
                <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
                  Editing: {configEditor.selectedConfig.name}
                </p>
              )}
            </div>
            <div className="flex items-center space-x-2">
              {configEditor.isValid ? (
                <div className="flex items-center space-x-1 text-green-600">
                  <CheckCircle className="w-4 h-4" />
                  <span className="text-sm">Valid JSON</span>
                </div>
              ) : (
                <div className="flex items-center space-x-1 text-red-600">
                  <AlertTriangle className="w-4 h-4" />
                  <span className="text-sm">Invalid JSON</span>
                </div>
              )}
            </div>
          </div>

          {configEditor.error && (
            <div className="bg-red-50 border border-red-200 rounded-md p-3">
              <div className="flex items-start">
                <AlertTriangle className="w-5 h-5 text-red-600 mr-2 mt-0.5" />
                <div>
                  <p className="text-red-800 font-medium">JSON Syntax Error</p>
                  <p className="text-red-700 text-sm mt-1">
                    {configEditor.error}
                  </p>
                </div>
              </div>
            </div>
          )}

          <div className="relative">
            <textarea
              ref={textareaRef}
              value={configEditor.rawConfig}
              onChange={(e) => handleConfigChange(e.target.value)}
              className="w-full h-96 p-4 border border-gray-300 dark:border-gray-600 rounded-md font-mono text-sm bg-gray-50 dark:bg-gray-900 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none relative z-20"
              placeholder={
                configEditor.selectedConfig
                  ? "Edit your SPT-AKI configuration here... (Ctrl+F to search)"
                  : "Select a configuration file to start editing..."
              }
              spellCheck={false}
              disabled={!configEditor.selectedConfig}
              onKeyDown={(e) => {
                // Handle Ctrl+F for search
                if (e.ctrlKey && e.key === "f") {
                  e.preventDefault();
                  setShowSearch(true);
                }

                // When search is open, prevent editing but allow selection
                if (showSearch) {
                  // Allow arrow keys and selection keys
                  if (
                    ![
                      "ArrowUp",
                      "ArrowDown",
                      "ArrowLeft",
                      "ArrowRight",
                      "Home",
                      "End",
                      "PageUp",
                      "PageDown",
                    ].includes(e.key)
                  ) {
                    e.preventDefault();
                  }
                }
              }}
            />

            {/* Search result indicator */}
            {showSearch && searchResults.length > 0 && (
              <div className="absolute top-2 right-2 bg-blue-500 text-white px-2 py-1 rounded text-xs font-medium z-30">
                {currentSearchIndex + 1} of {searchResults.length}
              </div>
            )}
          </div>
        </div>

        {/* Search Overlay */}
        {showSearch && (
          <div className="fixed top-4 left-1/2 transform -translate-x-1/2 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 rounded-lg shadow-lg p-4 z-50 min-w-80 max-w-md">
            <div className="flex items-center space-x-2 mb-3">
              <Search className="w-4 h-4 text-gray-500" />
              <input
                ref={searchInputRef}
                type="text"
                placeholder="Search in JSON..."
                className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
                autoFocus
                onInput={(e) => {
                  const value = e.target.value;
                  setSearchQuery(value);
                  performSearch(value);
                }}
                onKeyDown={(e) => {
                  e.stopPropagation();
                }}
              />
              <button
                onClick={closeSearch}
                className="p-1 text-gray-500 hover:text-gray-700 dark:hover:text-gray-300"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Search results */}
            {searchQuery && searchResults.length > 0 && (
              <div className="mt-2 flex items-center justify-between text-sm text-gray-600 dark:text-gray-400">
                <div className="flex items-center space-x-2">
                  <button
                    onClick={goToPreviousResult}
                    className="p-1 hover:bg-gray-100 dark:hover:bg-gray-700 rounded"
                  >
                    <ChevronUp className="w-4 h-4" />
                  </button>
                  <button
                    onClick={goToNextResult}
                    className="p-1 hover:bg-gray-100 dark:hover:bg-gray-700 rounded"
                  >
                    <ChevronDown className="w-4 h-4" />
                  </button>
                </div>
                <span>
                  {currentSearchIndex + 1} of {searchResults.length}
                </span>
              </div>
            )}
          </div>
        )}

        {/* Status Indicators */}
        <div className="space-y-4">
          {configEditor.hasChanges && (
            <div className="bg-yellow-50 border border-yellow-200 rounded-md p-4">
              <div className="flex items-center">
                <AlertTriangle className="w-5 h-5 text-yellow-600 mr-2" />
                <span className="text-yellow-800 font-medium">
                  You have unsaved changes
                </span>
              </div>
            </div>
          )}
        </div>
      </div>
    );
  };

  // Main component render
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
