const { app, BrowserWindow, ipcMain, dialog } = require("electron");
const path = require("path");
const fs = require("fs");
const { autoUpdater } = require("electron-updater");

// Keep a global reference of the window object
let mainWindow;

// Store running processes
const runningProcesses = new Map();

// Configure auto-updater
autoUpdater.logger = require("electron-log");
autoUpdater.logger.transports.file.level = "info";

// Set the feed URL for updates
autoUpdater.setFeedURL({
  provider: "github",
  owner: "bblair321",
  repo: "spt-launcher",
  private: false,
  releaseType: "release",
});

// Common SPT installation paths
const SPT_POSSIBLE_PATHS = [
  "C:\\SPT",
  "D:\\SPT",
  "E:\\SPT",
  "F:\\SPT",
  "C:\\SPT-AKI",
  "D:\\SPT-AKI",
  "E:\\SPT-AKI",
  "F:\\SPT-AKI",
  path.join(process.env.USERPROFILE || "", "Documents", "SPT"),
  path.join(process.env.USERPROFILE || "", "Documents", "SPT-AKI"),
  path.join(process.env.USERPROFILE || "", "Desktop", "SPT"),
  path.join(process.env.USERPROFILE || "", "Desktop", "SPT-AKI"),
];

// Common SPT launcher names
const SPT_LAUNCHER_NAMES = [
  "Aki.Launcher.exe",
  "SPT.Launcher.exe",
  "Launcher.exe",
  "SPT.exe",
];

function createWindow() {
  // Create the browser window
  mainWindow = new BrowserWindow({
    width: 1200,
    height: 800,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      enableRemoteModule: false,
      preload: path.join(__dirname, "preload.js"),
    },
    icon: path.join(__dirname, "../public/icon.ico"),
    titleBarStyle: "hidden",
    frame: false,
    resizable: true,
  });

  // Load the app
  if (process.env.IS_DEV) {
    console.log("Loading in development mode from http://localhost:5173");
    mainWindow.loadURL("http://localhost:5173");
    mainWindow.webContents.openDevTools();
  } else {
    const distPath = path.join(__dirname, "../dist/index.html");
    console.log("Loading in production mode from:", distPath);
    if (fs.existsSync(distPath)) {
      mainWindow.loadFile(distPath);
    } else {
      console.error("Dist file not found:", distPath);
      mainWindow.loadURL("http://localhost:5173");
    }
  }

  // Emitted when the window is closed
  mainWindow.on("closed", () => {
    mainWindow = null;
  });

  // Set up auto-updater events
  setupAutoUpdater();

  // Handle window errors
  mainWindow.webContents.on(
    "did-fail-load",
    (event, errorCode, errorDescription) => {
      console.error("Failed to load:", errorCode, errorDescription);
      if (process.env.IS_DEV) {
        mainWindow.loadURL("http://localhost:5173");
      }
    }
  );
}

function setupAutoUpdater() {
  // Check for updates when app starts (only in production)
  if (!process.env.IS_DEV) {
    console.log("Checking for updates...");
    autoUpdater.checkForUpdates();
    
    // Set up periodic background checks (every 30 minutes)
    setInterval(() => {
      if (mainWindow && !mainWindow.isDestroyed()) {
        console.log("Background update check...");
        autoUpdater.checkForUpdates();
      }
    }, 30 * 60 * 1000); // 30 minutes
  }

  // Auto-updater events
  const events = [
    { name: "checking-for-update", message: "Checking for updates..." },
    {
      name: "update-available",
      data: (info) => ({
        version: info.version,
        releaseNotes: info.releaseNotes || "No release notes available",
      }),
    },
    { name: "update-not-available", data: () => ({ status: "no-update" }) },
    { name: "error", data: (err) => ({ message: err.message }) },
    {
      name: "download-progress",
      data: (progressObj) => ({
        percent: progressObj.percent,
        speed: progressObj.bytesPerSecond,
        downloaded: progressObj.transferred,
        total: progressObj.total,
      }),
    },
    {
      name: "update-downloaded",
      data: (info) => ({
        version: info.version,
        releaseNotes: info.releaseNotes,
      }),
    },
  ];

  events.forEach(({ name, message, data }) => {
    autoUpdater.on(name, (info) => {
      if (message) console.log(message);
      if (mainWindow && !mainWindow.isDestroyed()) {
        const channel =
          name === "checking-for-update"
            ? "update-status"
            : name === "update-not-available"
            ? "update-status"
            : name === "error"
            ? "update-error"
            : name === "download-progress"
            ? "update-download-progress"
            : name === "update-downloaded"
            ? "update-downloaded"
            : name === "update-available"
            ? "update-available"
            : name;

        mainWindow.webContents.send(
          channel,
          data ? data(info) : { status: name }
        );
      }
    });
  });
}

