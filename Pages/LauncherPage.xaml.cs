using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
        private static Process? _launcherProcess;
        private static Process? _serverProcess;
        private static bool _isLauncherRunning = false;
        private static int _launcherPid = 0;
        private static int _serverPid = 0;
        private static string _launcherPath = "";
        private System.Windows.Threading.DispatcherTimer? _uiUpdateTimer;
        
        // Global process monitoring - independent of page lifecycle
        private static System.Windows.Threading.DispatcherTimer? _globalProcessTimer;
        private static bool _globalProcessMonitoring = false;

        public LauncherPage()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize LauncherPage XAML: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "XAML Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
            
            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load settings: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Load Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
                RestoreLauncherState(); // Restore state from static variables
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restore launcher state: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Restore State Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
                UpdateLauncherUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update launcher UI: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Update UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
                SetupUITimer(); // Set up periodic UI updates
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to setup UI timer: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Timer Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
                StartGlobalProcessMonitoring(); // Start global monitoring if not already running
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start process monitoring: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Process Monitoring Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Simple, direct UI update
            UpdateLauncherUI();
        }

        private void LauncherPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Stop the timer when the page is unloaded
            if (_uiUpdateTimer != null)
            {
                _uiUpdateTimer.Stop();
                _uiUpdateTimer = null;
            }
        }

        private void SetupUITimer()
        {
            // Set up a timer to periodically update the UI state
            _uiUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _uiUpdateTimer.Interval = TimeSpan.FromSeconds(1); // Check every 1 second for more responsiveness
            _uiUpdateTimer.Tick += (s, e) => {
                System.Diagnostics.Debug.WriteLine($"[Timer] Tick - _isLauncherRunning: {_isLauncherRunning}");
                // Always update UI state, regardless of static variables
                UpdateLauncherUI();
            };
            _uiUpdateTimer.Start();
            System.Diagnostics.Debug.WriteLine("[Timer] UI Update timer started");
        }

        private static void StartGlobalProcessMonitoring()
        {
            if (_globalProcessMonitoring) return;
            
            _globalProcessMonitoring = true;
            _globalProcessTimer = new System.Windows.Threading.DispatcherTimer();
            _globalProcessTimer.Interval = TimeSpan.FromSeconds(0.5); // Check every 0.5 seconds
            _globalProcessTimer.Tick += (s, e) => {
                // Check for running processes globally
                var sptProcesses = Process.GetProcessesByName("SPT.Server");
                var launcherProcesses = Process.GetProcessesByName("SPT.Launcher");
                bool hasRunningProcesses = sptProcesses.Length > 0 || launcherProcesses.Length > 0;
                
                System.Diagnostics.Debug.WriteLine($"[GlobalTimer] SPT.Server: {sptProcesses.Length}, SPT.Launcher: {launcherProcesses.Length}");
                
                if (hasRunningProcesses)
                {
                    _isLauncherRunning = true;
                    if (sptProcesses.Length > 0) _serverProcess = sptProcesses[0];
                    if (launcherProcesses.Length > 0) _launcherProcess = launcherProcesses[0];
                }
                else
                {
                    _isLauncherRunning = false;
                    _serverProcess = null;
                    _launcherProcess = null;
                }
            };
            _globalProcessTimer.Start();
            System.Diagnostics.Debug.WriteLine("[GlobalTimer] Global process monitoring started");
        }

        private void LoadSettings()
        {
            // Load launcher path from settings
            LauncherPathTextBox.Text = SettingsService.Instance.LauncherPath ?? "D:\\SPT\\SPT.Launcher.exe";
        }

        private void RestoreLauncherState()
        {
            // Always check for actual running processes first, regardless of static variables
            var sptProcesses = Process.GetProcessesByName("SPT.Server");
            var launcherProcesses = Process.GetProcessesByName("SPT.Launcher");
            
            if (sptProcesses.Length > 0 || launcherProcesses.Length > 0)
            {
                // Update our static variables with the found processes
                if (sptProcesses.Length > 0)
                {
                    _serverProcess = sptProcesses[0];
                    _isLauncherRunning = true;
                }
                if (launcherProcesses.Length > 0)
                {
                    _launcherProcess = launcherProcesses[0];
                    _launcherPid = _launcherProcess.Id;
                    _isLauncherRunning = true;
                }
            }
            else
            {
                // No processes found, reset the running state
                _isLauncherRunning = false;
                _launcherProcess = null;
                _serverProcess = null;
                _launcherPid = 0;
                _serverPid = 0;
            }
        }

        private void SaveSettings()
        {
            SettingsService.Instance.LauncherPath = LauncherPathTextBox.Text;
            SettingsService.Instance.SaveSettings();
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
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
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

                // Store the launcher path for later use
                _launcherPath = LauncherPathTextBox.Text;

                var processInfo = new ProcessStartInfo
                {
                    FileName = LauncherPathTextBox.Text,
                    WorkingDirectory = Path.GetDirectoryName(LauncherPathTextBox.Text) ?? "",
                    UseShellExecute = true, // Changed to true to allow the launcher to start properly
                    CreateNoWindow = false
                };

                _launcherProcess = Process.Start(processInfo);
                if (_launcherProcess != null)
                {
                    _isLauncherRunning = true;
                    _launcherPid = _launcherProcess.Id;
                    LaunchButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    StatusText.Text = $"Launcher started (PID: {_launcherPid})";

                    // Start monitoring for the server process
                    _ = Task.Run(() => MonitorForServerProcess());
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
            try
            {
                System.Diagnostics.Debug.WriteLine("[StopButton_Click] Stop button clicked!");
                
                // Get current process ID to exclude it from stopping
                int currentProcessId = Process.GetCurrentProcess().Id;
                System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Current process ID: {currentProcessId}");
                
                // Stop only SPT launcher processes (not servers)
                var sptLauncherProcesses = Process.GetProcessesByName("SPT.Launcher");
                var akiLauncherProcesses = Process.GetProcessesByName("Aki.Launcher");
                
                // Also check for any launcher processes that might have different names
                var allProcesses = Process.GetProcesses();
                var launcherProcesses = allProcesses.Where(p => 
                    p.Id != currentProcessId && // Exclude current process
                    (p.ProcessName.Contains("SPT", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Aki", StringComparison.OrdinalIgnoreCase)) &&
                    !p.ProcessName.Contains("Server", StringComparison.OrdinalIgnoreCase) // Exclude server processes
                ).ToList();
                
                System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Found {launcherProcesses.Count} SPT launcher processes to stop (excluding current process and servers):");
                foreach (var proc in launcherProcesses)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {proc.ProcessName} (PID: {proc.Id})");
                }
                
                int stoppedCount = 0;
                foreach (var process in launcherProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Killing {process.ProcessName} (PID: {process.Id})");
                            process.Kill();
                            stoppedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Error stopping {process.ProcessName}: {ex.Message}");
                        MessageBox.Show($"Error stopping {process.ProcessName} (PID: {process.Id}): {ex.Message}", "Warning", 
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Stopped {stoppedCount} launcher processes");
                
                // Update UI
                LaunchButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusText.Text = stoppedCount > 0 ? $"Stopped {stoppedCount} launcher processes" : "No launcher processes were running";
                
                // Force UI refresh
                UpdateLauncherUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StopButton_Click] Error: {ex.Message}");
                MessageBox.Show($"Error stopping processes: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MonitorForServerProcess()
        {
            // Wait a bit for the launcher to start the server
            await Task.Delay(3000);
            
            // Look for SPT.Server process
            var attempts = 0;
            while (attempts < 10) // Try for 10 seconds
            {
                try
                {
                    var serverProcesses = Process.GetProcessesByName("SPT.Server");
                    if (serverProcesses.Length > 0)
                    {
                        _serverProcess = serverProcesses[0];
                        _serverPid = _serverProcess.Id;
                        
                        // Update UI on main thread
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = $"Server detected (PID: {_serverPid})";
                        });
                        
                        // Monitor the server process
                        _serverProcess.EnableRaisingEvents = true;
                        _serverProcess.Exited += (s, e) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _serverPid = 0;
                                _serverProcess = null;
                                if (!_isLauncherRunning)
                                {
                                    StatusText.Text = "Server stopped";
                                }
                            });
                        };
                        
                        return; // Found server, exit monitoring
                    }
                }
                catch { }
                
                await Task.Delay(1000);
                attempts++;
            }
            
            // If we get here, no server was found
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Launcher running (no server detected)";
            });
        }

        private async Task MonitorProcess()
        {
            if (_launcherProcess == null) return;

            try
            {
                // Wait for launcher to exit (it typically exits after starting the server)
                await _launcherProcess.WaitForExitAsync();
                
                // Try to find the SPT server process
                await Task.Delay(2000); // Give server time to start
                var serverProcess = FindSptServerProcess();
                
                if (serverProcess != null)
                {
                    _serverProcess = serverProcess;
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = $"SPT Server running (Launcher PID: {_launcherPid}, Server PID: {_serverProcess.Id})";
                    });
                    
                    // Monitor the server process instead
                    await MonitorServerProcess();
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResetLauncherState();
                        StatusText.Text = "Launcher exited but server not found";
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResetLauncherState();
                    MessageBox.Show($"Process monitoring error: {ex.Message}", "Monitor Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private async Task MonitorServerProcess()
        {
            if (_serverProcess == null) return;

            try
            {
                await _serverProcess.WaitForExitAsync();
                
                Dispatcher.Invoke(() =>
                {
                    ResetLauncherState();
                    StatusText.Text = "SPT Server stopped";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResetLauncherState();
                    MessageBox.Show($"Server monitoring error: {ex.Message}", "Monitor Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        }

        private Process? FindSptServerProcess()
        {
            try
            {
                var processes = Process.GetProcessesByName("SPT.Server");
                return processes.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void ResetLauncherState()
        {
            _isLauncherRunning = false;
            _launcherProcess = null;
            _serverProcess = null;
            _launcherPid = 0;
            LaunchButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Ready";
        }

        private void UpdateLauncherUI()
        {
            try
            {
                // Get current process ID to exclude it from detection
                int currentProcessId = Process.GetCurrentProcess().Id;
                
                // Check for launcher processes (for Launch button state)
                var sptLauncherProcesses = Process.GetProcessesByName("SPT.Launcher");
                var akiLauncherProcesses = Process.GetProcessesByName("Aki.Launcher");
                
                // Check for any SPT-related processes for the Stop button (excluding current process)
                var allProcesses = Process.GetProcesses();
                var sptProcesses = allProcesses.Where(p => 
                    p.Id != currentProcessId && // Exclude current process
                    (p.ProcessName.Contains("SPT", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Aki", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Tarkov", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Escape", StringComparison.OrdinalIgnoreCase))
                ).ToList();
                
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Found {sptProcesses.Count} SPT-related processes (excluding current process):");
                foreach (var proc in sptProcesses)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {proc.ProcessName} (PID: {proc.Id})");
                }
                
                bool hasLauncherRunning = sptLauncherProcesses.Length > 0 || akiLauncherProcesses.Length > 0;
                bool hasAnySptProcesses = sptProcesses.Count > 0;
                
                if (hasLauncherRunning)
                {
                    LaunchButton.IsEnabled = false;
                    StopButton.IsEnabled = true;
                    var processId = sptLauncherProcesses.Length > 0 ? sptLauncherProcesses[0].Id : akiLauncherProcesses[0].Id;
                    StatusText.Text = $"SPT Launcher running (PID: {processId})";
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] Launcher running - Launch button DISABLED, Stop button ENABLED");
                }
                else if (hasAnySptProcesses)
                {
                    LaunchButton.IsEnabled = true;
                    StopButton.IsEnabled = true;
                    StatusText.Text = $"SPT process running (PID: {sptProcesses[0].Id})";
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] SPT process running - Launch button ENABLED, Stop button ENABLED");
                }
                else
                {
                    LaunchButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                    StatusText.Text = "Ready";
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] No processes - Launch button ENABLED, Stop button DISABLED");
                }
            }
            catch (Exception ex)
            {
                // Fallback to enabled state if there's an error
                LaunchButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusText.Text = "Ready";
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Error: {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Force refresh of the UI state
            UpdateLauncherUI();
        }

        private async void InstallSptButton_Click(object sender, RoutedEventArgs e)
        {
            const string installerUrl = "https://ligma.waffle-lord.net/SPTInstaller.exe";
            const string installerFileName = "SPTInstaller.exe";
            
            try
            {
                // Disable button during download
                InstallSptButton.IsEnabled = false;
                InstallSptButton.Content = "⏳ Downloading installer...";
                
                // Get temporary file path
                string tempPath = Path.GetTempPath();
                string installerPath = Path.Combine(tempPath, installerFileName);
                
                // Download the installer
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5); // Set timeout for large downloads
                    
                    byte[] fileBytes = await client.GetByteArrayAsync(installerUrl);
                    await File.WriteAllBytesAsync(installerPath, fileBytes);
                }
                
                // Update button text
                InstallSptButton.Content = "🚀 Launching installer...";
                
                // Execute the installer
                var processInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                
                Process.Start(processInfo);
                
                // Reset button state
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                
                MessageBox.Show("SPT installer has been launched. Please follow the installation wizard.", 
                    "Installer Launched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (HttpRequestException ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                MessageBox.Show($"Failed to download the SPT installer.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.", 
                    "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (TaskCanceledException ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                MessageBox.Show($"Download timed out.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.", 
                    "Download Timeout", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                MessageBox.Show($"An error occurred while installing SPT.\n\nError: {ex.Message}", 
                    "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
