# SPT Launcher - Simple Installer Solution

## What We're Doing

Instead of fighting with electron-builder's NSIS target and code signing issues, we're using a **two-step approach**:

1. **electron-builder** creates a portable executable (this works reliably)
2. **Inno Setup** creates a professional installer from the portable executable

## Why This Approach Works

- ✅ **electron-builder portable target** - No code signing issues
- ✅ **Inno Setup** - Professional installer with full control
- ✅ **User choice** - Users can pick installation location
- ✅ **Desktop shortcuts** - Automatic shortcut creation
- ✅ **Start menu** - Proper Windows integration
- ✅ **Uninstaller** - Clean removal from Add/Remove Programs

## Prerequisites

### 1. Install Inno Setup
Download and install from: https://jrsoftware.org/isdl.php

**Important**: During installation, make sure to check "Add Inno Setup directory to the PATH"

### 2. Verify Installation
Open Command Prompt and run:
```cmd
iscc
```
You should see Inno Setup Compiler help.

## Building the Installer

### Option 1: Automated (Recommended)
```cmd
build-installer.bat
```

This script will:
1. Build the application with electron-builder
2. Create the installer with Inno Setup
3. Output: `release\SPT-Launcher-Setup-2.0.0.exe`

### Option 2: Manual Steps
```cmd
# Step 1: Build the application
npm run dist:win

# Step 2: Build the installer
iscc installer.iss
```

## What You Get

### From electron-builder
- `release\win-unpacked\` - Portable application folder
- `release\SPT Launcher 2.0.0.exe` - Portable executable

### From Inno Setup
- `release\SPT-Launcher-Setup-2.0.0.exe` - Professional installer

## Installer Features

- **Installation Location**: User can choose where to install
- **Desktop Shortcut**: Optional desktop icon creation
- **Start Menu**: Proper Windows Start Menu integration
- **Uninstaller**: Clean removal from Control Panel
- **Modern UI**: Professional installer appearance

## Customization

### Modify installer.iss
- Change `AppName`, `AppVersion`, `AppPublisher`
- Adjust `DefaultDirName` for different default location
- Modify `Tasks` for different shortcut options
- Add custom pages or options

### Modify build-installer.bat
- Change build commands
- Add additional build steps
- Customize error handling

## Troubleshooting

### "iscc not found"
- Reinstall Inno Setup
- Make sure "Add to PATH" is checked
- Restart Command Prompt after installation

### Build fails
- Check `npm run dist:win` works first
- Verify all dependencies are installed
- Check for syntax errors in installer.iss

### Installer doesn't work
- Test the portable executable first
- Check Windows compatibility
- Verify installer.iss syntax

## Advantages Over Previous Approaches

| Approach | electron-builder NSIS | GitHub Actions | electron-builder + Inno Setup |
|----------|----------------------|----------------|------------------------------|
| **Reliability** | ❌ Broken on Windows | ❌ Complex setup | ✅ Simple and reliable |
| **Code Signing** | ❌ Always fails | ✅ No issues | ✅ No signing needed |
| **User Control** | ❌ Limited options | ✅ Full control | ✅ Full control |
| **Setup Time** | ❌ Hours of debugging | ❌ Complex workflows | ✅ 5 minutes setup |
| **Maintenance** | ❌ Constant issues | ❌ Complex debugging | ✅ Simple scripts |

## Next Steps

1. **Install Inno Setup** from the link above
2. **Run `build-installer.bat`** to create your first installer
3. **Test the installer** on a clean system
4. **Customize** installer.iss for your needs
5. **Distribute** the installer to users

This approach gives you everything you wanted:
- ✅ Users can pick installation location
- ✅ Desktop shortcuts are created
- ✅ Professional installer experience
- ✅ No code signing issues
- ✅ Reliable builds every time

Much simpler than the complex solutions we tried before! 🎉
