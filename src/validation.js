// UI Validation Helper Module
// Provides real-time validation feedback for user inputs

/**
 * Validates a file path and returns validation result
 * @param {string} path - The file path to validate
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validateFilePath(path) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_file_path", {
      path,
    });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Validates a port number and returns validation result
 * @param {number} port - The port number to validate
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validatePort(port) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_port", { port });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Validates a log level and returns validation result
 * @param {string} level - The log level to validate
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validateLogLevel(level) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_log_level", {
      level,
    });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Validates a refresh interval and returns validation result
 * @param {number} interval - The refresh interval in milliseconds
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validateRefreshInterval(interval) {
  try {
    const result = await window.__TAURI__.invoke(
      "validate_ui_refresh_interval",
      { interval }
    );
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Validates max log lines and returns validation result
 * @param {number} lines - The maximum number of log lines
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validateMaxLogLines(lines) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_max_log_lines", {
      lines,
    });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Checks if a port is available for binding
 * @param {number} port - The port number to check
 * @returns {Promise<Object>} Validation result with valid boolean and message
 */
export async function validatePortAvailable(port) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_port_available", {
      port,
    });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
    };
  }
}

/**
 * Comprehensive validation for all settings
 * @param {Object} settings - Object containing all settings to validate
 * @returns {Promise<Object>} Validation result with valid boolean, message, errors, and warnings
 */
export async function validateAllSettings(settings) {
  try {
    const result = await window.__TAURI__.invoke("validate_ui_settings", {
      settings,
    });
    return result;
  } catch (error) {
    return {
      valid: false,
      message: `Validation error: ${error}`,
      errors: [],
      warnings: [],
    };
  }
}

/**
 * Real-time validation for input fields
 * @param {string} fieldType - Type of field being validated
 * @param {any} value - Value to validate
 * @param {Function} callback - Callback function to handle validation result
 */
export async function validateField(fieldType, value, callback) {
  let result;

  switch (fieldType) {
    case "file_path":
      result = await validateFilePath(value);
      break;
    case "port":
      result = await validatePort(parseInt(value) || 0);
      break;
    case "log_level":
      result = await validateLogLevel(value);
      break;
    case "refresh_interval":
      result = await validateRefreshInterval(parseInt(value) || 0);
      break;
    case "max_log_lines":
      result = await validateMaxLogLines(parseInt(value) || 0);
      break;
    default:
      result = {
        valid: true,
        message: "Field type not supported for validation",
      };
  }

  callback(result);
}

/**
 * Creates a validation feedback element
 * @param {Object} validationResult - Result from validation function
 * @returns {HTMLElement} Validation feedback element
 */
export function createValidationFeedback(validationResult) {
  const feedback = document.createElement("div");
  feedback.className = `validation-feedback ${
    validationResult.valid ? "valid" : "invalid"
  }`;
  feedback.textContent = validationResult.message;

  return feedback;
}

/**
 * Updates validation feedback for an input field
 * @param {HTMLElement} inputElement - The input element to add feedback to
 * @param {Object} validationResult - Result from validation function
 */
export function updateValidationFeedback(inputElement, validationResult) {
  // Remove existing feedback
  const existingFeedback = inputElement.parentNode.querySelector(
    ".validation-feedback"
  );
  if (existingFeedback) {
    existingFeedback.remove();
  }

  // Add new feedback
  const feedback = createValidationFeedback(validationResult);
  inputElement.parentNode.appendChild(feedback);

  // Update input styling
  inputElement.classList.remove("valid", "invalid");
  inputElement.classList.add(validationResult.valid ? "valid" : "invalid");
}

/**
 * Debounced validation for real-time feedback
 * @param {Function} validationFunction - Function to call for validation
 * @param {number} delay - Delay in milliseconds
 * @returns {Function} Debounced validation function
 */
export function debounceValidation(validationFunction, delay = 500) {
  let timeoutId;
  return function (...args) {
    clearTimeout(timeoutId);
    timeoutId = setTimeout(() => validationFunction.apply(this, args), delay);
  };
}
