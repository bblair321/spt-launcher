const { app, BrowserWindow, ipcMain, dialog } = require("electron");
const path = require("path");
const fs = require("fs");
const AutoUpdateManager = require("./autoUpdater");

// Keep a global reference of the window object
let mainWindow;

// Store running processes
const runningProcesses = new Map();

// Initialize auto-update manager
const autoUpdateManager = new AutoUpdateManager();

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

  // Set up auto-update manager with the main window
  autoUpdateManager.setMainWindow(mainWindow);

  // Start checking for updates
  autoUpdateManager.startUpdateCheck();

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

// IPC handlers for your SPT Launcher features
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

ipcMain.handle("launch-process", async (event, filePath, args = []) => {
  const { spawn } = require("child_process");

  console.log("Launching process:", filePath, "with args:", args);

  try {
    // Check if file exists
    if (!require("fs").existsSync(filePath)) {
      throw new Error(`File not found: ${filePath}`);
    }

    // Get the directory of the executable
    const execDir = path.dirname(filePath);

    const childProcess = spawn(filePath, args, {
      cwd: execDir, // Set working directory to executable's folder
      detached: false, // Don't detach - let it run normally
      stdio: "pipe", // Capture stdio for debugging
      shell: false, // Don't use shell
    });

    console.log("Process spawned with PID:", childProcess.pid);
    console.log("Working directory:", execDir);

    // Store process info
    const processInfo = {
      pid: childProcess.pid,
      filePath: filePath,
      startTime: new Date(),
      killed: false,
    };

    runningProcesses.set(childProcess.pid, processInfo);

    // Handle process exit
    childProcess.on("exit", (code) => {
      console.log(`Process ${childProcess.pid} exited with code:`, code);
      runningProcesses.delete(childProcess.pid);
    });

    // Handle process errors
    childProcess.on("error", (error) => {
      console.error(`Process ${childProcess.pid} error:`, error);
      runningProcesses.delete(childProcess.pid);
    });

    // Capture stdout/stderr for debugging and send to renderer
    if (childProcess.stdout) {
      childProcess.stdout.on("data", (data) => {
        const output = data.toString();
        console.log(`Process ${childProcess.pid} stdout:`, output);

        // Send to renderer process if window exists
        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send("process-output", {
            pid: childProcess.pid,
            type: "stdout",
            data: output,
          });
        }
      });
    }

    if (childProcess.stderr) {
      childProcess.stderr.on("data", (data) => {
        const output = data.toString();
        console.error(`Process ${childProcess.pid} stderr:`, output);

        // Send to renderer process if window exists
        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send("process-output", {
            pid: childProcess.pid,
            type: "stderr",
            data: output,
          });
        }
      });
    }

    // Wait a moment to ensure process starts properly
    await new Promise((resolve) => setTimeout(resolve, 100));

    // Check if process is still running
    if (childProcess.killed) {
      throw new Error("Process was killed immediately after launch");
    }

    // Return success
    return {
      code: 0,
      pid: childProcess.pid,
      killed: false,
    };
  } catch (error) {
    console.error("Failed to launch process:", error);
    throw error;
  }
});

// Window control handlers
ipcMain.on("minimize-window", () => {
  if (mainWindow) mainWindow.minimize();
});

ipcMain.on("maximize-window", () => {
  if (mainWindow) {
    if (mainWindow.isMaximized()) {
      mainWindow.unmaximize();
    } else {
      mainWindow.maximize();
    }
  }
});

ipcMain.on("close-window", () => {
  if (mainWindow) mainWindow.close();
});

// File system operations
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

