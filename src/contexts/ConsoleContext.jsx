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

  const addConsoleOutput = (message, type = "info") => {
    const timestamp = new Date().toLocaleTimeString();
    const newOutput = { timestamp, message, type };
    setGlobalConsoleOutput((prev) => [...prev, newOutput]);
  };

  const clearConsoleOutput = () => {
    setGlobalConsoleOutput([]);
  };

  const value = {
    globalConsoleOutput,
    setGlobalConsoleOutput,
    addConsoleOutput,
    clearConsoleOutput,
  };

  return (
    <ConsoleContext.Provider value={value}>{children}</ConsoleContext.Provider>
  );
};
