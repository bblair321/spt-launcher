use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use std::fs;
use tauri::Manager;
use crate::models::Config;

// Global state for paths (shared with server and launcher modules)
static SERVER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static LAUNCHER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();

// Save configuration
#[tauri::command]
pub async fn save_config(app_handle: tauri::AppHandle) -> String {
    let app_dir = match app_handle.path().app_data_dir() {
        Ok(dir) => dir,
        Err(_) => return "ERROR: Failed to get app data directory".to_string(),
    };
    
    if let Err(e) = fs::create_dir_all(&app_dir) {
        return format!("ERROR: Failed to create config directory: {}", e);
    }
    
    let config_path = app_dir.join("config.json");
    
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    
    let server_path_value = if let Ok(path_guard) = server_path.lock() {
        (*path_guard).clone()
    } else {
        None
    };
    
    let launcher_path_value = if let Ok(path_guard) = launcher_path.lock() {
        (*path_guard).clone()
    } else {
        None
    };
    
    let config = Config {
        server_path: server_path_value,
        launcher_path: launcher_path_value,
        server_port: 6969,
        auto_start_server: false,
        auto_start_launcher: false,
        max_log_lines: 5000,
        auto_refresh: true,
        refresh_interval: 1000,
        log_level: "Normal".to_string(),
    };
    
    let config_json = match serde_json::to_string_pretty(&config) {
        Ok(json) => json,
        Err(e) => return format!("ERROR: Failed to serialize config: {}", e),
    };
    
    if let Err(e) = fs::write(&config_path, config_json) {
        return format!("ERROR: Failed to write config file: {}", e);
    }
    
    "SUCCESS: Configuration saved successfully".to_string()
}

// Load configuration
#[tauri::command]
pub async fn load_config(app_handle: tauri::AppHandle) -> String {
    let app_dir = match app_handle.path().app_data_dir() {
        Ok(dir) => dir,
        Err(_) => return "ERROR: Failed to get app data directory".to_string(),
    };
    
    let config_path = app_dir.join("config.json");
    
    if !config_path.exists() {
        return "ERROR: No configuration file found".to_string();
    }
    
    let config_json = match fs::read_to_string(&config_path) {
        Ok(json) => json,
        Err(e) => return format!("ERROR: Failed to read config file: {}", e),
    };
    
    let config: Config = match serde_json::from_str(&config_json) {
        Ok(config) => config,
        Err(e) => return format!("ERROR: Failed to parse config file: {}", e),
    };
    
    // Update server path
    if let Some(server_path) = config.server_path {
        let server_path_guard = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
        if let Ok(mut path_guard) = server_path_guard.lock() {
            *path_guard = Some(server_path);
        }
    }
    
    // Update launcher path
    if let Some(launcher_path) = config.launcher_path {
        let launcher_path_guard = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
        if let Ok(mut path_guard) = launcher_path_guard.lock() {
            *path_guard = Some(launcher_path);
        }
    }
    
    "SUCCESS: Configuration loaded successfully".to_string()
}

// Clear configuration file
#[tauri::command]
pub async fn clear_config(app_handle: tauri::AppHandle) -> String {
    let app_dir = match app_handle.path().app_data_dir() {
        Ok(dir) => dir,
        Err(_) => return "ERROR: Failed to get app data directory".to_string(),
    };
    
    let config_path = app_dir.join("config.json");
    
    if config_path.exists() {
        if let Err(e) = fs::remove_file(&config_path) {
            return format!("ERROR: Failed to delete config file: {}", e);
        }
    }
    
    // Also clear the global path variables
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = server_path.lock() {
        *path_guard = None;
    }
    
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = launcher_path.lock() {
        *path_guard = None;
    }
    
    "SUCCESS: Configuration cleared successfully".to_string()
}