// Utility function to find SPT installation
function findSptInstallation(userPath = null) {
  if (userPath && fs.existsSync(userPath)) {
    return userPath;
  }

  // Try to find launcher in possible paths
  for (const basePath of SPT_POSSIBLE_PATHS) {
    if (fs.existsSync(basePath)) {
      for (const launcherName of SPT_LAUNCHER_NAMES) {
        const potentialPath = path.join(basePath, launcherName);
        if (fs.existsSync(potentialPath)) {
          return potentialPath;
        }
      }
    }
  }
  return null;
}

// Utility function to find SPT config path
function findSptConfigPath(sptInstallPath = null) {
  if (sptInstallPath) {
    const potentialPaths = [
      path.join(sptInstallPath, "user", "launcher", "config.json"),
      path.join(sptInstallPath, "config.json"),
    ];

    for (const configPath of potentialPaths) {
      if (fs.existsSync(configPath)) {
        return configPath;
      }
    }
  }

  // Auto-detection
  for (const basePath of SPT_POSSIBLE_PATHS) {
    if (fs.existsSync(basePath)) {
      const configPath = path.join(basePath, "config.json");
      if (fs.existsSync(configPath)) {
        return configPath;
      }
    }
  }
  return null;
}

// This method will be called when Electron has finished initialization
app.whenReady().then(createWindow);

// Quit when all windows are closed
app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});

app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
  }
});

// ===== IPC HANDLERS =====

// Update management
ipcMain.handle("get-app-version", () => app.getVersion());

ipcMain.handle("check-for-updates", async () => {
  try {
    console.log("Manual update check requested");
    const result = await autoUpdater.checkForUpdates();
    return { success: true, result };
  } catch (error) {
    console.error("Update check failed:", error);
    return { success: false, error: error.message };
  }
});

ipcMain.handle("download-update", async () => {
  try {
    console.log("Download update requested");
    const result = await autoUpdater.downloadUpdate();
    return { success: true, result };
  } catch (error) {
    console.error("Download failed:", error);
    return { success: false, error: error.message };
  }
});

ipcMain.handle("install-update", async () => {
  try {
    console.log("Install update requested");
    autoUpdater.quitAndInstall();
    return { success: true };
  } catch (error) {
    console.error("Install failed:", error);
    return { success: false, error: error.message };
  }
});

// Window control
ipcMain.handle("minimize-window", () => {
  if (mainWindow) mainWindow.minimize();
  return { success: true };
});

ipcMain.handle("maximize-window", () => {
  if (mainWindow) {
    if (mainWindow.isMaximized()) {
      mainWindow.unmaximize();
    } else {
      mainWindow.maximize();
    }
  }
  return { success: true };
});

ipcMain.handle("close-window", () => {
  if (mainWindow) mainWindow.close();
  return { success: true };
});

// App restart
ipcMain.handle("restart-app", async () => {
  try {
    console.log("=== MAIN: Restart app requested ===");
    app.relaunch();
    app.exit(0);
    return { success: true, message: "App restarting..." };
  } catch (error) {
    console.error("=== MAIN: Restart app error ===", error);
    return { success: false, error: error.message };
  }
});

// File operations
ipcMain.handle("select-file", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ["openFile"],
    filters: [
      { name: "Executables", extensions: ["exe"] },
      { name: "All Files", extensions: ["*"] },
    ],
  });
  return result.filePaths[0];
});

ipcMain.handle("select-folder", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    properties: ["openDirectory"],
  });
  return result.filePaths[0];
});

ipcMain.handle("read-file", async (event, filePath) => {
  try {
    const content = fs.readFileSync(filePath, "utf8");
    return content;
  } catch (error) {
    throw new Error(`Failed to read file: ${error.message}`);
  }
});

