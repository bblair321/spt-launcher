import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from "react";

const LauncherContext = createContext();

export function LauncherProvider({ children }) {
  // Launcher state that persists across tab switches
  const [launcherPath, setLauncherPath] = useState(() => {
    try {
      return localStorage.getItem("launcherPath") || "";
    } catch {
      return "";
    }
  });
  const [isLauncherRunning, setIsLauncherRunning] = useState(false);
  const [launcherProcess, setLauncherProcess] = useState(null);
  const [status, setStatus] = useState("idle");

  // Persist launcher path to localStorage when it changes
  useEffect(() => {
    if (launcherPath) {
      localStorage.setItem("launcherPath", launcherPath);
    }
  }, [launcherPath]);

  // Fika configuration state - load from localStorage if available
  const [fikaConfig, setFikaConfig] = useState(() => {
    try {
      const stored = localStorage.getItem("fikaConfig");
      return stored
        ? JSON.parse(stored)
        : {
            serverAddress: "",
            serverPort: "6969",
            enableFika: false,
          };
    } catch {
      return {
        serverAddress: "",
        serverPort: "6969",
        enableFika: false,
      };
    }
  });
  const [configStatus, setConfigStatus] = useState("idle");
  const [showFikaSettings, setShowFikaSettings] = useState(false);
  const [configPath, setConfigPath] = useState("");

  // Persist Fika config to localStorage when it changes
  useEffect(() => {
    localStorage.setItem("fikaConfig", JSON.stringify(fikaConfig));
  }, [fikaConfig]);

  // Process monitoring callback
  const handleProcessStop = useCallback(() => {
    setIsLauncherRunning(false);
    setLauncherProcess(null);
    setStatus("stopped");
  }, []);

  const value = {
    // Launcher state
    launcherPath,
    setLauncherPath,
    isLauncherRunning,
    setIsLauncherRunning,
    launcherProcess,
    setLauncherProcess,
    status,
    setStatus,

    // Fika configuration
    fikaConfig,
    setFikaConfig,
    configStatus,
    setConfigStatus,
    showFikaSettings,
    setShowFikaSettings,
    configPath,
    setConfigPath,

    // Process monitoring
    handleProcessStop,
  };

  return (
    <LauncherContext.Provider value={value}>
      {children}
    </LauncherContext.Provider>
  );
}

export function useLauncher() {
  const context = useContext(LauncherContext);
  if (!context) {
    throw new Error("useLauncher must be used within a LauncherProvider");
  }
  return context;
}
