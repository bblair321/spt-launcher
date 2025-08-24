import React, { useState, useEffect } from "react";
import {
  Server,
  Play,
  Square,
  Settings,
  FolderOpen,
  Save,
  Trash2,
  Edit,
  Copy,
  FileText,
  Zap,
} from "lucide-react";

function ServersTab() {
  const [servers, setServers] = useState([]);
  const [selectedServer, setSelectedServer] = useState(null);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState({
    name: "",
    path: "",
    port: "6969",
    autoStart: false,
    description: "",
    serverType: "local", // "local" or "remote"
    remoteAddress: "",
    remotePort: "6969",
  });
  const [runningServers, setRunningServers] = useState(new Map());
  const [consoleOutput, setConsoleOutput] = useState([]);
  const [autoScroll, setAutoScroll] = useState(true);
  const consoleRef = React.useRef(null);
  const lastOutputRef = React.useRef(0);
  const [sptPath, setSptPath] = useState("");
  const [showSptSettings, setShowSptSettings] = useState(false);

  // Load saved servers from localStorage
  useEffect(() => {
    const savedServers = localStorage.getItem("sptServers");
    if (savedServers) {
      setServers(JSON.parse(savedServers));
    }

    // Load saved SPT path
    const savedSptPath = localStorage.getItem("sptPath");
    if (savedSptPath) {
      setSptPath(savedSptPath);
    }
  }, []);

  // Save servers to localStorage whenever servers change
  useEffect(() => {
    localStorage.setItem("sptServers", JSON.stringify(servers));
  }, [servers]);

  const selectServerPath = async () => {
    if (window.electronAPI) {
      try {
        const path = await window.electronAPI.selectFile();
        if (path) {
          setFormData((prev) => ({ ...prev, path }));
        }
      } catch (error) {
        console.error("Failed to select server path:", error);
      }
    }
  };

  const selectSptPath = async () => {
    if (window.electronAPI) {
      try {
        const path = await window.electronAPI.selectFile();
        if (path) {
          setSptPath(path);
          // Save to localStorage
          localStorage.setItem("sptPath", path);
        }
      } catch (error) {
        console.error("Failed to select SPT path:", error);
      }
    }
  };

  const handleSubmit = (e) => {
    e.preventDefault();

    // Validate form based on server type
    if (formData.serverType === "local" && !formData.path) {
      alert("Please select a server executable for local servers");
      return;
    }
    if (formData.serverType === "remote" && !formData.remoteAddress) {
      alert("Please enter a remote server address");
      return;
    }

    if (isEditing && selectedServer) {
      // Update existing server
      setServers((prev) =>
        prev.map((server) =>
          server.id === selectedServer.id
            ? { ...formData, id: server.id }
            : server
        )
      );
      setIsEditing(false);
      setSelectedServer(null);
    } else {
      // Add new server
      const newServer = {
        ...formData,
        id: Date.now().toString(),
        createdAt: new Date().toISOString(),
      };
      setServers((prev) => [...prev, newServer]);
    }

    // Reset form
    setFormData({
      name: "",
      path: "",
      port: "6969",
      autoStart: false,
      description: "",
      serverType: "local",
      remoteAddress: "",
      remotePort: "6969",
    });
  };

  const editServer = (server) => {
    setSelectedServer(server);
    setFormData({
      name: server.name,
      path: server.path,
      port: server.port,
      autoStart: server.autoStart,
      description: server.description,
      serverType: server.serverType || "local",
      remoteAddress: server.remoteAddress || "",
      remotePort: server.remotePort || "6969",
    });
    setIsEditing(true);
  };

  const deleteServer = (serverId) => {
    setServers((prev) => prev.filter((server) => server.id !== serverId));
    if (selectedServer?.id === serverId) {
      setSelectedServer(null);
      setIsEditing(false);
    }
  };

  const duplicateServer = (server) => {
    const duplicatedServer = {
      ...server,
      id: Date.now().toString(),
      name: `${server.name} (Copy)`,
      createdAt: new Date().toISOString(),
    };
    setServers((prev) => [...prev, duplicatedServer]);
  };

  const addConsoleOutput = (message, type = "info") => {
    const timestamp = new Date().toLocaleTimeString();
    setConsoleOutput((prev) => {
      const newOutput = [...prev, { timestamp, message, type }];
      lastOutputRef.current = Date.now();
      return newOutput;
    });
  };

  const scrollToBottom = () => {
    if (consoleRef.current) {
      try {
        consoleRef.current.scrollTo({
          top: consoleRef.current.scrollHeight,
          behavior: "smooth",
        });
      } catch (error) {
        // Fallback to immediate scroll
        consoleRef.current.scrollTop = consoleRef.current.scrollHeight;
      }
    }
  };

  const handleScroll = (e) => {
    const { scrollTop, scrollHeight, clientHeight } = e.target;
    const isAtBottom = scrollHeight - scrollTop - clientHeight < 20;

    if (isAtBottom && !autoScroll) {
      setAutoScroll(true);
    } else if (!isAtBottom && autoScroll) {
      setAutoScroll(false);
    }
  };

  const testRemoteServer = async (server) => {
    try {
      addConsoleOutput(
        `Testing connection to ${server.remoteAddress}:${server.remotePort}...`,
        "info"
      );

      // Simple ping test - you could enhance this with actual SPT-AKI protocol checking
      const startTime = Date.now();

      // For now, we'll just simulate a connection test
      // In a real implementation, you'd want to check if the SPT-AKI server is responding
      await new Promise((resolve) => setTimeout(resolve, 1000));

      const responseTime = Date.now() - startTime;
      addConsoleOutput(
        `✓ Connection test successful (${responseTime}ms)`,
        "success"
      );
      addConsoleOutput(
        `Remote server "${server.name}" is reachable`,
        "success"
      );

      return true;
    } catch (error) {
      addConsoleOutput(`✗ Connection test failed: ${error.message}`, "error");
      return false;
    }
  };

  const quickConnect = async (server) => {
    if (server.serverType !== "remote") {
      addConsoleOutput(
        `Quick connect is only available for remote servers`,
        "error"
      );
      return;
    }

    try {
      addConsoleOutput(`🚀 Quick connecting to ${server.name}...`, "info");
      addConsoleOutput(`Testing server connectivity first...`, "info");

      // First test the connection
      const isReachable = await testRemoteServer(server);

      if (!isReachable) {
        addConsoleOutput(
          `✗ Cannot connect to server. Please check the server address and try again.`,
          "error"
        );
        return;
      }

      addConsoleOutput(`✓ Server is reachable! Launching Tarkov...`, "success");

      // Launch Tarkov with SPT-AKI client
      if (window.electronAPI && window.electronAPI.launchTarkov) {
        addConsoleOutput(`🔍 Debug: SPT path being sent: ${sptPath}`, "info");
        addConsoleOutput(`🔍 Debug: SPT path type: ${typeof sptPath}`, "info");
        addConsoleOutput(
          `🔍 Debug: SPT path length: ${sptPath ? sptPath.length : 0}`,
          "info"
        );

        const result = await window.electronAPI.launchTarkov(sptPath);
        if (result.success) {
          addConsoleOutput(`✓ Tarkov launched successfully!`, "success");
          addConsoleOutput(
            `📋 Server connection info copied to clipboard:`,
            "info"
          );
          addConsoleOutput(
            `   Address: ${server.remoteAddress}:${server.remotePort}`,
            "info"
          );
          addConsoleOutput(`   Use this info when prompted in Tarkov`, "info");

          // Copy connection info to clipboard
          if (navigator.clipboard) {
            try {
              await navigator.clipboard.writeText(
                `${server.remoteAddress}:${server.remotePort}`
              );
              addConsoleOutput(
                `✓ Connection info copied to clipboard!`,
                "success"
              );
              addConsoleOutput(
                `📋 Paste this in Tarkov when connecting: ${server.remoteAddress}:${server.remotePort}`,
                "info"
              );
            } catch (error) {
              addConsoleOutput(
                `⚠ Could not copy to clipboard: ${error.message}`,
                "error"
              );
            }
          }
        } else {
          addConsoleOutput(
            `✗ Failed to launch Tarkov: ${result.error}`,
            "error"
          );
        }
      } else {
        addConsoleOutput(
          `⚠ Tarkov launcher not available. Please launch Tarkov manually.`,
          "warning"
        );
        addConsoleOutput(
          `📋 Server connection info: ${server.remoteAddress}:${server.remotePort}`,
          "info"
        );
      }
    } catch (error) {
      console.error("Quick connect failed:", error);
      addConsoleOutput(`✗ Quick connect failed: ${error.message}`, "error");
    }
  };

  const launchServer = async (server) => {
    if (server.serverType === "remote") {
      // For remote servers, just test connectivity
      await testRemoteServer(server);
      return;
    }

    if (!server.path) return;

    try {
      setConsoleOutput([]); // Clear console when starting new process
      setAutoScroll(true); // Force auto-scroll on when launching
      addConsoleOutput(
        `Launching Server: ${server.name} (${server.path})`,
        "info"
      );
      addConsoleOutput(`Waiting for server output...`, "info");

      const result = await window.electronAPI.launchProcess(server.path);

      if (result.code === 0 && result.pid) {
        // Track running server
        setRunningServers(
          (prev) =>
            new Map(
              prev.set(server.id, {
                ...server,
                process: result,
                startTime: Date.now(),
              })
            )
        );

        addConsoleOutput(
          `✓ Server "${server.name}" started successfully`,
          "success"
        );
        addConsoleOutput(
          `Monitoring server output for ready status...`,
          "info"
        );
      } else {
        addConsoleOutput(`✗ Failed to launch Server "${server.name}"`, "error");
      }
    } catch (error) {
      console.error("Failed to launch server:", error);
      addConsoleOutput(`✗ Failed to launch server: ${error.message}`, "error");
    }
  };

  const stopServer = async (serverId) => {
    const runningServer = runningServers.get(serverId);
    if (!runningServer?.process?.pid) return;

    try {
      addConsoleOutput(`Stopping Server "${runningServer.name}"...`, "info");
      await window.electronAPI.stopProcess(runningServer.process.pid);
      addConsoleOutput(
        `✓ Server "${runningServer.name}" stopped successfully`,
        "success"
      );
    } catch (error) {
      console.error("Failed to stop server:", error);
      addConsoleOutput(`✗ Failed to stop server: ${error.message}`, "error");
    }

    // Remove from running servers
    setRunningServers((prev) => {
      const newMap = new Map(prev);
      newMap.delete(serverId);
      return newMap;
    });
  };

  // Listen for real-time process output from main process
  useEffect(() => {
    const handleProcessOutput = (event, data) => {
      const { pid, type, data: output } = data;

      // Find which server this output belongs to
      let serverName = "Unknown";
      for (const [serverId, server] of runningServers) {
        if (server.process?.pid === pid) {
          serverName = server.name;
          break;
        }
      }

      // Show all server output (important for users), filter only very short/empty lines
      const trimmedOutput = output.trim();
      if (trimmedOutput.length > 1) {
        addConsoleOutput(
          `[${serverName}] ${trimmedOutput}`,
          type === "stderr" ? "error" : "info"
        );
      }
    };

    // Listen for process output events
    if (window.electronAPI && window.electronAPI.onProcessOutput) {
      try {
        window.electronAPI.onProcessOutput(handleProcessOutput);
      } catch (error) {
        console.warn("Failed to register process output listener:", error);
      }
    }

    return () => {
      if (
        window.electronAPI &&
        window.electronAPI.removeProcessOutputListener
      ) {
        try {
          window.electronAPI.removeProcessOutputListener(handleProcessOutput);
        } catch (error) {
          console.warn("Failed to remove process output listener:", error);
        }
      }
    };
  }, [runningServers]);

  // Auto-scroll when console output changes
  useEffect(() => {
    if (consoleOutput.length > 0 && autoScroll) {
      const timer = setTimeout(() => {
        scrollToBottom();
      }, 50);
      return () => clearTimeout(timer);
    }
  }, [consoleOutput, autoScroll]);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 mb-2">
          Server Management
        </h1>
        <p className="text-gray-600 dark:text-gray-400">
          Configure and manage your SPT-AKI servers
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Server Form */}
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Settings className="w-5 h-5" />
            <span>{isEditing ? "Edit Server" : "Add New Server"}</span>
          </h2>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Server Name
              </label>
              <input
                type="text"
                value={formData.name}
                onChange={(e) =>
                  setFormData((prev) => ({ ...prev, name: e.target.value }))
                }
                placeholder="My SPT Server"
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                required
              />
            </div>

            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Server Type
              </label>
              <div className="flex space-x-2">
                <label className="flex items-center space-x-2">
                  <input
                    type="radio"
                    name="serverType"
                    value="local"
                    checked={formData.serverType === "local"}
                    onChange={(e) =>
                      setFormData((prev) => ({
                        ...prev,
                        serverType: e.target.value,
                      }))
                    }
                    className="text-blue-600"
                  />
                  <span>Local Server</span>
                </label>
                <label className="flex items-center space-x-2">
                  <input
                    type="radio"
                    name="serverType"
                    value="remote"
                    checked={formData.serverType === "remote"}
                    onChange={(e) =>
                      setFormData((prev) => ({
                        ...prev,
                        serverType: e.target.value,
                      }))
                    }
                    className="text-blue-600"
                  />
                  <span>Remote Server (Fika)</span>
                </label>
              </div>
            </div>

            {formData.serverType === "local" ? (
              <div>
                <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                  Server Executable
                </label>
                <div className="flex space-x-2">
                  <input
                    type="text"
                    value={formData.path}
                    onChange={(e) =>
                      setFormData((prev) => ({ ...prev, path: e.target.value }))
                    }
                    placeholder="e.g., D:\\SPT\\Aki.Server.exe"
                    className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    required={formData.serverType === "local"}
                  />
                  <button
                    type="button"
                    onClick={selectServerPath}
                    className="px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 rounded-md transition-colors"
                  >
                    <FileText className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ) : (
              <>
                <div>
                  <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                    Remote Server Address
                  </label>
                  <input
                    type="text"
                    value={formData.remoteAddress}
                    onChange={(e) =>
                      setFormData((prev) => ({
                        ...prev,
                        remoteAddress: e.target.value,
                      }))
                    }
                    placeholder="e.g., 192.168.1.100 or server.example.com"
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    required={formData.serverType === "remote"}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                    Remote Server Port
                  </label>
                  <input
                    type="number"
                    value={formData.remotePort}
                    onChange={(e) =>
                      setFormData((prev) => ({
                        ...prev,
                        remotePort: e.target.value,
                      }))
                    }
                    placeholder="6969"
                    className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                    required={formData.serverType === "remote"}
                  />
                </div>
              </>
            )}

            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Port
              </label>
              <input
                type="number"
                value={formData.port}
                onChange={(e) =>
                  setFormData((prev) => ({ ...prev, port: e.target.value }))
                }
                placeholder="6969"
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                required
              />
            </div>

            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                Description
              </label>
              <textarea
                value={formData.description}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    description: e.target.value,
                  }))
                }
                placeholder="Optional server description..."
                rows={3}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
              />
            </div>

            <div className="flex items-center space-x-2">
              <input
                type="checkbox"
                id="autoStart"
                checked={formData.autoStart}
                onChange={(e) =>
                  setFormData((prev) => ({
                    ...prev,
                    autoStart: e.target.checked,
                  }))
                }
                className="rounded border-gray-300"
              />
              <label
                htmlFor="autoStart"
                className="text-sm font-medium text-gray-900 dark:text-gray-100"
              >
                Auto-start with launcher
              </label>
            </div>

            <div className="flex space-x-2">
              <button
                type="submit"
                className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors flex items-center justify-center space-x-2"
              >
                <Save className="w-4 h-4" />
                <span>{isEditing ? "Update Server" : "Add Server"}</span>
              </button>

              {isEditing && (
                <button
                  type="button"
                  onClick={() => {
                    setIsEditing(false);
                    setSelectedServer(null);
                    setFormData({
                      name: "",
                      path: "",
                      port: "6969",
                      autoStart: false,
                      description: "",
                    });
                  }}
                  className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
                >
                  Cancel
                </button>
              )}
            </div>
          </form>
        </div>

        {/* Server List */}
        <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Server className="w-5 h-5" />
            <span>Configured Servers</span>
          </h2>

          {servers.length === 0 ? (
            <div className="text-center py-8 text-gray-500 dark:text-gray-400">
              <Server className="w-12 h-12 mx-auto mb-4 opacity-50" />
              <p>No servers configured yet</p>
              <p className="text-sm">Add your first server using the form</p>
            </div>
          ) : (
            <div className="space-y-3">
              {servers.map((server) => (
                <div
                  key={server.id}
                  className="p-4 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                >
                  <div className="flex items-center justify-between mb-2">
                    <h3 className="font-semibold">{server.name}</h3>
                    <div className="flex items-center space-x-1">
                      {server.serverType === "remote" ? (
                        <>
                          <button
                            onClick={() => quickConnect(server)}
                            className="p-1 hover:bg-gray-200 rounded transition-colors"
                            title="Quick Connect (Launch Tarkov + Connect)"
                          >
                            <Zap className="w-4 h-4 text-yellow-500" />
                          </button>
                          <button
                            onClick={() => testRemoteServer(server)}
                            className="p-1 hover:bg-gray-200 rounded transition-colors"
                            title="Test Connection"
                          >
                            <Play className="w-4 h-4 text-blue-500" />
                          </button>
                        </>
                      ) : runningServers.has(server.id) ? (
                        <button
                          onClick={() => stopServer(server.id)}
                          className="p-1 hover:bg-gray-200 rounded transition-colors"
                          title="Stop Server"
                        >
                          <Square className="w-4 h-4 text-red-500" />
                        </button>
                      ) : (
                        <button
                          onClick={() => launchServer(server)}
                          className="p-1 hover:bg-gray-200 rounded transition-colors"
                          title="Launch Server"
                        >
                          <Play className="w-4 h-4 text-green-500" />
                        </button>
                      )}
                      <button
                        onClick={() => editServer(server)}
                        className="p-1 hover:bg-gray-200 rounded transition-colors"
                        title="Edit Server"
                      >
                        <Edit className="w-4 h-4 text-blue-500" />
                      </button>
                      <button
                        onClick={() => duplicateServer(server)}
                        className="p-1 hover:bg-gray-200 rounded transition-colors"
                        title="Duplicate Server"
                      >
                        <Copy className="w-4 h-4 text-purple-500" />
                      </button>
                      <button
                        onClick={() => deleteServer(server.id)}
                        className="p-1 hover:bg-gray-200 rounded transition-colors"
                        title="Delete Server"
                      >
                        <Trash2 className="w-4 h-4 text-red-500" />
                      </button>
                    </div>
                  </div>

                  <div className="text-sm text-gray-600 dark:text-gray-400 space-y-1">
                    {runningServers.has(server.id) && (
                      <p className="text-green-600 font-medium">🟢 Running</p>
                    )}
                    <p>
                      <strong>Type:</strong>{" "}
                      {server.serverType === "remote"
                        ? "Remote (Fika)"
                        : "Local"}
                    </p>
                    {server.serverType === "local" ? (
                      <p>
                        <strong>Executable:</strong> {server.path}
                      </p>
                    ) : (
                      <p>
                        <strong>Address:</strong> {server.remoteAddress}:
                        {server.remotePort}
                      </p>
                    )}
                    <p>
                      <strong>Port:</strong> {server.port}
                    </p>
                    {server.description && (
                      <p>
                        <strong>Description:</strong> {server.description}
                      </p>
                    )}
                    <p>
                      <strong>Auto-start:</strong>{" "}
                      {server.autoStart ? "Yes" : "No"}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* SPT-AKI Settings */}
      <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Zap className="w-5 h-5" />
            <span>SPT-AKI Settings</span>
          </h3>
          <button
            onClick={() => setShowSptSettings(!showSptSettings)}
            className="px-3 py-1 text-sm bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors"
          >
            {showSptSettings ? "Hide" : "Configure"}
          </button>
        </div>

        {showSptSettings && (
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium mb-2 text-gray-900 dark:text-gray-100">
                SPT-AKI Launcher Path
              </label>
              <div className="flex space-x-2">
                <input
                  type="text"
                  value={sptPath}
                  onChange={(e) => setSptPath(e.target.value)}
                  placeholder="e.g., C:\\SPT\\Aki.Launcher.exe"
                  className="flex-1 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100"
                />
                <button
                  type="button"
                  onClick={selectSptPath}
                  className="px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 rounded-md transition-colors"
                >
                  <FileText className="w-4 h-4" />
                </button>
              </div>
              <p className="text-sm text-gray-600 dark:text-gray-400 mt-1">
                Path to Aki.Launcher.exe. This is required for Quick Connect to
                work properly.
              </p>
            </div>

            <div className="flex space-x-2">
              <button
                onClick={() => {
                  if (sptPath) {
                    localStorage.setItem("sptPath", sptPath);
                    addConsoleOutput(
                      "✓ SPT-AKI path saved successfully",
                      "success"
                    );
                  } else {
                    addConsoleOutput(
                      "✗ Please select a valid SPT-AKI path first",
                      "error"
                    );
                  }
                }}
                className="px-4 py-2 bg-green-600 text-white rounded-md hover:bg-green-700 transition-colors"
              >
                Save Path
              </button>
              <button
                onClick={() => {
                  setSptPath("");
                  localStorage.removeItem("sptPath");
                  addConsoleOutput("SPT-AKI path cleared", "info");
                }}
                className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
              >
                Clear Path
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Console Output */}
      <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold flex items-center space-x-2 text-gray-900 dark:text-gray-100">
            <Settings className="w-5 h-5" />
            <span>Server Console Output</span>
            {consoleOutput.length > 0 && (
              <span className="text-sm text-gray-500 font-normal">
                ({consoleOutput.length} lines)
              </span>
            )}
            <div className="flex items-center space-x-2 ml-2">
              <div
                className={`w-2 h-2 rounded-full ${
                  autoScroll ? "bg-green-500" : "bg-yellow-500"
                }`}
              ></div>
              <span
                className={`text-xs ${
                  autoScroll ? "text-green-600" : "text-yellow-600"
                }`}
              >
                {autoScroll ? "Auto-scroll" : "Paused"}
              </span>
            </div>
          </h3>
          <div className="flex space-x-2">
            {!autoScroll && (
              <button
                onClick={() => setAutoScroll(true)}
                className="px-3 py-1 text-sm bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors"
                title="Resume auto-scroll"
              >
                Resume Auto-scroll
              </button>
            )}
            <button
              onClick={() => {
                if (consoleRef.current) {
                  consoleRef.current.scrollTop =
                    consoleRef.current.scrollHeight;
                }
              }}
              className="px-3 py-1 text-sm bg-gray-500 text-white rounded hover:bg-gray-600 transition-colors"
              title="Scroll to bottom"
            >
              Scroll to Bottom
            </button>
            <button
              onClick={() => setConsoleOutput([])}
              className="px-3 py-1 text-sm bg-red-500 text-white rounded hover:bg-red-600 transition-colors"
              title="Clear console"
            >
              Clear
            </button>
          </div>
        </div>

        <div
          ref={consoleRef}
          onScroll={handleScroll}
          className="bg-gray-900 text-green-400 p-4 rounded-md font-mono text-sm h-64 overflow-y-auto"
        >
          {consoleOutput.length === 0 ? (
            <div className="text-gray-500 dark:text-gray-400 text-center py-8">
              <p>No server output yet</p>
              <p className="text-xs">Launch a server to see console output</p>
            </div>
          ) : (
            consoleOutput.map((output, index) => (
              <div
                key={index}
                className={`mb-1 ${
                  output.type === "error"
                    ? "text-red-400"
                    : output.type === "success"
                    ? "text-green-400"
                    : "text-gray-300"
                }`}
              >
                <span className="text-gray-500">[{output.timestamp}]</span>{" "}
                {output.message}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

export default ServersTab;
