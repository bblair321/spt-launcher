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
        private Dictionary<string, Process?> _runningServers = new();
        
        // Static reference to preserve console output across tab switches
        private static string _staticConsoleOutput = "";
        private static bool _staticServerRunning = false;

        public ServersPage()
        {
            InitializeComponent();
            LoadServers();
            UpdateServersList();
            
            // Restore console state if server was running from previous tab switch
            if (_staticServerRunning && !string.IsNullOrEmpty(_staticConsoleOutput))
            {
                // Create a new console control and populate it with the saved output
                var consoleControl = new SptLauncherWpf.Controls.EmbeddedConsoleControl();
                consoleControl.SetOutput(_staticConsoleOutput);
                
                ConsoleContainer.Child = consoleControl;
                ConsoleContainer.Visibility = Visibility.Visible;
                NoServerRunningText.Visibility = Visibility.Collapsed;
            }
        }

        private void ServersPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Page loaded - no debug needed
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
                System.Windows.MessageBox.Show($"Failed to load servers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                System.Windows.MessageBox.Show($"Failed to save servers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var nameText = new TextBlock
            {
                Text = server.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryColor"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var typeText = new TextBlock
            {
                Text = $"({server.ServerType.ToUpper()})",
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor"),
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
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Action buttons
            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            var launchButton = new System.Windows.Controls.Button
            {
                Content = "Launch",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            launchButton.Click += (s, e) => LaunchServer(server);

            var stopButton = new System.Windows.Controls.Button
            {
                Content = "Stop",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12
            };
            stopButton.Click += (s, e) => StopServer(server);

            var deleteButton = new System.Windows.Controls.Button
            {
                Content = "Delete",
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)),
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
            var result = System.Windows.MessageBox.Show($"Are you sure you want to delete '{server.Name}'?", 
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
            var serverType = "local"; // Only local servers now
            var serverPort = PortTextBox?.Text ?? "6969";
            var description = DescriptionTextBox?.Text ?? "";
            var autoStart = AutoStartCheckBox?.IsChecked == true;

            _editingServer = new ServerInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = serverName,
                ServerType = serverType,
                Path = serverPath,
                RemoteAddress = "", // No longer used
                RemotePort = "", // No longer used
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
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
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
            // Check for running processes directly instead of relying on _runningServers dictionary
            var runningProcesses = System.Diagnostics.Process.GetProcessesByName("SPT.Server");
            
            if (runningProcesses.Length == 0)
            {
                System.Windows.MessageBox.Show($"No SPT.Server processes are currently running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Stop all SPT.Server processes directly
                foreach (var process in runningProcesses)
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill();
                        AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Stopping server: {server.Name} (PID: {process.Id})");
                    }
                }
                
                // Also stop the console control if it exists
                if (ConsoleContainer.Child is EmbeddedConsoleControl consoleControl)
                {
                    consoleControl.StopProcess();
                }

                // Remove from running servers (if it exists)
                _runningServers.Remove(server.Id);
                
                // Clear static console state
                _staticServerRunning = false;
                _staticConsoleOutput = "";
                
                // Hide console and show placeholder
                ConsoleContainer.Child = null;
                ConsoleContainer.Visibility = Visibility.Collapsed;
                NoServerRunningText.Visibility = Visibility.Visible;
                
                UpdateServersList();
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Server stopped: {server.Name}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to stop server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ServersListScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            // Check if the inner ScrollViewer can scroll in the direction of the wheel
            bool canScrollDown = e.Delta < 0 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 1;
            bool canScrollUp = e.Delta > 0 && scrollViewer.VerticalOffset > 1;

            // If the inner ScrollViewer can scroll, let it handle it
            if (canScrollDown || canScrollUp)
            {
                return; // Let the inner ScrollViewer handle it
            }

            // If the inner ScrollViewer can't scroll in this direction, pass the event to the parent
            var parentScrollViewer = FindParentScrollViewer(scrollViewer);
            if (parentScrollViewer != null)
            {
                // Scroll the parent ScrollViewer
                var newOffset = parentScrollViewer.VerticalOffset - (e.Delta / 3.0);
                parentScrollViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(newOffset, parentScrollViewer.ScrollableHeight)));
                e.Handled = true;
            }
        }

        private ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
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


        private void LaunchServer(ServerInfo server)
        {
            if (_runningServers.ContainsKey(server.Id))
            {
                System.Windows.MessageBox.Show("Server is already running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Windows.MessageBox.Show($"Server executable not found at: {serverPath}\n\nPlease use the Browse button to select the correct server executable.", 
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
                
                // Set up output monitoring to preserve console content across tab switches
                _staticServerRunning = true;
                _staticConsoleOutput = "";

                // Start the server in the embedded console
                consoleControl.StartProcess(serverPath);

                // Set up a timer to periodically save console output
                var outputTimer = new System.Windows.Threading.DispatcherTimer();
                outputTimer.Interval = TimeSpan.FromSeconds(1);
                outputTimer.Tick += (s, e) =>
                {
                    if (consoleControl != null)
                    {
                        _staticConsoleOutput = consoleControl.GetOutput();
                    }
                };
                outputTimer.Start();

                // Store reference to the console control's process
                _runningServers[server.Id] = consoleControl.Process;
                
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Starting server: {server.Name} ({serverPath})");
                AddLogOutput($"[{DateTime.Now:HH:mm:ss}] Server console embedded in launcher.");
                UpdateServersList();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to launch server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _runningServers.Remove(server.Id);
                UpdateServersList();
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