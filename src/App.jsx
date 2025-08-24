import React, { useState, useMemo } from "react";
import {
  Play,
  Server,
  Puzzle,
  Settings,
  Search,
  Wrench,
  X,
  Minimize,
  Square,
  Maximize,
} from "lucide-react";
import { useTheme } from "./hooks/useTheme";
import { ToastProvider } from "./contexts/ToastContext";

// Constants
import { TABS } from "./constants";

// Tab Components
import LauncherTab from "./components/LauncherTab";
import ServersTab from "./components/ServersTab";
import AddonsTab from "./components/AddonsTab";
import SettingsTab from "./components/SettingsTab";
import DevToolsTab from "./components/DevToolsTab";
import SearchTab from "./components/SearchTab";

// UI Components
import ToastContainer from "./components/ui/ToastContainer";

// Icon mapping
const ICON_MAP = {
  Play,
  Server,
  Puzzle,
  Settings,
  Search,
  Wrench,
};

function App() {
  const { theme, resolvedTheme } = useTheme();
  const [activeTab, setActiveTab] = useState("launcher");
  const [isMaximized, setIsMaximized] = useState(false);

  // Memoized tab configuration with components
  const tabConfig = useMemo(
    () => [
      { ...TABS[0], component: LauncherTab },
      { ...TABS[1], component: ServersTab },
      { ...TABS[2], component: AddonsTab },
      { ...TABS[3], component: SettingsTab },
      { ...TABS[4], component: DevToolsTab },
      { ...TABS[5], component: SearchTab },
    ],
    []
  );

  const handleWindowControl = (action) => {
    if (!window.electronAPI) return;

    switch (action) {
      case "minimize":
        window.electronAPI.minimize();
        break;
      case "maximize":
        window.electronAPI.maximize();
        setIsMaximized(!isMaximized);
        break;
      case "close":
        window.electronAPI.close();
        break;
      default:
        break;
    }
  };

  const ActiveComponent = useMemo(() => {
    const tab = tabConfig.find((tab) => tab.id === activeTab);
    return tab?.component || LauncherTab;
  }, [activeTab, tabConfig]);

  return (
    <ToastProvider>
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
          <div className="flex space-x-1 px-4 py-2">
            {tabConfig.map((tab) => {
              const Icon = ICON_MAP[tab.icon];
              return (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id)}
                  className={`flex items-center space-x-2 px-4 py-2 rounded-lg transition-all ${
                    activeTab === tab.id
                      ? "bg-blue-600 text-white shadow-sm"
                      : "hover:bg-gray-100 dark:hover:bg-gray-700 text-gray-600 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
                  }`}
                  title={tab.name}
                >
                  <Icon className="w-4 h-4" />
                  <span className="font-medium">{tab.name}</span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Main Content with top margin to account for fixed header */}
        <div className="flex-1 overflow-hidden mt-12 bg-gray-50 dark:bg-gray-900 transition-colors duration-300">
          <div className="h-full p-6">
            <ActiveComponent />
          </div>
        </div>

        {/* Toast Notifications */}
        <ToastContainer />
      </div>
    </ToastProvider>
  );
}

export default App;
