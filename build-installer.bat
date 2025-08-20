@echo off
echo Building SPT Launcher Installer...
echo.

REM Check if Inno Setup is installed
where iscc >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Inno Setup (iscc) not found in PATH
    echo Please install Inno Setup from: https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

REM Build the application
echo Building application...
npm run dist:win
if %errorlevel% neq 0 (
    echo ERROR: Application build failed
    pause
    exit /b 1
)

REM Build the installer
echo Building installer...
iscc installer.iss
if %errorlevel% equ 0 (
    echo.
    echo SUCCESS: Installer created successfully!
    echo Output: release\SPT-Launcher-Setup-2.0.0.exe
    echo.
) else (
    echo.
    echo ERROR: Failed to build installer
    echo.
)

pause
