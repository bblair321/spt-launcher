# SPT Launcher Electron

A comprehensive desktop launcher for SPT-AKI with Fika co-op support, built with Electron and React.

## ✨ Features

- **SPT-AKI Management**: Launch and manage SPT-AKI installations
- **Fika Co-op Support**: Connect to remote Fika co-op servers
- **Remote Server Management**: Add, test, and quick-connect to remote servers
- **Auto-Restart**: Automatic launcher restart when changing Fika configuration
- **Process Monitoring**: Real-time process status monitoring
- **Modern UI**: Clean, responsive interface built with Tailwind CSS

## 🏗️ Project Structure

```
spt-launcher-electron/
├── src/
│   ├── components/          # React components
│   │   ├── ui/             # Reusable UI components
│   │   │   ├── StatusCard.jsx
│   │   │   ├── PathInput.jsx
│   │   │   └── LoadingSpinner.jsx
│   │   ├── LauncherTab.jsx
│   │   ├── ServersTab.jsx
│   │   ├── AddonsTab.jsx
│   │   ├── SettingsTab.jsx
│   │   ├── DevToolsTab.jsx
│   │   ├── SearchTab.jsx
│   │   ├── MessageBoard.jsx
│   │   └── ErrorBoundary.jsx
│   ├── hooks/               # Custom React hooks
│   │   ├── useLocalStorage.js
│   │   └── useProcessMonitor.js
│   ├── utils/               # Utility functions
│   │   ├── pathUtils.js
│   │   └── statusUtils.js
│   ├── constants/           # Application constants
│   │   └── index.js
│   ├── App.jsx             # Main application component
│   ├── main.jsx            # React entry point
│   └── index.css           # Global styles
├── electron/                # Electron main process
│   ├── main.js             # Main process
│   └── preload.js          # Preload script
├── public/                  # Static assets
├── dist/                    # Build output
└── package.json            # Project configuration
```

## 🚀 Getting Started

### Prerequisites

- Node.js 18+
- npm or yarn

### Installation

1. **Clone the repository**

   ```bash
   git clone https://github.com/bblair321/spt-launcher.git
   cd spt-launcher
   ```

2. **Install dependencies**

   ```bash
   npm install
   ```

3. **Run in development mode**

   ```bash
   npm run electron:dev
   ```

4. **Build for production**
   ```bash
   npm run electron:build
   ```

## 🔧 Development

### Available Scripts

- `npm run dev` - Start Vite dev server
- `npm run build` - Build React app
- `npm run electron` - Start Electron (requires dev server)
- `npm run electron:dev` - Start both dev server and Electron
- `npm run electron:build` - Build and package Electron app
- `npm run dist` - Build and create distribution packages

### Code Quality

The project follows modern React best practices:

- **Custom Hooks**: Reusable state logic (`useLocalStorage`, `useProcessMonitor`)
- **Utility Functions**: Centralized helper functions (`pathUtils`, `statusUtils`)
- **Reusable Components**: Modular UI components (`StatusCard`, `PathInput`)
- **Constants**: Centralized configuration values
- **Error Boundaries**: Graceful error handling
- **Performance**: Optimized with `useMemo` and `useCallback`

### Architecture

- **Frontend**: React 18 with modern hooks
- **Backend**: Electron main process with IPC communication
- **Styling**: Tailwind CSS with custom components
- **State Management**: React hooks with localStorage persistence
- **Process Management**: Node.js child_process with monitoring

## 📱 Features

### Launcher Tab

- SPT-AKI launcher path configuration
- Launch/stop SPT launcher
- Process status monitoring
- Fika co-op configuration

### Servers Tab

- Local SPT server management
- Remote Fika server configuration
- Server testing and quick-connect
- Server list management

### Fika Co-op Support

- Enable/disable Fika mode
- Remote server configuration
- Automatic launcher restart
- Configuration persistence

## 🎯 Key Improvements

### Code Organization

- **Modular Structure**: Clear separation of concerns
- **Reusable Components**: Consistent UI patterns
- **Custom Hooks**: Shared state logic
- **Utility Functions**: Centralized helpers

### Performance

- **Memoization**: Optimized re-renders
- **Process Monitoring**: Efficient status checking
- **Lazy Loading**: Component-based code splitting

### User Experience

- **Error Boundaries**: Graceful error handling
- **Loading States**: Visual feedback during operations
- **Auto-Restart**: Seamless configuration changes
- **Responsive Design**: Modern, accessible interface

### Maintainability

- **Constants**: Centralized configuration
- **Type Safety**: Better code documentation
- **Error Handling**: Comprehensive error management
- **Testing Ready**: Modular, testable components

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License.

## 🙏 Acknowledgments

- SPT-AKI team for the modded server
- Fika co-op mod developers
- Electron and React communities
