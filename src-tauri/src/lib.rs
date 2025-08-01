use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use std::io::{BufRead, BufReader};
use std::thread;
use std::process::{Command, Stdio, Child};
use std::path::Path;
use tauri_plugin_dialog::DialogExt;
use std::fs;
use serde::{Deserialize, Serialize};
use tauri::Manager;

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

// Global state for paths and processes
static SERVER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static LAUNCHER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static SERVER_OUTPUT: OnceLock<Arc<Mutex<Vec<String>>>> = OnceLock::new();
static LAUNCHER_OUTPUT: OnceLock<Arc<Mutex<Vec<String>>>> = OnceLock::new();
static SERVER_PROCESS: OnceLock<Arc<Mutex<Option<Child>>>> = OnceLock::new();
static LAUNCHER_PROCESS: OnceLock<Arc<Mutex<Option<Child>>>> = OnceLock::new();

// Global state for selected paths (updated by file selection)
static SELECTED_SERVER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();
static SELECTED_LAUNCHER_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();

// Global variable for the last selected path (simpler approach) - UNUSED
// static LAST_SELECTED_PATH: OnceLock<Arc<Mutex<Option<String>>>> = OnceLock::new();

// Configuration struct
#[derive(Serialize, Deserialize)]
struct Config {
    server_path: Option<String>,
    launcher_path: Option<String>,
    server_port: u16,
    auto_start_server: bool,
    auto_start_launcher: bool,
    max_log_lines: usize,
    auto_refresh: bool,
    refresh_interval: u64,
    log_level: String,
}

// Set server path from UI (original working function)
#[tauri::command]
fn set_server_path_from_ui() -> String {
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = server_path.lock() {
        *path_guard = Some("D:\\SPT\\SPT.Server.exe".to_string());
        "SUCCESS: Server path set to default".to_string()
    } else {
        "ERROR: Failed to set server path".to_string()
    }
}

// Set launcher path from UI (original working function)
#[tauri::command]
fn set_launcher_path_from_ui() -> String {
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = launcher_path.lock() {
        *path_guard = Some("D:\\SPT\\SPT.Launcher.exe".to_string());
        "SUCCESS: Launcher path set to default".to_string()
    } else {
        "ERROR: Failed to set launcher path".to_string()
    }
}

// Set server path with args wrapper (for Tauri v2 compatibility)
#[tauri::command]
fn set_server_path_args_wrapper(args: serde_json::Value) -> String {
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

// Set launcher path with args wrapper (for Tauri v2 compatibility)
#[tauri::command]
fn set_launcher_path_args_wrapper(args: serde_json::Value) -> String {
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
    
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(mut path_guard) = launcher_path.lock() {
        *path_guard = Some(path.clone());
        format!("SUCCESS: Launcher path set to: {}", path)
    } else {
        "ERROR: Failed to set launcher path".to_string()
    }
}

// Launch server
#[tauri::command]
async fn launch_server() -> String {
    let server_path = SERVER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    
    let path = if let Ok(path_guard) = server_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            return "ERROR: No server path set".to_string();
        }
    } else {
        return "ERROR: Failed to access server path".to_string();
    };
    
    let path_obj = Path::new(&path);
    
    if !path_obj.exists() {
        return format!("ERROR: Server executable not found at path: {}", path);
    }
    
    let working_dir = match path_obj.parent() {
        Some(parent) => parent.to_string_lossy().to_string(),
        None => return "ERROR: Could not determine working directory".to_string(),
    };
    
    let server_exe = match path_obj.file_name() {
        Some(name) => name.to_string_lossy().to_string(),
        None => return "ERROR: Could not determine executable name".to_string(),
    };
    
    let full_exe_path = format!("{}\\{}", working_dir, server_exe);
    
    // Initialize the output storage
    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    
    // Initialize the process storage
    let process = SERVER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)));
    
    match Command::new(&full_exe_path)
        .current_dir(&working_dir)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(0x08000000) // CREATE_NO_WINDOW flag
        .spawn() {
            Ok(mut child) => {
                // Take stdout and stderr before storing the child
                let stdout = child.stdout.take();
                let stderr = child.stderr.take();
                
                // Store the process handle
                if let Ok(mut process_guard) = process.lock() {
                    *process_guard = Some(child);
                } else {
                    return "ERROR: Failed to store process handle".to_string();
                }
                
                // Start output capture in a separate thread
                if let (Some(stdout), Some(stderr)) = (stdout, stderr) {
                    let output_clone = output.clone();
                    
                    thread::spawn(move || {
                        let reader = BufReader::new(stdout);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("[SERVER] {}", line));
                                }
                            }
                        }
                    });
                    
                    let output_clone = output.clone();
                    thread::spawn(move || {
                        let reader = BufReader::new(stderr);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("[SERVER ERROR] {}", line));
                                }
                            }
                        }
                    });
                }
                
                "SUCCESS: Server launched successfully".to_string()
            },
            Err(e) => {
                format!("ERROR: Failed to start server: {}", e)
            }
        }
}

