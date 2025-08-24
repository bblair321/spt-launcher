import React, { useEffect } from "react";
import { X, CheckCircle, AlertCircle, Info, AlertTriangle } from "lucide-react";

const TOAST_TYPES = {
  success: {
    icon: CheckCircle,
    bgColor: "bg-green-100 dark:bg-green-900/20",
    borderColor: "border-green-300 dark:border-green-700",
    textColor: "text-green-700 dark:text-green-300",
    iconColor: "text-green-500",
  },
  error: {
    icon: AlertCircle,
    bgColor: "bg-red-100 dark:bg-red-900/20",
    borderColor: "border-red-300 dark:border-red-700",
    textColor: "text-red-700 dark:text-red-300",
    iconColor: "text-red-500",
  },
  warning: {
    icon: AlertTriangle,
    bgColor: "bg-yellow-100 dark:bg-yellow-900/20",
    borderColor: "border-yellow-300 dark:border-yellow-700",
    textColor: "text-yellow-700 dark:text-yellow-300",
    iconColor: "text-yellow-500",
  },
  info: {
    icon: Info,
    bgColor: "bg-blue-100 dark:bg-blue-900/20",
    borderColor: "border-blue-300 dark:border-blue-700",
    textColor: "text-blue-700 dark:text-blue-300",
    iconColor: "text-blue-500",
  },
};

function Toast({
  id,
  type = "info",
  title,
  message,
  duration = 5000,
  onClose,
  onRemove,
}) {
  const toastConfig = TOAST_TYPES[type] || TOAST_TYPES.info;
  const Icon = toastConfig.icon;

  useEffect(() => {
    if (duration > 0) {
      const timer = setTimeout(() => {
        onClose(id);
      }, duration);
      return () => clearTimeout(timer);
    }
  }, [duration, id, onClose]);

  const handleClose = () => {
    // Add exit animation
    const toastElement = document.querySelector(`[data-toast-id="${id}"]`);
    if (toastElement) {
      toastElement.style.animation = "slideOutRight 0.3s ease-in-out";
      setTimeout(() => {
        onClose(id);
      }, 300);
    } else {
      onClose(id);
    }
  };

  return (
    <div
      data-toast-id={id}
      className={`${toastConfig.bgColor} ${toastConfig.borderColor} border rounded-lg p-4 shadow-lg max-w-sm w-full transform transition-all duration-300 ease-in-out`}
      style={{
        animation: "slideInRight 0.3s ease-out",
      }}
    >
      <div className="flex items-start space-x-3">
        <Icon
          className={`w-5 h-5 ${toastConfig.iconColor} flex-shrink-0 mt-0.5`}
        />
        <div className="flex-1 min-w-0">
          {title && (
            <h4 className={`text-sm font-medium ${toastConfig.textColor} mb-1`}>
              {title}
            </h4>
          )}
          {message && (
            <p className={`text-sm ${toastConfig.textColor}`}>{message}</p>
          )}
        </div>
        <button
          onClick={handleClose}
          className={`${toastConfig.textColor} hover:opacity-70 transition-opacity flex-shrink-0`}
        >
          <X className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}

export default Toast;
