use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use std::process::Child;
use crate::utils::process::{ProcessType, launch_process, stop_process, get_output, clear_output};
use crate::utils::validation::validate_file_path;
use crate::state::get_app_state;

// Global state for launcher output and process
static LAUNCHER_OUTPUT: OnceLock<Arc<Mutex<Vec<String>>>> = OnceLock::new();
static LAUNCHER_PROCESS: OnceLock<Arc<Mutex<Option<Child>>>> = OnceLock::new();

// Set launcher path from UI (original working function)
#[tauri::command]
pub fn set_launcher_path_from_ui() -> String {
    let app_state = get_app_state();
    if let Ok(mut path_guard) = app_state.launcher_path.lock() {
        let config = app_state.config.lock().unwrap().clone();
        *path_guard = Some(config.default_launcher_path);
        "SUCCESS: Launcher path set to default".to_string()
    } else {
        "ERROR: Failed to set launcher path".to_string()
    }
}

// Set launcher path with args wrapper (for Tauri v2 compatibility)
#[tauri::command]
pub fn set_launcher_path_args_wrapper(args: serde_json::Value) -> String {
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
    
    let app_state = get_app_state();
    if let Ok(mut path_guard) = app_state.launcher_path.lock() {
        *path_guard = Some(path.clone());
        format!("SUCCESS: Launcher path set to: {}", path)
    } else {
        "ERROR: Failed to set launcher path".to_string()
    }
}

// Launch launcher
#[tauri::command]
pub async fn launch_launcher() -> String {
    let app_state = get_app_state();
    
    let path: String = if let Ok(path_guard) = app_state.launcher_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            return "ERROR: No launcher path set".to_string();
        }
    } else {
        return "ERROR: Failed to access launcher path".to_string();
    };
    
    // Validate the launcher path before launching
    if let Err(e) = validate_file_path(&path) {
        return format!("ERROR: Invalid launcher path: {}", e);
    }
    
    match launch_process(&path, ProcessType::Launcher, &LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new()))), &LAUNCHER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)))).await {
        Ok(result) => result,
        Err(e) => format!("ERROR: {}", e)
    }
}

// Get launcher output
#[tauri::command]
pub async fn get_launcher_output() -> Vec<String> {
    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    match get_output(output) {
        Ok(output) => output,
        Err(_) => vec!["ERROR: Failed to access launcher output".to_string()]
    }
}

// Clear launcher output
#[tauri::command]
pub async fn clear_launcher_output() -> String {
    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    match clear_output(output) {
        Ok(result) => result,
        Err(_) => "ERROR: Failed to clear launcher output".to_string()
    }
}

// Stop launcher
#[tauri::command]
pub async fn stop_launcher() -> String {
    match stop_process(&LAUNCHER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None))), &LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new()))), "Launcher").await {
        Ok(result) => result,
        Err(e) => format!("ERROR: {}", e)
    }
}

// Get launcher path
#[tauri::command]
pub async fn get_launcher_path() -> String {
    let app_state = get_app_state();
    if let Ok(path_guard) = app_state.launcher_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            "ERROR: No launcher path set".to_string()
        }
    } else {
        "ERROR: Failed to access launcher path".to_string()
    }
} 