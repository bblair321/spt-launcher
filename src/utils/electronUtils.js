/**
 * Utility functions for safe Electron API calls
 */

/**
 * Safely call an Electron API function with error handling
 * @param {string} apiName - Name of the API function
 * @param {Function} apiCall - The actual API call function
 * @param {string} fallbackMessage - Message to show if API fails
 * @returns {Promise<any>} - Result of the API call
 */
export const safeElectronCall = async (
  apiName,
  apiCall,
  fallbackMessage = "Operation failed"
) => {
  try {
    // Check if Electron API is available
    if (!window.electronAPI) {
      throw new Error("Electron API not available - running in browser mode");
    }

    // Check if the specific API function exists
    if (typeof window.electronAPI[apiName] !== "function") {
      throw new Error(`Electron API function '${apiName}' not available`);
    }

    // Make the API call
    const result = await apiCall();
    return result;
  } catch (error) {
    console.error(`Electron API call failed (${apiName}):`, error);

    // Return a standardized error response
    return {
      success: false,
      error: error.message || fallbackMessage,
      code: -1,
      isElectronError: true,
    };
  }
};

/**
 * Check if Electron API is available
 * @returns {boolean} - True if Electron API is available
 */
export const isElectronAvailable = () => {
  return typeof window !== "undefined" && window.electronAPI;
};

/**
 * Check if a specific Electron API function is available
 * @param {string} apiName - Name of the API function
 * @returns {boolean} - True if the function is available
 */
export const isElectronFunctionAvailable = (apiName) => {
  return (
    isElectronAvailable() && typeof window.electronAPI[apiName] === "function"
  );
};

/**
 * Get a list of available Electron API functions
 * @returns {string[]} - Array of available API function names
 */
export const getAvailableElectronAPIs = () => {
  if (!isElectronAvailable()) return [];

  return Object.keys(window.electronAPI).filter(
    (key) => typeof window.electronAPI[key] === "function"
  );
};