// Save configuration with UI settings - simplified version
#[tauri::command]
pub async fn save_config_with_ui_settings(
    app_handle: tauri::AppHandle,
    settings: serde_json::Value,
) -> String {
    // Extract values from the settings object
    let auto_start_server = settings["autoStartServer"].as_bool().unwrap_or(false);
    let auto_start_launcher = settings["autoStartLauncher"].as_bool().unwrap_or(false);
    let server_port = settings["serverPort"].as_u64().unwrap_or(6969) as u16;
    let max_log_lines = settings["maxLogLines"].as_u64().unwrap_or(1000) as usize;
    let auto_refresh = settings["autoRefresh"].as_bool().unwrap_or(true);
    let refresh_interval = settings["refreshInterval"].as_u64().unwrap_or(5000);
    let log_level = settings["logLevel"].as_str().unwrap_or("Normal").to_string();
    
    let app_dir = match app_handle.path().app_data_dir() {
        Ok(dir) => dir,
        Err(_) => return "ERROR: Failed to get app data directory".to_string(),
    };
    
    if let Err(e) = fs::create_dir_all(&app_dir) {
        return format!("ERROR: Failed to create config directory: {}", e);
    }
    
    let config_path = app_dir.join("config.json");
    
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    
    let server_path_value = if let Ok(path_guard) = server_path.lock() {
        (*path_guard).clone()
    } else {
        None
    };
    
    let launcher_path_value = if let Ok(path_guard) = launcher_path.lock() {
        (*path_guard).clone()
    } else {
        None
    };
    
    let config = Config {
        server_path: server_path_value,
        launcher_path: launcher_path_value,
        server_port,
        auto_start_server,
        auto_start_launcher,
        max_log_lines,
        auto_refresh,
        refresh_interval,
        log_level,
    };
    
    let config_json = match serde_json::to_string_pretty(&config) {
        Ok(json) => json,
        Err(e) => return format!("ERROR: Failed to serialize config: {}", e),
    };
    
    if let Err(e) = fs::write(&config_path, config_json) {
        return format!("ERROR: Failed to write config file: {}", e);
    }
    
    "SUCCESS: Configuration saved successfully".to_string()
}

// Load configuration with UI settings
#[tauri::command]
pub async fn load_config_with_ui_settings(app_handle: tauri::AppHandle) -> String {
    let app_dir = match app_handle.path().app_data_dir() {
        Ok(dir) => dir,
        Err(_) => return "ERROR: Failed to get app data directory".to_string(),
    };
    
    let config_path = app_dir.join("config.json");
    
    if !config_path.exists() {
        return "ERROR: No configuration file found".to_string();
    }
    
    let config_json = match fs::read_to_string(&config_path) {
        Ok(json) => json,
        Err(e) => return format!("ERROR: Failed to read config file: {}", e),
    };
    
    let config: Config = match serde_json::from_str(&config_json) {
        Ok(config) => config,
        Err(e) => return format!("ERROR: Failed to parse config file: {}", e),
    };
    
    // Update server path
    if let Some(ref server_path) = config.server_path {
        let server_path_guard = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
        if let Ok(mut path_guard) = server_path_guard.lock() {
            *path_guard = Some(server_path.clone());
        }
    }
    
    // Update launcher path
    if let Some(ref launcher_path) = config.launcher_path {
        let launcher_path_guard = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
        if let Ok(mut path_guard) = launcher_path_guard.lock() {
            *path_guard = Some(launcher_path.clone());
        }
    }
    
    // Return the UI settings as JSON
    let ui_settings = serde_json::json!({
        "server_path": config.server_path,
        "launcher_path": config.launcher_path,
        "server_port": config.server_port,
        "auto_start_server": config.auto_start_server,
        "auto_start_launcher": config.auto_start_launcher,
        "max_log_lines": config.max_log_lines,
        "auto_refresh": config.auto_refresh,
        "refresh_interval": config.refresh_interval,
        "log_level": config.log_level
    });
    
    match serde_json::to_string(&ui_settings) {
        Ok(json) => format!("SUCCESS: {}", json),
        Err(e) => format!("ERROR: Failed to serialize UI settings: {}", e),
    }
} 