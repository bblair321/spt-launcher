# SPT Launcher - GitHub Actions Alternative

## Why GitHub Actions?

Since electron-builder has a fundamental bug with code signing on Windows that prevents NSIS installers from working, **GitHub Actions provides a perfect alternative** by building on Linux runners where these issues don't exist.

## How It Works

1. **Build on Linux**: GitHub Actions runs on Ubuntu runners, avoiding Windows-specific code signing issues
2. **Cross-compile for Windows**: Electron can build Windows apps from Linux
3. **Automatic releases**: Create GitHub releases automatically when you push tags
4. **No local setup**: No need to install electron-builder or deal with signing issues locally

## Available Workflows

### 1. Simple Build (`build-simple.yml`)

- **Triggers**: Push to main/develop, pull requests, manual
- **Output**: Portable ZIP package
- **Use case**: Development builds, testing

### 2. Full Release (`build.yml`)

- **Triggers**: Push tags (v\*), manual
- **Output**: Complete release with installers
- **Use case**: Production releases

## Getting Started

### Option 1: Use Existing Workflows

1. **Push your code** to GitHub
2. **Go to Actions tab** in your repository
3. **Select a workflow** and click "Run workflow"
4. **Download artifacts** when complete

### Option 2: Trigger with Tags

```bash
# Create and push a tag to trigger release build
git tag v2.0.0
git push origin v2.0.0
```

### Option 3: Manual Trigger

1. Go to Actions tab
2. Select workflow
3. Click "Run workflow"
4. Choose branch and run

## What You Get

### From Simple Build

- ✅ **Portable ZIP package** - Extract and run anywhere
- ✅ **No signing issues** - Built on Linux, runs on Windows
- ✅ **Automatic builds** - Every push creates a new build
- ✅ **Easy distribution** - Single ZIP file for users

### From Full Release

- ✅ **All simple build features** +
- ✅ **Install scripts** - `install.bat`, `uninstall.bat`
- ✅ **Inno Setup config** - Professional installer template
- ✅ **GitHub release** - Automatic release page
- ✅ **Multiple formats** - ZIP, scripts, and configs

## Installation Options for Users

### 1. Portable (Recommended)

1. Download the ZIP file
2. Extract to any location
3. Run `SPT Launcher.exe`

### 2. Simple Installer

1. Download the ZIP file
2. Extract and run `install.bat` as Administrator
3. Follow prompts to choose location

### 3. Professional Installer

1. Download the ZIP file
2. Install Inno Setup from https://jrsoftware.org/isdl.php
3. Open `installer.iss` in Inno Setup Compiler
4. Build and run the installer

## Advantages Over Local Building

| Aspect           | Local electron-builder   | GitHub Actions             |
| ---------------- | ------------------------ | -------------------------- |
| **Code Signing** | ❌ Broken on Windows     | ✅ No issues (Linux)       |
| **Dependencies** | ❌ Complex setup         | ✅ Automatic               |
| **Consistency**  | ❌ Environment dependent | ✅ Always same environment |
| **Automation**   | ❌ Manual process        | ✅ Fully automated         |
| **Distribution** | ❌ Manual upload         | ✅ Automatic releases      |
| **CI/CD**        | ❌ Separate setup        | ✅ Built-in                |

## Next Steps

1. **Push your code** to GitHub
2. **Test the workflows** by running them manually
3. **Create a release** by pushing a tag
4. **Distribute** the built packages to users

## Example Usage

```bash
# Development workflow
git push origin main  # Triggers simple build

# Release workflow
git tag v2.0.1
git push origin v2.0.1  # Triggers full release
```

This approach completely bypasses electron-builder's Windows code signing issues while providing a professional, automated build and release process!
