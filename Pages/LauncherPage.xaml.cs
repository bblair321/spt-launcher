using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SptLauncherWpf.Services;
using WinForms = System.Windows.Forms;

namespace SptLauncherWpf.Pages
{
    public partial class LauncherPage : Page
    {
        // P/Invoke declarations for unblocking files (removes Zone.Identifier)
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        // Check if running as administrator
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // Helper method to get process executable path safely
        private static string? TryGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        // Terminate process using WMI (sometimes works when Process.Kill() doesn't)
        private static bool TerminateProcessWithWmi(int processId)
        {
            try
            {
                using (ManagementObject process = new ManagementObject($"Win32_Process.Handle='{processId}'"))
                {
                    process.Get();
                    process.InvokeMethod("Terminate", null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TerminateProcessWithWmi] Failed to terminate PID {processId}: {ex.Message}");
                return false;
            }
        }

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
        
        // Fika Co-op configuration
        private bool _fikaEnabled = false;
        private const string _defaultIp = "127.0.0.1";
        
        // SPT Update tracking
        private SptUpdateInfo? _currentUpdateInfo = null;
        
        // Fika Update tracking
        private FikaUpdateInfo? _currentFikaUpdateInfo = null;

        public LauncherPage()
        {
            try
        {
            InitializeComponent();
                
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to initialize LauncherPage XAML: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "XAML Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
            
            try
            {
            LoadSettings();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load settings: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Load Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
            RestoreLauncherState(); // Restore state from static variables
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to restore launcher state: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Restore State Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
            UpdateLauncherUI();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to update launcher UI: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Update UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
            SetupUITimer(); // Set up periodic UI updates
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to setup UI timer: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Timer Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            try
            {
            StartGlobalProcessMonitoring(); // Start global monitoring if not already running
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start process monitoring: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Process Monitoring Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure Stop button border is properly configured
            if (StopButtonBorder != null)
            {
                StopButtonBorder.IsHitTestVisible = true;
                StopButtonBorder.Visibility = Visibility.Visible;
                StopButtonBorder.Opacity = 1.0;
                
                System.Diagnostics.Debug.WriteLine($"[LauncherPage_Loaded] StopButtonBorder configured - IsHitTestVisible: {StopButtonBorder.IsHitTestVisible}, IsVisible: {StopButtonBorder.IsVisible}");
            }
            
            // Simple, direct UI update (same as Launch button approach)
            UpdateLauncherUI();
            
            // Update SPT version display
            UpdateSptVersionDisplay();
            
            // Update Fika version display
            UpdateFikaVersionDisplay();
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
            var savedPath = SettingsService.Instance.LauncherPath;
            if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
            {
                LauncherPathTextBox.Text = savedPath;
            }
            else
            {
                // Try to auto-detect SPT launcher if not saved or if saved path doesn't exist
                var detectedPath = AutoDetectSptLauncher();
                if (!string.IsNullOrEmpty(detectedPath))
                {
                    LauncherPathTextBox.Text = detectedPath;
                    SaveSettings(); // Save the auto-detected path
                }
            }
            
            // Load Fika Co-op settings
            _fikaEnabled = SettingsService.Instance.FikaEnabled;
            if (EnableFikaCheckBox != null)
            {
                EnableFikaCheckBox.IsChecked = _fikaEnabled;
                
                if (_fikaEnabled)
                {
                    // Show IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Visible;
                    
                    // Load saved IP address
                    var savedIp = SettingsService.Instance.FikaIpAddress;
                    if (!string.IsNullOrEmpty(savedIp))
                    {
                        FikaIpTextBox.Text = savedIp;
                    }
                    else
                    {
                        // Try to load from config.json
                        var launcherConfig = LoadLauncherConfig();
                        if (launcherConfig != null && launcherConfig.Server != null && !string.IsNullOrEmpty(launcherConfig.Server.Url))
                        {
                            try
                            {
                                var uri = new Uri(launcherConfig.Server.Url);
                                var ipFromConfig = uri.Host;
                                if (!string.IsNullOrEmpty(ipFromConfig))
                                {
                                    FikaIpTextBox.Text = ipFromConfig;
                                }
                                else
                                {
                                    FikaIpTextBox.Text = _defaultIp;
                                }
                            }
                            catch
                            {
                                FikaIpTextBox.Text = _defaultIp;
                            }
                        }
                        else
                        {
                            FikaIpTextBox.Text = _defaultIp;
                        }
                    }
                }
                else
                {
                    FikaIpEditorPanel.Visibility = Visibility.Collapsed;
                }
            }
            
            // Update path status after loading
            UpdatePathStatus();
            
            // Update SPT version display
            UpdateSptVersionDisplay();
            
            // Check if Fika mod is installed and update version display
            UpdateFikaVersionDisplay();
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
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select SPT Launcher Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = !string.IsNullOrEmpty(LauncherPathTextBox.Text) && File.Exists(LauncherPathTextBox.Text)
                    ? Path.GetDirectoryName(LauncherPathTextBox.Text)
                    : "C:\\"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LauncherPathTextBox.Text = openFileDialog.FileName;
                SaveSettings();
                UpdatePathStatus();
                UpdateSptVersionDisplay();
            }
        }

        private void LauncherPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePathStatus();
            UpdateSptVersionDisplay();
        }

        private void UpdatePathStatus()
        {
            if (string.IsNullOrWhiteSpace(LauncherPathTextBox.Text))
            {
                PathStatusText.Text = "Please select your SPT Launcher executable using the Browse button.";
                PathStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
            }
            else
            {
                string path = LauncherPathTextBox.Text.Trim();
                if (File.Exists(path))
                {
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        PathStatusText.Text = "✓ Valid launcher path selected.";
                        PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    }
                    else
                    {
                        PathStatusText.Text = "⚠ Selected file is not an executable (.exe).";
                        PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)); // Yellow
                    }
                }
                else
                {
                    PathStatusText.Text = "✗ File not found. Please check the path.";
                    PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                }
            }
        }

