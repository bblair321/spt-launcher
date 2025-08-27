import React, { createContext, useContext, useState } from "react";

const ConsoleContext = createContext();

export const useConsole = () => {
  const context = useContext(ConsoleContext);
  if (!context) {
    throw new Error("useConsole must be used within a ConsoleProvider");
  }
  return context;
};

export const ConsoleProvider = ({ children }) => {
  const [globalConsoleOutput, setGlobalConsoleOutput] = useState([]);
  const [runningServers, setRunningServers] = useState(new Map());

  const addConsoleOutput = (message, type = "info") => {
    const timestamp = new Date().toLocaleTimeString();
    const newOutput = { timestamp, message, type };
    setGlobalConsoleOutput((prev) => [...prev, newOutput]);
  };

  const clearConsoleOutput = () => {
    setGlobalConsoleOutput([]);
  };

  const addRunningServer = (serverId, serverData) => {
    setRunningServers((prev) => {
      const newMap = new Map(prev);
      newMap.set(serverId, serverData);
      return newMap;
    });
  };

  const removeRunningServer = (serverId) => {
    setRunningServers((prev) => {
      const newMap = new Map(prev);
      newMap.delete(serverId);
      return newMap;
    });
  };

  const value = {
    globalConsoleOutput,
    setGlobalConsoleOutput,
    addConsoleOutput,
    clearConsoleOutput,
    runningServers,
    setRunningServers,
    addRunningServer,
    removeRunningServer,
  };

  return (
    <ConsoleContext.Provider value={value}>{children}</ConsoleContext.Provider>
  );
};
