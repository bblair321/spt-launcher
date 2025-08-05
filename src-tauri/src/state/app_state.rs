use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use std::process::Child;
use crate::models::Config;

pub struct AppState {
    pub server_path: Arc<Mutex<Option<String>>>,
    pub launcher_path: Arc<Mutex<Option<String>>>,
    pub server_output: Arc<Mutex<Vec<String>>>,
    pub launcher_output: Arc<Mutex<Vec<String>>>,
    pub server_process: Arc<Mutex<Option<Child>>>,
    pub launcher_process: Arc<Mutex<Option<Child>>>,
    pub selected_server_path: Arc<Mutex<Option<String>>>,
    pub selected_launcher_path: Arc<Mutex<Option<String>>>,
    pub config: Arc<Mutex<Config>>,
}

impl AppState {
    pub fn new() -> Self {
        Self {
            server_path: Arc::new(Mutex::new(None)),
            launcher_path: Arc::new(Mutex::new(None)),
            server_output: Arc::new(Mutex::new(Vec::new())),
            launcher_output: Arc::new(Mutex::new(Vec::new())),
            server_process: Arc::new(Mutex::new(None)),
            launcher_process: Arc::new(Mutex::new(None)),
            selected_server_path: Arc::new(Mutex::new(None)),
            selected_launcher_path: Arc::new(Mutex::new(None)),
            config: Arc::new(Mutex::new(Config::default())),
        }
    }
}

// Global state instance
static APP_STATE: OnceLock<AppState> = OnceLock::new();

pub fn get_app_state() -> &'static AppState {
    APP_STATE.get_or_init(AppState::new)
} 