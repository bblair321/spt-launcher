import { useState, useCallback } from "react";

export function useToast() {
  const [toasts, setToasts] = useState([]);

  const addToast = useCallback(
    ({ type = "info", title, message, duration = 5000 }) => {
      const id = Date.now().toString();
      const newToast = { id, type, title, message, duration };

      setToasts((prev) => [...prev, newToast]);

      // Return the toast ID in case we need to reference it later
      return id;
    },
    []
  );

  const removeToast = useCallback((id) => {
    setToasts((prev) => prev.filter((toast) => toast.id !== id));
  }, []);

  const clearToasts = useCallback(() => {
    setToasts([]);
  }, []);

  // Convenience methods for common toast types
  const showSuccess = useCallback(
    (title, message, duration) => {
      return addToast({ type: "success", title, message, duration });
    },
    [addToast]
  );

  const showError = useCallback(
    (title, message, duration) => {
      return addToast({ type: "error", title, message, duration });
    },
    [addToast]
  );

  const showWarning = useCallback(
    (title, message, duration) => {
      return addToast({ type: "warning", title, message, duration });
    },
    [addToast]
  );

  const showInfo = useCallback(
    (title, message, duration) => {
      return addToast({ type: "info", title, message, duration });
    },
    [addToast]
  );

  return {
    toasts,
    addToast,
    removeToast,
    clearToasts,
    showSuccess,
    showError,
    showWarning,
    showInfo,
  };
}
