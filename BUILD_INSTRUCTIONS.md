# SPT-AKI Launcher - Tauri Version

## Prerequisites

### 1. Install Rust

```bash
# Download and install Rust from https://rustup.rs/
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
# Or download from https://www.rust-lang.org/tools/install
```

### 2. Install Node.js (if not already installed)

- Download from https://nodejs.org/
- Version 16 or higher

## Build Steps

### Step 1: Install dependencies

```bash
npm install
```

### Step 2: Development mode

```bash
npm run tauri dev
```

### Step 3: Build for production

```bash
npm run tauri build
```

## What this creates:

- **Single `.exe` file** (much smaller than Electron)
- **No external dependencies** - everything is bundled
- **Better performance** - native Rust backend
- **Smaller file size** - typically 5-10MB vs 150MB+

## Features:

- ✅ File selection for server and launcher executables
- ✅ Start server and launcher processes
- ✅ Modern UI with your existing design
- ✅ Single-file distribution

## Manual Commands:

```bash
cd spt-aki-launcher-tauri
npm install
npm run tauri build
```

The executable will be in `src-tauri/target/release/` folder.
