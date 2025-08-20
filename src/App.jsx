import React, { useState, useEffect } from "react";
import {
  Play,
  Server,
  Puzzle,
  Settings,
  Search,
  Wrench,
  FolderOpen,
  FileText,
  Save,
  X,
  Minimize,
  Square,
  Maximize,
} from "lucide-react";

// Tab Components
import LauncherTab from "./components/LauncherTab";
import ServersTab from "./components/ServersTab";
import AddonsTab from "./components/AddonsTab";
import SettingsTab from "./components/SettingsTab";
import DevToolsTab from "./components/DevToolsTab";
import SearchTab from "./components/SearchTab";

function App() {
  const [activeTab, setActiveTab] = useState("launcher");
  const [isMaximized, setIsMaximized] = useState(false);

  const tabs = [
    { id: "launcher", name: "Launcher", icon: Play, component: LauncherTab },
    { id: "servers", name: "Servers", icon: Server, component: ServersTab },
    { id: "addons", name: "Addons", icon: Puzzle, component: AddonsTab },
    {
      id: "settings",
      name: "Settings",
      icon: Settings,
      component: SettingsTab,
    },
    { id: "devtools", name: "Dev Tools", icon: Wrench, component: DevToolsTab },
    { id: "search", name: "Search", icon: Search, component: SearchTab },
  ];

  const handleWindowControl = (action) => {
    if (window.electronAPI) {
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
      }
    }
  };

  const ActiveComponent =
    tabs.find((tab) => tab.id === activeTab)?.component || LauncherTab;

  return (
    <div className="min-h-screen bg-gray-50 text-gray-900 flex flex-col">
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
          >
            <Minimize className="w-4 h-4" />
          </button>
          <button
            onClick={() => handleWindowControl("maximize")}
            className="w-8 h-8 hover:bg-blue-500 rounded flex items-center justify-center transition-colors"
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
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Sticky Tab Navigation */}
      <div className="sticky top-12 z-40 bg-white border-b border-gray-200 shadow-sm">
        <div className="flex space-x-1 px-4 py-2">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`flex items-center space-x-2 px-4 py-2 rounded-lg transition-all ${
                  activeTab === tab.id
                    ? "bg-blue-600 text-white shadow-sm"
                    : "hover:bg-gray-100 text-gray-600 hover:text-gray-900"
                }`}
              >
                <Icon className="w-4 h-4" />
                <span className="font-medium">{tab.name}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Main Content with top margin to account for fixed header */}
      <div className="flex-1 overflow-hidden bg-gray-50 mt-12">
        <div className="h-full p-6">
          <ActiveComponent />
        </div>
      </div>
    </div>
  );
}

export default App;
