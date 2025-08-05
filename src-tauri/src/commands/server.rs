use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use std::process::Child;
use crate::utils::process::{ProcessType, launch_process, stop_process, get_output, clear_output};
use crate::utils::validation::validate_file_path;

// Global state for server
static SERVER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static SERVER_OUTPUT: OnceLock<Arc<Mutex<Vec<String>>>> = OnceLock::new();
static SERVER_PROCESS: OnceLock<Arc<Mutex<Option<Child>>>> = OnceLock::new();

// Set server path from UI (original working function)
#[tauri::command]
pub fn set_server_path_from_ui() -> String {
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = server_path.lock() {
        let config = crate::state::get_app_state().config.lock().unwrap().clone();
        *path_guard = Some(config.default_server_path);
        "SUCCESS: Server path set to default".to_string()
    } else {
        "ERROR: Failed to set server path".to_string()
    }
}

// Set server path with args wrapper (for Tauri v2 compatibility)
#[tauri::command]
pub fn set_server_path_args_wrapper(args: serde_json::Value) -> String {
    // Try different ways to extract the path
    let path = if let Some(args_obj) = args.get("args") {
        // If args is wrapped in an "args" key
        if let Some(path_value) = args_obj.get("path") {
            if let Some(p) = path_value.as_str() {
                p.to_string()
            } else {
                return "ERROR: Invalid path format in args wrapper".to_string();
            }
        } else {
            return "ERROR: No path provided in args wrapper".to_string();
        }
    } else if let Some(path_value) = args.get("path") {
        // If path is directly in the root
        if let Some(p) = path_value.as_str() {
            p.to_string()
        } else {
            return "ERROR: Invalid path format in root".to_string();
        }
    } else {
        return "ERROR: No args or path provided".to_string();
    };
    
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = server_path.lock() {
        *path_guard = Some(path.clone());
        format!("SUCCESS: Server path set to: {}", path)
    } else {
        "ERROR: Failed to set server path".to_string()
    }
}

// Launch server
#[tauri::command]
pub async fn launch_server() -> String {
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    
    let path: String = if let Ok(path_guard) = server_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            return "ERROR: No server path set".to_string();
        }
    } else {
        return "ERROR: Failed to access server path".to_string();
    };
    
    // Validate the server path before launching
    if let Err(e) = validate_file_path(&path) {
        return format!("ERROR: Invalid server path: {}", e);
    }
    
    match launch_process(&path, ProcessType::Server, &SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new()))), &SERVER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)))).await {
        Ok(result) => result,
        Err(e) => format!("ERROR: {}", e)
    }
}

// Get server output
#[tauri::command]
pub async fn get_server_output() -> Vec<String> {
    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    match get_output(output) {
        Ok(output) => output,
        Err(_) => vec!["ERROR: Failed to access server output".to_string()]
    }
}

// Clear server output
#[tauri::command]
pub async fn clear_server_output() -> String {
    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    match clear_output(output) {
        Ok(result) => result,
        Err(_) => "ERROR: Failed to clear server output".to_string()
    }
}

// Stop server
#[tauri::command]
pub async fn stop_server() -> String {
    match stop_process(&SERVER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None))), &SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new()))), "Server").await {
        Ok(result) => result,
        Err(e) => format!("ERROR: {}", e)
    }
}

// Get server path
#[tauri::command]
pub async fn get_server_path() -> String {
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(path_guard) = server_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            "ERROR: No server path set".to_string()
        }
    } else {
        "ERROR: Failed to access server path".to_string()
    }
} 