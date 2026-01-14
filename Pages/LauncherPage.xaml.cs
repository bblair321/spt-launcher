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
            
            // Update path status after loading
            UpdatePathStatus();
            
            // Update SPT version display
            UpdateSptVersionDisplay();
            
            // Load FIKA enabled state but not the IP address
            _fikaEnabled = SettingsService.Instance.FikaEnabled;
            if (EnableFikaCheckBox != null)
            {
                EnableFikaCheckBox.IsChecked = _fikaEnabled;
                
                if (_fikaEnabled)
                {
                    // Show IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Visible;
                    // Only set default IP if text box is empty
                    if (string.IsNullOrWhiteSpace(FikaIpTextBox.Text))
                    {
                        FikaIpTextBox.Text = _defaultIp;
                    }
                }
                else
                {
                    FikaIpEditorPanel.Visibility = Visibility.Collapsed;
                }
            }
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
                StatusText.Text = "Starting launcher...";
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
                    StatusText.Text = $"Launcher started (PID: {_launcherPid})";
                    
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
                StatusText.Text = "Failed to start launcher";
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
                StatusText.Text = "Failed to start launcher";
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
                        if (failedCount > 0)
                        {
                            StatusText.Text = $"Stopped {stoppedCount} process(es), {failedCount} failed";
                        }
                        else
                        {
                            StatusText.Text = $"Stopped {stoppedCount} launcher process(es)";
                        }
                    }
                    else if (failedCount > 0)
                    {
                        StatusText.Text = $"Failed to stop {failedCount} process(es)";
                    }
                    else
                    {
                        StatusText.Text = "No launcher processes were running";
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
                    StatusText.Text = "SPT Server stopped";
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
            StatusText.Text = "Ready";
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
                    StatusText.Text = $"SPT Launcher running (PID: {processId})";
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] Launcher running - Launch button DISABLED, Stop button ENABLED");
                }
                else if (hasAnySptProcesses)
                {
                    LaunchButton.IsEnabled = true;
                        if (StopButtonBorder != null)
                        {
                            StopButtonBorder.Opacity = 1.0;
                        }
                    StatusText.Text = $"SPT process running (PID: {sptProcesses[0].Id})";
                    System.Diagnostics.Debug.WriteLine("[UpdateLauncherUI] SPT process running - Launch button ENABLED, Stop button ENABLED");
                }
                else
                {
                    LaunchButton.IsEnabled = true;
                        if (StopButtonBorder != null)
                        {
                            StopButtonBorder.Opacity = 0.6; // Slightly dim when no processes, but still clickable
                        }
                    StatusText.Text = "Ready";
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
                StatusText.Text = "Ready";
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

        private void InstallFikaButton_Click(object sender, RoutedEventArgs e)
        {
            const string fikaReleasesUrl = "https://github.com/project-fika/Fika-Installer/releases";
            
            try
            {
                // Open the releases page in the default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = fikaReleasesUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open FIKA releases page.\n\nError: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void UpdateSptVersionDisplay()
        {
            _ = UpdateSptVersionDisplayAsync();
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
                var sptPath = GetSptInstallPath();
                if (string.IsNullOrEmpty(sptPath))
                {
                    return string.Empty;
                }
                
                return Path.Combine(sptPath, "user", "launcher", "config.json");
            }
            catch
            {
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
                    System.Diagnostics.Debug.WriteLine("[SaveLauncherConfig] Unable to determine config.json path");
                    return false;
                }
                
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
                        }
                        
                        // Update URL with the provided IP
                        config.Server.Url = $"https://{ipAddress}:6969";
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Updated Server.Url to {config.Server.Url}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Invalid IP address format: {ipAddress}");
                    }
                }
                
                // Ensure directory exists
                var configDir = Path.GetDirectoryName(configJsonPath);
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
                
                System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Attempting to save IsDevMode={isDevMode} to {configJsonPath}");
                
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        File.WriteAllText(configJsonPath, jsonContent);
                        System.Diagnostics.Debug.WriteLine($"[SaveLauncherConfig] Successfully saved IsDevMode={isDevMode} to config.json");
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
                
                // Enable/disable developer mode in config.json based on FIKA state
                SaveLauncherConfig(_fikaEnabled);
                
                if (_fikaEnabled)
                {
                    // Show IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Visible;
                    
                    // Load current IP - try saved IP first, then config.json, then default
                    var savedIp = SettingsService.Instance.FikaIpAddress;
                    if (!string.IsNullOrEmpty(savedIp) && savedIp != _defaultIp)
                    {
                        FikaIpTextBox.Text = savedIp;
                    }
                    else
                    {
                        // Try to load from config.json Server.Url
                        var launcherConfig = LoadLauncherConfig();
                        if (launcherConfig != null && launcherConfig.Server != null && !string.IsNullOrEmpty(launcherConfig.Server.Url))
                        {
                            // Extract IP from URL (format: https://IP:PORT)
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
                    // Hide IP editor
                    FikaIpEditorPanel.Visibility = Visibility.Collapsed;
                    
                    // Only revert to default IP if user explicitly unchecks (don't auto-revert on load)
                    // This allows users to disable FIKA without losing their IP setting
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error updating Fika Co-op configuration: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (_fikaEnabled)
                {
                    bool configJsonSaved = SaveLauncherConfig(true, ipAddress);
                    System.Diagnostics.Debug.WriteLine($"[SaveFikaIpButton] Attempted to save IsDevMode and IP to config.json: {configJsonSaved}");
                }
                
                // Show toast notification
                ShowToastNotification("IP was applied");
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
