use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Clone, Debug)]
pub struct Config {
    pub server_path: Option<String>,
    pub launcher_path: Option<String>,
    pub server_port: u16,
    pub auto_start_server: bool,
    pub auto_start_launcher: bool,
    pub max_log_lines: usize,
    pub auto_refresh: bool,
    pub refresh_interval: u64,
    pub log_level: String,
    // Update configuration
    pub github_repo_owner: String,
    pub github_repo_name: String,
    pub github_token: Option<String>,
    pub update_check_interval: u64,
    pub auto_check_updates: bool,
    // Application names and patterns
    pub launcher_exe_name: String,
    pub server_exe_name: String,
    pub update_file_pattern: String,
    // Default paths
    pub default_launcher_path: String,
    pub default_server_path: String,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            server_path: None,
            launcher_path: None,
            server_port: 6969,
            auto_start_server: false,
            auto_start_launcher: false,
            max_log_lines: 5000,
            auto_refresh: true,
            refresh_interval: 1000,
            log_level: "Normal".to_string(),
            // Update configuration defaults
            github_repo_owner: "bblair321".to_string(),
            github_repo_name: "spt-launcher".to_string(),
            github_token: None,
            update_check_interval: 24 * 60 * 60 * 1000, // 24 hours in milliseconds
            auto_check_updates: true,
            // Application names and patterns
            launcher_exe_name: "SPT.Launcher.exe".to_string(),
            server_exe_name: "SPT.Server.exe".to_string(),
            update_file_pattern: "SPT-Launcher-".to_string(),
            // Default paths
            default_launcher_path: "D:\\SPT\\SPT.Launcher.exe".to_string(),
            default_server_path: "D:\\SPT\\SPT.Server.exe".to_string(),
        }
    }
} 