// Module declarations
pub mod models;
pub mod state;
pub mod utils;
pub mod commands;

// Re-export commonly used items
pub use models::Config;
pub use state::{AppState, get_app_state};
pub use utils::error::{AppError, AppResult};
pub use utils::process::{ProcessType, ProcessInfo, launch_process, stop_process, get_output, clear_output};

// Import all commands
use commands::*;

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|_app| {
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            // Server commands
            set_server_path_from_ui,
            set_server_path_args_wrapper,
            launch_server,
            get_server_output,
            clear_server_output,
            stop_server,
            get_server_path,
            
            // Launcher commands
            set_launcher_path_from_ui,
            set_launcher_path_args_wrapper,
            launch_launcher,
            get_launcher_output,
            clear_launcher_output,
            stop_launcher,
            get_launcher_path,
            
            // Configuration commands
            save_config,
            save_config_with_ui_settings,
            load_config,
            load_config_with_ui_settings,
            clear_config,
            
            // UI commands
            select_file,
            select_server_file,
            select_launcher_file,
            check_default_port_status,
            minimize_window,
            close_window,
            
            // UI Validation commands
            validate_ui_file_path,
            validate_ui_port,
            validate_ui_log_level,
            validate_ui_refresh_interval,
            validate_ui_max_log_lines,
            validate_ui_port_available,
            validate_ui_settings
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
