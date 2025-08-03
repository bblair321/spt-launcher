use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use tauri_plugin_dialog::DialogExt;

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