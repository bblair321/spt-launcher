import path from "path-browserify";

/**
 * Parse SPT installation directory from launcher executable path
 * @param {string} launcherPath - Full path to the launcher executable
 * @returns {string|null} - Directory path or null if invalid
 */
export function parseSptDirectory(launcherPath) {
  if (!launcherPath) return null;

  // Handle Windows paths properly
  if (launcherPath.includes("\\")) {
    // Windows path - split by backslash and remove the last part (filename)
    const parts = launcherPath.split("\\");
    parts.pop(); // Remove filename
    return parts.join("\\");
  } else {
    // Unix path - use path.dirname
    return path.dirname(launcherPath);
  }
}

/**
 * Get the expected config.json path for SPT installation
 * @param {string} sptDir - SPT installation directory
 * @returns {string} - Expected config.json path
 */
export function getConfigPath(sptDir) {
  if (!sptDir) return "SPT Installation Folder\\config.json";
  return `${sptDir}\\user\\launcher\\config.json`;
}

/**
 * Format path for display
 * @param {string} path - File path
 * @returns {string} - Formatted path for display
 */
export function formatPathForDisplay(path) {
  if (!path) return "Not set";

  // Truncate long paths for display
  if (path.length > 50) {
    const parts = path.split("\\");
    if (parts.length > 2) {
      return `${parts[0]}\\...\\${parts[parts.length - 2]}\\${
        parts[parts.length - 1]
      }`;
    }
  }
  return path;
}
