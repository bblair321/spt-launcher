@echo off
echo Building SPT-AKI Launcher...
echo.

npm run tauri build

if %errorlevel% equ 0 (
    echo.
    echo Build successful! Executable created at:
    echo src-tauri\target\release\spt-aki-launcher-tauri.exe
    echo.
) else (
    echo.
    echo Build failed! Check the error messages above.
    echo.
)

pause 