        /// <summary>
        /// Attempts to unblock a file that was downloaded from the internet by removing the Zone.Identifier alternate data stream.
        /// This is necessary because Windows blocks execution of files downloaded from the internet.
        /// </summary>
        private static bool UnblockFile(string filePath)
        {
            try
            {
                string zoneIdentifier = $"{filePath}:Zone.Identifier";
                bool result = DeleteFile(zoneIdentifier);
                System.Diagnostics.Debug.WriteLine($"[UnblockFile] Attempted to unblock {filePath}: {(result ? "Success" : "Failed or no Zone.Identifier found")}");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnblockFile] Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Alternative method to unblock files using PowerShell (more reliable in some cases)
        /// </summary>
        private static bool UnblockFileWithPowerShell(string filePath)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Unblock-File -Path '{filePath}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000); // Wait up to 5 seconds
                        bool success = process.ExitCode == 0;
                        System.Diagnostics.Debug.WriteLine($"[UnblockFileWithPowerShell] Unblock result for {filePath}: {(success ? "Success" : "Failed")} (Exit code: {process.ExitCode})");
                        return success;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnblockFileWithPowerShell] Exception: {ex.Message}");
                return false;
            }
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] ========== BUTTON CLICKED ==========");
            System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Sender: {sender?.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] LaunchButton.IsEnabled: {LaunchButton.IsEnabled}");
            
            // Double-check button is enabled
            if (!LaunchButton.IsEnabled)
            {
                System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] WARNING: Button is disabled but click was received!");
                System.Windows.MessageBox.Show("The Launch button is currently disabled. Please wait a moment and try again.", 
                    "Button Disabled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Validate path is provided
            if (string.IsNullOrWhiteSpace(LauncherPathTextBox.Text))
            {
                System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] Path is empty");
                System.Windows.MessageBox.Show("Please select a launcher path first.\n\nUse the 'Browse' button to locate your SPT Launcher executable.", 
                    "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate file exists
            string launcherPath = LauncherPathTextBox.Text.Trim();
            System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Launcher path: {launcherPath}");
            
            if (!File.Exists(launcherPath))
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] File does not exist: {launcherPath}");
                System.Windows.MessageBox.Show($"The specified launcher path does not exist:\n\n{launcherPath}\n\nPlease use the 'Browse' button to select the correct SPT Launcher executable.", 
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate it's actually an executable
            if (!launcherPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] File is not an .exe: {launcherPath}");
                System.Windows.MessageBox.Show("The selected file is not an executable (.exe file).\n\nPlease select a valid SPT Launcher executable.", 
                    "Invalid File Type", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                LaunchButton.IsEnabled = false;
                System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] Starting launch process...");

                // Try to unblock the file if it was downloaded from the internet
                // This removes the Zone.Identifier alternate data stream that Windows adds
                // Try both methods for maximum compatibility
                bool unblocked = UnblockFile(launcherPath);
                if (!unblocked)
                {
                    System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] First unblock method failed, trying PowerShell...");
                    UnblockFileWithPowerShell(launcherPath);
                }

                // Save the path to settings
                SettingsService.Instance.LauncherPath = launcherPath;
                SettingsService.Instance.SaveSettings();

                // Store the launcher path for later use
                _launcherPath = launcherPath;

                var processInfo = new ProcessStartInfo
                {
                    FileName = launcherPath,
                    WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? Environment.CurrentDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Attempting to start: {launcherPath}");
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Working directory: {processInfo.WorkingDirectory}");

                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Calling Process.Start...");
                _launcherProcess = Process.Start(processInfo);
                
                if (_launcherProcess == null)
                {
                    System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] Process.Start returned null");
                    throw new Exception("Process.Start returned null - the process could not be started. This may be due to security restrictions or file blocking.");
                }
                
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Process started, PID: {_launcherProcess.Id}");
                
                // Give it a moment to see if it exits immediately
                System.Threading.Thread.Sleep(100);
                
                if (_launcherProcess.HasExited)
                {
                    int exitCode = _launcherProcess.ExitCode;
                    System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Process exited immediately with code: {exitCode}");
                    throw new Exception($"Process started but exited immediately with code {exitCode}. This may indicate a security restriction or the executable is blocked.");
                }
                
                    _isLauncherRunning = true;
                    _launcherPid = _launcherProcess.Id;
                
                // Force button states on UI thread - ensure Stop button is definitely enabled
                Dispatcher.Invoke(() =>
                {
                    LaunchButton.IsEnabled = false;
                    if (StopButtonBorder != null)
                    {
                        StopButtonBorder.Opacity = 1.0;
                        StopButtonBorder.IsHitTestVisible = true;
                        StopButtonBorder.Visibility = Visibility.Visible;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] StopButtonBorder configured");
                });

                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Launcher started successfully (PID: {_launcherPid})");

                    // Start monitoring for the server process
                    _ = Task.Run(() => MonitorForServerProcess());
            }
            catch (System.ComponentModel.Win32Exception winEx)
            {
                string errorMsg = $"Failed to launch SPT launcher.\n\n";
                string solution = "";
                
                if (winEx.NativeErrorCode == 2)
                {
                    errorMsg += "Error: File not found or path is incorrect.";
                }
                else if (winEx.NativeErrorCode == 5)
                {
                    errorMsg += "Error: Access denied. This may be due to Windows security restrictions on downloaded files.";
                    solution = "\n\nSolution: Right-click the SPT Launcher executable, select 'Properties', and click 'Unblock' if available. Then try again.";
                }
                else if (winEx.NativeErrorCode == 1223) // ERROR_CANCELLED
                {
                    errorMsg += "Error: Operation was cancelled by the user or blocked by Windows security.";
                    solution = "\n\nSolution: The file may be blocked. Right-click the executable, select 'Properties', and click 'Unblock' if available.";
                }
                else
                {
                    errorMsg += $"Error: {winEx.Message}";
                    solution = "\n\nIf this file was downloaded from the internet, try right-clicking it, selecting 'Properties', and clicking 'Unblock'.";
                }
                
                errorMsg += $"\n\nPath: {launcherPath}";
                errorMsg += solution;
                
                System.Windows.MessageBox.Show(errorMsg, "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Win32Exception: {winEx.Message} (Error Code: {winEx.NativeErrorCode})");
                
                LaunchButton.IsEnabled = true;
                if (StopButtonBorder != null)
                {
                    StopButtonBorder.Opacity = 0.6;
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to launch SPT launcher.\n\nError: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\n\nDetails: {ex.InnerException.Message}";
                }
                errorMsg += $"\n\nPath: {launcherPath}";
                
                System.Windows.MessageBox.Show(errorMsg, "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Exception: {ex.Message}\nStack trace: {ex.StackTrace}");
                
                LaunchButton.IsEnabled = true;
                if (StopButtonBorder != null)
                {
                    StopButtonBorder.Opacity = 0.6;
                }
            }
        }



        private void StopButtonBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[StopButtonBorder_MouseEnter] Mouse entered stop button border");
            if (StopButtonBorder != null)
            {
                StopButtonBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5B, 0x62, 0x70)); // Slightly darker on hover
            }
        }

        private void StopButtonBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[StopButtonBorder_MouseLeave] Mouse left stop button border");
            if (StopButtonBorder != null)
            {
                StopButtonBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80)); // Original color
            }
        }

        private async void StopButtonBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[StopButtonBorder_MouseDown] ========== STOP BUTTON BORDER CLICKED ==========");
            
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }
            
            // Check if there are actually processes to stop
            int currentProcessId = Process.GetCurrentProcess().Id;
            var sptLauncherProcesses = Process.GetProcessesByName("SPT.Launcher")
                .Where(p => p.Id != currentProcessId && !p.HasExited)
                .ToArray();
            var akiLauncherProcesses = Process.GetProcessesByName("Aki.Launcher")
                .Where(p => p.Id != currentProcessId && !p.HasExited)
                .ToArray();
            
            if (sptLauncherProcesses.Length == 0 && akiLauncherProcesses.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("[StopButtonBorder_MouseDown] No processes to stop");
                System.Windows.MessageBox.Show("No SPT launcher processes are currently running.", 
                    "No Processes", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            await StopSptProcessesAsync();
        }

        private async Task StopSptProcessesAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Starting stop process...");
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] StopButtonBorder configured");
                
                // Get current process ID and name to exclude it from stopping
                int currentProcessId = Process.GetCurrentProcess().Id;
                string currentProcessName = Process.GetCurrentProcess().ProcessName;
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Current process: {currentProcessName} (PID: {currentProcessId})");
                
                // Build list of processes to stop - prioritize tracked processes we started
                List<Process> launcherProcesses = new List<Process>();
                
                // First, add the tracked launcher process if we have one
                if (_launcherProcess != null && !_launcherProcess.HasExited && _launcherProcess.Id != currentProcessId)
                {
                    try
                    {
                        // Verify the process still exists
                        var checkProcess = Process.GetProcessById(_launcherProcess.Id);
                        if (!checkProcess.HasExited)
                        {
                            launcherProcesses.Add(checkProcess);
                            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Adding tracked launcher process: {_launcherProcess.ProcessName} (PID: {_launcherProcess.Id})");
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process doesn't exist anymore, skip it
                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Tracked launcher process no longer exists");
                    }
                }
                
                // If we don't have a tracked process, look for running SPT/Aki launcher processes
                // (but only if we don't have a tracked one - this handles cases where the process was started outside our app)
                if (launcherProcesses.Count == 0)
                {
                    var sptLauncherProcesses = Process.GetProcessesByName("SPT.Launcher")
                        .Where(p => p.Id != currentProcessId && !p.HasExited)
                        .ToArray();
                    var akiLauncherProcesses = Process.GetProcessesByName("Aki.Launcher")
                        .Where(p => p.Id != currentProcessId && !p.HasExited)
                        .ToArray();
                    
                    launcherProcesses.AddRange(sptLauncherProcesses);
                    launcherProcesses.AddRange(akiLauncherProcesses);
                }
                
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Found {launcherProcesses.Count} SPT launcher process(es) to stop:");
                foreach (var proc in launcherProcesses)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {proc.ProcessName} (PID: {proc.Id}, HasExited: {proc.HasExited})");
                }
                
                int stoppedCount = 0;
                int failedCount = 0;
                List<string> failedProcesses = new List<string>();
                
                foreach (var process in launcherProcesses)
                {
                    try
                    {
                        // Double-check it hasn't exited
                        if (process.HasExited)
                        {
                            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process {process.ProcessName} (PID: {process.Id}) already exited");
                            stoppedCount++;
                            continue;
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Attempting to stop {process.ProcessName} (PID: {process.Id})");
                        
                        // Try to close the main window gracefully first (if it has one)
                        try
                        {
                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process has a window, attempting to close gracefully");
                                process.CloseMainWindow();
                                
                                // Wait a bit for graceful shutdown
                                if (process.WaitForExit(2000))
                                {
                                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process closed gracefully");
                                    stoppedCount++;
                                    continue;
                                }
                            }
                        }
                        catch (Exception closeEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Could not close window gracefully: {closeEx.Message}");
                        }
                        
                        // If graceful close didn't work, try to kill the process
                        try
                        {
                            process.Kill();
                            if (process.WaitForExit(5000))
                            {
                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Successfully killed {process.ProcessName} (PID: {process.Id})");
                            stoppedCount++;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process did not exit within timeout");
                                failedCount++;
                                failedProcesses.Add($"{process.ProcessName} (PID: {process.Id})");
                            }
                        }
                        catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 5) // Access Denied
                        {
                            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Access denied when killing {process.ProcessName} (PID: {process.Id})");
                            
                            bool terminated = false;
                            int processId = process.Id; // Store ID before potential disposal
                            string processName = process.ProcessName;
                            
                            // Try WMI termination first (sometimes works when Process.Kill() doesn't)
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Attempting to use WMI to terminate process");
                                if (TerminateProcessWithWmi(processId))
                                {
                                    System.Threading.Thread.Sleep(1000); // Give it a moment
                                    // Re-check if process still exists
                                    try
                                    {
                                        var checkProcess = Process.GetProcessById(processId);
                                        if (checkProcess.HasExited)
                                        {
                                            terminated = true;
                                        }
                                        checkProcess.Dispose();
                                    }
                                    catch (ArgumentException)
                                    {
                                        // Process doesn't exist anymore - it was terminated!
                                        terminated = true;
                                    }
                                    
                                    if (terminated)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Successfully terminated using WMI");
                                        stoppedCount++;
                                    }
                                }
                            }
                            catch (Exception wmiEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] WMI termination failed: {wmiEx.Message}");
                            }
                            
                            // If WMI didn't work, try taskkill
                            if (!terminated)
                            {
                                try
                                {
                                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Attempting to use taskkill as fallback");
                                    var taskkillInfo = new ProcessStartInfo
                                    {
                                        FileName = "taskkill",
                                        Arguments = $"/F /PID {processId}",
                                        UseShellExecute = false,
                                        CreateNoWindow = true,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true
                                    };
                                    
                                    using (var taskkill = Process.Start(taskkillInfo))
                                    {
                                        if (taskkill != null)
                                        {
                                            taskkill.WaitForExit(5000);
                                            System.Threading.Thread.Sleep(500); // Give it a moment
                                            
                                            // Re-check if process still exists
                                            try
                                            {
                                                var checkProcess = Process.GetProcessById(processId);
                                                if (checkProcess.HasExited)
                                                {
                                                    terminated = true;
                                                }
                                                checkProcess.Dispose();
                                            }
                                            catch (ArgumentException)
                                            {
                                                // Process doesn't exist anymore - it was terminated!
                                                terminated = true;
                                            }
                                            
                                            if (terminated)
                                            {
                                                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Successfully killed using taskkill");
                                                stoppedCount++;
                                            }
                                        }
                                    }
                                }
                                catch (Exception taskkillEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] taskkill also failed: {taskkillEx.Message}");
                                }
                            }
                            
                            // Final check - maybe the process exited despite the error
                            if (!terminated)
                            {
                                try
                                {
                                    var finalCheck = Process.GetProcessById(processId);
                                    if (finalCheck.HasExited)
                                    {
                                        terminated = true;
                                        stoppedCount++;
                                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process actually exited (final check)");
                                    }
                                    finalCheck.Dispose();
                                }
                                catch (ArgumentException)
                                {
                                    // Process doesn't exist - it was terminated!
                                    terminated = true;
                                    stoppedCount++;
                                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process doesn't exist anymore (final check)");
                                }
                            }
                            
                            if (!terminated)
                            {
                                failedCount++;
                                string errorMsg = $"{processName} (PID: {processId}) - Access Denied";
                                if (!IsRunningAsAdministrator())
                                {
                                    errorMsg += " (Try running this launcher as Administrator)";
                                }
                                failedProcesses.Add(errorMsg);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Error stopping {process.ProcessName} (PID: {process.Id}): {ex.Message}");
                        failedCount++;
                        failedProcesses.Add($"{process.ProcessName} (PID: {process.Id}) - {ex.Message}");
                    }
                }
                
                // Show summary message
                if (failedCount > 0)
                {
                    string failedMessage = stoppedCount > 0 
                        ? $"Successfully stopped {stoppedCount} process(es).\n\n"
                        : "";
                    
                    failedMessage += $"Failed to stop {failedCount} process(es):\n";
                    failedMessage += string.Join("\n", failedProcesses);
                    
                    if (!IsRunningAsAdministrator())
                    {
                        failedMessage += "\n\n📌 This is normal - Windows requires Administrator privileges to stop some processes.";
                        failedMessage += "\n\nTo stop these processes, you have two options:";
                        failedMessage += "\n\nOption 1 - Run as Administrator (Recommended):";
                        failedMessage += "\n  1. Close this launcher";
                        failedMessage += "\n  2. Right-click 'SPT Launcher.exe'";
                        failedMessage += "\n  3. Select 'Run as administrator'";
                        failedMessage += "\n  4. Try stopping again";
                        failedMessage += "\n\nOption 2 - Use Task Manager:";
                        failedMessage += "\n  Press Ctrl+Shift+Esc, find the process(es) above, and click 'End Task'";
                    }
                    else
                    {
                        failedMessage += "\n\n⚠ Even with Administrator privileges, some processes could not be stopped.";
                        failedMessage += "\n\nYou may need to manually close them from Task Manager:";
                        failedMessage += "\n1. Press Ctrl+Shift+Esc to open Task Manager";
                        failedMessage += "\n2. Find the process(es) listed above";
                        failedMessage += "\n3. Right-click and select 'End Task'";
                    }
                    
                    Dispatcher.Invoke(() =>
                    {
                        var result = System.Windows.MessageBox.Show(failedMessage, "Stop Processes", 
                                      MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        
                        if (result == MessageBoxResult.OK && !IsRunningAsAdministrator())
                        {
                            // Offer to open Task Manager
                            var taskMgrResult = System.Windows.MessageBox.Show(
                                "Would you like to open Task Manager to manually close the processes?",
                                "Open Task Manager?",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                            
                            if (taskMgrResult == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "taskmgr.exe",
                                        UseShellExecute = true
                                    });
                                }
                                catch
                                {
                                    // Ignore if we can't open Task Manager
                                }
                            }
                        }
                    });
                }
                
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Stopped {stoppedCount} launcher processes, {failedCount} failed");
                
                // Clean up tracked process if it was stopped
                if (stoppedCount > 0 && _launcherProcess != null)
                {
                    try
                    {
                        if (_launcherProcess.HasExited)
                        {
                            _launcherProcess.Dispose();
                            _launcherProcess = null;
                            _isLauncherRunning = false;
                            _launcherPid = 0;
                            System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Cleaned up tracked launcher process");
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                
                // Update UI on UI thread
                Dispatcher.Invoke(() =>
                {
                    if (stoppedCount > 0)
                    {
                        // Status messages removed - StatusText element was removed from UI
                    }
                    else if (failedCount > 0)
                    {
                        // Status messages removed - StatusText element was removed from UI
                    }
                    else
                    {
                        // Status messages removed - StatusText element was removed from UI
                    }
                });
                
                // Force UI refresh after a short delay to allow processes to fully exit
                await Task.Delay(500);
                UpdateLauncherUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Error: {ex.Message}\n{ex.StackTrace}");
                Dispatcher.Invoke(() =>
                {
                System.Windows.MessageBox.Show($"Error stopping processes: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateLauncherUI();
                });
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
                            // Status messages removed - StatusText element was removed from UI
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
                                    // Status messages removed - StatusText element was removed from UI
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
                // Status messages removed - StatusText element was removed from UI
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
                        // Status messages removed - StatusText element was removed from UI
                    });
                    
                    // Monitor the server process instead
                    await MonitorServerProcess();
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        ResetLauncherState();
                        // Status messages removed - StatusText element was removed from UI
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResetLauncherState();
                    System.Windows.MessageBox.Show($"Process monitoring error: {ex.Message}", "Monitor Error", 
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
                    // Status messages removed - StatusText element was removed from UI
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ResetLauncherState();
                    System.Windows.MessageBox.Show($"Server monitoring error: {ex.Message}", "Monitor Error", 
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
            if (StopButtonBorder != null)
            {
                StopButtonBorder.Opacity = 0.6;
            }
        }

        private void UpdateLauncherUI()
        {
            try
            {
                // Get current process ID and name to exclude it from detection
                int currentProcessId = Process.GetCurrentProcess().Id;
                string currentProcessName = Process.GetCurrentProcess().ProcessName;
                
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Current process: {currentProcessName} (PID: {currentProcessId})");
                
                // Check for launcher processes (for Launch button state)
                // Only check for actual SPT/Aki launcher executables, not this app
                var sptLauncherProcesses = Process.GetProcessesByName("SPT.Launcher")
                    .Where(p => p.Id != currentProcessId && !p.HasExited)
                    .ToArray();
                var akiLauncherProcesses = Process.GetProcessesByName("Aki.Launcher")
                    .Where(p => p.Id != currentProcessId && !p.HasExited)
                    .ToArray();
                
                // Check for any SPT-related processes for the Stop button (excluding current process)
                var allProcesses = Process.GetProcesses();
                
                // Get current process executable path for comparison
                string? currentProcessPath = null;
                try
                {
                    currentProcessPath = Process.GetCurrentProcess().MainModule?.FileName;
                }
                catch { }
                
                var sptProcesses = allProcesses.Where(p => 
                    p.Id != currentProcessId && // Exclude current process by ID
                    !p.HasExited && // Exclude exited processes
                    !p.ProcessName.Equals(currentProcessName, StringComparison.OrdinalIgnoreCase) && // Exclude current process by name
                    (p.ProcessName.Contains("SPT", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Aki", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Tarkov", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Contains("Escape", StringComparison.OrdinalIgnoreCase)) &&
                    // Also exclude if it's the same executable path (handles cases where process name might differ)
                    !(currentProcessPath != null && TryGetProcessPath(p) == currentProcessPath)
                ).ToList();
                
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Found {sptProcesses.Count} SPT-related processes (excluding current process):");
                foreach (var proc in sptProcesses)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {proc.ProcessName} (PID: {proc.Id}, HasExited: {proc.HasExited})");
                }
                
                bool hasLauncherRunning = sptLauncherProcesses.Length > 0 || akiLauncherProcesses.Length > 0;
                bool hasAnySptProcesses = sptProcesses.Count > 0;
                
                // Always ensure button state is set on UI thread
                Dispatcher.Invoke(() =>
                {
                    // Check if we have a tracked launcher process (but don't require it to not have exited)
                    // The launcher process may exit after starting the server, but the server is still running
                    bool hasTrackedLauncher = _isLauncherRunning && _launcherProcess != null;
                    
                    // Always keep Stop button border clickable
                    if (StopButtonBorder != null)
                    {
                        StopButtonBorder.IsHitTestVisible = true;
                        StopButtonBorder.Visibility = Visibility.Visible;
                    }
                    
                    if (hasTrackedLauncher || hasLauncherRunning)
                {
                    LaunchButton.IsEnabled = false;
                        if (StopButtonBorder != null)
                        {
                            StopButtonBorder.Opacity = 1.0;
                        }
                        var processId = hasTrackedLauncher && _launcherProcess != null && !_launcherProcess.HasExited ? _launcherPid : (sptLauncherProcesses.Length > 0 ? sptLauncherProcesses[0].Id : akiLauncherProcesses[0].Id);
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] Launcher running - Launch button DISABLED, Stop button ENABLED");
                }
                else if (hasAnySptProcesses)
                {
                    LaunchButton.IsEnabled = true;
                        if (StopButtonBorder != null)
                        {
                            StopButtonBorder.Opacity = 1.0;
                        }
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] SPT process running - Launch button ENABLED, Stop button ENABLED");
                }
                else
                {
                    LaunchButton.IsEnabled = true;
                        if (StopButtonBorder != null)
                        {
                            StopButtonBorder.Opacity = 0.6; // Slightly dim when no processes, but still clickable
                        }
                        System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] No processes - Launch button ENABLED, Stop button dimmed but clickable");
                    }
                    
                    // Log final button state for debugging
                    System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Final state - LaunchButton.IsEnabled: {LaunchButton.IsEnabled}");
                    if (StopButtonBorder != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] StopButtonBorder.Opacity: {StopButtonBorder.Opacity}, IsHitTestVisible: {StopButtonBorder.IsHitTestVisible}");
                    }
                    System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] _isLauncherRunning: {_isLauncherRunning}, _launcherProcess: {(_launcherProcess != null ? "not null" : "null")}");
                });
            }
            catch (Exception ex)
            {
                // Fallback to enabled state if there's an error
                Dispatcher.Invoke(() =>
                {
                LaunchButton.IsEnabled = true;
                    if (StopButtonBorder != null)
                    {
                        StopButtonBorder.Opacity = 0.6;
                    }
                });
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Error: {ex.Message}\n{ex.StackTrace}");
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
                
                System.Windows.MessageBox.Show("SPT installer has been launched. Please follow the installation wizard.", 
                    "Installer Launched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (HttpRequestException ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"Failed to download the SPT installer.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.", 
                    "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (TaskCanceledException ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"Download timed out.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.", 
                    "Download Timeout", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                InstallSptButton.Content = "📥 Install Latest SPT Version";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"An error occurred while installing SPT.\n\nError: {ex.Message}", 
                    "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InstallFikaButton_Click(object sender, RoutedEventArgs e)
        {
            // First check if Fika is already installed
            var fikaModPath = AutoDetectFikaMod();
            if (!string.IsNullOrEmpty(fikaModPath))
            {
                var result = System.Windows.MessageBox.Show(
                    $"Fika mod appears to be already installed at:\n{fikaModPath}\n\n" +
                    "Do you want to reinstall it anyway?",
                    "Fika Already Installed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            // Check if SPT path is set
            var sptPath = GetSptInstallPath();
            if (string.IsNullOrEmpty(sptPath) || !Directory.Exists(sptPath))
            {
                System.Windows.MessageBox.Show(
                    "SPT installation path is not set. Please set the SPT launcher path first.",
                    "SPT Path Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            const string fikaReleasesApi = "https://api.github.com/repos/project-fika/Fika-Installer/releases/latest";
            const string installerFileName = "FikaInstaller.exe";
            
            try
            {
                // Disable button during download
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.IsEnabled = false;
                    InstallFikaButton.Content = "⏳ Checking for latest version...";
                }

                // Get latest release from GitHub API
                string? installerDownloadUrl = null;
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "SPT-Launcher-WPF");
                        client.Timeout = TimeSpan.FromSeconds(30);
                        
                        var response = await client.GetStringAsync(fikaReleasesApi);
                        var jsonDoc = JsonDocument.Parse(response);
                        
                        // Look for installer assets in the release
                        if (jsonDoc.RootElement.TryGetProperty("assets", out var assets))
                        {
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (asset.TryGetProperty("name", out var nameElement))
                                {
                                    var fileName = nameElement.GetString() ?? "";
                                    // Look for .exe files, preferably installer or setup
                                    if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (asset.TryGetProperty("browser_download_url", out var urlElement))
                                        {
                                            installerDownloadUrl = urlElement.GetString();
                                            // Prefer installer or setup files
                                            if (fileName.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
                                                fileName.Contains("setup", StringComparison.OrdinalIgnoreCase))
                                            {
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("403") || ex.Message.Contains("rate limit"))
                {
                    // GitHub API rate limit exceeded - fallback to manual download
                    var result = System.Windows.MessageBox.Show(
                        "GitHub API rate limit exceeded. Cannot automatically download the installer.\n\n" +
                        "Would you like to open the GitHub releases page to download it manually?\n\n" +
                        "After downloading, place the installer in your SPT directory and run it from there.",
                        "Rate Limit Exceeded",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/project-fika/Fika-Installer/releases",
                            UseShellExecute = true
                        });
                    }
                    
                    if (InstallFikaButton != null)
                    {
                        InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                        InstallFikaButton.IsEnabled = true;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    // Other API errors - fallback to manual download
                    System.Diagnostics.Debug.WriteLine($"[InstallFikaButton] Error fetching release info: {ex.Message}");
                }

                // If no installer found, fallback to opening releases page
                if (string.IsNullOrEmpty(installerDownloadUrl))
                {
                    var result = System.Windows.MessageBox.Show(
                        "Could not automatically find the Fika installer download link.\n\n" +
                        "Would you like to open the GitHub releases page to download it manually?\n\n" +
                        "After downloading, place the installer in your SPT directory and run it from there.",
                        "Manual Download Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/project-fika/Fika-Installer/releases",
                            UseShellExecute = true
                        });
                    }
                    
                    if (InstallFikaButton != null)
                    {
                        InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                        InstallFikaButton.IsEnabled = true;
                    }
                    return;
                }

                // Update button text
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "⏳ Downloading installer...";
                }

                // Place installer in SPT directory (required for Fika installer to work)
                string installerPath = Path.Combine(sptPath, installerFileName);
                
                // Download the installer with progress
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    
                    using (var response = await client.GetAsync(installerDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[8192];
                            long totalBytesRead = 0;
                            int bytesRead;
                            
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;
                                
                                // Update button text with progress if we have total size
                                if (totalBytes > 0 && InstallFikaButton != null)
                                {
                                    var percent = (double)totalBytesRead / totalBytes * 100;
                                    Dispatcher.Invoke(() =>
                                    {
                                        InstallFikaButton.Content = $"⏳ Downloading... {percent:F0}%";
                                    });
                                }
                            }
                        }
                    }
                }
                
                // Update button text
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "🚀 Launching installer...";
                }

                // Execute the installer from SPT directory (required for Fika installer)
                var processInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = sptPath
                };

                Process.Start(processInfo);

                // Reset button state
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                    InstallFikaButton.IsEnabled = true;
                }

                System.Windows.MessageBox.Show(
                    "Fika installer has been launched. Please follow the installation wizard.\n\n" +
                    "Make sure to install Fika to your SPT installation directory.\n\n" +
                    "The version will be detected automatically after installation completes.",
                    "Installer Launched",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                // Refresh Fika detection after a delay (to allow installer to complete)
                _ = Task.Run(async () =>
                {
                    // Wait a bit for installer to potentially complete
                    await Task.Delay(5000);
                    Dispatcher.Invoke(() =>
                    {
                        UpdateFikaVersionDisplay();
                    });
                });
            }
            catch (HttpRequestException ex)
            {
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                    InstallFikaButton.IsEnabled = true;
                }
                
                string errorMessage = $"Failed to download the Fika installer.\n\nError: {ex.Message}";
                string suggestion = "Please check your internet connection and try again.";
                
                // Check if it's a rate limit error
                if (ex.Message.Contains("403") || ex.Message.Contains("rate limit"))
                {
                    errorMessage = "GitHub rate limit exceeded. Cannot download the installer automatically.";
                    suggestion = "Would you like to open the GitHub releases page to download it manually?\n\n" +
                                "After downloading, place the installer in your SPT directory and run it from there.";
                    
                    var result = System.Windows.MessageBox.Show(
                        $"{errorMessage}\n\n{suggestion}",
                        "Rate Limit Exceeded",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "https://github.com/project-fika/Fika-Installer/releases",
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"{errorMessage}\n\n{suggestion}",
                        "Download Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (TaskCanceledException ex)
            {
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                    InstallFikaButton.IsEnabled = true;
                }
                System.Windows.MessageBox.Show(
                    $"Download timed out.\n\nError: {ex.Message}\n\n" +
                    "Please check your internet connection and try again.",
                    "Download Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                if (InstallFikaButton != null)
                {
                    InstallFikaButton.Content = "📥 Install Latest FIKA Version";
                    InstallFikaButton.IsEnabled = true;
                }
                System.Windows.MessageBox.Show(
                    $"An error occurred while installing Fika.\n\nError: {ex.Message}",
                    "Installation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Fika Co-op Configuration Methods
        
        /// <summary>
        /// Auto-detects SPT.Launcher.exe in common installation locations
        /// </summary>
        private string AutoDetectSptLauncher()
        {
            try
            {
                // First, check if there's a running SPT.Launcher process and get its path
                var launcherProcesses = Process.GetProcessesByName("SPT.Launcher");
                if (launcherProcesses.Length > 0)
                {
                    try
                    {
                        var processPath = TryGetProcessPath(launcherProcesses[0]);
                        if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Found running process: {processPath}");
                            return processPath;
                        }
                    }
                    catch
                    {
                        // Continue with file system search if process path fails
                    }
                }

                // Get all drive letters
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                // Common SPT folder names to check
                var folderNames = new[] { "SPT", "SPT-AKI", "SinglePlayerTarkov", "spt" };

                // Search in common locations
                foreach (var drive in drives)
                {
                    foreach (var folderName in folderNames)
                    {
                        // Check root level (e.g., D:\SPT\SPT.Launcher.exe)
                        var rootPath = Path.Combine(drive, folderName, "SPT.Launcher.exe");
                        if (File.Exists(rootPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Found at root: {rootPath}");
                            return rootPath;
                        }

                        // Check nested structure (e.g., D:\SPT\SPT\SPT.Launcher.exe)
                        var nestedPath = Path.Combine(drive, folderName, folderName, "SPT.Launcher.exe");
                        if (File.Exists(nestedPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Found nested: {nestedPath}");
                            return nestedPath;
                        }

                        // Also check in SPT subdirectory (e.g., D:\SPT\SPT\SPT.Launcher.exe)
                        var subDirPath = Path.Combine(drive, folderName, "SPT", "SPT.Launcher.exe");
                        if (File.Exists(subDirPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Found in subdirectory: {subDirPath}");
                            return subDirPath;
                        }
                    }
                }

                // If not found in common locations, do a limited recursive search
                foreach (var drive in drives.Take(3)) // Limit to first 3 drives for performance
                {
                    var found = SearchForLauncherRecursive(drive, maxDepth: 2);
                    if (!string.IsNullOrEmpty(found))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Found via recursive search: {found}");
                        return found;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[AutoDetectSptLauncher] No launcher found");
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Recursively searches for SPT.Launcher.exe (with depth limit for performance)
        /// </summary>
        private string SearchForLauncherRecursive(string directory, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth >= maxDepth)
            {
                return string.Empty;
            }

            try
            {
                if (!Directory.Exists(directory))
                {
                    return string.Empty;
                }

                // Check current directory
                var launcherPath = Path.Combine(directory, "SPT.Launcher.exe");
                if (File.Exists(launcherPath))
                {
                    return launcherPath;
                }

                // Search subdirectories (limit to avoid scanning system folders)
                var skipFolders = new[] { "Windows", "Program Files", "Program Files (x86)", "ProgramData", 
                                         "$Recycle.Bin", "System Volume Information", "PerfLogs", 
                                         "Recovery", "Documents and Settings" };

                var dirs = Directory.GetDirectories(directory);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (skipFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue; // Skip system folders
                    }

                    var found = SearchForLauncherRecursive(dir, maxDepth, currentDepth + 1);
                    if (!string.IsNullOrEmpty(found))
                    {
                        return found;
                    }
                }
            }
            catch
            {
                // Ignore errors (permissions, etc.)
            }

            return string.Empty;
        }

        private string GetSptInstallPath()
        {
            try
            {
                var launcherPath = LauncherPathTextBox.Text;
                if (string.IsNullOrEmpty(launcherPath) || !File.Exists(launcherPath))
                {
                    return string.Empty;
                }
                
                // Extract directory from launcher path (e.g., D:\SPT\SPT\SPT.Launcher.exe -> D:\SPT\SPT)
                var launcherDir = Path.GetDirectoryName(launcherPath);
                if (string.IsNullOrEmpty(launcherDir))
                {
                    return string.Empty;
                }
                
                // Check if the parent directory exists and has more files/subdirectories than just the nested SPT folder
                // This handles cases where SPT is in a nested structure like D:\SPT\SPT\SPT.Launcher.exe
                // In this case, we want to back up the entire D:\SPT directory, not just D:\SPT\SPT
                var parentDir = Path.GetDirectoryName(launcherDir);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    // Get the name of the launcher directory (e.g., "SPT" from "D:\SPT\SPT")
                    var launcherDirName = Path.GetFileName(launcherDir);
                    // Get the name of the parent directory (e.g., "SPT" from "D:\SPT")
                    var parentDirName = Path.GetFileName(parentDir);
                    
                    // If the parent and launcher directories have the same name (e.g., both are "SPT"),
                    // this suggests a nested structure like D:\SPT\SPT\ where we should use the parent
                    if (string.Equals(launcherDirName, parentDirName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Also check if parent has multiple items (not just the nested directory)
                        var parentItems = Directory.GetFileSystemEntries(parentDir);
                        if (parentItems.Length > 1)
                        {
                            // Parent directory is the root SPT directory
                            return parentDir;
                        }
                    }
                    else
                    {
                        // Check if parent directory contains SPT-related files
                        var serverExePath = Path.Combine(parentDir, "SPT.Server.exe");
                        var sptDataPath = Path.Combine(parentDir, "SPT_Data");
                        if (File.Exists(serverExePath) || Directory.Exists(sptDataPath))
                        {
                            // Parent directory contains SPT files, so use it as the root SPT directory
                            return parentDir;
                        }
                    }
                }
                
                return launcherDir;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Auto-detects if Fika mod is installed in the SPT mods directory
        /// </summary>
        private string AutoDetectFikaMod()
        {
            try
            {
                var sptPath = GetSptInstallPath();
                return AutoDetectFikaModWithPath(sptPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaMod] Error: {ex.Message}\n{ex.StackTrace}");
                return string.Empty;
            }
        }

        private string AutoDetectFikaModWithPath(string sptPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] SPT Path: {sptPath}");
                
                if (string.IsNullOrEmpty(sptPath) || !Directory.Exists(sptPath))
                {
                    System.Diagnostics.Debug.WriteLine("[AutoDetectFikaModWithPath] SPT path is invalid or doesn't exist");
                    return string.Empty;
                }

                // Common Fika mod names and locations
                var fikaModNames = new[] { "Fika", "Fika-Coop", "FikaCoop", "FIKA", "fika" };
                var modsDirectories = new[]
                {
                    Path.Combine(sptPath, "user", "mods"),           // Standard SPT mods location
                    Path.Combine(sptPath, "BepInEx", "plugins"),     // BepInEx plugins location
                    Path.Combine(sptPath, "mods"),                   // Alternative mods location
                    Path.Combine(sptPath, "SPT", "user", "mods"),    // Nested SPT structure
                    Path.Combine(sptPath, "SPT", "BepInEx", "plugins") // Nested BepInEx structure
                };

                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Checking {modsDirectories.Length} mod directories");

                // Check each mods directory
                foreach (var modsDir in modsDirectories)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Checking directory: {modsDir}");
                    
                    if (!Directory.Exists(modsDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Directory does not exist: {modsDir}");
                        continue;
                    }

                    // List all subdirectories for debugging
                    try
                    {
                        var subDirs = Directory.GetDirectories(modsDir);
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Found {subDirs.Length} subdirectories in {modsDir}");
                        foreach (var subDir in subDirs)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath]   - {Path.GetFileName(subDir)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Error listing subdirectories: {ex.Message}");
                    }

                    // Check for each Fika mod name
                    foreach (var modName in fikaModNames)
                    {
                        var fikaModPath = Path.Combine(modsDir, modName);
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Checking: {fikaModPath}");
                        
                        if (Directory.Exists(fikaModPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Directory exists: {fikaModPath}");
                            
                            // Verify it's actually Fika by checking for common Fika files
                            var fikaDll = Path.Combine(fikaModPath, "Fika.dll");
                            var fikaCoreDll = Path.Combine(fikaModPath, "Fika.Core.dll");
                            var packageJson = Path.Combine(fikaModPath, "package.json");
                            
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath]   Fika.dll: {File.Exists(fikaDll)}");
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath]   Fika.Core.dll: {File.Exists(fikaCoreDll)}");
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath]   package.json: {File.Exists(packageJson)}");
                            
                            if (File.Exists(fikaDll) || File.Exists(fikaCoreDll) || 
                                (File.Exists(packageJson) && CheckIfFikaPackageJson(packageJson)))
                            {
                                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Found Fika mod at: {fikaModPath}");
                                return fikaModPath;
                            }
                        }
                    }

                    // Also search for Fika in subdirectories (some mods might be in version folders)
                    // Also search for any directory containing Fika DLLs (broader search)
                    try
                    {
                        var allDirs = Directory.GetDirectories(modsDir, "*", SearchOption.AllDirectories);
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Searching {allDirs.Length} subdirectories recursively");
                        
                        foreach (var subDir in allDirs)
                        {
                            // Check if this directory contains Fika DLLs
                            var fikaDll = Path.Combine(subDir, "Fika.dll");
                            var fikaCoreDll = Path.Combine(subDir, "Fika.Core.dll");
                            var packageJson = Path.Combine(subDir, "package.json");
                            
                            if (File.Exists(fikaDll) || File.Exists(fikaCoreDll) || 
                                (File.Exists(packageJson) && CheckIfFikaPackageJson(packageJson)))
                            {
                                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Found Fika mod at (recursive search): {subDir}");
                                return subDir;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Error in recursive search: {ex.Message}");
                    }
                }

                // Last resort: search entire SPT directory for Fika DLLs
                System.Diagnostics.Debug.WriteLine("[AutoDetectFikaModWithPath] Performing broad search for Fika DLLs in SPT directory");
                try
                {
                    var allFikaDlls = Directory.GetFiles(sptPath, "Fika*.dll", SearchOption.AllDirectories);
                    System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Found {allFikaDlls.Length} Fika DLL files");
                    
                    foreach (var dllPath in allFikaDlls)
                    {
                        var dir = Path.GetDirectoryName(dllPath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Found Fika DLL at: {dllPath}, returning directory: {dir}");
                            return dir;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Error in broad search: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("[AutoDetectFikaModWithPath] Fika mod not found");
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetectFikaModWithPath] Error: {ex.Message}\n{ex.StackTrace}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks if a package.json file belongs to Fika mod
        /// </summary>
        private bool CheckIfFikaPackageJson(string packageJsonPath)
        {
            try
            {
                var jsonContent = File.ReadAllText(packageJsonPath);
                var json = JsonDocument.Parse(jsonContent);
                
                // Check if it's a Fika package by looking for "fika" in name or id
                if (json.RootElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString() ?? "";
                    if (name.Contains("fika", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                
                if (json.RootElement.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.GetString() ?? "";
                    if (id.Contains("fika", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return false;
        }

        private void UpdateSptVersionDisplay()
        {
            _ = UpdateSptVersionDisplayAsync();
        }

        private void UpdateFikaVersionDisplay()
        {
            _ = UpdateFikaVersionDisplayAsync();
        }

        private async Task UpdateFikaVersionDisplayAsync()
        {
            try
            {
                if (FikaVersionText == null)
                {
                    System.Diagnostics.Debug.WriteLine("[UpdateFikaVersionDisplayAsync] FikaVersionText is null");
                    return;
                }

                // Get SPT path on UI thread first (before going to background thread)
                string sptPath = string.Empty;
                Dispatcher.Invoke(() =>
                {
                    sptPath = GetSptInstallPath();
                });
                
                System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] SPT Path from UI thread: {sptPath}");

                // Detect Fika mod and get version on background thread
                string? version = null;
                bool fikaInstalled = false;
                string? fikaModPath = null;
                
                await Task.Run(() =>
                {
                    try
                    {
                        // Detect Fika mod using the captured SPT path
                        fikaModPath = AutoDetectFikaModWithPath(sptPath);
                        fikaInstalled = !string.IsNullOrEmpty(fikaModPath);
                        
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Fika mod detected: {fikaInstalled}, Path: {fikaModPath}");
                        
                        // Get Fika version if installed
                        if (fikaInstalled)
                        {
                            System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Getting version from: {fikaModPath}");
                            version = GetFikaVersion(fikaModPath);
                            System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Version result: {version ?? "(null)"}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Error in background task: {ex.Message}");
                    }
                });
                
                // Check for updates if version is available (outside Task.Run since it's async)
                FikaUpdateInfo? updateInfo = null;
                if (!string.IsNullOrEmpty(version))
                {
                    try
                    {
                        updateInfo = await SptDetectionService.Instance.CheckForFikaUpdatesAsync(version);
                        _currentFikaUpdateInfo = updateInfo;
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Update check result: {(updateInfo?.IsUpdateAvailable == true ? $"Update available: {updateInfo.LatestVersion}" : "Up to date")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Error checking for updates: {ex.Message}");
                    }
                }
                
                // Update UI on UI thread
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (FikaVersionText == null)
                        {
                            return;
                        }

                        // Show/hide install button based on whether Fika is installed
                        if (InstallFikaButton != null)
                        {
                            InstallFikaButton.Visibility = fikaInstalled ? Visibility.Collapsed : Visibility.Visible;
                        }

                        if (!fikaInstalled)
                        {
                            FikaVersionText.Text = "Not detected";
                            FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                            
                            // Hide update status if not installed
                            if (FikaUpdateStatusPanel != null)
                            {
                                FikaUpdateStatusPanel.Visibility = Visibility.Collapsed;
                            }
                            
                            // Disable Fika checkbox if not installed
                            if (EnableFikaCheckBox != null)
                            {
                                EnableFikaCheckBox.IsEnabled = false;
                                if (_fikaEnabled)
                                {
                                    _fikaEnabled = false;
                                    EnableFikaCheckBox.IsChecked = false;
                                    SettingsService.Instance.FikaEnabled = false;
                                    SettingsService.Instance.SaveSettings();
                                }
                            }
                        }
                        else
                        {
                            // Update version display
                            if (string.IsNullOrEmpty(version))
                            {
                                FikaVersionText.Text = "Installed (version unknown)";
                                FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                            }
                            else
                            {
                                FikaVersionText.Text = version;
                                FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryColor");
                            }
                            
                            // Update update status display
                            if (FikaUpdateStatusPanel != null && FikaUpdateStatusText != null)
                            {
                                if (updateInfo == null)
                                {
                                    // Check failed (network error, etc.) - hide update status
                                    FikaUpdateStatusPanel.Visibility = Visibility.Collapsed;
                                }
                                else if (updateInfo.IsUpdateAvailable)
                                {
                                    // Update available
                                    FikaUpdateStatusPanel.Visibility = Visibility.Visible;
                                    FikaUpdateStatusText.Text = $"Update available: {updateInfo.LatestVersion}";
                                    FikaUpdateStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                                }
                                else
                                {
                                    // Up to date
                                    FikaUpdateStatusPanel.Visibility = Visibility.Visible;
                                    FikaUpdateStatusText.Text = "Up to date";
                                    FikaUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                                }
                            }
                            
                            // Enable Fika checkbox if installed
                            if (EnableFikaCheckBox != null)
                            {
                                EnableFikaCheckBox.IsEnabled = true;
                                EnableFikaCheckBox.IsChecked = _fikaEnabled;
                                
                                if (_fikaEnabled)
                                {
                                    // Show IP editor
                                    if (FikaIpEditorPanel != null)
                                    {
                                        FikaIpEditorPanel.Visibility = Visibility.Visible;
                                    }
                                    // Only set default IP if text box is empty
                                    if (FikaIpTextBox != null && string.IsNullOrWhiteSpace(FikaIpTextBox.Text))
                                    {
                                        FikaIpTextBox.Text = _defaultIp;
                                    }
                                }
                                else
                                {
                                    if (FikaIpEditorPanel != null)
                                    {
                                        FikaIpEditorPanel.Visibility = Visibility.Collapsed;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Error updating UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Outer exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Gets the Fika mod version from the mod directory
        /// </summary>
        private string GetFikaVersion(string fikaModPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fikaModPath) || !Directory.Exists(fikaModPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Invalid path: {fikaModPath}");
                    return string.Empty;
                }

                System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Searching for version in: {fikaModPath}");

                // Try to read version from package.json first
                var packageJsonPath = Path.Combine(fikaModPath, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found package.json: {packageJsonPath}");
                        var jsonContent = File.ReadAllText(packageJsonPath);
                        var jsonDoc = JsonDocument.Parse(jsonContent);
                        
                        if (jsonDoc.RootElement.TryGetProperty("version", out var versionElement))
                        {
                            var version = versionElement.GetString();
                            if (!string.IsNullOrEmpty(version))
                            {
                                System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found version in package.json: {version}");
                                // Normalize version (strip commit hash and suffixes)
                                version = NormalizeVersion(version);
                                return version;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Error reading package.json: {ex.Message}");
                        // Continue to try DLL version
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] package.json not found at: {packageJsonPath}");
                }

                // Search for DLL files in the mod directory and subdirectories
                var dllFiles = Directory.GetFiles(fikaModPath, "*.dll", SearchOption.AllDirectories);
                System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found {dllFiles.Length} DLL files in mod directory");

                // Try to read version from Fika.Core.dll (most reliable)
                var fikaCoreDll = dllFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Fika.Core.dll", StringComparison.OrdinalIgnoreCase))
                    ?? Path.Combine(fikaModPath, "Fika.Core.dll");
                
                if (File.Exists(fikaCoreDll))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found Fika.Core.dll: {fikaCoreDll}");
                        var versionInfo = FileVersionInfo.GetVersionInfo(fikaCoreDll);
                        var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
                        if (!string.IsNullOrEmpty(version))
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found version in Fika.Core.dll: {version}");
                            version = NormalizeVersion(version);
                            return version;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Fika.Core.dll exists but version is empty");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Error reading Fika.Core.dll: {ex.Message}");
                        // Continue to try Fika.dll
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Fika.Core.dll not found at: {fikaCoreDll}");
                }

                // Try to read version from Fika.dll
                var fikaDll = dllFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("Fika.dll", StringComparison.OrdinalIgnoreCase))
                    ?? Path.Combine(fikaModPath, "Fika.dll");
                
                if (File.Exists(fikaDll))
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found Fika.dll: {fikaDll}");
                        var versionInfo = FileVersionInfo.GetVersionInfo(fikaDll);
                        var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
                        if (!string.IsNullOrEmpty(version))
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Found version in Fika.dll: {version}");
                            version = NormalizeVersion(version);
                            return version;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Fika.dll exists but version is empty");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Error reading Fika.dll: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Fika.dll not found at: {fikaDll}");
                }

                // List all files in the directory for debugging
                try
                {
                    var allFiles = Directory.GetFiles(fikaModPath, "*", SearchOption.TopDirectoryOnly);
                    System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Files in mod directory: {string.Join(", ", allFiles.Select(Path.GetFileName))}");
                }
                catch { }

                System.Diagnostics.Debug.WriteLine("[GetFikaVersion] No version found");
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetFikaVersion] Exception: {ex.Message}\n{ex.StackTrace}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Normalizes version string by removing commit hashes and common suffixes
        /// </summary>
        private string NormalizeVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return version;
            }

            // Strip commit hash if present (format: "version+commithash")
            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }

            // Strip common suffixes like "-RELEASE", "-DEV", "-ALPHA", "-BETA"
            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                var suffix = version.Substring(dashIndex).ToUpperInvariant();
                if (suffix == "-RELEASE" || suffix == "-DEV" || suffix == "-ALPHA" || suffix == "-BETA" || suffix.StartsWith("-RC"))
                {
                    version = version.Substring(0, dashIndex);
                }
            }

            return version.Trim();
        }

        private async Task UpdateSptVersionDisplayAsync()
        {
            try
            {
                if (SptVersionText == null)
                {
                    return;
                }

                // Get launcher path from text box or settings
                var launcherPath = LauncherPathTextBox.Text;
                if (string.IsNullOrWhiteSpace(launcherPath))
                {
                    launcherPath = SettingsService.Instance.LauncherPath;
                }

                if (string.IsNullOrWhiteSpace(launcherPath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        SptVersionText.Text = "Not detected";
                        SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                        if (SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                    });
                    return;
                }

                // Check if SPT is installed and get version
                var isInstalled = SptDetectionService.Instance.IsSptInstalled(launcherPath);
                if (!isInstalled)
                {
                    Dispatcher.Invoke(() =>
                    {
                        SptVersionText.Text = "Not detected";
                        SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                        if (SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                    });
                    return;
                }

                var version = SptDetectionService.Instance.GetSptVersion(launcherPath);
                if (string.IsNullOrEmpty(version))
                {
                    Dispatcher.Invoke(() =>
                    {
                        SptVersionText.Text = "Installed (version unknown)";
                        SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                        if (SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                    });
                    return;
                }

                // Update version display
                Dispatcher.Invoke(() =>
                {
                    SptVersionText.Text = version;
                    SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryColor");
                    
                    // Show checking status
                    if (SptUpdateStatusPanel != null && SptUpdateStatusText != null)
                    {
                        SptUpdateStatusPanel.Visibility = Visibility.Visible;
                        SptUpdateStatusText.Text = "Checking for updates...";
                        SptUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    }
                });

                // Check for updates asynchronously
                var updateInfo = await SptDetectionService.Instance.CheckForUpdatesAsync(version);
                _currentUpdateInfo = updateInfo;
                
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateStatusPanel == null || SptUpdateStatusText == null)
                    {
                        return;
                    }

                    if (updateInfo == null)
                    {
                        // Check failed (network error, etc.) - hide update status
                        SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        if (UpdateNowButton != null)
                        {
                            UpdateNowButton.Visibility = Visibility.Collapsed;
                        }
                    }
                    else if (updateInfo.IsUpdateAvailable)
                    {
                        // Update available
                        SptUpdateStatusPanel.Visibility = Visibility.Visible;
                        if (string.IsNullOrWhiteSpace(updateInfo.InstallerDownloadUrl))
                        {
                            // No installer available - show manual download message
                            SptUpdateStatusText.Text = $"Update available: {updateInfo.LatestVersion} (Manual download required)";
                        }
                        else
                        {
                            SptUpdateStatusText.Text = $"Update available: {updateInfo.LatestVersion}";
                        }
                        SptUpdateStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                        if (UpdateNowButton != null)
                        {
                            UpdateNowButton.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        // Up to date
                        SptUpdateStatusPanel.Visibility = Visibility.Visible;
                        SptUpdateStatusText.Text = "Up to date";
                        SptUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                        if (UpdateNowButton != null)
                        {
                            UpdateNowButton.Visibility = Visibility.Collapsed;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateSptVersionDisplay] Error: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    if (SptVersionText != null)
                    {
                        SptVersionText.Text = "Error detecting version";
                        SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    }
                    if (SptUpdateStatusPanel != null)
                    {
                        SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                    }
                });
            }
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUpdateInfo == null || !_currentUpdateInfo.IsUpdateAvailable)
            {
                System.Windows.MessageBox.Show("No update information available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get SPT path using GetSptInstallPath which handles nested directories
            var sptPath = GetSptInstallPath();
            if (string.IsNullOrEmpty(sptPath) || !Directory.Exists(sptPath))
            {
                System.Windows.MessageBox.Show("SPT installation directory not found. Please set the SPT launcher path in settings first.", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if installer download URL is available
            if (string.IsNullOrWhiteSpace(_currentUpdateInfo.InstallerDownloadUrl))
            {
                var releaseUrl = _currentUpdateInfo.ReleaseUrl ?? "https://github.com/sp-tarkov/build/releases/latest";
                var result = System.Windows.MessageBox.Show(
                    $"Automatic update is not available for this release.\n\n" +
                    $"The GitHub release does not include a downloadable installer.\n\n" +
                    $"You can download the update manually from the GitHub releases page.\n\n" +
                    $"Would you like to open the releases page now?",
                    "Manual Update Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = releaseUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                return;
            }

            // Ask about backup
            var backupResult = System.Windows.MessageBox.Show(
                "Would you like to backup your current SPT folder before updating?\n\n" +
                "This may take a long time and consume large amounts of storage space.\n\n" +
                "Click Yes to create a backup, or No to skip backup.",
                "Backup SPT Folder?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            bool createBackup = backupResult == MessageBoxResult.Yes;
            string? backupPath = null;

            if (createBackup)
            {
                // Show folder browser for backup location
                var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "Select where to save the SPT backup",
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    backupPath = Path.Combine(folderDialog.SelectedPath, $"SPT_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                }
                else
                {
                    // User cancelled folder selection, ask if they want to continue without backup
                    var continueResult = System.Windows.MessageBox.Show(
                        "No backup location selected. Continue without backup?",
                        "No Backup",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (continueResult == MessageBoxResult.No)
                    {
                        return; // User cancelled
                    }
                    createBackup = false;
                }
            }

            // Disable button during update
            if (UpdateNowButton != null)
            {
                UpdateNowButton.IsEnabled = false;
            }

            try
            {
                // Show progress UI
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Visibility = Visibility.Visible;
                        SptUpdateProgressBar.Value = 0;
                    }
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Visibility = Visibility.Visible;
                        SptUpdateProgressText.Text = "Starting update...";
                    }
                    if (UpdateNowButton != null)
                    {
                        UpdateNowButton.Visibility = Visibility.Collapsed;
                    }
                });

                // Step 1: Download installer
                var tempPath = Path.GetTempPath();
                var installerFileName = "SPTInstaller.exe";
                var installerPath = Path.Combine(tempPath, installerFileName);

                var downloadProgress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (SptUpdateProgressBar != null)
                        {
                            SptUpdateProgressBar.Value = percent;
                        }
                        if (SptUpdateProgressText != null)
                        {
                            SptUpdateProgressText.Text = $"Downloading installer... {percent:F0}%";
                        }
                    });
                });

                await SptUpdateService.Instance.DownloadInstallerAsync(
                    _currentUpdateInfo.InstallerDownloadUrl,
                    installerPath,
                    downloadProgress);

                // Step 2: Run update process
                var statusProgress = new Progress<string>(status =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (SptUpdateProgressText != null)
                        {
                            SptUpdateProgressText.Text = status;
                        }
                    });
                });

                var progressProgress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (SptUpdateProgressBar != null)
                        {
                            SptUpdateProgressBar.Value = percent;
                        }
                    });
                });

                await SptUpdateService.Instance.UpdateSptAsync(
                    sptPath,
                    installerPath,
                    createBackup,
                    backupPath,
                    statusProgress,
                    progressProgress);

                // Update completed successfully
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Text = "Update completed successfully!";
                        SptUpdateProgressText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    }
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Value = 100;
                    }
                });

                System.Windows.MessageBox.Show(
                    "SPT has been updated successfully!\n\nThe version display will refresh automatically.",
                    "Update Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh version display with retry logic
                // The installer may need time to finish writing all files
                for (int retry = 0; retry < 5; retry++)
                {
                    await Task.Delay(2000); // Wait 2 seconds between retries
                    UpdateSptVersionDisplay();
                    
                    // Check if we got a valid version (not empty and not "Not detected")
                    await Task.Delay(500); // Small delay to let UI update
                    var currentVersion = Dispatcher.Invoke(() => SptVersionText?.Text);
                    if (!string.IsNullOrWhiteSpace(currentVersion) && 
                        currentVersion != "Not detected" && 
                        currentVersion != "Error detecting version")
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateNowButton] Version detected after {retry + 1} retries: {currentVersion}");
                        break;
                    }
                    System.Diagnostics.Debug.WriteLine($"[UpdateNowButton] Retry {retry + 1}/5: Version not yet detected");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Text = $"Update failed: {ex.Message}";
                        SptUpdateProgressText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                    }
                    if (UpdateNowButton != null)
                    {
                        UpdateNowButton.IsEnabled = true;
                        UpdateNowButton.Visibility = Visibility.Visible;
                    }
                });

                System.Windows.MessageBox.Show(
                    $"Update failed: {ex.Message}\n\n" +
                    (createBackup && !string.IsNullOrEmpty(backupPath) 
                        ? $"A backup was created at: {backupPath}\nYou can restore from there if needed."
                        : "No backup was created."),
                    "Update Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Hide progress UI after a delay
                await Task.Delay(3000);
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Visibility = Visibility.Collapsed;
                    }
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Visibility = Visibility.Collapsed;
                    }
                    if (UpdateNowButton != null && _currentUpdateInfo != null && _currentUpdateInfo.IsUpdateAvailable)
                    {
                        UpdateNowButton.Visibility = Visibility.Visible;
                        UpdateNowButton.IsEnabled = true;
                    }
                });
                
                // Force refresh version display after update completes
                // Wait a bit more to ensure all files are written
                await Task.Delay(2000);
                UpdateSptVersionDisplay();
                
                // Also refresh the update check to see if we're now up to date
                await Task.Delay(1000);
                var currentVersion = SptDetectionService.Instance.GetSptVersion(SettingsService.Instance.LauncherPath);
                if (!string.IsNullOrWhiteSpace(currentVersion))
                {
                    var updateInfo = await SptDetectionService.Instance.CheckForUpdatesAsync(currentVersion);
                    _currentUpdateInfo = updateInfo;
                    
                    Dispatcher.Invoke(() =>
                    {
                        if (updateInfo == null || !updateInfo.IsUpdateAvailable)
                        {
                            // Up to date now
                            if (SptUpdateStatusPanel != null)
                            {
                                SptUpdateStatusPanel.Visibility = Visibility.Visible;
                            }
                            if (SptUpdateStatusText != null)
                            {
                                SptUpdateStatusText.Text = "Up to date";
                                SptUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                            }
                            if (UpdateNowButton != null)
                            {
                                UpdateNowButton.Visibility = Visibility.Collapsed;
                            }
                        }
                    });
                }
            }
        }

        private string GetHttpJsonPath()
        {
            try
            {
                var sptPath = GetSptInstallPath();
                if (string.IsNullOrEmpty(sptPath))
                {
                    return string.Empty;
                }
                
                return Path.Combine(sptPath, "SPT_Data", "configs", "http.json");
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetLauncherConfigJsonPath()
        {
            try
            {
                var launcherPath = LauncherPathTextBox.Text;
                if (string.IsNullOrEmpty(launcherPath) || !File.Exists(launcherPath))
                {
                    return string.Empty;
                }
                
                // First, try the launcher executable directory (newer SPT versions)
                // config.json is now in the same directory as SPT.Launcher.exe
                var launcherDir = Path.GetDirectoryName(launcherPath);
                if (!string.IsNullOrEmpty(launcherDir))
                {
                    var configInLauncherDir = Path.Combine(launcherDir, "user", "launcher", "config.json");
                    if (File.Exists(configInLauncherDir))
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Found config in launcher directory: {configInLauncherDir}");
                        return configInLauncherDir;
                    }
                }
                
                // Fallback: try the SPT root directory (older SPT versions)
                var sptPath = GetSptInstallPath();
                if (!string.IsNullOrEmpty(sptPath))
                {
                    var configInSptRoot = Path.Combine(sptPath, "user", "launcher", "config.json");
                    if (File.Exists(configInSptRoot))
                    {
                        System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Found config in SPT root: {configInSptRoot}");
                        return configInSptRoot;
                    }
                    
                    // If file doesn't exist yet, prefer launcher directory for new files
                    if (!string.IsNullOrEmpty(launcherDir))
                    {
                        var newConfigPath = Path.Combine(launcherDir, "user", "launcher", "config.json");
                        System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Using launcher directory for new config: {newConfigPath}");
                        return newConfigPath;
                    }
                    
                    // Last resort: use SPT root
                    System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Using SPT root for new config: {configInSptRoot}");
                    return configInSptRoot;
                }
                
                // If we can't determine SPT path, use launcher directory
                if (!string.IsNullOrEmpty(launcherDir))
                {
                    var fallbackPath = Path.Combine(launcherDir, "user", "launcher", "config.json");
                    System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Fallback to launcher directory: {fallbackPath}");
                    return fallbackPath;
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetLauncherConfigJsonPath] Error: {ex.Message}");
                return string.Empty;
            }
        }

        private HttpConfig? LoadHttpJson()
        {
            try
            {
                var httpJsonPath = GetHttpJsonPath();
                if (string.IsNullOrEmpty(httpJsonPath) || !File.Exists(httpJsonPath))
                {
                    return null;
                }
                
                var jsonContent = File.ReadAllText(httpJsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                return JsonSerializer.Deserialize<HttpConfig>(jsonContent, options);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load http.json: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private bool SaveHttpJson(string ip)
        {
            try
            {
                // Validate IP address format first
                if (!System.Net.IPAddress.TryParse(ip, out _))
                {
                    System.Windows.MessageBox.Show("Invalid IP address format. Please enter a valid IP address.", 
                        "Invalid IP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                // Validate launcher path is set
                var launcherPath = LauncherPathTextBox.Text;
                if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
                {
                    System.Windows.MessageBox.Show("Please set a valid SPT Launcher path first using the Browse button.", 
                        "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                var httpJsonPath = GetHttpJsonPath();
                if (string.IsNullOrEmpty(httpJsonPath))
                {
                    System.Windows.MessageBox.Show("Unable to determine SPT installation path. Please check your launcher path.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Load existing config or create default
                HttpConfig? config = null;
                int retries = 3;
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        config = LoadHttpJson() ?? new HttpConfig();
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (i == retries - 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveHttpJson] Failed to load http.json after {retries} attempts: {ex.Message}");
                            // Create new config if we can't load existing
                            config = new HttpConfig();
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(100); // Wait a bit before retry
                        }
                    }
                }
                
                if (config == null)
                {
                    config = new HttpConfig();
                }
                
                // Update IP addresses
                config.ip = ip;
                config.backendIp = ip;
                
                // Ensure directory exists
                var configDir = Path.GetDirectoryName(httpJsonPath);
                if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                
                // Save JSON with retry logic
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var jsonContent = JsonSerializer.Serialize(config, options);
                
                System.Diagnostics.Debug.WriteLine($"[SaveHttpJson] Attempting to save IP {ip} to {httpJsonPath}");
                
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        File.WriteAllText(httpJsonPath, jsonContent);
                        System.Diagnostics.Debug.WriteLine($"[SaveHttpJson] Successfully saved IP {ip} to http.json");
                        return true;
                    }
                    catch (IOException) when (i < retries - 1)
                    {
                        // File might be locked, wait and retry
                        System.Diagnostics.Debug.WriteLine($"[SaveHttpJson] File locked, retrying... ({i + 1}/{retries})");
                        System.Threading.Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveHttpJson] Error on attempt {i + 1}: {ex.Message}");
                        if (i == retries - 1)
                        {
                            throw;
                        }
                        System.Threading.Thread.Sleep(200);
                    }
                }
                
                throw new Exception("Failed to save after multiple retries - file may be locked by another process");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save http.json: {ex.Message}\n\nMake sure SPT is not running and try again.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void RevertToDefaultIp()
        {
            try
            {
                SaveHttpJson(_defaultIp);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to revert IP address: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private LauncherConfig? LoadLauncherConfig()
        {
            try
            {
                var configJsonPath = GetLauncherConfigJsonPath();
                if (string.IsNullOrEmpty(configJsonPath) || !File.Exists(configJsonPath))
                {
                    return null;
                }
                
                var jsonContent = File.ReadAllText(configJsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                return JsonSerializer.Deserialize<LauncherConfig>(jsonContent, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadLauncherConfig] Failed to load config.json: {ex.Message}");
                return null;
            }
        }

        private bool SaveLauncherConfig(bool isDevMode, string? ipAddress = null)
        {
            try
            {
                // Validate launcher path is set
                var launcherPath = LauncherPathTextBox.Text;
                if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
                {
                    System.Diagnostics.Debug.WriteLine("[SaveLauncherConfig] Invalid launcher path, skipping config.json save");
                    return false;
                }
                
                var configJsonPath = GetLauncherConfigJsonPath();
                if (string.IsNullOrEmpty(configJsonPath))
                {
                    var sptPath = GetSptInstallPath();
                    System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Unable to determine config.json path. SPT path: '{sptPath}'");
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Config.json path: {configJsonPath}");
                
                // Load existing config or create default
                LauncherConfig? config = null;
                int retries = 3;
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        config = LoadLauncherConfig();
                        if (config == null)
                        {
                            // Create default config if file doesn't exist
                            config = new LauncherConfig();
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (i == retries - 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Failed to load config.json after {retries} attempts: {ex.Message}");
                            // Create new config if we can't load existing
                            config = new LauncherConfig();
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(100); // Wait a bit before retry
                        }
                    }
                }
                
                if (config == null)
                {
                    config = new LauncherConfig();
                }
                
                // Update IsDevMode
                config.IsDevMode = isDevMode;
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Setting IsDevMode to {isDevMode}");
                
                // If dev mode is enabled and IP is provided, update the Server URL
                if (isDevMode && !string.IsNullOrEmpty(ipAddress))
                {
                    // Validate IP address format
                    if (System.Net.IPAddress.TryParse(ipAddress, out _))
                    {
                        // Ensure Server object exists
                        if (config.Server == null)
                        {
                            config.Server = new LauncherServerConfig();
                            System.Diagnostics.Debug.WriteLine("[SaveLauncherConfig] Created new Server config object");
                        }
                        
                        // Store old URL for logging
                        var oldUrl = config.Server.Url;
                        
                        // Update URL with the provided IP
                        var newUrl = $"https://{ipAddress}:6969";
                        config.Server.Url = newUrl;
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Updated Server.Url from '{oldUrl}' to '{newUrl}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Invalid IP address format: {ipAddress}");
                    }
                }
                else if (isDevMode && string.IsNullOrEmpty(ipAddress))
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] WARNING: Dev mode enabled but no IP address provided!");
                }
                
                // Ensure directory exists
                var configDir = Path.GetDirectoryName(configJsonPath);
                if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                    System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Created directory: {configDir}");
                }
                
                // Save JSON with retry logic
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var jsonContent = JsonSerializer.Serialize(config, options);
                
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Attempting to save IsDevMode={isDevMode}, IP={ipAddress ?? "null"} to {configJsonPath}");
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Config JSON preview: IsDevMode={config.IsDevMode}, Server.Url={config.Server?.Url ?? "null"}");
                
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        // Directory should already exist from above, but double-check
                        if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                        {
                            Directory.CreateDirectory(configDir);
                            System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Created directory on retry: {configDir}");
                        }
                        
                        File.WriteAllText(configJsonPath, jsonContent);
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Successfully saved config.json to: {configJsonPath}");
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Final values: IsDevMode={config.IsDevMode}, Server.Url={config.Server?.Url ?? "null"}");
                        
                        // Verify the file was written correctly
                        if (File.Exists(configJsonPath))
                        {
                            var savedContent = File.ReadAllText(configJsonPath);
                            var savedConfig = JsonSerializer.Deserialize<LauncherConfig>(savedContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (savedConfig != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Verification - IsDevMode={savedConfig.IsDevMode}, Server.Url={savedConfig.Server?.Url ?? "null"}");
                                
                                // Double-check that the values match what we intended
                                if (savedConfig.IsDevMode != isDevMode)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] WARNING: IsDevMode mismatch! Expected {isDevMode}, got {savedConfig.IsDevMode}");
                                }
                                
                                if (isDevMode && !string.IsNullOrEmpty(ipAddress))
                                {
                                    var expectedUrl = $"https://{ipAddress}:6969";
                                    if (savedConfig.Server?.Url != expectedUrl)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] WARNING: Server.Url mismatch! Expected {expectedUrl}, got {savedConfig.Server?.Url ?? "null"}");
                                    }
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] WARNING: Could not deserialize saved config.json");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] ERROR: File was not created at {configJsonPath}");
                            return false;
                        }
                        
                        return true;
                    }
                    catch (IOException) when (i < retries - 1)
                    {
                        // File might be locked, wait and retry
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] File locked, retrying... ({i + 1}/{retries})");
                        System.Threading.Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Error on attempt {i + 1}: {ex.Message}");
                        if (i == retries - 1)
                        {
                            throw;
                        }
                        System.Threading.Thread.Sleep(200);
                    }
                }
                
                throw new Exception("Failed to save after multiple retries - file may be locked by another process");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Failed to save config.json: {ex.Message}");
                return false;
            }
        }

        private void EnableFikaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var checkBox = sender as System.Windows.Controls.CheckBox;
                if (checkBox == null) return;
                
                _fikaEnabled = checkBox.IsChecked == true;
                
                // Save FIKA enabled state
                SettingsService.Instance.FikaEnabled = _fikaEnabled;
                SettingsService.Instance.SaveSettings();
                
                if (_fikaEnabled)
                {
                    // Show IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Visible;
                    
                    // Determine IP to use - prioritize textbox value if user has entered one,
                    // then saved IP, then config.json, then default
                    string ipToUse = _defaultIp;
                    
                    // First, check if user has entered an IP in the textbox
                    if (FikaIpTextBox != null && !string.IsNullOrWhiteSpace(FikaIpTextBox.Text))
                    {
                        var textboxIp = FikaIpTextBox.Text.Trim();
                        if (System.Net.IPAddress.TryParse(textboxIp, out _))
                        {
                            ipToUse = textboxIp;
                            System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Using IP from textbox: {ipToUse}");
                        }
                    }
                    
                    // If textbox is empty or invalid, try saved IP
                    if (ipToUse == _defaultIp)
                    {
                        var savedIp = SettingsService.Instance.FikaIpAddress;
                        if (!string.IsNullOrEmpty(savedIp) && System.Net.IPAddress.TryParse(savedIp, out _))
                        {
                            if (FikaIpTextBox != null)
                            {
                                FikaIpTextBox.Text = savedIp;
                            }
                            ipToUse = savedIp;
                            System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Using saved IP: {ipToUse}");
                        }
                    }
                    
                    // If still default, try loading from config.json
                    if (ipToUse == _defaultIp)
                    {
                        var launcherConfig = LoadLauncherConfig();
                        if (launcherConfig != null && launcherConfig.Server != null && !string.IsNullOrEmpty(launcherConfig.Server.Url))
                        {
                            try
                            {
                                var uri = new Uri(launcherConfig.Server.Url);
                                var ipFromConfig = uri.Host;
                                if (!string.IsNullOrEmpty(ipFromConfig) && System.Net.IPAddress.TryParse(ipFromConfig, out _))
                                {
                                    if (FikaIpTextBox != null)
                                    {
                                        FikaIpTextBox.Text = ipFromConfig;
                                    }
                                    ipToUse = ipFromConfig;
                                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Using IP from config.json: {ipToUse}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Error parsing URL from config: {ex.Message}");
                            }
                        }
                    }
                    
                    // If still default, set textbox to default
                    if (ipToUse == _defaultIp && FikaIpTextBox != null)
                    {
                        FikaIpTextBox.Text = _defaultIp;
                        System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Using default IP: {ipToUse}");
                    }
                    
                    // Enable developer mode in config.json AND save the IP address
                    // This ensures both IsDevMode and Server.Url are updated when enabling Fika
                    var configPath = GetLauncherConfigJsonPath();
                    var sptPath = GetSptInstallPath();
                    
                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] SPT Path: {sptPath}");
                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Config Path: {configPath}");
                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] IP to save: {ipToUse}");
                    
                    bool saved = SaveLauncherConfig(true, ipToUse);
                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Enabled Fika with IP: {ipToUse}, SaveLauncherConfig returned: {saved}");
                    
                    if (!saved)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to save Fika configuration to config.json.\n\n" +
                            $"SPT Path: {sptPath}\n" +
                            $"Config path: {configPath}\n" +
                            $"IP Address: {ipToUse}\n\n" +
                            $"Please ensure:\n" +
                            $"1. The SPT launcher path is set correctly\n" +
                            $"2. You have write permissions to the SPT directory\n" +
                            $"3. The config.json file is not locked by another process\n" +
                            $"4. Check the Debug output for more details",
                            "Configuration Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        // Verify the file was actually written
                        if (File.Exists(configPath))
                        {
                            try
                            {
                                var savedConfig = LoadLauncherConfig();
                                if (savedConfig != null)
                                {
                                    var actualUrl = savedConfig.Server?.Url ?? "null";
                                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Verification - IsDevMode: {savedConfig.IsDevMode}, Server.Url: {actualUrl}");
                                    
                                    // Show success message with details
                                    ShowToastNotification($"Fika Co-op enabled with IP: {ipToUse}");
                                    
                                    // Also show a detailed message in debug
                                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] SUCCESS - Config saved to: {configPath}");
                                    System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Config contents - IsDevMode: {savedConfig.IsDevMode}, Server.Url: {actualUrl}");
                                }
                                else
                                {
                                    System.Windows.MessageBox.Show(
                                        $"Warning: Config file was created but could not be verified.\n\n" +
                                        $"Path: {configPath}\n\n" +
                                        $"Please check the file manually to ensure it contains the correct settings.",
                                        "Verification Warning",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Error verifying saved config: {ex.Message}");
                            }
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                $"Warning: SaveLauncherConfig returned true, but config file was not found at:\n\n{configPath}\n\n" +
                                $"Please check the Debug output for more details.",
                                "File Not Found",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    // Hide IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Collapsed;
                    
                    // Disable developer mode in config.json
                    SaveLauncherConfig(false);
                    System.Diagnostics.Debug.WriteLine("[EnableFikaCheckBox_Changed] Disabled Fika");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error updating Fika Co-op configuration: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[EnableFikaCheckBox_Changed] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SaveFikaIpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ipAddress = FikaIpTextBox.Text.Trim();
                
                if (string.IsNullOrEmpty(ipAddress))
                {
                    System.Windows.MessageBox.Show("Please enter an IP address.", "Invalid Input", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
                }
                
                // Validate IP address format
                if (!System.Net.IPAddress.TryParse(ipAddress, out _))
                {
                    System.Windows.MessageBox.Show("Invalid IP address format. Please enter a valid IP address.", 
                        "Invalid IP", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // Always save to settings first (as backup)
                SettingsService.Instance.FikaIpAddress = ipAddress;
                SettingsService.Instance.SaveSettings();
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Saved IP to settings: {ipAddress}");
                
                // Save to config.json with dev mode enabled and IP address
                // When dev mode is true, the URL in config.json will be updated with the user's IP
                var configPath = GetLauncherConfigJsonPath();
                var sptPath = GetSptInstallPath();
                
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] SPT Path: {sptPath}");
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Config Path: {configPath}");
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Fika Enabled: {_fikaEnabled}");
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] IP to save: {ipAddress}");
                
                if (_fikaEnabled)
                {
                    bool configJsonSaved = SaveLauncherConfig(true, ipAddress);
                    System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] SaveLauncherConfig returned: {configJsonSaved}");
                    
                    if (!configJsonSaved)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to save IP address to config.json.\n\n" +
                            $"SPT Path: {sptPath}\n" +
                            $"Config path: {configPath}\n" +
                            $"IP Address: {ipAddress}\n\n" +
                            $"IP was saved to settings, but config.json could not be updated.\n" +
                            $"Please ensure:\n" +
                            $"1. Fika Co-op is enabled\n" +
                            $"2. The SPT launcher path is correct\n" +
                            $"3. You have write permissions\n" +
                            $"4. Check the Debug output for more details",
                            "Configuration Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Verify the save
                    if (File.Exists(configPath))
                    {
                        var savedConfig = LoadLauncherConfig();
                        if (savedConfig != null)
                        {
                            var actualUrl = savedConfig.Server?.Url ?? "null";
                            System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Verification - IsDevMode: {savedConfig.IsDevMode}, Server.Url: {actualUrl}");
                            
                            var expectedUrl = $"https://{ipAddress}:6969";
                            if (actualUrl != expectedUrl)
                            {
                                System.Windows.MessageBox.Show(
                                    $"Warning: IP address may not have been saved correctly.\n\n" +
                                    $"Expected URL: {expectedUrl}\n" +
                                    $"Actual URL: {actualUrl}\n\n" +
                                    $"Please check the config.json file manually at:\n{configPath}",
                                    "Verification Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                            else
                            {
                                // Show toast notification
                                ShowToastNotification($"IP address saved: {ipAddress}");
                            }
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                $"Warning: Config file exists but could not be read.\n\n" +
                                $"Path: {configPath}\n\n" +
                                $"Please check the file manually.",
                                "Verification Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(
                            $"Warning: SaveLauncherConfig returned true, but config file was not found.\n\n" +
                            $"Expected path: {configPath}\n\n" +
                            $"Please check the Debug output for more details.",
                            "File Not Found",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    // If Fika is not enabled, enable it first
                    if (EnableFikaCheckBox != null)
                    {
                        EnableFikaCheckBox.IsChecked = true;
                        // The checkbox change handler will save the config with the IP
                        System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Fika was not enabled, enabling it now (checkbox handler will save config)");
                        ShowToastNotification($"IP address saved: {ipAddress}");
                    }
                    else
                    {
                        ShowToastNotification("IP was applied");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Error: {ex.Message}\n{ex.StackTrace}");
                System.Windows.MessageBox.Show($"Error saving IP address: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowToastNotification(string message)
        {
            try
            {
                if (ToastNotification == null || ToastMessage == null) return;

                // Set the message
                ToastMessage.Text = message;

                // Make visible
                ToastNotification.Visibility = Visibility.Visible;

                // Fade in animation
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300)
                };

                var slideIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 20,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300)
                };

                ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                var transform = ToastNotification.RenderTransform as System.Windows.Media.TranslateTransform;
                if (transform != null)
                {
                    transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);
                }

                // Auto-dismiss after 2 seconds
                var dismissTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                dismissTimer.Tick += (s, e) =>
                {
                    dismissTimer.Stop();
                    
                    // Fade out animation
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };

                    var slideOut = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 20,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };

                    fadeOut.Completed += (sender, args) =>
                    {
                        ToastNotification.Visibility = Visibility.Collapsed;
                    };

                    ToastNotification.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    if (transform != null)
                    {
                        transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
                    }
                };
                dismissTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShowToastNotification] Error: {ex.Message}");
            }
        }

    }

    // HttpConfig class for JSON serialization
    public class HttpConfig
    {
        public string ip { get; set; } = "127.0.0.1";
        public int port { get; set; } = 6969;
        public string backendIp { get; set; } = "127.0.0.1";
        public int backendPort { get; set; } = 6969;
        public bool logRequests { get; set; } = true;
        public Dictionary<string, object> serverImagePathOverride { get; set; } = new();
    }

    // LauncherConfig class for JSON serialization (SPT\SPT\user\launcher\config.json)
    public class LauncherConfig
    {
        [JsonPropertyName("FirstRun")]
        public bool FirstRun { get; set; } = false;
        
        [JsonPropertyName("DefaultLocale")]
        public string DefaultLocale { get; set; } = "English";
        
        [JsonPropertyName("LauncherStartGameAction")]
        public int LauncherStartGameAction { get; set; } = 0;
        
        [JsonPropertyName("UseAutoLogin")]
        public bool UseAutoLogin { get; set; } = false;
        
        [JsonPropertyName("IsDevMode")]
        public bool IsDevMode { get; set; } = false;
        
        [JsonPropertyName("GamePath")]
        public string? GamePath { get; set; }
        
        [JsonPropertyName("ExcludeFromCleanup")]
        public List<string> ExcludeFromCleanup { get; set; } = new();
        
        [JsonPropertyName("Server")]
        public LauncherServerConfig? Server { get; set; }
    }

    public class LauncherServerConfig
    {
        [JsonPropertyName("AutoLoginCreds")]
        public object? AutoLoginCreds { get; set; }
        
        [JsonPropertyName("Name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("Url")]
        public string Url { get; set; } = "https://127.0.0.1:6969";
    }
}
