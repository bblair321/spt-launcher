use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use tauri_plugin_dialog::DialogExt;
use crate::utils::validation::{validate_file_path, validate_port, validate_log_level, validate_refresh_interval, validate_max_log_lines, validate_port_available};

// Global state for selected paths (updated by file selection)
static SELECTED_SERVER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static SELECTED_LAUNCHER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();

// Select file using native dialog
#[tauri::command]
pub async fn select_file(app_handle: tauri::AppHandle) -> String {
    let (tx, rx) = std::sync::mpsc::channel();
    
    app_handle.dialog().file().pick_file(move |path| {
        let _ = tx.send(path.map(|p| p.to_string()));
    });
    
    match rx.recv() {
        Ok(Some(path)) => {
            // Store the selected path in global state
            let selected_path = SELECTED_SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
            if let Ok(mut path_guard) = selected_path.lock() {
                *path_guard = Some(path.clone());
            }
            path
        }
        Ok(None) => "ERROR: No file selected".to_string(),
        Err(_) => "ERROR: Failed to get file selection result".to_string(),
    }
}

// Select server file
#[tauri::command]
pub async fn select_server_file(app_handle: tauri::AppHandle) -> String {
    let (tx, rx) = std::sync::mpsc::channel();
    
    app_handle.dialog().file().pick_file(move |path| {
        let _ = tx.send(path.map(|p| p.to_string()));
    });
    
    match rx.recv() {
        Ok(Some(path)) => {
            // Store the selected server path in global state
            let selected_path = SELECTED_SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
            if let Ok(mut path_guard) = selected_path.lock() {
                *path_guard = Some(path.clone());
            }
            path
        }
        Ok(None) => "ERROR: No file selected".to_string(),
        Err(_) => "ERROR: Failed to get file selection result".to_string(),
    }
}

// Select launcher file
#[tauri::command]
pub async fn select_launcher_file(app_handle: tauri::AppHandle) -> String {
    let (tx, rx) = std::sync::mpsc::channel();
    
    app_handle.dialog().file().pick_file(move |path| {
        let _ = tx.send(path.map(|p| p.to_string()));
    });
    
    match rx.recv() {
        Ok(Some(path)) => {
            // Store the selected launcher path in global state
            let selected_path = SELECTED_LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
            if let Ok(mut path_guard) = selected_path.lock() {
                *path_guard = Some(path.clone());
            }
            path
        }
        Ok(None) => "ERROR: No file selected".to_string(),
        Err(_) => "ERROR: Failed to get file selection result".to_string(),
    }
}

// Check default port status (no parameters)
#[tauri::command]
pub fn check_default_port_status() -> String {
    use std::net::TcpListener;
    
    // Check the default SPT-AKI port (6969)
    let port = 6969;
    
    // Try to bind to the port to see if it's available
    match TcpListener::bind(format!("127.0.0.1:{}", port)) {
        Ok(_) => {
            // Port is available
            "Available".to_string()
        },
        Err(_) => {
            // Port is in use
            "In Use".to_string()
        }
    }
}

// Window control commands
#[tauri::command]
pub fn minimize_window(window: tauri::Window) -> String {
    match window.minimize() {
        Ok(_) => "SUCCESS: Window minimized".to_string(),
        Err(e) => format!("ERROR: Failed to minimize window: {}", e)
    }
}

#[tauri::command]
pub fn close_window(window: tauri::Window) -> String {
    match window.close() {
        Ok(_) => "SUCCESS: Window closed".to_string(),
        Err(e) => format!("ERROR: Failed to close window: {}", e)
    }
}

// UI Validation Commands

/// Validates a file path and returns a validation result
#[tauri::command]
pub fn validate_ui_file_path(path: String) -> serde_json::Value {
    match validate_file_path(&path) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Path is valid"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Validates a port number and returns a validation result
#[tauri::command]
pub fn validate_ui_port(port: u16) -> serde_json::Value {
    match validate_port(port) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Port is valid"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Validates a log level and returns a validation result
#[tauri::command]
pub fn validate_ui_log_level(level: String) -> serde_json::Value {
    match validate_log_level(&level) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Log level is valid"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Validates a refresh interval and returns a validation result
#[tauri::command]
pub fn validate_ui_refresh_interval(interval: u64) -> serde_json::Value {
    match validate_refresh_interval(interval) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Refresh interval is valid"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Validates max log lines and returns a validation result
#[tauri::command]
pub fn validate_ui_max_log_lines(lines: usize) -> serde_json::Value {
    match validate_max_log_lines(lines) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Max log lines is valid"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Checks if a port is available for binding
#[tauri::command]
pub fn validate_ui_port_available(port: u16) -> serde_json::Value {
    match validate_port_available(port) {
        Ok(_) => serde_json::json!({
            "valid": true,
            "message": "Port is available"
        }),
        Err(e) => serde_json::json!({
            "valid": false,
            "message": e.to_string()
        })
    }
}

/// Comprehensive UI validation for all settings
#[tauri::command]
pub fn validate_ui_settings(settings: serde_json::Value) -> serde_json::Value {
    let mut errors = Vec::new();
    let mut warnings = Vec::new();
    
    // Validate server path if provided
    if let Some(server_path) = settings.get("server_path").and_then(|v| v.as_str()) {
        if !server_path.is_empty() {
            if let Err(e) = validate_file_path(server_path) {
                errors.push(format!("Server Path: {}", e));
            }
        }
    }
    
    // Validate launcher path if provided
    if let Some(launcher_path) = settings.get("launcher_path").and_then(|v| v.as_str()) {
        if !launcher_path.is_empty() {
            if let Err(e) = validate_file_path(launcher_path) {
                errors.push(format!("Launcher Path: {}", e));
            }
        }
    }
    
    // Validate port
    if let Some(port) = settings.get("server_port").and_then(|v| v.as_u64()) {
        if let Err(e) = validate_port(port as u16) {
            errors.push(format!("Server Port: {}", e));
        } else {
            // Check if port is available
            if let Err(e) = validate_port_available(port as u16) {
                warnings.push(format!("Server Port: {}", e));
            }
        }
    }
    
    // Validate log level
    if let Some(level) = settings.get("log_level").and_then(|v| v.as_str()) {
        if let Err(e) = validate_log_level(level) {
            errors.push(format!("Log Level: {}", e));
        }
    }
    
    // Validate refresh interval
    if let Some(interval) = settings.get("refresh_interval").and_then(|v| v.as_u64()) {
        if let Err(e) = validate_refresh_interval(interval) {
            errors.push(format!("Refresh Interval: {}", e));
        }
    }
    
    // Validate max log lines
    if let Some(lines) = settings.get("max_log_lines").and_then(|v| v.as_u64()) {
        if let Err(e) = validate_max_log_lines(lines as usize) {
            errors.push(format!("Max Log Lines: {}", e));
        }
    }
    
    // Return validation result
    if errors.is_empty() && warnings.is_empty() {
        serde_json::json!({
            "valid": true,
            "message": "All settings are valid",
            "errors": [],
            "warnings": []
        })
    } else {
        serde_json::json!({
            "valid": errors.is_empty(),
            "message": if errors.is_empty() { "Settings have warnings" } else { "Settings have errors" },
            "errors": errors,
            "warnings": warnings
        })
    }
} 