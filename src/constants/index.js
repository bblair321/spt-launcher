// Status constants
export const STATUS = {
  IDLE: "idle",
  SUCCESS: "success",
  ERROR: "error",
  LAUNCHING: "launching",
  STOPPED: "stopped",
  RESTARTING: "restarting",
  SAVING: "saving",
};

// Default configuration values
export const DEFAULTS = {
  SERVER_PORT: "6969",
  LOCAL_SERVER: "127.0.0.1",
  PROCESS_CHECK_INTERVAL: 2000,
  CONFIG_SAVE_DELAY: 2000,
  RESTART_DELAY: 1000,
};

// File filters for dialogs
export const FILE_FILTERS = {
  EXECUTABLES: [
    { name: "Executables", extensions: ["exe"] },
    { name: "All Files", extensions: ["*"] },
  ],
  ALL_FILES: [{ name: "All Files", extensions: ["*"] }],
};

// SPT Launcher executable names
export const SPT_EXECUTABLES = [
  "Aki.Launcher.exe",
  "SPT.Launcher.exe",
  "Launcher.exe",
  "SPT.exe",
];

// Common SPT installation paths
export const COMMON_SPT_PATHS = [
  "C:\\SPT",
  "D:\\SPT",
  "C:\\Games\\SPT",
  "D:\\Games\\SPT",
];

// Tab configuration
export const TABS = [
  { id: "launcher", name: "Launcher", icon: "Play" },
  { id: "servers", name: "Servers", icon: "Server" },
  { id: "settings", name: "Settings", icon: "Settings" },
  { id: "devtools", name: "Tools", icon: "Wrench" },
];