// SPT Launcher configuration management
ipcMain.handle("get-spt-config", async (event, sptInstallPath = null) => {
  try {
    let configPath = null;

    // If SPT path is provided, look for config.json in the user/launcher subdirectory
    if (sptInstallPath) {
      const potentialConfigPath = path.join(
        sptInstallPath,
        "user",
        "launcher",
        "config.json"
      );
      if (fs.existsSync(potentialConfigPath)) {
        configPath = potentialConfigPath;
      }
    }

    // If no config found, try to detect SPT installation automatically
    if (!configPath) {
      const possiblePaths = [
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

      for (const basePath of possiblePaths) {
        if (fs.existsSync(basePath)) {
          const potentialConfigPath = path.join(
            basePath,
            "user",
            "launcher",
            "config.json"
          );
          if (fs.existsSync(potentialConfigPath)) {
            configPath = potentialConfigPath;
            break;
          }
        }
      }
    }

    if (!configPath) {
      return {
        success: false,
        error:
          "SPT config.json not found. Please ensure SPT-AKI is properly installed.",
      };
    }

    const content = fs.readFileSync(configPath, "utf8");
    const config = JSON.parse(content);

    // Extract Fika-relevant settings from the SPT config
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

      let configPath = null;

      // If SPT path is provided, use that directory
      if (sptInstallPath) {
        // First, try to find the actual SPT launcher config location
        let actualConfigPath = null;

        // Common SPT launcher config locations
        const possibleConfigPaths = [
          // Primary location: SPT installation\user\launcher\config.json
          path.join(sptInstallPath, "user", "launcher", "config.json"),
          // Alternative: SPT installation directory
          path.join(sptInstallPath, "config.json"),
        ];

        console.log("🔍 Backend Debug: Checking possible config locations:");
        for (const potentialPath of possibleConfigPaths) {
          console.log("  - Checking:", potentialPath);
          if (fs.existsSync(potentialPath)) {
            actualConfigPath = potentialPath;
            console.log(
              "✅ Backend Debug: Found existing config at:",
              actualConfigPath
            );
            break;
          }
        }

        if (actualConfigPath) {
          // Use the existing config location
          configPath = actualConfigPath;
          console.log(
            "🔍 Backend Debug: Using existing config at:",
            configPath
          );
        } else {
          // Create new config in SPT installation directory
          configPath = path.join(sptInstallPath, "config.json");
          console.log("🔍 Backend Debug: Creating new config at:", configPath);

          // Check if the directory exists
          if (!fs.existsSync(sptInstallPath)) {
            console.log(
              "❌ Backend Debug: SPT directory does not exist:",
              sptInstallPath
            );
            return {
              success: false,
              error: `SPT directory does not exist: ${sptInstallPath}`,
            };
          }

          // List directory contents for debugging
          try {
            const dirContents = fs.readdirSync(sptInstallPath);
            console.log("🔍 Backend Debug: Directory contents:", dirContents);
          } catch (readError) {
            console.log(
              "🔍 Backend Debug: Could not read directory contents:",
              readError.message
            );
          }

          // Create the config file if it doesn't exist
          if (!fs.existsSync(configPath)) {
            try {
              // Create empty config file
              fs.writeFileSync(configPath, "{}", "utf8");
              console.log("✅ Backend Debug: Successfully created config.json");
            } catch (writeError) {
              console.error(
                "❌ Backend Debug: Failed to create config.json:",
                writeError
              );
              return {
                success: false,
                error: `Failed to create config.json: ${writeError.message}`,
              };
            }
          } else {
            console.log("✅ Backend Debug: config.json already exists");
          }
        }
      } else {
        console.log(
          "🔍 Backend Debug: No SPT path provided, trying auto-detection"
        );
        // Try to detect SPT installation automatically
        const possiblePaths = [
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

        for (const basePath of possiblePaths) {
          if (fs.existsSync(basePath)) {
            const potentialConfigPath = path.join(basePath, "config.json");
            if (fs.existsSync(potentialConfigPath)) {
              configPath = potentialConfigPath;
              console.log(
                "🔍 Backend Debug: Found config via auto-detection:",
                configPath
              );
              break;
            }
          }
        }
      }

      if (!configPath) {
        console.log("❌ Backend Debug: No config path found");
        return {
          success: false,
          error:
            "SPT config.json not found. Please ensure SPT-AKI is properly installed.",
        };
      }

      console.log("🔍 Backend Debug: Final config path:", configPath);

      // Read existing config first
      let existingConfig = {};
      if (fs.existsSync(configPath)) {
        const content = fs.readFileSync(configPath, "utf8");
        existingConfig = JSON.parse(content);
        console.log("🔍 Backend Debug: Existing config:", existingConfig);
      }

      // Merge with new config data and handle SPT launcher specific structure
      const updatedConfig = { ...existingConfig };

      // Set required SPT launcher fields for Fika to work
      if (configData.enableFika) {
        // Enable dev mode (required for custom server settings)
        updatedConfig.IsDevMode = true;

        // Update server configuration
        if (!updatedConfig.Server) {
          updatedConfig.Server = {};
        }

        // Set the server URL to the Fika server
        updatedConfig.Server.Url = `https://${configData.serverAddress}:${configData.serverPort}`;

        console.log(
          "🔍 Backend Debug: Fika mode enabled - setting dev mode and server URL"
        );
      } else {
        // If Fika is disabled, revert to default SPT settings
        updatedConfig.IsDevMode = false;
        if (updatedConfig.Server) {
          updatedConfig.Server.Url = "https://127.0.0.1:6969";
          updatedConfig.Server.Name = "SPT";
        }
        console.log(
          "🔍 Backend Debug: Fika mode disabled - reverting to default SPT settings"
        );
      }

      console.log("🔍 Backend Debug: Updated config:", updatedConfig);

      // Write updated config
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
    let configPath = null;

    // If SPT path is provided, use that directory
    if (sptInstallPath) {
      configPath = path.join(sptInstallPath, "config.json");
    } else {
      // Try to detect SPT installation automatically
      const possiblePaths = [
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

      for (const basePath of possiblePaths) {
        if (fs.existsSync(basePath)) {
          const potentialConfigPath = path.join(basePath, "config.json");
          if (fs.existsSync(potentialConfigPath)) {
            configPath = potentialConfigPath;
            break;
          }
        }
      }
    }

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

// Process management - Get system processes (for reference)
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
        .slice(1) // Skip header
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
      if (error) {
        reject(error);
        return;
      }
      resolve(true);
    });
  });
});

