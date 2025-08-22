const { contextBridge, ipcRenderer } = require("electron");

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld("electronAPI", {
  selectFile: () => ipcRenderer.invoke("select-file"),
  selectFolder: () => ipcRenderer.invoke("select-folder"),
  launchProcess: (filePath, args) =>
    ipcRenderer.invoke("launch-process", filePath, args),

  // Window controls
  minimize: () => ipcRenderer.send("minimize-window"),
  maximize: () => ipcRenderer.send("maximize-window"),
  close: () => ipcRenderer.send("close-window"),

  // File system operations
  readFile: (filePath) => ipcRenderer.invoke("read-file", filePath),
  writeFile: (filePath, content) =>
    ipcRenderer.invoke("write-file", filePath, content),

  // Process management
  getRunningProcesses: () => ipcRenderer.invoke("get-running-processes"),
  getSystemProcesses: () => ipcRenderer.invoke("get-system-processes"),
  killProcess: (pid) => ipcRenderer.invoke("kill-process", pid),
  stopProcess: (pid) => ipcRenderer.invoke("stop-process", pid),

  // Process output listeners
  onProcessOutput: (callback) => ipcRenderer.on("process-output", callback),
  removeProcessOutputListener: (callback) =>
    ipcRenderer.removeListener("process-output", callback),

  // Tarkov launcher
  launchTarkov: () => ipcRenderer.invoke("launch-tarkov"),

  // SPT Launcher configuration
  getSptConfig: (sptInstallPath) =>
    ipcRenderer.invoke("get-spt-config", sptInstallPath),
  updateSptConfig: (configData, sptInstallPath) =>
    ipcRenderer.invoke("update-spt-config", configData, sptInstallPath),
  getSptConfigPath: (sptInstallPath) =>
    ipcRenderer.invoke("get-spt-config-path", sptInstallPath),

  // Auto-update system
  checkForUpdates: () => ipcRenderer.invoke("check-for-updates"),
  downloadUpdate: () => ipcRenderer.invoke("download-update"),
  installUpdate: () => ipcRenderer.invoke("install-update"),

  // Auto-update event listeners
  on: (channel, callback) => ipcRenderer.on(channel, callback),
  removeListener: (channel, callback) =>
    ipcRenderer.removeListener(channel, callback),
});
