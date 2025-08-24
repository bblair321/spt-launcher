import React from "react";
import Toast from "./Toast";
import { useToastContext } from "../../contexts/ToastContext";

function ToastContainer() {
  const { toasts, removeToast } = useToastContext();

  return (
    <div className="fixed top-20 right-4 z-50 space-y-3 pointer-events-none">
      {toasts.map((toast) => (
        <div key={toast.id} className="pointer-events-auto">
          <Toast {...toast} onClose={removeToast} />
        </div>
      ))}
    </div>
  );
}

export default ToastContainer;
