# SPT-AKI Launcher

A modern, single-executable desktop launcher for SPT-AKI (Single Player Tarkov - AKI) that provides an intuitive interface to manage your SPT-AKI server and launcher from one convenient application.

## 🎯 Features
- **Server Management**: Start and stop your SPT-AKI server with one click
- **Launcher Control**: Launch the SPT-AKI launcher directly from the application
- **Real-time Status**: Monitor server and launcher status in real-time
- **Port Monitoring**: Check if the default SPT-AKI port (6969) is available
- **File Selection**: Easy path selection for server and launcher executables
- **Configuration**: Save and load your preferred settings

## 🚀 Quick Start

1. **Download** the latest release from the releases page
2. **Run** the `SPT-AKI Launcher_1.0.0_x64-setup.exe` installer
3. **Launch** the application from your desktop or start menu
4. **Configure** your SPT-AKI server and launcher paths
5. **Start** your server and launcher with one click!


## 🎮 How to Use

### Initial Setup
1. Click **"Browse"** next to Server Path to select your `SPT.Server.exe`
2. Click **"Browse"** next to Launcher Path to select your `SPT.Launcher.exe`
3. Click **"Save Config"** to remember your settings

### Starting Your Server
1. Click **"Start Server"** to launch your SPT-AKI server
2. Monitor the server status in the System Status section
3. Check the Server Log tab for real-time output

### Starting the Launcher
1. Click **"Start Launcher"** to open the SPT-AKI launcher
2. Monitor the launcher status in the System Status section

### Stopping Services
- Click **"Stop Server"** to terminate the server process
- Click **"Stop Launcher"** to close the launcher

## 🔧 Development

### Prerequisites
- **Node.js** (v16 or higher)
- **Rust** (latest stable)
- **npm** or **yarn**

### Building from Source
```bash
# Clone the repository
git clone https://github.com/yourusername/spt-aki-launcher.git
cd spt-aki-launcher

# Install dependencies
npm install

# Build the application
npm run tauri build
```

### Development Mode
```bash
# Start development server
npm run tauri dev
``` README.md              # This file

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **SPT-AKI Team** for creating the amazing single-player Tarkov experience
- **Tauri Team** for the excellent desktop application framework
- **Escape from Tarkov** developers for the original game