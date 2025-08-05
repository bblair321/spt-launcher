use std::sync::{Arc, Mutex};
use std::sync::OnceLock;
use serde::{Deserialize, Serialize};
use tauri::Emitter;

// Update state management
static UPDATE_STATE: OnceLock<Arc<Mutex<UpdateState>>> = OnceLock::new();

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UpdateInfo {
    pub version: String,
    pub release_notes: String,
    pub download_url: String,
    pub file_size: u64,
    pub published_at: String,
    pub is_prerelease: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UpdateState {
    pub is_checking: bool,
    pub is_downloading: bool,
    pub progress: f64,
    pub current_version: String,
    pub latest_version: Option<String>,
    pub update_available: bool,
    pub last_check: Option<String>,
    pub error_message: Option<String>,
}

impl Default for UpdateState {
    fn default() -> Self {
        Self {
            is_checking: false,
            is_downloading: false,
            progress: 0.0,
            current_version: env!("CARGO_PKG_VERSION").to_string(),
            latest_version: None,
            update_available: false,
            last_check: None,
            error_message: None,
        }
    }
}

// GitHub API response structure
#[derive(Debug, Deserialize)]
struct GitHubRelease {
    tag_name: String,
    name: String,
    body: String,
    published_at: String,
    prerelease: bool,
    assets: Vec<GitHubAsset>,
}

#[derive(Debug, Deserialize)]
struct GitHubAsset {
    name: String,
    browser_download_url: String,
    size: u64,
}

// Check for updates
#[tauri::command]
pub async fn check_for_updates(app_handle: tauri::AppHandle) -> Result<UpdateInfo, String> {
    let state = UPDATE_STATE.get_or_init(|| Arc::new(Mutex::new(UpdateState::default())));
    
    // Set checking state
    if let Ok(mut state_guard) = state.lock() {
        state_guard.is_checking = true;
        state_guard.error_message = None;
    }
    
    // Emit state change event
    app_handle.emit("update-state-changed", &*state.lock().unwrap()).unwrap();
    
    let result = perform_update_check().await;
    
    // Update state based on result
    if let Ok(mut state_guard) = state.lock() {
        state_guard.is_checking = false;
        state_guard.last_check = Some(chrono::Utc::now().to_rfc3339());
        
        match &result {
            Ok(update_info) => {
                state_guard.latest_version = Some(update_info.version.clone());
                state_guard.update_available = true;
                state_guard.error_message = None;
            }
            Err(_e) => {
                state_guard.error_message = Some(_e.clone());
                state_guard.update_available = false;
            }
        }
    }
    
    // Emit final state
    app_handle.emit("update-state-changed", &*state.lock().unwrap()).unwrap();
    
    result
}

// Perform the actual update check
async fn perform_update_check() -> Result<UpdateInfo, String> {
    let current_version = env!("CARGO_PKG_VERSION");
    
    // Get configuration
    let config = crate::state::get_app_state().config.lock().unwrap().clone();
    let repo_owner = config.github_repo_owner;
    let repo_name = config.github_repo_name;
    
    let url = format!(
        "https://api.github.com/repos/{}/{}/releases/latest",
        repo_owner, repo_name
    );
    
    // Create HTTP client with proper headers
    let client = reqwest::Client::new();
    let mut request = client.get(&url)
        .header("User-Agent", format!("SPT-AKI-Launcher/{}", env!("CARGO_PKG_VERSION")))
        .header("Accept", "application/vnd.github.v3+json");
    
    // Add authentication if available
    if let Some(token) = &config.github_token {
        request = request.header("Authorization", &format!("token {}", token));
    } else if let Ok(token) = std::env::var("GITHUB_TOKEN") {
        request = request.header("Authorization", &format!("token {}", token));
    }
    
    let response = request
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;
    
    // Handle different HTTP status codes
    match response.status().as_u16() {
        200 => {
            // Success
        }
        403 => {
            return Err("GitHub API rate limit exceeded. Please try again later or add a GitHub token.".to_string());
        }
        404 => {
            return Err("Repository not found. Please check the repository name and owner.".to_string());
        }
        status => {
            return Err(format!("GitHub API error: {} - {}", status, response.status().canonical_reason().unwrap_or("Unknown")));
        }
    }
    
    let release: GitHubRelease = response
        .json()
        .await
        .map_err(|e| format!("Failed to parse response: {}", e))?;
    
    // Skip prereleases unless explicitly requested
    if release.prerelease {
        return Err("Latest release is a prerelease".to_string());
    }
    
    // Compare versions
    if !is_newer_version(&release.tag_name, current_version) {
        return Err("Already on latest version".to_string());
    }
    
    // Find the appropriate asset for the current platform
    let asset = find_platform_asset(&release.assets)
        .ok_or("No compatible release found for this platform")?;
    
    Ok(UpdateInfo {
        version: release.tag_name,
        release_notes: release.body,
        download_url: asset.browser_download_url.clone(),
        file_size: asset.size,
        published_at: release.published_at,
        is_prerelease: release.prerelease,
    })
}

// Compare version strings
fn is_newer_version(latest: &str, current: &str) -> bool {
    // Simple version comparison - you might want to use a proper semver crate
    latest != current && latest > current
}

// Find the appropriate asset for the current platform
fn find_platform_asset(assets: &[GitHubAsset]) -> Option<&GitHubAsset> {
    #[cfg(target_os = "windows")]
    let platform_suffix = ".exe";
    
    #[cfg(target_os = "linux")]
    let platform_suffix = ".AppImage";
    
    #[cfg(target_os = "macos")]
    let platform_suffix = ".dmg";
    
    assets.iter().find(|asset| {
        asset.name.contains(platform_suffix) && 
        !asset.name.contains("debug") &&
        !asset.name.contains("symbols")
    })
}

// Get current update state
#[tauri::command]
pub fn get_update_state() -> UpdateState {
    let state = UPDATE_STATE.get_or_init(|| Arc::new(Mutex::new(UpdateState::default())));
    state.lock().unwrap().clone()
}

// Get current version
#[tauri::command]
pub fn get_current_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

// Download and install update
#[tauri::command]
pub async fn download_update(app_handle: tauri::AppHandle, downloadUrl: String, version: String) -> Result<String, String> {
    let download_url = downloadUrl;
    
    let state = UPDATE_STATE.get_or_init(|| Arc::new(Mutex::new(UpdateState::default())));
    
    // Set downloading state
    if let Ok(mut state_guard) = state.lock() {
        state_guard.is_downloading = true;
        state_guard.progress = 0.0;
    }
    
    app_handle.emit("update-state-changed", &*state.lock().unwrap()).unwrap();
    
    // Download the update file
    let client = reqwest::Client::new();
    let response = client
        .get(&download_url)
        .send()
        .await
        .map_err(|e| format!("Download failed: {}", e))?;
    
    if !response.status().is_success() {
        return Err(format!("Download failed with status: {}", response.status()));
    }
    
    // Get the download directory
    let download_dir = std::env::current_exe()
        .map_err(|e| format!("Failed to get executable path: {}", e))?
        .parent()
        .ok_or("Failed to get executable directory")?
        .join("updates");
    
    // Create updates directory if it doesn't exist
    std::fs::create_dir_all(&download_dir)
        .map_err(|e| format!("Failed to create updates directory: {}", e))?;
    
    // Download the file
    let bytes = response.bytes().await
        .map_err(|e| format!("Failed to read response: {}", e))?;
    
    // Save the downloaded file
    let config = crate::state::get_app_state().config.lock().unwrap().clone();
    let file_name = format!("{}{}.exe", config.update_file_pattern, version);
    let file_path = download_dir.join(&file_name);
    
    std::fs::write(&file_path, &bytes)
        .map_err(|e| format!("Failed to save update file: {}", e))?;
    
    // Update progress
    if let Ok(mut state_guard) = state.lock() {
        state_guard.progress = 100.0;
        state_guard.is_downloading = false;
    }
    
    app_handle.emit("update-state-changed", &*state.lock().unwrap()).unwrap();
    
    let result = format!("Update downloaded successfully to: {}. Please restart the application to apply the update.", file_path.display());
    Ok(result)
}

// Install update (restart application)
#[tauri::command]
pub async fn install_update(app_handle: tauri::AppHandle) -> Result<String, String> {
    // Get the current executable path
    let current_exe = std::env::current_exe()
        .map_err(|e| format!("Failed to get executable path: {}", e))?;
    
    // Get the updates directory
    let updates_dir = current_exe
        .parent()
        .ok_or("Failed to get executable directory")?
        .join("updates");
    
    // Find the latest downloaded update file
    let config = crate::state::get_app_state().config.lock().unwrap().clone();
    let mut update_files = Vec::new();
    if let Ok(entries) = std::fs::read_dir(&updates_dir) {
        for entry in entries {
            if let Ok(entry) = entry {
                if let Some(file_name) = entry.file_name().to_str() {
                    if file_name.starts_with(&config.update_file_pattern) && file_name.ends_with(".exe") {
                        update_files.push(entry.path());
                    }
                }
            }
        }
    }
    
    // Sort by modification time to get the latest
    update_files.sort_by(|a, b| {
        let time_a = std::fs::metadata(a).unwrap().modified().unwrap();
        let time_b = std::fs::metadata(b).unwrap().modified().unwrap();
        time_b.cmp(&time_a) // Most recent first
    });
    
    if update_files.is_empty() {
        return Err("No update files found in updates directory".to_string());
    }
    
    let update_file = &update_files[0];
    
    // Launch the update installer
    let status = std::process::Command::new(update_file)
        .status()
        .map_err(|e| format!("Failed to launch update installer: {}", e))?;
    
    if status.success() {
        // Close the current application
        app_handle.exit(0);
        Ok("Update installer launched successfully".to_string())
    } else {
        Err(format!("Update installer failed with exit code: {}", status.code().unwrap_or(-1)))
    }
}

// Skip this update
#[tauri::command]
pub fn skip_update() -> String {
    let state = UPDATE_STATE.get_or_init(|| Arc::new(Mutex::new(UpdateState::default())));
    
    if let Ok(mut state_guard) = state.lock() {
        state_guard.update_available = false;
        state_guard.latest_version = None;
    }
    
    "Update skipped".to_string()
}

// Clear update state (useful after installation)
#[tauri::command]
pub fn clear_update_state() -> String {
    let state = UPDATE_STATE.get_or_init(|| Arc::new(Mutex::new(UpdateState::default())));
    
    if let Ok(mut state_guard) = state.lock() {
        state_guard.update_available = false;
        state_guard.latest_version = None;
        state_guard.error_message = None;
        state_guard.is_downloading = false;
        state_guard.progress = 0.0;
    }
    
    "Update state cleared".to_string()
}

// Get release notes for the latest version
#[tauri::command]
pub async fn get_release_notes(version: String) -> Result<String, String> {
    // Get configuration
    let config = crate::state::get_app_state().config.lock().unwrap().clone();
    let repo_owner = config.github_repo_owner;
    let repo_name = config.github_repo_name;
    
    let url = format!(
        "https://api.github.com/repos/{}/{}/releases/tags/{}",
        repo_owner, repo_name, version
    );
    
    let client = reqwest::Client::new();
    let mut request = client.get(&url)
        .header("User-Agent", format!("SPT-AKI-Launcher/{}", env!("CARGO_PKG_VERSION")))
        .header("Accept", "application/vnd.github.v3+json");
    
    // Add authentication if available
    if let Some(token) = &config.github_token {
        request = request.header("Authorization", &format!("token {}", token));
    } else if let Ok(token) = std::env::var("GITHUB_TOKEN") {
        request = request.header("Authorization", &format!("token {}", token));
    }
    
    let response = request
        .send()
        .await
        .map_err(|e| format!("Network error: {}", e))?;
    
    if !response.status().is_success() {
        return Err(format!("GitHub API error: {}", response.status()));
    }
    
    let release: GitHubRelease = response
        .json()
        .await
        .map_err(|e| format!("Failed to parse response: {}", e))?;
    
    Ok(release.body)
} 