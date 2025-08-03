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
        }
    }
} 