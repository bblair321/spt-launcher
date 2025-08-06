// Tauri API - invoke should be globally available in Tauri v2
// If invoke is not available, we'll use a fallback approach



// DOM Elements
const serverPathInput = document.getElementById("server-path");
const launcherPathInput = document.getElementById("launcher-path");
const serverPortInput = document.getElementById("server-port");
const autoStartCheckbox = document.getElementById("auto-start");
const autoLauncherCheckbox = document.getElementById("auto-launcher");

const startServerBtn = document.getElementById("start-server");
const stopServerBtn = document.getElementById("stop-server");
const startLauncherBtn = document.getElementById("start-launcher");
const stopLauncherBtn = document.getElementById("stop-launcher");
const startAllBtn = document.getElementById("start-all");
const stopAllBtn = document.getElementById("stop-all");

const browseServerFileBtn = document.getElementById("browse-server-file");
const browseLauncherFileBtn = document.getElementById("browse-launcher-file");
const saveConfigBtn = document.getElementById("save-config");
const loadConfigBtn = document.getElementById("load-config");

const serverStatus = document.getElementById("server-status");
const launcherStatus = document.getElementById("launcher-status");
const portStatus = document.getElementById("port-status");
const processCount = document.getElementById("process-count");
const refreshInfoBtn = document.getElementById("refresh-info");

const logOutput = document.getElementById("log-output");
const clearLogBtn = document.getElementById("clear-log");
const copyLogBtn = document.getElementById("copy-log"); // Add copy log button
const resumeScrollBtn = document.getElementById("resume-scroll"); // Add resume scroll button
const processList = document.getElementById("process-list");
const refreshProcessesBtn = document.getElementById("refresh-processes");

const tabBtns = document.querySelectorAll(".tab-btn");
const tabPanes = document.querySelectorAll(".tab-pane");

// State
let serverRunning = false;
let launcherRunning = false;
let autoRefreshInterval = null;
let launcherOutputInterval = null;
let serverOutputInterval = null;
let maxLogLines = 1000;
let isLoading = false; // Add loading state
let autoScrollPaused = false; // Add auto-scroll pause state

// Helper function to safely invoke Tauri commands
function safeInvoke(command, ...args) {
  // Try different ways to access invoke in Tauri v2
  if (
    window.__TAURI__ &&
    window.__TAURI__.core &&
    window.__TAURI__.core.invoke
  ) {
    return window.__TAURI__.core.invoke(command, ...args);
  } else if (window.__TAURI__ && window.__TAURI__.invoke) {
    return window.__TAURI__.invoke(command, ...args);
  } else if (
    window.__TAURI__ &&
    window.__TAURI__.api &&
    window.__TAURI__.api.invoke
  ) {
    return window.__TAURI__.api.invoke(command, ...args);
  } else if (typeof invoke !== "undefined") {
    return invoke(command, ...args);
  } else {
    throw new Error("Tauri invoke not available");
  }
}

// Initialize
document.addEventListener("DOMContentLoaded", () => {
  loadConfigWithUISettings();
  setupEventListeners();
  setupTabs();
  setupWindowControls();
  setupKeyboardShortcuts(); // Add keyboard shortcuts
  setupLogAutoScroll(); // Add log auto-scroll setup
  startAutoRefresh();
  checkPortStatus();
  updateProcessCount();

  // Store original button text for loading states
  storeOriginalButtonText();

  // Validation temporarily disabled to focus on core functionality
  // setupValidationListeners();
});

