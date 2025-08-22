import React, { useState, useEffect } from "react";
import { Download, CheckCircle, AlertCircle, RefreshCw } from "lucide-react";

const UpdateManager = () => {
  const [updateInfo, setUpdateInfo] = useState(null);
  const [isChecking, setIsChecking] = useState(false);
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState(0);
  const [error, setError] = useState(null);
  const [currentVersion] = useState("2.0.0"); // This should come from package.json

  useEffect(() => {
    // Listen for update events from main process
    window.electronAPI?.on("update-available", (info) => {
      setUpdateInfo(info);
      setError(null);
    });

    window.electronAPI?.on("update-download-progress", (progress) => {
      setDownloadProgress(progress.percent);
    });

    window.electronAPI?.on("update-error", (errorMessage) => {
      setError(errorMessage);
      setIsDownloading(false);
    });

    return () => {
      // Cleanup listeners if needed
    };
  }, []);

  const checkForUpdates = async () => {
    setIsChecking(true);
    setError(null);

    try {
      const result = await window.electronAPI?.invoke("check-for-updates");
      if (result.success) {
        if (result.updateInfo) {
          setUpdateInfo(result.updateInfo);
        } else {
          setUpdateInfo(null);
        }
      } else {
        setError(result.error || "Failed to check for updates");
      }
    } catch (err) {
      setError("Error checking for updates");
    } finally {
      setIsChecking(false);
    }
  };

  const downloadUpdate = async () => {
    if (!updateInfo) return;

    setIsDownloading(true);
    setError(null);

    try {
      const result = await window.electronAPI?.invoke("download-update");
      if (!result.success) {
        setError(result.error || "Failed to download update");
        setIsDownloading(false);
      }
    } catch (err) {
      setError("Error downloading update");
      setIsDownloading(false);
    }
  };

  const installUpdate = async () => {
    try {
      await window.electronAPI?.invoke("install-update");
    } catch (err) {
      setError("Error installing update");
    }
  };

  const formatBytes = (bytes) => {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
  };

  const formatSpeed = (bytesPerSecond) => {
    return formatBytes(bytesPerSecond) + "/s";
  };

  if (error) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-4 mb-4">
        <div className="flex items-center">
          <AlertCircle className="h-5 w-5 text-red-400 mr-2" />
          <span className="text-red-800">Update Error: {error}</span>
        </div>
        <button
          onClick={() => setError(null)}
          className="mt-2 text-sm text-red-600 hover:text-red-800 underline"
        >
          Dismiss
        </button>
      </div>
    );
  }

  return (
    <div className="bg-white border border-gray-200 rounded-lg p-6 shadow-sm">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-semibold text-gray-900">Updates</h3>
        <button
          onClick={checkForUpdates}
          disabled={isChecking}
          className="flex items-center px-3 py-2 text-sm font-medium text-gray-700 bg-gray-100 border border-gray-300 rounded-md hover:bg-gray-200 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:opacity-50"
        >
          <RefreshCw
            className={`h-4 w-4 mr-2 ${isChecking ? "animate-spin" : ""}`}
          />
          {isChecking ? "Checking..." : "Check for Updates"}
        </button>
      </div>

      <div className="space-y-4">
        {/* Current Version */}
        <div className="flex items-center justify-between p-3 bg-gray-50 rounded-md">
          <span className="text-sm text-gray-600">Current Version</span>
          <span className="text-sm font-medium text-gray-900">
            {currentVersion}
          </span>
        </div>

        {/* Update Available */}
        {updateInfo && (
          <div className="border border-blue-200 bg-blue-50 rounded-lg p-4">
            <div className="flex items-start">
              <div className="flex-shrink-0">
                <CheckCircle className="h-5 w-5 text-blue-400" />
              </div>
              <div className="ml-3 flex-1">
                <h4 className="text-sm font-medium text-blue-800">
                  Update Available: Version {updateInfo.version}
                </h4>
                <p className="mt-1 text-sm text-blue-700">
                  A new version is available for download.
                </p>

                {!isDownloading && (
                  <button
                    onClick={downloadUpdate}
                    className="mt-3 inline-flex items-center px-3 py-2 border border-transparent text-sm leading-4 font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                  >
                    <Download className="h-4 w-4 mr-2" />
                    Download Update
                  </button>
                )}
              </div>
            </div>
          </div>
        )}

        {/* Download Progress */}
        {isDownloading && (
          <div className="border border-green-200 bg-green-50 rounded-lg p-4">
            <div className="flex items-start">
              <div className="flex-shrink-0">
                <Download className="h-5 w-5 text-green-400" />
              </div>
              <div className="ml-3 flex-1">
                <h4 className="text-sm font-medium text-green-800">
                  Downloading Update...
                </h4>

                {/* Progress Bar */}
                <div className="mt-3">
                  <div className="flex justify-between text-sm text-green-700 mb-1">
                    <span>{downloadProgress.toFixed(1)}%</span>
                    <span>Complete</span>
                  </div>
                  <div className="w-full bg-green-200 rounded-full h-2">
                    <div
                      className="bg-green-600 h-2 rounded-full transition-all duration-300"
                      style={{ width: `${downloadProgress}%` }}
                    />
                  </div>
                </div>

                <p className="mt-2 text-sm text-green-700">
                  Update will be ready to install when download completes.
                </p>
              </div>
            </div>
          </div>
        )}

        {/* No Updates Available */}
        {!updateInfo && !isChecking && (
          <div className="text-center py-8 text-gray-500">
            <CheckCircle className="h-12 w-12 text-green-400 mx-auto mb-3" />
            <p className="text-sm">You're running the latest version!</p>
          </div>
        )}
      </div>
    </div>
  );
};

export default UpdateManager;
