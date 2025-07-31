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

// Helper function to safely invoke Tauri commands
function safeInvoke(command, ...args) {
  console.log(`[DEBUG] Invoking command: ${command} with args:`, args);

  // Try different ways to access invoke in Tauri v2
  if (
    window.__TAURI__ &&
    window.__TAURI__.core &&
    window.__TAURI__.core.invoke
  ) {
    console.log(`[DEBUG] Using window.__TAURI__.core.invoke`);
    return window.__TAURI__.core.invoke(command, ...args);
  } else if (window.__TAURI__ && window.__TAURI__.invoke) {
    console.log(`[DEBUG] Using window.__TAURI__.invoke`);
    return window.__TAURI__.invoke(command, ...args);
  } else if (
    window.__TAURI__ &&
    window.__TAURI__.api &&
    window.__TAURI__.api.invoke
  ) {
    console.log(`[DEBUG] Using window.__TAURI__.api.invoke`);
    return window.__TAURI__.api.invoke(command, ...args);
  } else if (typeof invoke !== "undefined") {
    console.log(`[DEBUG] Using global invoke`);
    return invoke(command, ...args);
  } else {
    console.error("[DEBUG] Tauri invoke not available");
    console.error("[DEBUG] window.__TAURI__:", window.__TAURI__);
    throw new Error("Tauri invoke not available");
  }
}

// Initialize
document.addEventListener("DOMContentLoaded", () => {
  loadConfig();
  setupEventListeners();
  setupTabs();
  setupWindowControls();
  startAutoRefresh();
  checkPortStatus();
  updateProcessCount();
});