ipcMain.handle("write-file", async (event, filePath, content) => {
  try {
    fs.writeFileSync(filePath, content, "utf8");
    return true;
  } catch (error) {
    throw new Error(`Failed to write file: ${error.message}`);
  }
});

// Process management
ipcMain.handle("launch-process", async (event, filePath, args = []) => {
  const { spawn } = require("child_process");

  console.log("Launching process:", filePath, "with args:", args);

  try {
    if (!fs.existsSync(filePath)) {
      throw new Error(`File not found: ${filePath}`);
    }

    const execDir = path.dirname(filePath);
    const childProcess = spawn(filePath, args, {
      cwd: execDir,
      detached: false,
      stdio: "pipe",
      shell: false,
    });

    console.log("Process spawned with PID:", childProcess.pid);

    const processInfo = {
      pid: childProcess.pid,
      filePath: filePath,
      startTime: new Date(),
      killed: false,
    };

    runningProcesses.set(childProcess.pid, processInfo);

    // Handle process lifecycle
    childProcess.on("exit", (code) => {
      console.log(`Process ${childProcess.pid} exited with code:`, code);
      runningProcesses.delete(childProcess.pid);
    });

    childProcess.on("error", (error) => {
      console.error(`Process ${childProcess.pid} error:`, error);
      runningProcesses.delete(childProcess.pid);
    });

    // Capture output
    [childProcess.stdout, childProcess.stderr].forEach((stream, index) => {
      if (stream) {
        stream.on("data", (data) => {
          const output = data.toString();
          const type = index === 0 ? "stdout" : "stderr";

          if (index === 0) {
            console.log(`Process ${childProcess.pid} ${type}:`, output);
          } else {
            console.error(`Process ${childProcess.pid} ${type}:`, output);
          }

          if (mainWindow && !mainWindow.isDestroyed()) {
            mainWindow.webContents.send("process-output", {
              pid: childProcess.pid,
              type,
              data: output,
            });
          }
        });
      }
    });

    await new Promise((resolve) => setTimeout(resolve, 100));

    if (childProcess.killed) {
      throw new Error("Process was killed immediately after launch");
    }

    return { code: 0, pid: childProcess.pid, killed: false };
  } catch (error) {
    console.error("Failed to launch process:", error);
    throw error;
  }
});

ipcMain.handle("get-running-processes", async () => {
  return Array.from(runningProcesses.values());
});

ipcMain.handle("stop-process", async (event, pid) => {
  try {
    const { exec } = require("child_process");
    await new Promise((resolve, reject) => {
      exec(`taskkill /PID ${pid} /F`, (error) => {
        if (error) reject(error);
        else resolve(true);
      });
    });

    runningProcesses.delete(pid);
    return { success: true };
  } catch (error) {
    console.error("Failed to stop process:", error);
    throw error;
  }
});

ipcMain.handle("get-system-processes", async () => {
  const { exec } = require("child_process");
  return new Promise((resolve, reject) => {
    exec("tasklist /FO CSV", (error, stdout) => {
      if (error) {
        reject(error);
        return;
      }
      const processes = stdout
        .split("\n")
        .slice(1)
        .filter((line) => line.trim())
        .map((line) => {
          const [name, pid] = line.split(",");
          return { name: name.replace(/"/g, ""), pid: pid.replace(/"/g, "") };
        });
      resolve(processes);
    });
  });
});

ipcMain.handle("kill-process", async (event, pid) => {
  const { exec } = require("child_process");
  return new Promise((resolve, reject) => {
    exec(`taskkill /PID ${pid} /F`, (error) => {
      if (error) reject(error);
      else resolve(true);
    });
  });
});

