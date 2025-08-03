use std::path::Path;
use std::fs;
use crate::models::Config;
use crate::utils::error::{AppError, AppResult};

/// Validates a file path exists and is accessible
pub fn validate_file_path(path: &str) -> AppResult<()> {
    if path.is_empty() {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: "Path cannot be empty".to_string(),
        });
    }

    let path_obj = Path::new(path);
    
    // Check if path exists
    if !path_obj.exists() {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: format!("Path does not exist: {}", path),
        });
    }

    // Check if it's a file (not a directory)
    if !path_obj.is_file() {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: format!("Path is not a file: {}", path),
        });
    }

    // Check file permissions (readable)
    if let Err(_) = fs::metadata(path_obj) {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: format!("Cannot access file: {}", path),
        });
    }

    // Validate file extension for executables
    if let Some(extension) = path_obj.extension() {
        let ext = extension.to_string_lossy().to_lowercase();
        if ext != "exe" {
            return Err(AppError::ValidationError {
                field: "path".to_string(),
                message: format!("File must be an executable (.exe), found: .{}", ext),
            });
        }
    } else {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: "File must have an extension".to_string(),
        });
    }

    Ok(())
}

/// Validates a port number is within valid range
pub fn validate_port(port: u16) -> AppResult<()> {
    if port == 0 {
        return Err(AppError::ValidationError {
            field: "port".to_string(),
            message: "Port cannot be 0".to_string(),
        });
    }

    // Check if port is in reserved range (1-1023)
    if port <= 1023 {
        return Err(AppError::ValidationError {
            field: "port".to_string(),
            message: format!("Port {} is in reserved range (1-1023)", port),
        });
    }

    Ok(())
}

/// Validates log level is one of the allowed values
pub fn validate_log_level(level: &str) -> AppResult<()> {
    let valid_levels = ["Normal", "Verbose", "Debug", "Error"];
    
    if !valid_levels.contains(&level) {
        return Err(AppError::ValidationError {
            field: "log_level".to_string(),
            message: format!("Invalid log level: {}. Must be one of: {:?}", level, valid_levels),
        });
    }

    Ok(())
}

/// Validates refresh interval is reasonable
pub fn validate_refresh_interval(interval: u64) -> AppResult<()> {
    if interval < 100 {
        return Err(AppError::ValidationError {
            field: "refresh_interval".to_string(),
            message: "Refresh interval must be at least 100ms".to_string(),
        });
    }

    if interval > 60000 {
        return Err(AppError::ValidationError {
            field: "refresh_interval".to_string(),
            message: "Refresh interval cannot exceed 60000ms (60 seconds)".to_string(),
        });
    }

    Ok(())
}

/// Validates max log lines is reasonable
pub fn validate_max_log_lines(lines: usize) -> AppResult<()> {
    if lines < 100 {
        return Err(AppError::ValidationError {
            field: "max_log_lines".to_string(),
            message: "Max log lines must be at least 100".to_string(),
        });
    }

    if lines > 100000 {
        return Err(AppError::ValidationError {
            field: "max_log_lines".to_string(),
            message: "Max log lines cannot exceed 100,000".to_string(),
        });
    }

    Ok(())
}

/// Comprehensive configuration validation
pub fn validate_config(config: &Config) -> AppResult<()> {
    // Validate server path if provided
    if let Some(ref server_path) = config.server_path {
        validate_file_path(server_path)?;
    }

    // Validate launcher path if provided
    if let Some(ref launcher_path) = config.launcher_path {
        validate_file_path(launcher_path)?;
    }

    // Validate port
    validate_port(config.server_port)?;

    // Validate log level
    validate_log_level(&config.log_level)?;

    // Validate refresh interval
    validate_refresh_interval(config.refresh_interval)?;

    // Validate max log lines
    validate_max_log_lines(config.max_log_lines)?;

    Ok(())
}

/// Sanitizes a file path to prevent path traversal attacks
pub fn sanitize_path(path: &str) -> AppResult<String> {
    let _path_obj = Path::new(path);
    
    // Check for path traversal attempts
    if path.contains("..") || path.contains("\\..") || path.contains("/..") {
        return Err(AppError::ValidationError {
            field: "path".to_string(),
            message: "Path contains invalid traversal characters".to_string(),
        });
    }

    // Normalize path separators
    let normalized = path.replace("/", "\\");
    
    Ok(normalized)
}

/// Validates that a port is available for binding
pub fn validate_port_available(port: u16) -> AppResult<()> {
    use std::net::TcpListener;
    
    match TcpListener::bind(format!("127.0.0.1:{}", port)) {
        Ok(_) => Ok(()),
        Err(_) => Err(AppError::ValidationError {
            field: "port".to_string(),
            message: format!("Port {} is already in use", port),
        }),
    }
} 