import React from "react";
import { CheckCircle, AlertCircle, RefreshCw, Clock } from "lucide-react";

/**
 * Get status icon component based on status
 * @param {string} status - Status string
 * @returns {JSX.Element} - Status icon component
 */
export function getStatusIcon(status) {
  switch (status) {
    case "success":
      return <CheckCircle className="w-5 h-5 text-green-500" />;
    case "error":
      return <AlertCircle className="w-5 h-5 text-red-500" />;
    case "launching":
      return <RefreshCw className="w-5 h-5 text-blue-500 animate-spin" />;
    case "stopped":
      return <Clock className="w-5 h-5 text-gray-500" />;
    case "restarting":
      return <RefreshCw className="w-5 h-5 text-orange-500 animate-spin" />;
    case "saving":
      return <RefreshCw className="w-5 h-5 text-blue-500 animate-spin" />;
    default:
      return <Clock className="w-5 h-5 text-gray-400" />;
  }
}

/**
 * Get status text based on status
 * @param {string} status - Status string
 * @returns {string} - Status text
 */
export function getStatusText(status) {
  switch (status) {
    case "success":
      return "Ready";
    case "error":
      return "Error occurred";
    case "launching":
      return "Launching...";
    case "stopped":
      return "Stopped";
    case "restarting":
      return "Restarting...";
    case "saving":
      return "Saving...";
    default:
      return "Idle";
  }
}

/**
 * Get button text based on status
 * @param {string} status - Status string
 * @param {string} defaultText - Default button text
 * @returns {string} - Button text
 */
export function getButtonText(status, defaultText) {
  switch (status) {
    case "saving":
      return "Saving...";
    case "restarting":
      return "Restarting...";
    case "success":
      return "Saved!";
    case "error":
      return "Error";
    default:
      return defaultText;
  }
}
