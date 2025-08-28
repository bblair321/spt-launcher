import { useEffect, useCallback } from "react";

export function useProcessMonitor(processId, isRunning, onProcessStop) {
  const checkProcessStatus = useCallback(async () => {
    if (!isRunning || !processId) return;

    try {
      const result = await window.electronAPI.getRunningProcesses();

      // Handle the API response structure properly
      if (result && result.success && Array.isArray(result.processes)) {
        const isStillRunning = result.processes.some(
          (p) => p.pid === processId
        );

        if (!isStillRunning) {
          // Add a retry mechanism to avoid false positives
          // Check again after a short delay before calling onProcessStop
          setTimeout(async () => {
            try {
              const retryResult =
                await window.electronAPI.getRunningProcesses();
              if (
                retryResult &&
                retryResult.success &&
                Array.isArray(retryResult.processes)
              ) {
                const isStillRunningRetry = retryResult.processes.some(
                  (p) => p.pid === processId
                );
                if (!isStillRunningRetry) {
                  onProcessStop();
                }
              }
            } catch (retryError) {
              console.error("Retry check failed:", retryError);
            }
          }, 1000); // 1 second retry delay
        }
      } else if (result && Array.isArray(result.processes)) {
        // Fallback for backward compatibility
        const isStillRunning = result.processes.some(
          (p) => p.pid === processId
        );

        if (!isStillRunning) {
          // Add a retry mechanism to avoid false positives
          setTimeout(async () => {
            try {
              const retryResult =
                await window.electronAPI.getRunningProcesses();
              if (
                retryResult &&
                retryResult.success &&
                Array.isArray(retryResult.processes)
              ) {
                const isStillRunningRetry = retryResult.processes.some(
                  (p) => p.pid === processId
                );
                if (!isStillRunningRetry) {
                  onProcessStop();
                }
              }
            } catch (retryError) {
              console.error("Retry check failed (fallback):", retryError);
            }
          }, 1000);
        }
      } else if (result && Array.isArray(result)) {
        // Additional fallback for direct array response
        const isStillRunning = result.some((p) => p.pid === processId);

        if (!isStillRunning) {
          // Add a retry mechanism to avoid false positives
          setTimeout(async () => {
            try {
              const retryResult =
                await window.electronAPI.getRunningProcesses();
              if (
                retryResult &&
                retryResult.success &&
                Array.isArray(retryResult.processes)
              ) {
                const isStillRunningRetry = retryResult.processes.some(
                  (p) => p.pid === processId
                );
                if (!isStillRunningRetry) {
                  onProcessStop();
                }
              }
            } catch (retryError) {
              console.error("Retry check failed (direct array):", retryError);
            }
          }, 1000);
        }
      } else {
        console.warn("Invalid process list format received");
      }
    } catch (error) {
      console.error("Failed to check process status:", error.message);
    }
  }, [processId, isRunning, onProcessStop]);

  useEffect(() => {
    if (!isRunning || !processId) return;

    // Add a delay before starting to monitor to avoid race conditions
    // This gives the process time to be properly registered in the system
    let intervalId = null;

    const initialDelay = setTimeout(() => {
      intervalId = setInterval(checkProcessStatus, 2000);
    }, 3000); // 3 second delay

    return () => {
      clearTimeout(initialDelay);
      if (intervalId) {
        clearInterval(intervalId);
      }
    };
  }, [isRunning, processId, checkProcessStatus]);

  return { checkProcessStatus };
}