// Event Listeners
function setupEventListeners() {
  // Browse buttons - use native Tauri file dialog
  browseServerFileBtn.addEventListener("click", async () => {
    try {
      const path = await safeInvoke("select_server_file");
      if (path && !path.startsWith("ERROR:")) {
        serverPathInput.value = path;
        addLogLine(`Selected server file: ${path}`, "info");

        // Set the server path using the args wrapper function
        const setResult = await safeInvoke("set_server_path_args_wrapper", {
          args: { path: path },
        });
        addLogLine(`Server path set result: ${setResult}`, "info");

        if (setResult.startsWith("SUCCESS:")) {
          showNotification("Server file selected successfully", "success");
          // Auto-save settings when file is selected
          setTimeout(() => saveConfigWithUISettings(), 100);
        } else {
          showNotification("Failed to set server path", "error");
          addLogLine(`Server path set failed: ${setResult}`, "error");
        }
      } else {
        showNotification("No file selected or selection failed", "warning");
      }
    } catch (error) {
      addLogLine(`Failed to select server file: ${error.message}`, "error");
      addLogLine(`Server file selection error details: ${error}`, "error");
      showNotification("Failed to select server file", "error");
    }
  });

  browseLauncherFileBtn.addEventListener("click", async () => {
    try {
      const path = await safeInvoke("select_launcher_file");
      if (path && !path.startsWith("ERROR:")) {
        launcherPathInput.value = path;
        addLogLine(`Selected launcher file: ${path}`, "info");

        // Set the launcher path using the args wrapper function
        const setResult = await safeInvoke("set_launcher_path_args_wrapper", {
          args: { path: path },
        });
        addLogLine(`Launcher path set result: ${setResult}`, "info");

        if (setResult.startsWith("SUCCESS:")) {
          showNotification("Launcher file selected successfully", "success");
          // Auto-save settings when file is selected
          setTimeout(() => saveConfigWithUISettings(), 100);
        } else {
          showNotification("Failed to set launcher path", "error");
          addLogLine(`Launcher path set failed: ${setResult}`, "error");
        }
      } else {
        showNotification("No file selected or selection failed", "warning");
      }
    } catch (error) {
      addLogLine(`Failed to select launcher file: ${error.message}`, "error");
      addLogLine(`Launcher file selection error details: ${error}`, "error");
      showNotification("Failed to select launcher file", "error");
    }
  });

  // Control buttons
  startServerBtn.addEventListener("click", startServer);
  stopServerBtn.addEventListener("click", stopServer);
  startLauncherBtn.addEventListener("click", startLauncher);
  stopLauncherBtn.addEventListener("click", stopLauncher);
  startAllBtn.addEventListener("click", startAll);
  stopAllBtn.addEventListener("click", stopAll);

  // Config buttons
  saveConfigBtn.addEventListener("click", saveConfig);
  loadConfigBtn.addEventListener("click", loadConfig);

  // Info buttons
  refreshInfoBtn.addEventListener("click", refreshInfo);
  clearLogBtn.addEventListener("click", clearLog);
  refreshProcessesBtn.addEventListener("click", updateProcessList);

  // Debug button
  const clearPathsBtn = document.getElementById("clear-paths");
  if (clearPathsBtn) {
    clearPathsBtn.addEventListener("click", clearPaths);
  }

  // Copy log button
  copyLogBtn.addEventListener("click", copyLogToClipboard);

  // Resume scroll button
  resumeScrollBtn.addEventListener("click", resumeAutoScroll);

  // Settings
  const logLevelSelect = document.getElementById("log-level");
  const maxLogLinesInput = document.getElementById("max-log-lines");
  const autoRefreshCheckbox = document.getElementById("auto-refresh");
  const refreshIntervalInput = document.getElementById("refresh-interval");

  logLevelSelect.addEventListener("change", (e) => {
    // Auto-save settings when changed
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  maxLogLinesInput.addEventListener("change", (e) => {
    maxLogLines = parseInt(e.target.value);
    // Auto-save settings when changed
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  autoRefreshCheckbox.addEventListener("change", (e) => {
    if (e.target.checked) {
      startAutoRefresh();
    } else {
      stopAutoRefresh();
    }
    // Auto-save settings when changed
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  refreshIntervalInput.addEventListener("change", (e) => {
    const interval = parseInt(e.target.value);
    if (autoRefreshCheckbox.checked) {
      stopAutoRefresh();
      startAutoRefresh(interval);
    }
    // Auto-save settings when changed
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  // Auto-save when auto-start checkboxes change
  autoStartCheckbox.addEventListener("change", () => {
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  autoLauncherCheckbox.addEventListener("change", () => {
    setTimeout(() => saveConfigWithUISettings(), 100);
  });

  // Auto-save when server port changes
  serverPortInput.addEventListener("change", () => {
    setTimeout(() => saveConfigWithUISettings(), 100);
  });
}

// Tab Management
function setupTabs() {
  tabBtns.forEach((btn) => {
    btn.addEventListener("click", () => {
      const targetTab = btn.getAttribute("data-tab");

      // Remove active class from all tabs
      tabBtns.forEach((b) => b.classList.remove("active"));
      tabPanes.forEach((p) => p.classList.remove("active"));

      // Add active class to clicked tab
      btn.classList.add("active");
      document.getElementById(targetTab).classList.add("active");
    });
  });
}

// Window Controls
function setupWindowControls() {
  const minimizeBtn = document.getElementById("minimize-window");
  const closeBtn = document.getElementById("close-window");

  minimizeBtn.addEventListener("click", async () => {
    try {
      const result = await safeInvoke("minimize_window");

      if (result.startsWith("ERROR:")) {
        console.error("Failed to minimize window:", result);
      }
    } catch (error) {
      console.error("Failed to minimize window:", error);
    }
  });

  closeBtn.addEventListener("click", async () => {
    try {
      const result = await safeInvoke("close_window");

      if (result.startsWith("ERROR:")) {
        console.error("Failed to close window:", result);
      }
    } catch (error) {
      console.error("Failed to close window:", error);
    }
  });
}

// Start server function
async function startServer() {
  if (isLoading) return; // Prevent multiple simultaneous operations

  try {
    isLoading = true;
    updateButtonState(startServerBtn, true, "Starting...");

    const serverPath = serverPathInput.value.trim();
    addLogLine(`Starting server with path: ${serverPath}`, "info");

    if (!serverPath) {
      showNotification("Please enter the server path", "error");
      return;
    }

    addLogLine(`Starting server...`);

    // Set the server path using the args wrapper function
    const setResult = await safeInvoke("set_server_path_args_wrapper", {
      args: { path: serverPath },
    });
    addLogLine(`Server path set result: ${setResult}`, "info");
    addLogLine(`Server path set: ${setResult}`);

    // Launch the server
    const result = await safeInvoke("launch_server");
    addLogLine(`Server start result: ${result}`, "info");

    if (result.startsWith("SUCCESS:")) {
      serverRunning = true;
      updateServerStatus("Running");
      showNotification("Server started successfully", "success");
      startServerOutputRefresh();
    } else {
      showNotification(`Failed to start server: ${result}`, "error");
    }
  } catch (error) {
    addLogLine(`Error starting server: ${error}`, "error");
    showNotification("Failed to start server", "error");
  } finally {
    isLoading = false;
    updateButtonState(startServerBtn, false, "Start Server");
  }
}

async function stopServer() {
  if (isLoading) return;

  try {
    isLoading = true;
    updateButtonState(stopServerBtn, true, "Stopping...");

    const result = await safeInvoke("stop_server");
    addLogLine(`Server stop result: ${result}`, "info");

    if (result.startsWith("SUCCESS:")) {
      serverRunning = false;
      updateServerStatus("Stopped");
      showNotification("Server stopped successfully", "success");
      stopServerOutputRefresh();
    } else {
      showNotification(`Failed to stop server: ${result}`, "error");
    }
  } catch (error) {
    addLogLine(`Error stopping server: ${error}`, "error");
    showNotification("Failed to stop server", "error");
  } finally {
    isLoading = false;
    updateButtonState(stopServerBtn, false, "Stop Server");
  }
}

// Start launcher function
async function startLauncher() {
  if (isLoading) return;

  try {
    isLoading = true;
    updateButtonState(startLauncherBtn, true, "Starting...");

    const launcherPath = launcherPathInput.value.trim();
    addLogLine(`Starting launcher with path: ${launcherPath}`, "info");

    if (!launcherPath) {
      showNotification("Please enter the launcher path", "error");
      return;
    }

    addLogLine(`Starting launcher...`);

    // Set the launcher path using the args wrapper function
    const setResult = await safeInvoke("set_launcher_path_args_wrapper", {
      args: { path: launcherPath },
    });
    addLogLine(`Launcher path set result: ${setResult}`, "info");
    addLogLine(`Launcher path set: ${setResult}`);

    // Launch the launcher
    const result = await safeInvoke("launch_launcher");
    addLogLine(`Launcher start result: ${result}`, "info");

    if (result.startsWith("SUCCESS:")) {
      launcherRunning = true;
      updateLauncherStatus("Running");
      showNotification("Launcher started successfully", "success");
      startLauncherOutputRefresh();
    } else {
      showNotification(`Failed to start launcher: ${result}`, "error");
    }
  } catch (error) {
    addLogLine(`Error starting launcher: ${error}`, "error");
    showNotification("Failed to start launcher", "error");
  } finally {
    isLoading = false;
    updateButtonState(startLauncherBtn, false, "Start Launcher");
  }
}

async function stopLauncher() {
  if (isLoading) return;

  try {
    isLoading = true;
    updateButtonState(stopLauncherBtn, true, "Stopping...");

    const result = await safeInvoke("stop_launcher");
    addLogLine(`Launcher stop result: ${result}`, "info");

    if (result.startsWith("SUCCESS:")) {
      launcherRunning = false;
      updateLauncherStatus("Stopped");
      showNotification("Launcher stopped successfully", "success");
      stopLauncherOutputRefresh();
    } else {
      showNotification(`Failed to stop launcher: ${result}`, "error");
    }
  } catch (error) {
    addLogLine(`Error stopping launcher: ${error}`, "error");
    showNotification("Failed to stop launcher", "error");
  } finally {
    isLoading = false;
    updateButtonState(stopLauncherBtn, false, "Stop Launcher");
  }
}

async function startAll() {
  addLogLine("Starting all services...");

  if (autoStartCheckbox.checked) {
    addLogLine("Auto-start server enabled, starting server...");
    await startServer();
  }

  if (autoLauncherCheckbox.checked) {
    addLogLine("Auto-start launcher enabled, starting launcher...");
    await startLauncher();
  }

  if (!autoStartCheckbox.checked && !autoLauncherCheckbox.checked) {
    addLogLine("No auto-start options enabled");
    showNotification("No auto-start options enabled", "warning");
  }
}

async function stopAll() {
  await stopServer();
  await stopLauncher();
}

// Configuration - enhanced with UI settings
async function saveConfigWithUISettings() {
  try {
    // Collect all UI settings
    const autoStartServer = autoStartCheckbox.checked;
    const autoStartLauncher = autoLauncherCheckbox.checked;
    const serverPort = parseInt(serverPortInput.value) || 6969;
    const maxLogLines =
      parseInt(document.getElementById("max-log-lines")?.value) || 1000;
    const autoRefresh =
      document.getElementById("auto-refresh")?.checked || true;
    const refreshInterval =
      parseInt(document.getElementById("refresh-interval")?.value) || 5000;
    const logLevel = document.getElementById("log-level")?.value || "Normal";

    // Get current paths from input fields
    const serverPath = serverPathInput.value.trim();
    const launcherPath = launcherPathInput.value.trim();

    console.log("Saving configuration with paths:", {
      serverPath,
      launcherPath,
      autoStartServer,
      autoStartLauncher,
      serverPort,
      maxLogLines,
      autoRefresh,
      refreshInterval,
      logLevel
    });

    // First, set the paths in the backend
    if (serverPath) {
      const setServerResult = await safeInvoke("set_server_path_args_wrapper", {
        args: { path: serverPath },
      });
      console.log("Set server path result:", setServerResult);
    }

    if (launcherPath) {
      const setLauncherResult = await safeInvoke("set_launcher_path_args_wrapper", {
        args: { path: launcherPath },
      });
      console.log("Set launcher path result:", setLauncherResult);
    }

    const result = await safeInvoke("save_config_with_ui_settings", {
      settings: {
        autoStartServer,
        autoStartLauncher,
        serverPort,
        maxLogLines,
        autoRefresh,
        refreshInterval,
        logLevel,
      },
    });

    if (result.startsWith("SUCCESS:")) {
      addLogLine("Configuration saved successfully", "success");
      console.log("Configuration saved successfully");
      // Show a subtle notification for auto-save
      showNotification("Settings auto-saved", "success");
    } else {
      addLogLine(`Failed to save configuration: ${result}`, "error");
      console.error("Failed to save configuration:", result);
    }
  } catch (error) {
    addLogLine(`Failed to save configuration: ${error.message}`, "error");
    console.error("Save config error:", error);
  }
}

async function loadConfigWithUISettings() {
  try {
    console.log("Loading configuration with UI settings...");
    const result = await safeInvoke("load_config_with_ui_settings");

    if (result.startsWith("SUCCESS:")) {
      // Parse the JSON settings from the result
      const settingsJson = result.replace("SUCCESS: ", "");
      const settings = JSON.parse(settingsJson);

      console.log("Loaded settings:", settings);

      // Update UI with loaded settings
      if (settings.server_path) {
        serverPathInput.value = settings.server_path;
        addLogLine(`Loaded server path: ${settings.server_path}`, "info");
        console.log("Set server path input to:", settings.server_path);
      } else {
        console.log("No server path found in settings");
      }

      if (settings.launcher_path) {
        launcherPathInput.value = settings.launcher_path;
        addLogLine(`Loaded launcher path: ${settings.launcher_path}`, "info");
        console.log("Set launcher path input to:", settings.launcher_path);
      } else {
        console.log("No launcher path found in settings");
      }

      if (settings.server_port) {
        serverPortInput.value = settings.server_port;
      }

      if (settings.auto_start_server !== undefined) {
        autoStartCheckbox.checked = settings.auto_start_server;
      }

      if (settings.auto_start_launcher !== undefined) {
        autoLauncherCheckbox.checked = settings.auto_start_launcher;
      }

      if (settings.max_log_lines) {
        const maxLogLinesInput = document.getElementById("max-log-lines");
        if (maxLogLinesInput) {
          maxLogLinesInput.value = settings.max_log_lines;
          maxLogLines = settings.max_log_lines;
        }
      }

      if (settings.auto_refresh !== undefined) {
        const autoRefreshCheckbox = document.getElementById("auto-refresh");
        if (autoRefreshCheckbox) {
          autoRefreshCheckbox.checked = settings.auto_refresh;
          if (settings.auto_refresh) {
            startAutoRefresh();
          } else {
            stopAutoRefresh();
          }
        }
      }

      if (settings.refresh_interval) {
        const refreshIntervalInput =
          document.getElementById("refresh-interval");
        if (refreshIntervalInput) {
          refreshIntervalInput.value = settings.refresh_interval;
        }
      }

      if (settings.log_level) {
        const logLevelSelect = document.getElementById("log-level");
        if (logLevelSelect) {
          logLevelSelect.value = settings.log_level;
        }
      }

      addLogLine("Configuration loaded successfully", "success");
      showNotification("Configuration loaded", "success");
    } else if (result.includes("No configuration file found")) {
      // This is expected for first-time users, so don't show any logs or notifications
      // Just silently use default settings
      console.log("No configuration file found - using defaults");
    } else {
      addLogLine(`Failed to load configuration: ${result}`, "error");
      showNotification("Failed to load configuration", "error");
      console.error("Failed to load configuration:", result);
    }
  } catch (error) {
    addLogLine(`Load config error: ${error.message}`, "error");
    showNotification("Failed to load configuration", "error");
    console.error("Load config error:", error);
  }
}

// Enhanced save config function that includes UI settings
async function saveConfig() {
  await saveConfigWithUISettings();
  showNotification("Configuration saved", "success");
}

// Enhanced load config function that includes UI settings
async function loadConfig() {
  await loadConfigWithUISettings();
}

// Debug function to clear paths and help identify hard-coded path issues
async function clearPaths() {
  try {
    // Clear the input fields
    serverPathInput.value = "";
    launcherPathInput.value = "";

    // Clear the configuration file and backend paths
    const clearResult = await safeInvoke("clear_config");
    addLogLine(`Clear config result: ${clearResult}`, "info");

    addLogLine("Paths cleared for debugging", "info");
    addLogLine("Paths cleared for debugging");
    showNotification("Paths cleared for debugging", "info");
  } catch (error) {
    addLogLine(`Failed to clear paths: ${error.message}`, "error");
    addLogLine(`Failed to clear paths: ${error.message}`);
  }
}

// Status Updates
function updateServerStatus(status) {
  const statusValue = serverStatus.querySelector(".status-value");
  statusValue.textContent = status.charAt(0).toUpperCase() + status.slice(1);
  serverStatus.className = `status-item ${status}`;
}

function updateLauncherStatus(status) {
  const statusValue = launcherStatus.querySelector(".status-value");
  statusValue.textContent = status.charAt(0).toUpperCase() + status.slice(1);
  launcherStatus.className = `status-item ${status}`;
}

async function checkPortStatus() {
  try {
    const status = await safeInvoke("check_default_port_status");
    portStatus.textContent = status;
    portStatus.className =
      status === "Available" ? "info-value" : "status error";
  } catch (error) {
    portStatus.textContent = "Error";
    portStatus.className = "status error";
    console.error("Port status check error:", error);
  }
}

async function updateProcessCount() {
  try {
    let count = 0;
    if (serverRunning) count++;
    if (launcherRunning) count++;
    processCount.textContent = count.toString();
  } catch (error) {
    processCount.textContent = "0";
    console.error("Process count error:", error);
  }
}

async function updateProcessList() {
  try {
    let processInfo = [];

    if (serverRunning) {
      processInfo.push("[RUNNING] SPT-AKI Server");
    }

    if (launcherRunning) {
      processInfo.push("[RUNNING] SPT-AKI Launcher");
    }

    if (processInfo.length === 0) {
      processInfo.push(
        "[NO PROCESSES] No SPT-AKI processes are currently running"
      );
    }

    processList.innerHTML = processInfo
      .map(
        (info) =>
          `<div class="process-item"><div class="process-info"><div class="process-name">${info}</div></div></div>`
      )
      .join("");
  } catch (error) {
    processList.innerHTML =
      '<div class="process-item"><div class="process-info"><div class="process-name">Error loading processes</div></div></div>';
    console.error("Process list error:", error);
  }
}

// Log Management
function addLogLine(message, type = "info") {
  const logLine = document.createElement("div");
  logLine.className = `log-line ${type}`;
  logLine.textContent = `[${new Date().toLocaleTimeString()}] ${message}`;

  logOutput.appendChild(logLine);

  // Limit log lines
  const lines = logOutput.querySelectorAll(".log-line");
  if (lines.length > maxLogLines) {
    lines[0].remove();
  }

  // Auto-scroll to bottom
  logOutput.scrollTop = logOutput.scrollHeight;
}

function clearLog() {
  logOutput.innerHTML = "";
}

// Server Log Management
async function clearServerLog() {
  try {
    await safeInvoke("clear_server_output");
    logOutput.innerHTML = "";
    addLogLine("Server log cleared");
  } catch (error) {
    addLogLine(`Failed to clear server log: ${error.message}`);
    console.error("Clear server log error:", error);
  }
}

// Update server output
async function updateServerOutput() {
  try {
    const output = await safeInvoke("get_server_output");
    if (output && output.length > 0) {
      logOutput.innerHTML = output.join("<br>");

      // Only auto-scroll if not paused
      if (!autoScrollPaused) {
        logOutput.scrollTop = logOutput.scrollHeight;
      }
    }
  } catch (error) {
    console.error("Failed to update server output:", error);
  }
}

// Update launcher output
async function updateLauncherOutput() {
  try {
    const output = await safeInvoke("get_launcher_output");
    if (output && output.length > 0) {
      logOutput.innerHTML = output.join("<br>");

      // Only auto-scroll if not paused
      if (!autoScrollPaused) {
        logOutput.scrollTop = logOutput.scrollHeight;
      }
    }
  } catch (error) {
    console.error("Failed to update launcher output:", error);
  }
}

function startServerOutputRefresh(interval = 1000) {
  if (serverOutputInterval) {
    clearInterval(serverOutputInterval);
  }

  serverOutputInterval = setInterval(() => {
    if (serverRunning) {
      updateServerOutput();
    }
  }, interval);
}

function stopServerOutputRefresh() {
  if (serverOutputInterval) {
    clearInterval(serverOutputInterval);
    serverOutputInterval = null;
  }
}

// Launcher output refresh functions
function startLauncherOutputRefresh(interval = 1000) {
  if (launcherOutputInterval) {
    clearInterval(launcherOutputInterval);
  }

  launcherOutputInterval = setInterval(() => {
    if (launcherRunning) {
      updateLauncherOutput();
    }
  }, interval);
}

function stopLauncherOutputRefresh() {
  if (launcherOutputInterval) {
    clearInterval(launcherOutputInterval);
    launcherOutputInterval = null;
  }
}

// Auto-refresh
function startAutoRefresh(interval = 5000) {
  if (autoRefreshInterval) {
    clearInterval(autoRefreshInterval);
  }

  autoRefreshInterval = setInterval(() => {
    updateProcessCount();
    updateProcessList();
    checkPortStatus();
  }, interval);
}

function stopAutoRefresh() {
  if (autoRefreshInterval) {
    clearInterval(autoRefreshInterval);
    autoRefreshInterval = null;
  }
}

// Info refresh
async function refreshInfo() {
  await checkPortStatus();
  await updateProcessCount();
  await updateProcessList();
  showNotification("Information refreshed", "info");
}

// Notifications
function showNotification(message, type = "info") {
  // Remove existing notifications
  const existingNotifications = document.querySelectorAll(".notification");
  existingNotifications.forEach((notification) => notification.remove());

  const notification = document.createElement("div");
  notification.className = `notification notification-${type}`;
  notification.textContent = message;

  // Add styles for better notifications
  notification.style.cssText = `
    position: fixed;
    top: 50px;
    right: 20px;
    padding: 12px 20px;
    border-radius: 8px;
    color: white;
    font-weight: 500;
    z-index: 10000;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
    animation: slideIn 0.3s ease-out;
    max-width: 300px;
    word-wrap: break-word;
  `;

  // Set background color based on type
  switch (type) {
    case "success":
      notification.style.backgroundColor = "#4CAF50";
      break;
    case "error":
      notification.style.backgroundColor = "#F44336";
      break;
    case "warning":
      notification.style.backgroundColor = "#FF9800";
      break;
    default:
      notification.style.backgroundColor = "#2196F3";
  }

  // Add animation styles
  const style = document.createElement("style");
  style.textContent = `
    @keyframes slideIn {
      from {
        transform: translateX(100%);
        opacity: 0;
      }
      to {
        transform: translateX(0);
        opacity: 1;
      }
    }
  `;
  document.head.appendChild(style);

  document.body.appendChild(notification);

  // Auto-remove after 4 seconds
  setTimeout(() => {
    if (notification.parentNode) {
      notification.style.animation = "slideOut 0.3s ease-in";
      setTimeout(() => {
        if (notification.parentNode) {
          notification.parentNode.removeChild(notification);
        }
      }, 300);
    }
  }, 4000);
}

// CSS for notifications
const style = document.createElement("style");
style.textContent = `
    @keyframes slideIn {
        from {
            transform: translateX(100%);
            opacity: 0;
        }
        to {
            transform: translateX(0);
            opacity: 1;
        }
    }

    @keyframes slideOut {
        from {
            transform: translateX(0);
            opacity: 1;
        }
        to {
            transform: translateX(100%);
            opacity: 0;
        }
    }
`;
document.head.appendChild(style);

// Helper function to update button states
function updateButtonState(button, isLoading, loadingText) {
  if (isLoading) {
    button.disabled = true;
    button.textContent = loadingText;
    button.style.opacity = "0.7";
  } else {
    button.disabled = false;
    button.textContent =
      button.getAttribute("data-original-text") || button.textContent;
    button.style.opacity = "1";
  }
}

// Store original button text for loading states
function storeOriginalButtonText() {
  const buttons = [
    startServerBtn,
    stopServerBtn,
    startLauncherBtn,
    stopLauncherBtn,
    saveConfigBtn,
    loadConfigBtn,
  ];
  buttons.forEach((button) => {
    if (button) {
      button.setAttribute("data-original-text", button.textContent);
    }
  });
}

// Setup keyboard shortcuts
function setupKeyboardShortcuts() {
  document.addEventListener("keydown", (e) => {
    // Ctrl+S to save config
    if (e.ctrlKey && e.key === "s") {
      e.preventDefault();
      saveConfigWithUISettings();
      showNotification("Configuration saved", "success");
    }

    // Ctrl+L to load config
    if (e.ctrlKey && e.key === "l") {
      e.preventDefault();
      loadConfigWithUISettings();
      showNotification("Configuration loaded", "success");
    }

    // Ctrl+R to resume auto-scroll
    if (e.ctrlKey && e.key === "r") {
      e.preventDefault();
      if (autoScrollPaused) {
        resumeAutoScroll();
      }
    }

    // Ctrl+1 to start server
    if (e.ctrlKey && e.key === "1") {
      e.preventDefault();
      if (!serverRunning && !isLoading) {
        startServer();
      }
    }

    // Ctrl+2 to start launcher
    if (e.ctrlKey && e.key === "2") {
      e.preventDefault();
      if (!launcherRunning && !isLoading) {
        startLauncher();
      }
    }

    // Ctrl+Shift+1 to stop server
    if (e.ctrlKey && e.shiftKey && e.key === "1") {
      e.preventDefault();
      if (serverRunning && !isLoading) {
        stopServer();
      }
    }

    // Ctrl+Shift+2 to stop launcher
    if (e.ctrlKey && e.shiftKey && e.key === "2") {
      e.preventDefault();
      if (launcherRunning && !isLoading) {
        stopLauncher();
      }
    }
  });
}

// Setup log auto-scroll functionality
function setupLogAutoScroll() {
  const logOutput = document.getElementById("log-output");

  if (logOutput) {
    // Check if user has scrolled up (not at bottom)
    logOutput.addEventListener("scroll", () => {
      const isAtBottom =
        logOutput.scrollTop + logOutput.clientHeight >=
        logOutput.scrollHeight - 5;
      autoScrollPaused = !isAtBottom;

      // Show/hide resume scroll button
      if (autoScrollPaused) {
        resumeScrollBtn.style.display = "inline-block";
        showNotification(
          "Auto-scroll paused. Scroll to bottom or click 'Resume Auto-scroll' to resume.",
          "info"
        );
      } else {
        resumeScrollBtn.style.display = "none";
      }
    });
  }
}

// Resume auto-scroll function
function resumeAutoScroll() {
  autoScrollPaused = false;
  resumeScrollBtn.style.display = "none";

  // Scroll to bottom immediately
  const logOutput = document.getElementById("log-output");
  if (logOutput) {
    logOutput.scrollTop = logOutput.scrollHeight;
  }

  showNotification("Auto-scroll resumed", "success");
}

// Copy log to clipboard
async function copyLogToClipboard() {
  try {
    const logContent = logOutput.textContent;
    if (!logContent.trim()) {
      showNotification("No log content to copy", "warning");
      return;
    }

    await navigator.clipboard.writeText(logContent);
    showNotification("Log copied to clipboard", "success");
  } catch (error) {
    // Fallback for older browsers
    const textArea = document.createElement("textarea");
    textArea.value = logOutput.textContent;
    document.body.appendChild(textArea);
    textArea.select();
    document.execCommand("copy");
    document.body.removeChild(textArea);
    showNotification("Log copied to clipboard", "success");
  }
}


