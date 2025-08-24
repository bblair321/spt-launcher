import React from "react";
import { RefreshCw } from "lucide-react";
import { getStatusIcon, getStatusText } from "../../utils/statusUtils";

/**
 * Reusable status card component
 * @param {Object} props - Component props
 * @param {string} props.title - Card title
 * @param {string} props.status - Current status
 * @param {boolean} props.isRunning - Whether the process is running
 * @param {Function} props.onRefresh - Refresh callback function
 * @param {React.ReactNode} props.children - Additional content
 * @returns {JSX.Element} - Status card component
 */
function StatusCard({ title, status, isRunning, onRefresh, children }) {
  return (
    <div className="bg-white dark:bg-gray-800 p-6 rounded-lg border border-gray-200 dark:border-gray-700 shadow-sm">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
          {title}
        </h2>
        <div className="flex items-center space-x-2">
          {getStatusIcon(status)}
          <span className="font-medium text-gray-900 dark:text-gray-100">
            {getStatusText(status)}
          </span>
          {onRefresh && (
            <button
              onClick={onRefresh}
              className="ml-2 p-1 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-300 transition-colors"
              title="Refresh status"
            >
              <RefreshCw className="w-4 h-4" />
            </button>
          )}
        </div>
      </div>

      <div className="flex items-center space-x-2">
        <div
          className={`w-3 h-3 rounded-full ${
            isRunning ? "bg-green-500" : "bg-gray-400"
          }`}
        ></div>
        <span className="text-gray-900 dark:text-gray-100">
          Status: {isRunning ? "Running" : "Stopped"}
        </span>
      </div>

      {children}
    </div>
  );
}

export default StatusCard;