// Launch launcher
#[tauri::command]
async fn launch_launcher() -> String {
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    
    let path = if let Ok(path_guard) = launcher_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            return "ERROR: No launcher path set".to_string();
        }
    } else {
        return "ERROR: Failed to access launcher path".to_string();
    };
    
    let path_obj = Path::new(&path);
    
    if !path_obj.exists() {
        return format!("ERROR: Launcher executable not found at path: {}", path);
    }
    
    let working_dir = match path_obj.parent() {
        Some(parent) => parent.to_string_lossy().to_string(),
        None => return "ERROR: Could not determine working directory".to_string(),
    };
    
    let launcher_exe = match path_obj.file_name() {
        Some(name) => name.to_string_lossy().to_string(),
        None => return "ERROR: Could not determine executable name".to_string(),
    };
    
    let full_exe_path = format!("{}\\{}", working_dir, launcher_exe);
    
    // Initialize the output storage
    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    
    // Initialize the process storage
    let process = LAUNCHER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)));
    
    match Command::new(&full_exe_path)
        .current_dir(&working_dir)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(0x08000000) // CREATE_NO_WINDOW flag
        .spawn() {
            Ok(mut child) => {
                // Take stdout and stderr before storing the child
                let stdout = child.stdout.take();
                let stderr = child.stderr.take();
                
                // Store the process handle
                if let Ok(mut process_guard) = process.lock() {
                    *process_guard = Some(child);
                } else {
                    return "ERROR: Failed to store process handle".to_string();
                }
                
                // Start output capture in a separate thread
                if let (Some(stdout), Some(stderr)) = (stdout, stderr) {
                    let output_clone = output.clone();
                    
                    thread::spawn(move || {
                        let reader = BufReader::new(stdout);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("[LAUNCHER] {}", line));
                                }
                            }
                        }
                    });
                    
                    let output_clone = output.clone();
                    thread::spawn(move || {
                        let reader = BufReader::new(stderr);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("[LAUNCHER ERROR] {}", line));
                                }
                            }
                        }
                    });
                }
                
                "SUCCESS: Launcher launched successfully".to_string()
            },
            Err(e) => {
                format!("ERROR: Failed to start launcher: {}", e)
            }
        }
}

// Select file using native dialog
#[tauri::command]
async fn select_file(app_handle: tauri::AppHandle) -> String {
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
async fn select_server_file(app_handle: tauri::AppHandle) -> String {
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
async fn select_launcher_file(app_handle: tauri::AppHandle) -> String {
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

// Get launcher output
#[tauri::command]
async fn get_launcher_output() -> Vec<String> {
    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    if let Ok(output_vec) = output.lock() {
        output_vec.clone()
    } else {
        vec!["ERROR: Failed to access launcher output".to_string()]
    }
}

// Clear launcher output
#[tauri::command]
async fn clear_launcher_output() -> String {
    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    if let Ok(mut output_vec) = output.lock() {
        output_vec.clear();
        "SUCCESS: Launcher output cleared".to_string()
    } else {
        "ERROR: Failed to clear launcher output".to_string()
    }
}

// Get server output
#[tauri::command]
async fn get_server_output() -> Vec<String> {
    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    if let Ok(output_vec) = output.lock() {
        output_vec.clone()
    } else {
        vec!["ERROR: Failed to access server output".to_string()]
    }
}

// Clear server output
#[tauri::command]
async fn clear_server_output() -> String {
    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
    if let Ok(mut output_vec) = output.lock() {
        output_vec.clear();
        "SUCCESS: Server output cleared".to_string()
    } else {
        "ERROR: Failed to clear server output".to_string()
    }
}

// Stop server
#[tauri::command]
async fn stop_server() -> String {
    let process = SERVER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)));
    
    if let Ok(mut process_guard) = process.lock() {
        if let Some(mut child) = process_guard.take() {
            match child.kill() {
                Ok(_) => {
                    // Clear the output when stopping
                    let output = SERVER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
                    if let Ok(mut output_vec) = output.lock() {
                        output_vec.clear();
                    }
                    "SUCCESS: Server stopped successfully".to_string()
                },
                Err(e) => format!("ERROR: Failed to stop server: {}", e)
            }
        } else {
            "ERROR: No server process found".to_string()
        }
    } else {
        "ERROR: Failed to access server process".to_string()
    }
}

