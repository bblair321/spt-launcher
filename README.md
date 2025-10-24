# SPT Launcher WPF

A modern Windows Presentation Foundation (WPF) launcher for SPT-AKI with Fika Co-op support, replacing the previous Electron-based version.

## Features

- **Modern WPF Interface**: Native Windows application with modern UI design
- **SPT-AKI Support**: Launch and manage SPT-AKI instances
- **Fika Co-op Integration**: Connect to Fika servers for multiplayer
- **Server Management**: Manage local and remote servers
- **Mod Management**: Install and manage SPT mods
- **Settings Configuration**: Customize launcher behavior
- **Developer Tools**: Built-in debugging and development tools

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- SPT-AKI installation

## Installation

1. Download the latest release from the [Releases](https://github.com/bblair321/spt-launcher/releases) page
2. Extract the files to your desired location
3. Run `SptLauncherWpf.exe`

## Building from Source

### Prerequisites

- Visual Studio 2022 or later
- .NET 8.0 SDK


## Usage

### First Time Setup

1. Launch the application
2. Navigate to the **Settings** tab
3. Set your SPT-AKI installation path
4. Configure your preferences

### Launching SPT-AKI

1. Go to the **Launcher** tab
2. Select your server type (Local or Remote)
3. Click **Configure** to set up your server
4. Click **Launch** to start SPT-AKI

### Server Management

1. Go to the **Servers** tab
2. Choose between Local Server or Remote Server (Fika)
3. Configure server settings
4. Use **Auto Start** for automatic server startup

### Mod Management

1. Go to the **Mods** tab
2. Install mods from the SPT mod repository
3. Enable/disable mods as needed
4. Manage mod configurations

## Configuration

The launcher stores its configuration in:

- **Settings**: `%APPDATA%\SPT Launcher WPF\settings.json`
- **Server Configs**: `%APPDATA%\SPT Launcher WPF\servers.json`

## Troubleshooting

### Common Issues

**Application won't start:**

- Ensure .NET 8.0 Runtime is installed
- Check Windows compatibility
- Run as administrator if needed

**SPT-AKI won't launch:**

- Verify SPT-AKI installation path in Settings
- Check that SPT-AKI is properly installed
- Ensure no antivirus is blocking the process

**Server connection issues:**

- Verify server settings in the Servers tab
- Check network connectivity
- Ensure firewall allows the connection


### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## Migration from Electron Version

This WPF version replaces the previous Electron-based launcher. Key improvements:

- **Better Performance**: Native Windows application
- **Lower Resource Usage**: No Chromium overhead
- **Better Integration**: Native Windows UI components
- **Faster Startup**: No Electron initialization time

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- SPT-AKI team for the amazing mod
- Fika Co-op team for multiplayer support
- The SPT community for feedback and contributions

## Support

For support, please:

1. Check the [Issues](https://github.com/bblair321/spt-launcher/issues) page
2. Search for existing solutions
3. Create a new issue with detailed information

---

**Note**: This WPF version replaces the previous Electron-based launcher. The Electron version is no longer maintained.
