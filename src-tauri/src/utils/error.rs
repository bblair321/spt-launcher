use thiserror::Error;

#[derive(Debug, Error)]
pub enum AppError {
    #[error("No path set for {component}")]
    NoPathSet { component: String },
    
    #[error("Executable not found: {path}")]
    ExecutableNotFound { path: String },
    
    #[error("Failed to access {component} path")]
    PathAccessError { component: String },
    
    #[error("Could not determine working directory")]
    WorkingDirError,
    
    #[error("Could not determine executable name")]
    ExecutableNameError,
    
    #[error("Failed to store process handle")]
    ProcessHandleError,
    
    #[error("Failed to access process")]
    ProcessAccessError,
    
    #[error("No process found")]
    NoProcessFound,
    
    #[error("Failed to stop process: {0}")]
    ProcessStopError(String),
    
    #[error("Failed to get app data directory")]
    AppDataDirError,
    
    #[error("Failed to create config directory: {0}")]
    ConfigDirError(String),
    
    #[error("Failed to serialize config: {0}")]
    ConfigSerializeError(String),
    
    #[error("Failed to write config file: {0}")]
    ConfigWriteError(String),
    
    #[error("Failed to read config file: {0}")]
    ConfigReadError(String),
    
    #[error("Failed to parse config file: {0}")]
    ConfigParseError(String),
    
    #[error("No configuration file found")]
    NoConfigFile,
    
    #[error("Failed to delete config file: {0}")]
    ConfigDeleteError(String),
    
    #[error("Failed to access output")]
    OutputAccessError,
    
    #[error("Failed to clear output")]
    OutputClearError,
    
    #[error("Failed to get file selection result")]
    FileSelectionError,
    
    #[error("No file selected")]
    NoFileSelected,
    
    #[error("Invalid path format")]
    InvalidPathFormat,
    
    #[error("No path provided")]
    NoPathProvided,
    
    #[error("Window operation failed: {0}")]
    WindowError(String),
    
    #[error("Process error: {0}")]
    ProcessError(#[from] std::io::Error),
    
    #[error("Validation error in {field}: {message}")]
    ValidationError { field: String, message: String },
}

pub type AppResult<T> = Result<T, AppError>; 