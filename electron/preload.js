const { contextBridge, ipcRenderer } = require("electron");

// Expose protected methods that allow the renderer process to use
// the ipcRenderer without exposing the entire object
contextBridge.exposeInMainWorld("electronAPI", {
  // Update management
  checkForUpdates: () => ipcRenderer.invoke("check-for-updates"),
  downloadUpdate: () => ipcRenderer.invoke("download-update"),
  installUpdate: () => ipcRenderer.invoke("install-update"),

  // App info
  getAppVersion: () => ipcRenderer.invoke("get-app-version"),

  // Window control
  minimize: () => ipcRenderer.invoke("minimize-window"),
  maximize: () => ipcRenderer.invoke("maximize-window"),
  close: () => ipcRenderer.invoke("close-window"),

  // File operations
  selectFile: () => ipcRenderer.invoke("select-file"),
  selectFolder: () => ipcRenderer.invoke("select-folder"),
  launchProcess: (filePath, args) =>
    ipcRenderer.invoke("launch-process", filePath, args),
  launchTarkov: (sptPath) => ipcRenderer.invoke("launch-tarkov", sptPath),
  stopProcess: (pid) => ipcRenderer.invoke("stop-process", pid),
  getSptConfig: (sptDir) => ipcRenderer.invoke("get-spt-config", sptDir),
  updateSptConfig: (configData, sptDir) =>
    ipcRenderer.invoke("update-spt-config", configData, sptDir),
  getRunningProcesses: () => ipcRenderer.invoke("get-running-processes"),

  // Update events
  onUpdateStatus: (callback) => ipcRenderer.on("update-status", callback),
  onUpdateAvailable: (callback) => ipcRenderer.on("update-available", callback),
  onUpdateError: (callback) => ipcRenderer.on("update-error", callback),
  onUpdateDownloadProgress: (callback) =>
    ipcRenderer.on("update-download-progress", callback),
  onUpdateDownloaded: (callback) =>
    ipcRenderer.on("update-downloaded", callback),

  // Process management
  onProcessOutput: (callback) => ipcRenderer.on("process-output", callback),
  removeProcessOutputListener: (callback) =>
    ipcRenderer.removeListener("process-output", callback),

  // SPT Log reading
  readSptLogs: (sptPath, maxLines) =>
    ipcRenderer.invoke("readSptLogs", sptPath, maxLines),
  scanSptLogDirectory: (sptPath) =>
    ipcRenderer.invoke("scanSptLogDirectory", sptPath),

  // Remove listeners
  removeAllListeners: (channel) => ipcRenderer.removeAllListeners(channel),
});
