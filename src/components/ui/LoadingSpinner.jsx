import React from "react";
import { RefreshCw } from "lucide-react";

/**
 * Reusable loading spinner component
 * @param {Object} props - Component props
 * @param {string} props.size - Size of the spinner (sm, md, lg)
 * @param {string} props.color - Color of the spinner
 * @param {string} props.text - Optional loading text
 * @param {boolean} props.fullScreen - Whether to show as full screen overlay
 * @returns {JSX.Element} - Loading spinner component
 */
function LoadingSpinner({
  size = "md",
  color = "blue",
  text,
  fullScreen = false,
}) {
  const sizeClasses = {
    sm: "w-4 h-4",
    md: "w-6 h-6",
    lg: "w-8 h-8",
  };

  const colorClasses = {
    blue: "text-blue-500",
    green: "text-green-500",
    red: "text-red-500",
    gray: "text-gray-500",
    white: "text-white",
  };

  const spinner = (
    <div className="flex flex-col items-center justify-center space-y-2">
      <RefreshCw
        className={`${sizeClasses[size]} ${colorClasses[color]} animate-spin`}
      />
      {text && <span className="text-sm text-gray-600">{text}</span>}
    </div>
  );

  if (fullScreen) {
    return (
      <div className="fixed inset-0 bg-white bg-opacity-75 flex items-center justify-center z-50">
        {spinner}
      </div>
    );
  }

  return spinner;
}

export default LoadingSpinner;
