use std::sync::{Arc, Mutex};
use std::io::{BufRead, BufReader};
use std::thread;
use std::process::{Command, Stdio, Child};
use std::path::Path;
use crate::utils::error::{AppError, AppResult};

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[derive(Debug, Clone, Copy)]
pub enum ProcessType {
    Server,
    Launcher,
}

impl ProcessType {
    pub fn tag(&self) -> &'static str {
        match self {
            ProcessType::Server => "[SERVER]",
            ProcessType::Launcher => "[LAUNCHER]",
        }
    }
    
    pub fn error_tag(&self) -> &'static str {
        match self {
            ProcessType::Server => "[SERVER ERROR]",
            ProcessType::Launcher => "[LAUNCHER ERROR]",
        }
    }
}

pub struct ProcessInfo {
    pub path: String,
    pub working_dir: String,
    pub executable_name: String,
    pub full_exe_path: String,
}

impl ProcessInfo {
    pub fn from_path(path: &str) -> AppResult<Self> {
        let path_obj = Path::new(path);
        
        if !path_obj.exists() {
            return Err(AppError::ExecutableNotFound { path: path.to_string() });
        }
        
        let working_dir = path_obj.parent()
            .ok_or(AppError::WorkingDirError)?
            .to_string_lossy()
            .to_string();
            
        let executable_name = path_obj.file_name()
            .ok_or(AppError::ExecutableNameError)?
            .to_string_lossy()
            .to_string();
            
        let full_exe_path = format!("{}\\{}", working_dir, executable_name);
        
        Ok(Self {
            path: path.to_string(),
            working_dir,
            executable_name,
            full_exe_path,
        })
    }
}

pub async fn launch_process(
    path: &str,
    process_type: ProcessType,
    output_storage: &Arc<Mutex<Vec<String>>>,
    process_storage: &Arc<Mutex<Option<Child>>>
) -> AppResult<String> {
    let process_info = ProcessInfo::from_path(path)?;
    
    match Command::new("powershell")
        .args(&["-Command", &format!("& '{}'", process_info.full_exe_path)])
        .current_dir(&process_info.working_dir)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(0x08000000) // CREATE_NO_WINDOW flag
        .spawn() {
            Ok(mut child) => {
                // Take stdout and stderr before storing the child
                let stdout = child.stdout.take();
                let stderr = child.stderr.take();
                
                // Store the process handle
                if let Ok(mut process_guard) = process_storage.lock() {
                    *process_guard = Some(child);
                } else {
                    return Err(AppError::ProcessHandleError);
                }
                
                // Start output capture in separate threads
                if let (Some(stdout), Some(stderr)) = (stdout, stderr) {
                    let output_clone = output_storage.clone();
                    let tag = process_type.tag();
                    
                    thread::spawn(move || {
                        let reader = BufReader::new(stdout);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("{} {}", tag, line));
                                }
                            }
                        }
                    });
                    
                    let output_clone = output_storage.clone();
                    let error_tag = process_type.error_tag();
                    
                    thread::spawn(move || {
                        let reader = BufReader::new(stderr);
                        for line in reader.lines() {
                            if let Ok(line) = line {
                                if let Ok(mut output_guard) = output_clone.lock() {
                                    output_guard.push(format!("{} {}", error_tag, line));
                                }
                            }
                        }
                    });
                }
                
                Ok(format!("SUCCESS: {} launched successfully", process_type.tag().trim_matches('[').trim_matches(']')))
            },
            Err(e) => Err(AppError::ProcessError(e))
        }
}

pub async fn stop_process(
    process_storage: &Arc<Mutex<Option<Child>>>,
    output_storage: &Arc<Mutex<Vec<String>>>,
    process_name: &str
) -> AppResult<String> {
    if let Ok(mut process_guard) = process_storage.lock() {
        if let Some(mut child) = process_guard.take() {
            match child.kill() {
                Ok(_) => {
                    // Clear the output when stopping
                    if let Ok(mut output_vec) = output_storage.lock() {
                        output_vec.clear();
                    }
                    Ok(format!("SUCCESS: {} stopped successfully", process_name))
                },
                Err(e) => Err(AppError::ProcessStopError(e.to_string()))
            }
        } else {
            Err(AppError::NoProcessFound)
        }
    } else {
        Err(AppError::ProcessAccessError)
    }
}

pub fn get_output(output_storage: &Arc<Mutex<Vec<String>>>) -> AppResult<Vec<String>> {
    if let Ok(output_vec) = output_storage.lock() {
        Ok(output_vec.clone())
    } else {
        Err(AppError::OutputAccessError)
    }
}

pub fn clear_output(output_storage: &Arc<Mutex<Vec<String>>>) -> AppResult<String> {
    if let Ok(mut output_vec) = output_storage.lock() {
        output_vec.clear();
        Ok("SUCCESS: Output cleared".to_string())
    } else {
        Err(AppError::OutputClearError)
    }
} 