using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using SptLauncherWpf.Controls;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Pages
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        public const int GWL_STYLE = -16;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_CHILD = 0x40000000;
    }

    public partial class ServersPage : Page
    {
        private ObservableCollection<ServerInfo> _servers = new();
        private ServerInfo? _editingServer = null;
        private Dictionary<string, Process> _runningServers = new();

        public ServersPage()
        {
            InitializeComponent();
            LoadServers();
            UpdateServersList();
        }

        private void LoadServers()
        {
            try
            {
                var serversPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");
                if (File.Exists(serversPath))
                {
                    var json = File.ReadAllText(serversPath);
                    var servers = JsonSerializer.Deserialize<List<ServerInfo>>(json);
                    if (servers != null)
                    {
                        _servers.Clear();
                        foreach (var server in servers)
                        {
                            _servers.Add(server);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load servers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveServers()
        {
            try
            {
                var serversPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");
                var json = JsonSerializer.Serialize(_servers.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(serversPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save servers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateServersList()
        {
            if (ServersListPanel == null) return;

            // Clear existing server UI elements
            ServersListPanel.Children.Clear();

            // Add each server as a UI element
            foreach (var server in _servers)
            {
                var serverCard = CreateServerCard(server);
                ServersListPanel.Children.Add(serverCard);
            }
        }

        private Border CreateServerCard(ServerInfo server)
        {
            var card = new Border
            {
                Style = (Style)FindResource("ModernCardStyle"),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16)
            };

            var stackPanel = new StackPanel();

            // Server name and type
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var nameText = new TextBlock
            {
                Text = server.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var typeText = new TextBlock
            {
                Text = $"({server.ServerType.ToUpper()})",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            headerPanel.Children.Add(nameText);
            headerPanel.Children.Add(typeText);

            // Server details
            var detailsText = new TextBlock
            {
                Text = $"Path: {server.Path}\nPort: {server.Port}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var launchButton = new Button
            {
                Content = "Launch",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            launchButton.Click += (s, e) => LaunchServer(server);

            var stopButton = new Button
            {
                Content = "Stop",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            stopButton.Click += (s, e) => StopServer(server);

            var deleteButton = new Button
            {
                Content = "Delete",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            deleteButton.Click += (s, e) => DeleteServer(server);

            buttonPanel.Children.Add(launchButton);
            buttonPanel.Children.Add(stopButton);
            buttonPanel.Children.Add(deleteButton);

            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(detailsText);
            stackPanel.Children.Add(buttonPanel);

            card.Child = stackPanel;
            return card;
        }

        private void DeleteServer(ServerInfo server)
        {
            var result = MessageBox.Show($"Are you sure you want to delete '{server.Name}'?", 
                "Delete Server", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _servers.Remove(server);
                SaveServers();
                UpdateServersList();
            }
        }

        private void AddLogOutput(string message)
        {
            // For now, we'll just show a simple message since we're using embedded console
            // The actual server output will be shown in the embedded console control
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private string CleanAnsiCodes(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Remove ANSI escape sequences (color codes, cursor movements, etc.)
            var ansiPattern = @"\x1B\[[0-9;]*[mK]";
            var cleaned = System.Text.RegularExpressions.Regex.Replace(input, ansiPattern, "");
            
            // Also remove other common escape sequences
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\x1B\[[0-9;]*[A-Za-z]", "");
            
            return cleaned.Trim();
        }

        private void SaveServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Read current form values
            var serverName = ServerNameTextBox?.Text ?? "New Server";
            var serverPath = ServerPathTextBox?.Text ?? "";
            var serverType = LocalServerRadio?.IsChecked == true ? "local" : "remote";
            var serverPort = PortTextBox?.Text ?? "6969";
            var remoteAddress = RemoteAddressTextBox?.Text ?? "";
            var remotePort = RemotePortTextBox?.Text ?? "6969";
            var description = DescriptionTextBox?.Text ?? "";
            var autoStart = AutoStartCheckBox?.IsChecked == true;

            _editingServer = new ServerInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = serverName,
                ServerType = serverType,
                Path = serverPath,
                RemoteAddress = remoteAddress,
                RemotePort = remotePort,
                Port = serverPort,
                Description = description,
                AutoStart = autoStart,
                CreatedAt = DateTime.Now
            };

            _servers.Add(_editingServer);
            SaveServers();
            UpdateServersList();
        }

        private void EditServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Edit server functionality - will be implemented with proper UI controls
        }

        private void DeleteServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Delete server functionality - will be implemented with proper UI controls
        }


        private void CancelServerButton_Click(object sender, RoutedEventArgs e)
        {
            _editingServer = null;
        }

        private void BrowseServerButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select SPT-AKI Server Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Update the server path text box
                if (ServerPathTextBox != null)
                {
                    ServerPathTextBox.Text = openFileDialog.FileName;
                }
                
                // Update the current editing server's path if we have one
                if (_editingServer != null)
                {
                    _editingServer.Path = openFileDialog.FileName;
                }
            }
        }

        private void LaunchServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Launch server functionality - will be implemented with proper UI controls
        }

        private void StopServer(ServerInfo server)
        {
            if (!_runningServers.ContainsKey(server.Id))
            {
                MessageBox.Show("Server is not currently running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Get the console control and stop the process
                if (ConsoleContainer.Child is EmbeddedConsoleControl consoleControl)
                {
                    consoleControl.StopProcess();
                    AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Stopping server: {server.Name}");
                }

                // Remove from running servers
                _runningServers.Remove(server.Id);
                
                // Hide console and show placeholder
                ConsoleContainer.Child = null;
                ConsoleContainer.Visibility = Visibility.Collapsed;
                NoServerRunningText.Visibility = Visibility.Visible;
                
                UpdateServersList();
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Server stopped: {server.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuickConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Quick connect functionality - will be implemented with proper UI controls
        }

        private void ServerType_Changed(object sender, RoutedEventArgs e)
        {
            // Server type change functionality - will be implemented with proper UI controls
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel edit functionality - will be implemented with proper UI controls
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear the embedded console if it exists
            if (ConsoleContainer.Child is EmbeddedConsoleControl consoleControl)
            {
                consoleControl.ClearConsole();
                AddLogOutput("Console cleared.");
            }
        }


        private async void LaunchServer(ServerInfo server)
        {
            if (_runningServers.ContainsKey(server.Id))
            {
                MessageBox.Show("Server is already running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Get the server path - either from the server object or from the form
            string serverPath = server.Path;
            if (string.IsNullOrEmpty(serverPath) && ServerPathTextBox != null)
            {
                serverPath = ServerPathTextBox.Text;
            }

            if (string.IsNullOrEmpty(serverPath) || !File.Exists(serverPath))
            {
                MessageBox.Show($"Server executable not found at: {serverPath}\n\nPlease use the Browse button to select the correct server executable.", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Create embedded console control
                var consoleControl = new SptLauncherWpf.Controls.EmbeddedConsoleControl();
                
                // Add console to the container
                ConsoleContainer.Child = consoleControl;
                ConsoleContainer.Visibility = Visibility.Visible;
                NoServerRunningText.Visibility = Visibility.Collapsed;

                // Start the server in the embedded console
                consoleControl.StartProcess(serverPath);

                // Store reference to the console control (we'll track it differently)
                _runningServers[server.Id] = null; // We'll track the console control instead
                
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Starting server: {server.Name} ({serverPath})");
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Server console embedded in launcher.");
                UpdateServersList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _runningServers.Remove(server.Id);
                UpdateServersList();
            }
        }


        private async void QuickConnectToServer(ServerInfo server)
        {
            if (server.ServerType != "remote")
            {
                MessageBox.Show("Quick connect is only available for remote servers", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                await TestRemoteServer(server);
                MessageBox.Show($"Server connection info: {server.RemoteAddress}:{server.RemotePort}\nUse this info when prompted in Tarkov", "Server Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Quick connect failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TestRemoteServer(ServerInfo server)
        {
            try
            {
                // Simple connectivity test - you could implement actual server ping here
                await Task.Delay(1000); // Simulate network test
            }
            catch (Exception ex)
            {
                throw new Exception($"Connection test failed: {ex.Message}");
            }
        }
    }

    public class ServerInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ServerType { get; set; } = "local";
        public string Path { get; set; } = "";
        public string RemoteAddress { get; set; } = "";
        public string RemotePort { get; set; } = "6969";
        public string Port { get; set; } = "6969";
        public string Description { get; set; } = "";
        public bool AutoStart { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}