// Event Listeners
function setupEventListeners() {
  // Browse buttons - use native Tauri file dialog
  browseServerFileBtn.addEventListener("click", async () => {
    try {
      const path = await safeInvoke("select_server_file");
      if (path && !path.startsWith("ERROR:")) {
        serverPathInput.value = path;
        console.log("Selected server file:", path);

        // Don't call the parameter-based function - just store the path in the UI
        showNotification("Server file selected successfully", "success");
      } else {
        showNotification("No file selected or selection failed", "warning");
      }
    } catch (error) {
      console.error("Failed to select server file:", error);
      showNotification("Failed to select server file", "error");
    }
  });

  browseLauncherFileBtn.addEventListener("click", async () => {
    try {
      const path = await safeInvoke("select_launcher_file");
      if (path && !path.startsWith("ERROR:")) {
        launcherPathInput.value = path;
        console.log("Selected launcher file:", path);

        // Don't call the parameter-based function - just store the path in the UI
        showNotification("Launcher file selected successfully", "success");
      } else {
        showNotification("No file selected or selection failed", "warning");
      }
    } catch (error) {
      console.error("Failed to select launcher file:", error);
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

  // Settings
  const logLevelSelect = document.getElementById("log-level");
  const maxLogLinesInput = document.getElementById("max-log-lines");
  const autoRefreshCheckbox = document.getElementById("auto-refresh");
  const refreshIntervalInput = document.getElementById("refresh-interval");

  logLevelSelect.addEventListener("change", (e) => {
    maxLogLines = parseInt(e.target.value);
  });

  maxLogLinesInput.addEventListener("change", (e) => {
    maxLogLines = parseInt(e.target.value);
  });

  autoRefreshCheckbox.addEventListener("change", (e) => {
    if (e.target.checked) {
      startAutoRefresh();
    } else {
      stopAutoRefresh();
    }
  });

  refreshIntervalInput.addEventListener("change", (e) => {
    const interval = parseInt(e.target.value);
    if (autoRefreshCheckbox.checked) {
      stopAutoRefresh();
      startAutoRefresh(interval);
    }
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
    console.log("Minimize button clicked");
    
    try {
      const result = await safeInvoke("minimize_window");
      console.log("Minimize result:", result);
      
      if (result.startsWith("ERROR:")) {
        console.error("Failed to minimize window:", result);
      }
    } catch (error) {
      console.error("Failed to minimize window:", error);
    }
  });

  closeBtn.addEventListener("click", async () => {
    console.log("Close button clicked");
    
    try {
      const result = await safeInvoke("close_window");
      console.log("Close result:", result);
      
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
  const serverPath = serverPathInput.value.trim();
  if (!serverPath) {
    showNotification("Please enter the server path", "error");
    return;
  }

  addLogLine(`Starting server...`);

  try {
    // Set the server path using the working function
    const setResult = await safeInvoke("set_server_path_d");
    addLogLine(`Server path set: ${setResult}`);

    // Launch the server
    const result = await safeInvoke("launch_server");

    // Check if result starts with "ERROR:"
    if (result && result.startsWith("ERROR:")) {
      addLogLine(`Failed to start server: ${result}`);
    } else if (result && result.startsWith("SUCCESS:")) {
      addLogLine("Server started successfully");
      updateServerStatus("running");
      serverRunning = true;
      startServerOutputRefresh();
    } else {
      addLogLine(`Server result: ${result}`);
    }
  } catch (error) {
    addLogLine(`Failed to start server: ${error.message}`);
  }
}

async function stopServer() {
  try {
    const result = await safeInvoke("stop_server");

    if (result.startsWith("SUCCESS:")) {
      serverRunning = false;
      updateServerStatus("stopped");
      showNotification("Server stopped", "info");
      addLogLine("Server stopped successfully");
      stopServerOutputRefresh();
    } else {
      addLogLine(`Failed to stop server: ${result}`);
      showNotification("Failed to stop server", "error");
    }
  } catch (error) {
    addLogLine(`Failed to stop server: ${error.message}`);
    showNotification("Failed to stop server", "error");
  }
}

// Start launcher function
async function startLauncher() {
  const launcherPath = launcherPathInput.value.trim();
  if (!launcherPath) {
    showNotification("Please enter the launcher path", "error");
    return;
  }

  addLogLine(`Starting launcher...`);

  try {
    // Set the launcher path using the working function
    const setResult = await safeInvoke("set_launcher_path_d");
    addLogLine(`Launcher path set: ${setResult}`);

    // Launch the launcher
    const result = await safeInvoke("launch_launcher");

    // Check if result starts with "ERROR:"
    if (result.startsWith("ERROR:")) {
      addLogLine(`Failed to start launcher: ${result}`);
    } else if (result.startsWith("SUCCESS:")) {
      addLogLine("Launcher started successfully");
      updateLauncherStatus("running");
      launcherRunning = true;
    } else {
      addLogLine(`Launcher result: ${result}`);
    }
  } catch (error) {
    addLogLine(`Failed to start launcher: ${error.message}`);
    console.error("Launcher start error:", error);
  }
}

async function stopLauncher() {
  try {
    const result = await safeInvoke("stop_launcher");

    if (result.startsWith("SUCCESS:")) {
      launcherRunning = false;
      updateLauncherStatus("stopped");
      showNotification("Launcher stopped", "info");
      addLogLine("Launcher stopped successfully");
    } else {
      addLogLine(`Failed to stop launcher: ${result}`);
      showNotification("Failed to stop launcher", "error");
    }
  } catch (error) {
    addLogLine(`Failed to stop launcher: ${error.message}`);
    showNotification("Failed to stop launcher", "error");
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

// Configuration - simplified for Tauri
async function saveConfig() {
  try {
    const result = await safeInvoke("save_config");
    showNotification("Configuration saved", "success");
    addLogLine("Configuration saved successfully");
  } catch (error) {
    showNotification("Failed to save configuration", "error");
    addLogLine(`Failed to save configuration: ${error.message}`);
  }
}

async function loadConfig() {
  try {
    const result = await safeInvoke("load_config");
    showNotification("Configuration loaded", "success");
    addLogLine("Configuration loaded successfully");

    // Update the UI with loaded paths
    const serverPath = await safeInvoke("get_server_path");
    const launcherPath = await safeInvoke("get_launcher_path");

    if (serverPath && !serverPath.startsWith("ERROR:")) {
      serverPathInput.value = serverPath;
    }

    if (launcherPath && !launcherPath.startsWith("ERROR:")) {
      launcherPathInput.value = launcherPath;
    }
  } catch (error) {
    showNotification("Failed to load configuration", "error");
    addLogLine(`Failed to load configuration: ${error.message}`);
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
      logOutput.scrollTop = logOutput.scrollHeight;
    }
  } catch (error) {
    console.error("Failed to update server output:", error);
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
  // Create notification element
  const notification = document.createElement("div");
  notification.className = `notification ${type}`;
  notification.textContent = message;

  // Style the notification
  notification.style.cssText = `
        position: fixed;
        top: 20px;
        right: 20px;
        padding: 1rem 1.5rem;
        border-radius: 5px;
        color: white;
        font-weight: 500;
        z-index: 1000;
        animation: slideIn 0.3s ease-out;
        max-width: 300px;
    `;

  // Set background color based on type
  switch (type) {
    case "success":
      notification.style.background =
        "linear-gradient(45deg, #00ff88, #00cc6a)";
      break;
    case "error":
      notification.style.background =
        "linear-gradient(45deg, #ff4757, #ff3742)";
      break;
    case "warning":
      notification.style.background =
        "linear-gradient(45deg, #ffa502, #ff9500)";
      break;
    default:
      notification.style.background =
        "linear-gradient(45deg, #00d4ff, #0099cc)";
  }

  document.body.appendChild(notification);

  // Remove notification after 3 seconds
  setTimeout(() => {
    notification.style.animation = "slideOut 0.3s ease-in";
    setTimeout(() => {
      if (notification.parentNode) {
        notification.parentNode.removeChild(notification);
      }
    }, 300);
  }, 3000);
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