// Get running processes
ipcMain.handle("get-running-processes", async () => {
  return Array.from(runningProcesses.values());
});

// Stop a specific process
ipcMain.handle("stop-process", async (event, pid) => {
  try {
    const { exec } = require("child_process");
    await new Promise((resolve, reject) => {
      exec(`taskkill /PID ${pid} /F`, (error) => {
        if (error) {
          reject(error);
          return;
        }
        resolve(true);
      });
    });

    // Remove from running processes
    runningProcesses.delete(pid);
    return { success: true };
  } catch (error) {
    console.error("Failed to stop process:", error);
    throw error;
  }
});

// Launch Tarkov with SPT-AKI client
ipcMain.handle("launch-tarkov", async (event, userSptPath = null) => {
  try {
    const { spawn } = require("child_process");

    console.log("🔍 Debug: Received userSptPath:", userSptPath);
    console.log("🔍 Debug: userSptPath type:", typeof userSptPath);
    console.log(
      "🔍 Debug: userSptPath length:",
      userSptPath ? userSptPath.length : 0
    );

    let sptPath = null;

    // First, try to use the user-specified path if provided
    if (userSptPath) {
      if (fs.existsSync(userSptPath)) {
        sptPath = userSptPath;
        console.log("✅ Using user-specified SPT path:", sptPath);
      } else {
        // Try to find the launcher in the same directory with different names
        const userDir = path.dirname(userSptPath);
        const possibleLauncherNames = [
          "Aki.Launcher.exe",
          "SPT.Launcher.exe",
          "Launcher.exe",
          "SPT.exe",
        ];

        for (const launcherName of possibleLauncherNames) {
          const potentialPath = path.join(userDir, launcherName);
          if (fs.existsSync(potentialPath)) {
            sptPath = potentialPath;
            console.log("✅ Found launcher with different name:", sptPath);
            break;
          }
        }

        if (!sptPath) {
          console.log("❌ User path exists but file not found:", userSptPath);
          console.log(
            "❌ File exists check result:",
            fs.existsSync(userSptPath)
          );
        }
      }
    }

    if (!sptPath) {
      // Fall back to automatic detection
      console.log(
        "⚠️ User path not found or invalid, trying automatic detection..."
      );

      // Common SPT-AKI installation paths with multiple launcher names
      const possibleLauncherNames = [
        "Aki.Launcher.exe",
        "SPT.Launcher.exe",
        "Launcher.exe",
        "SPT.exe",
      ];

      const possiblePaths = [
        // SPT-AKI launcher paths
        "C:\\SPT",
        "D:\\SPT",
        "E:\\SPT",
        "F:\\SPT",
        // Alternative SPT folder names
        "C:\\SPT-AKI",
        "D:\\SPT-AKI",
        "E:\\SPT-AKI",
        "F:\\SPT-AKI",
        // User Documents folder
        path.join(process.env.USERPROFILE || "", "Documents", "SPT"),
        path.join(process.env.USERPROFILE || "", "Documents", "SPT-AKI"),
        // Desktop folder
        path.join(process.env.USERPROFILE || "", "Desktop", "SPT"),
        path.join(process.env.USERPROFILE || "", "Desktop", "SPT-AKI"),
      ];

      // Check each directory for any of the possible launcher names
      for (const basePath of possiblePaths) {
        if (fs.existsSync(basePath)) {
          for (const launcherName of possibleLauncherNames) {
            const potentialPath = path.join(basePath, launcherName);
            if (fs.existsSync(potentialPath)) {
              sptPath = potentialPath;
              console.log("✅ Found launcher in automatic detection:", sptPath);
              break;
            }
          }
          if (sptPath) break;
        }
      }
    }

    if (!sptPath) {
      return {
        success: false,
        error:
          "SPT-AKI launcher not found. Please ensure SPT-AKI is properly installed with Aki.Launcher.exe",
      };
    }

    console.log("Launching SPT-AKI from:", sptPath);

    // Launch SPT-AKI launcher (which will then launch Tarkov with mods)
    const sptProcess = spawn(sptPath, [], {
      detached: true,
      stdio: "ignore",
      cwd: path.dirname(sptPath), // Set working directory to SPT folder
    });

    // Don't wait for the process to exit
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
