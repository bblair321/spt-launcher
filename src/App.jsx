import React, {
  useState,
  useMemo,
  useCallback,
  memo,
  useEffect,
  useRef,
} from "react";
import {
  Play,
  Server,
  Settings,
  Wrench,
  X,
  Minimize,
  Square,
  Maximize,
} from "lucide-react";
import { useTheme } from "./contexts/ThemeContext";
import { ToastProvider } from "./contexts/ToastContext";
import { ConsoleProvider } from "./contexts/ConsoleContext";

// Constants
import { TABS } from "./constants";

// Tab Components
import LauncherTab from "./components/LauncherTab";
import ServersTab from "./components/ServersTab";
import SettingsTab from "./components/SettingsTab";
import DevToolsTab from "./components/DevToolsTab";

// UI Components
import ToastContainer from "./components/ui/ToastContainer";
import ErrorBoundary from "./components/ErrorBoundary";

// Utilities
import { isElectronFunctionAvailable } from "./utils/electronUtils";

// Icon mapping - memoized to prevent recreation
const ICON_MAP = {
  Play,
  Server,
  Settings,
  Wrench,
};

// Memoized tab components to prevent unnecessary re-renders
const MemoizedLauncherTab = memo(LauncherTab);
const MemoizedServersTab = memo(ServersTab);
const MemoizedSettingsTab = memo(SettingsTab);
const MemoizedDevToolsTab = memo(DevToolsTab);

function App() {
  const { theme, resolvedTheme } = useTheme();
  const [activeTab, setActiveTab] = useState("launcher");
  const [isMaximized, setIsMaximized] = useState(false);

  // Global process output handling - persists across tab switches
  const processOutputRef = useRef(null);

  useEffect(() => {
    if (!isElectronFunctionAvailable("onProcessOutput")) {
      return;
    }

    const handleProcessOutput = (event, data) => {
      // Store the latest process output data globally
      processOutputRef.current = data;

      // Dispatch a custom event that any component can listen to
      const customEvent = new CustomEvent("spt-process-output", {
        detail: {
          ...data,
          timestamp: new Date().toLocaleTimeString(),
          message: data.data,
          outputType: data.type === "stderr" ? "error" : "info",
        },
      });
      window.dispatchEvent(customEvent);
    };

    try {
      // Register the global process output listener
      window.electronAPI.onProcessOutput(handleProcessOutput);
      console.log("Global process output listener registered in App");
    } catch (error) {
      console.warn("Failed to register global process output listener:", error);
    }

    return () => {
      if (isElectronFunctionAvailable("removeProcessOutputListener")) {
        try {
          window.electronAPI.removeProcessOutputListener(handleProcessOutput);
          console.log("Global process output listener removed from App");
        } catch (error) {
          console.warn(
            "Failed to remove global process output listener:",
            error
          );
        }
      }
    };
  }, []); // Only run once on mount

  // Memoized tab configuration with components
  const tabConfig = useMemo(
    () => [
      { ...TABS[0], component: MemoizedLauncherTab },
      { ...TABS[1], component: MemoizedServersTab },
      { ...TABS[2], component: MemoizedSettingsTab },
      { ...TABS[3], component: MemoizedDevToolsTab },
    ],
    []
  );

  // Memoized window control handler
  const handleWindowControl = useCallback((action) => {
    if (!window.electronAPI) return;

    switch (action) {
      case "minimize":
        window.electronAPI.minimize();
        break;
      case "maximize":
        window.electronAPI.maximize();
        setIsMaximized((prev) => !prev);
        break;
      case "close":
        window.electronAPI.close();
        break;
      default:
        break;
    }
  }, []);

  // Memoized tab change handler
  const handleTabChange = useCallback((tabId) => {
    setActiveTab(tabId);
  }, []);

  // Memoized active component
  const ActiveComponent = useMemo(() => {
    const tab = tabConfig.find((tab) => tab.id === activeTab);
    return tab?.component || MemoizedLauncherTab;
  }, [activeTab, tabConfig]);

  // Memoized tab buttons to prevent unnecessary re-renders
  const tabButtons = useMemo(() => {
    return tabConfig.map((tab) => {
      const Icon = ICON_MAP[tab.icon];
      const isActive = activeTab === tab.id;

      return (
        <button
          key={tab.id}
          onClick={() => handleTabChange(tab.id)}
          className={`flex items-center space-x-1 sm:space-x-2 px-2 sm:px-4 py-2 rounded-lg transition-all whitespace-nowrap text-sm sm:text-base ${
            isActive
              ? "bg-blue-600 text-white shadow-sm"
              : "hover:bg-gray-100 dark:hover:bg-gray-700 text-gray-600 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
          }`}
          title={tab.name}
        >
          <Icon className="w-4 h-4" />
          <span className="font-medium hidden sm:inline">{tab.name}</span>
        </button>
      );
    });
  }, [tabConfig, activeTab, handleTabChange]);

  return (
    <ErrorBoundary>
      <ToastProvider>
        <ConsoleProvider>
          <div className="min-h-screen flex flex-col bg-gray-50 dark:bg-gray-900 text-gray-900 dark:text-gray-100 transition-colors duration-300">
            {/* Fixed Custom Title Bar */}
            <div
              className="fixed top-0 left-0 right-0 z-50 bg-blue-600 text-white h-12 flex items-center justify-between px-4 select-none"
              style={{ WebkitAppRegion: "drag" }}
            >
              <div className="flex items-center space-x-2">
                <div className="w-6 h-6 bg-blue-200 rounded flex items-center justify-center">
                  <Play className="w-4 h-4 text-blue-800" />
                </div>
                <span className="font-semibold">SPT Launcher</span>
              </div>

              {/* Window Controls */}
              <div
                className="flex items-center space-x-1"
                style={{ WebkitAppRegion: "no-drag" }}
              >
                <button
                  onClick={() => handleWindowControl("minimize")}
                  className="w-8 h-8 hover:bg-blue-500 rounded flex items-center justify-center transition-colors"
                  title="Minimize"
                >
                  <Minimize className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleWindowControl("maximize")}
                  className="w-8 h-8 hover:bg-blue-500 rounded flex items-center justify-center transition-colors"
                  title={isMaximized ? "Restore" : "Maximize"}
                >
                  {isMaximized ? (
                    <Square className="w-4 h-4" />
                  ) : (
                    <Maximize className="w-4 h-4" />
                  )}
                </button>
                <button
                  onClick={() => handleWindowControl("close")}
                  className="w-8 h-8 hover:bg-red-500 rounded flex items-center justify-center transition-colors"
                  title="Close"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>

            {/* Sticky Tab Navigation */}
            <div className="sticky top-12 z-40 bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700 shadow-sm transition-colors duration-300">
              <div className="flex flex-wrap gap-1 px-2 sm:px-4 py-2 overflow-x-auto">
                {tabButtons}
              </div>
            </div>

            {/* Main Content with top margin to account for fixed header */}
            <div className="flex-1 overflow-hidden mt-12 bg-gray-50 dark:bg-gray-900 transition-colors duration-300">
              <div className="h-full p-3 sm:p-4 md:p-6">
                <ActiveComponent />
              </div>
            </div>

            {/* Toast Notifications */}
            <ToastContainer />
          </div>
        </ConsoleProvider>
      </ToastProvider>
    </ErrorBoundary>
  );
}

export default memo(App);