// SPT Launcher specific handlers
ipcMain.handle("get-spt-config", async (event, sptInstallPath = null) => {
  try {
    const configPath = findSptConfigPath(sptInstallPath);

    if (!configPath) {
      return {
        success: false,
        error:
          "SPT config.json not found. Please ensure SPT-AKI is properly installed.",
      };
    }

    const content = fs.readFileSync(configPath, "utf8");
    const config = JSON.parse(content);

    const fikaConfig = {
      enableFika: config.IsDevMode === true,
      serverAddress: config.Server?.Url
        ? config.Server.Url.replace(/^https?:\/\//, "").split(":")[0]
        : "",
      serverPort: config.Server?.Url
        ? config.Server.Url.split(":")[1]?.split("/")[0]
        : "6969",
    };

    return { success: true, config: fikaConfig, configPath };
  } catch (error) {
    return {
      success: false,
      error: `Failed to read SPT config: ${error.message}`,
    };
  }
});

ipcMain.handle(
  "update-spt-config",
  async (event, configData, sptInstallPath = null) => {
    try {
      console.log("🔍 Backend Debug: Received configData:", configData);
      console.log("🔍 Backend Debug: Received sptInstallPath:", sptInstallPath);

      let configPath = findSptConfigPath(sptInstallPath);

      if (!configPath) {
        // Create new config if none exists
        if (sptInstallPath && fs.existsSync(sptInstallPath)) {
          configPath = path.join(sptInstallPath, "config.json");
          if (!fs.existsSync(configPath)) {
            fs.writeFileSync(configPath, "{}", "utf8");
            console.log("✅ Backend Debug: Successfully created config.json");
          }
        } else {
          return {
            success: false,
            error:
              "SPT config.json not found. Please ensure SPT-AKI is properly installed.",
          };
        }
      }

      console.log("🔍 Backend Debug: Final config path:", configPath);

      // Read existing config
      let existingConfig = {};
      if (fs.existsSync(configPath)) {
        const content = fs.readFileSync(configPath, "utf8");
        existingConfig = JSON.parse(content);
        console.log("🔍 Backend Debug: Existing config:", existingConfig);
      }

      // Update config
      const updatedConfig = { ...existingConfig };

      if (configData.enableFika) {
        updatedConfig.IsDevMode = true;
        if (!updatedConfig.Server) updatedConfig.Server = {};
        updatedConfig.Server.Url = `https://${configData.serverAddress}:${configData.serverPort}`;
        console.log("🔍 Backend Debug: Fika mode enabled");
      } else {
        updatedConfig.IsDevMode = false;
        if (updatedConfig.Server) {
          updatedConfig.Server.Url = "https://127.0.0.1:6969";
          updatedConfig.Server.Name = "SPT";
        }
        console.log("🔍 Backend Debug: Fika mode disabled");
      }

      console.log("🔍 Backend Debug: Updated config:", updatedConfig);

      fs.writeFileSync(
        configPath,
        JSON.stringify(updatedConfig, null, 2),
        "utf8"
      );
      console.log("✅ Backend Debug: Config saved successfully");

      return { success: true, configPath };
    } catch (error) {
      console.error("❌ Backend Debug: Exception occurred:", error);
      return {
        success: false,
        error: `Failed to update SPT config: ${error.message}`,
      };
    }
  }
);

ipcMain.handle("get-spt-config-path", async (event, sptInstallPath = null) => {
  try {
    const configPath = findSptConfigPath(sptInstallPath);

    if (!configPath) {
      return { success: false, error: "SPT config.json not found" };
    }

    return {
      success: true,
      path: configPath,
      exists: fs.existsSync(configPath),
    };
  } catch (error) {
    return {
      success: false,
      error: `Failed to get config path: ${error.message}`,
    };
  }
});

ipcMain.handle("launch-tarkov", async (event, userSptPath = null) => {
  try {
    const { spawn } = require("child_process");

    console.log("🔍 Debug: Received userSptPath:", userSptPath);

    const sptPath = findSptInstallation(userSptPath);

    if (!sptPath) {
      return {
        success: false,
        error:
          "SPT-AKI launcher not found. Please ensure SPT-AKI is properly installed with Aki.Launcher.exe",
      };
    }

    console.log("Launching SPT-AKI from:", sptPath);

    const sptProcess = spawn(sptPath, [], {
      detached: true,
      stdio: "ignore",
      cwd: path.dirname(sptPath),
    });

    sptProcess.unref();

    return { success: true, pid: sptProcess.pid };
  } catch (error) {
    console.error("Failed to launch SPT-AKI:", error);
    return {
      success: false,
      error: `Failed to launch SPT-AKI: ${error.message}`,
    };
  }
});
