import { useEffect, useCallback } from "react";

export function useProcessMonitor(processId, isRunning, onProcessStop) {
  const checkProcessStatus = useCallback(async () => {
    if (!isRunning || !processId) return;

    try {
      const processes = await window.electronAPI.getRunningProcesses();
      const isStillRunning = processes.some((p) => p.pid === processId);

      if (!isStillRunning) {
        onProcessStop();
      }
    } catch (error) {
      console.error("Failed to check process status:", error);
    }
  }, [processId, isRunning, onProcessStop]);

  useEffect(() => {
    if (!isRunning || !processId) return;

    const interval = setInterval(checkProcessStatus, 2000);
    return () => clearInterval(interval);
  }, [isRunning, processId, checkProcessStatus]);

  return { checkProcessStatus };
}
