using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Pages
{
    public partial class LauncherPage : Page
    {
        private Process? _launcherProcess;
        private bool _isLauncherRunning = false;
        private bool _showFikaSettings = false;
        private string _configPath = "";

        public LauncherPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load launcher path from settings
            LauncherPathTextBox.Text = SettingsService.Instance.LauncherPath ?? "D:\\SPT\\SPT.Launcher.exe";
            
            // Load Fika configuration
            LoadFikaConfig();
        }

        private void SaveSettings()
        {
            SettingsService.Instance.LauncherPath = LauncherPathTextBox.Text;
            SettingsService.Instance.SaveSettings();
        }

        private async void LoadFikaConfig()
        {
            try
            {
                var sptDirectory = Path.GetDirectoryName(LauncherPathTextBox.Text);
                if (string.IsNullOrEmpty(sptDirectory)) return;

                var configFile = Path.Combine(sptDirectory, "config.json");
                if (File.Exists(configFile))
                {
                    _configPath = configFile;
                    ConfigPathText.Text = configFile;

                    var json = await File.ReadAllTextAsync(configFile);
                    var config = JsonSerializer.Deserialize<JsonElement>(json);

                    if (config.TryGetProperty("enableFika", out var enableFika))
                        EnableFikaCheckBox.IsChecked = enableFika.GetBoolean();

                    if (config.TryGetProperty("serverAddress", out var serverAddress))
                        ServerAddressTextBox.Text = serverAddress.GetString() ?? "";

                    if (config.TryGetProperty("serverPort", out var serverPort))
                        ServerPortTextBox.Text = serverPort.GetString() ?? "6969";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load Fika configuration: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void SaveFikaConfig()
        {
            try
            {
                var sptDirectory = Path.GetDirectoryName(LauncherPathTextBox.Text);
                if (string.IsNullOrEmpty(sptDirectory))
                {
                    MessageBox.Show("Please set a valid launcher path first.", "Invalid Path", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var configFile = Path.Combine(sptDirectory, "config.json");
                var config = new
                {
                    enableFika = EnableFikaCheckBox.IsChecked ?? false,
                    serverAddress = ServerAddressTextBox.Text,
                    serverPort = ServerPortTextBox.Text
                };

                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configFile, json);

                MessageBox.Show("Fika configuration saved successfully!", "Success", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save Fika configuration: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select SPT Launcher Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(LauncherPathTextBox.Text) ?? "C:\\"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LauncherPathTextBox.Text = openFileDialog.FileName;
                SaveSettings();
                LoadFikaConfig(); // Reload Fika config for new path
            }
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(LauncherPathTextBox.Text))
            {
                MessageBox.Show("Please select a launcher path first.", "Invalid Path", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(LauncherPathTextBox.Text))
            {
                MessageBox.Show("The specified launcher path does not exist.", "File Not Found", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                LaunchButton.IsEnabled = false;
                StatusText.Text = "Starting launcher...";

                var processInfo = new ProcessStartInfo
                {
                    FileName = LauncherPathTextBox.Text,
                    WorkingDirectory = Path.GetDirectoryName(LauncherPathTextBox.Text) ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                _launcherProcess = Process.Start(processInfo);
                if (_launcherProcess != null)
                {
                    _isLauncherRunning = true;
                    LaunchButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusText.Text = $"Launcher started (PID: {_launcherProcess.Id})";

                    // Start monitoring the process
                    _ = Task.Run(() => MonitorProcess());
                }
                else
                {
                    throw new Exception("Failed to start launcher process");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch SPT: {ex.Message}", "Launch Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                LaunchButton.IsEnabled = true;
                StatusText.Text = "Failed to start launcher";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_launcherProcess != null && !_launcherProcess.HasExited)
            {
                try
                {
                    _launcherProcess.Kill();
                    _launcherProcess.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error stopping launcher: {ex.Message}", "Stop Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            _launcherProcess = null;
            _isLauncherRunning = false;
            LaunchButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Ready";
        }

        private async Task MonitorProcess()
        {
            if (_launcherProcess == null) return;

            try
            {
                await _launcherProcess.WaitForExitAsync();
                
                Dispatcher.Invoke(() =>
                {
                    _isLauncherRunning = false;
                    LaunchButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                    StatusText.Text = "Launcher stopped";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Process monitoring error: {ex.Message}", "Monitor Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLauncherRunning && _launcherProcess != null)
            {
                if (_launcherProcess.HasExited)
                {
                    _isLauncherRunning = false;
                    LaunchButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                    StatusText.Text = "Launcher stopped";
                }
                else
                {
                    StatusText.Text = $"Launcher running (PID: {_launcherProcess.Id})";
                }
            }
            else
            {
                StatusText.Text = "Ready";
            }
        }

        private void ToggleFikaButton_Click(object sender, RoutedEventArgs e)
        {
            _showFikaSettings = !_showFikaSettings;
            FikaSettingsPanel.Visibility = _showFikaSettings ? Visibility.Visible : Visibility.Collapsed;
            ToggleFikaButton.Content = _showFikaSettings ? "Hide" : "Configure";
        }

        private void EnableFikaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Check if controls are initialized before accessing them
            if (FikaConfigPanel == null)
                return;
                
            FikaConfigPanel.Visibility = EnableFikaCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFikaConfig();
        }

        private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFikaConfig();
        }
    }
}