// Stop launcher
#[tauri::command]
async fn stop_launcher() -> String {
    let process = LAUNCHER_PROCESS.get_or_init(|| Arc::new(Mutex::new(None)));
    
    if let Ok(mut process_guard) = process.lock() {
        if let Some(mut child) = process_guard.take() {
            match child.kill() {
                Ok(_) => {
                    // Clear the output when stopping
                    let output = LAUNCHER_OUTPUT.get_or_init(|| Arc::new(Mutex::new(Vec::new())));
                    if let Ok(mut output_vec) = output.lock() {
                        output_vec.clear();
                    }
                    "SUCCESS: Launcher stopped successfully".to_string()
                },
                Err(e) => format!("ERROR: Failed to stop launcher: {}", e)
            }
        } else {
            "ERROR: No launcher process found".to_string()
        }
    } else {
        "ERROR: Failed to access launcher process".to_string()
    }
}

// Save configuration
#[tauri::command]
async fn save_config(app_handle: tauri::AppHandle) -> String {
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
async fn load_config(app_handle: tauri::AppHandle) -> String {
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
async fn clear_config(app_handle: tauri::AppHandle) -> String {
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

// Get server path
#[tauri::command]
async fn get_server_path() -> String {
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

// Get launcher path
#[tauri::command]
async fn get_launcher_path() -> String {
    let launcher_path = LAUNCHER_PATH.get_or_init(|| Arc::new(Mutex::new(None)));
    if let Ok(path_guard) = launcher_path.lock() {
        if let Some(ref path) = *path_guard {
            path.clone()
        } else {
            "ERROR: No launcher path set".to_string()
        }
    } else {
        "ERROR: Failed to access launcher path".to_string()
    }
}

// Check port status - DISABLED due to Tauri v2 parameter issues
// #[tauri::command]
// async fn check_port_status(port: u16) -> String {
//     use std::net::TcpListener;
//     
//     // Try to bind to the port to see if it's available
//     match TcpListener::bind(format!("127.0.0.1:{}", port)) {
//         Ok(_) => {
//             // Port is available
//             "Available".to_string()
//         },
//         Err(_) => {
//             // Port is in use
//             "In Use".to_string()
//         }
//     }
// }

// Check default port status (no parameters)
#[tauri::command]
fn check_default_port_status() -> String {
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
fn minimize_window(window: tauri::Window) -> String {
    match window.minimize() {
        Ok(_) => "SUCCESS: Window minimized".to_string(),
        Err(e) => format!("ERROR: Failed to minimize window: {}", e)
    }
}

#[tauri::command]
fn close_window(window: tauri::Window) -> String {
    match window.close() {
        Ok(_) => "SUCCESS: Window closed".to_string(),
        Err(e) => format!("ERROR: Failed to close window: {}", e)
    }
}

// Save configuration with UI settings - simplified version
#[tauri::command]
async fn save_config_with_ui_settings(
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
async fn load_config_with_ui_settings(app_handle: tauri::AppHandle) -> String {
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

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|_app| {
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            set_server_path_from_ui,
            set_launcher_path_from_ui,
            set_server_path_args_wrapper,
            set_launcher_path_args_wrapper,
            launch_server,
            launch_launcher,
            select_file,
            select_server_file,
            select_launcher_file,
            get_launcher_output,
            clear_launcher_output,
            get_server_output,
            clear_server_output,
            stop_server,
            stop_launcher,
            save_config,
            save_config_with_ui_settings,
            load_config,
            load_config_with_ui_settings,
            clear_config,
            get_server_path,
            get_launcher_path,
            check_default_port_status,
            minimize_window,
            close_window
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
