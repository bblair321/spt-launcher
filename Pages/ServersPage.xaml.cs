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
            // Update the servers list - this will be handled by the XAML binding
        }

        private void AddServerButton_Click(object sender, RoutedEventArgs e)
        {
            _editingServer = new ServerInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = "New Server",
                ServerType = "local",
                Path = "",
                RemoteAddress = "",
                RemotePort = "6969",
                Port = "6969",
                Description = "",
                AutoStart = false,
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

        private void SaveServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Save server functionality - will be implemented with proper UI controls
        }

        private void CancelServerButton_Click(object sender, RoutedEventArgs e)
        {
            _editingServer = null;
        }

        private void BrowseServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Browse server functionality - will be implemented with proper UI controls
        }

        private void LaunchServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Launch server functionality - will be implemented with proper UI controls
        }

        private void StopServerButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop server functionality - will be implemented with proper UI controls
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

        private async void LaunchServer(ServerInfo server)
        {
            if (_runningServers.ContainsKey(server.Id))
            {
                MessageBox.Show("Server is already running.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrEmpty(server.Path) || !File.Exists(server.Path))
            {
                MessageBox.Show("Server executable not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = server.Path,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(server.Path),
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    Arguments = ""
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    MessageBox.Show("Failed to start server process.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _runningServers.Remove(server.Id);
                        UpdateServersList();
                    });
                };

                _runningServers[server.Id] = process;
                UpdateServersList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _runningServers.Remove(server.Id);
                UpdateServersList();
            }
        }

        private async Task StopServer(ServerInfo server)
        {
            if (_runningServers.TryGetValue(server.Id, out var process))
            {
                try
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                    _runningServers.Remove(server.Id);
                    UpdateServersList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to stop server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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