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
using System.Threading;
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
        
        // SPT Update tracking
        private SptUpdateInfo? _currentUpdateInfo = null;
        private CancellationTokenSource? _sptUpdateCts;
        private bool _sptUpdateInProgress;
        private EftCompatibilityInfo? _currentEftInfo;
        
        // Fika Update tracking
        private FikaUpdateInfo? _currentFikaUpdateInfo = null;

        // Post-update verify panel
        private CancellationTokenSource? _updateVerifyCts;

        // Compact readiness: remember the user's Details preference across async refreshes
        private bool _readinessUserCollapsed; // hid details while Needs attention
        private bool _readinessUserExpanded;  // opened details (keep open even when Ready)

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

            // Update live Tarkov / EFT version display
            UpdateEftVersionDisplay();
            
            // Update Fika version display
            UpdateFikaVersionDisplay();

            RefreshSptRecoveryPanel();
            RefreshFirstRunWizard();
        }

        private void LauncherPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _sptUpdateCts?.Cancel();

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
            var forceFirstRun = App.ForceFirstRun && !SettingsService.Instance.FirstRunWizardDismissed;

            if (forceFirstRun)
            {
                // New-user test mode: start with an empty path so the walkthrough is shown.
                LauncherPathTextBox.Text = string.Empty;
            }
            else if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
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

            // Update live Tarkov / EFT version display
            UpdateEftVersionDisplay();
            
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
                PathStatusText.Text = "Select your SPT.Launcher.exe path.";
                PathStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
            }
            else
            {
                string path = LauncherPathTextBox.Text.Trim();
                if (File.Exists(path))
                {
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        PathStatusText.Text = "Valid launcher path selected.";
                        PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green
                    }
                    else
                    {
                        PathStatusText.Text = "Selected file is not an executable (.exe).";
                        PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)); // Yellow
                    }
                }
                else
                {
                    PathStatusText.Text = "File not found. Please check the path.";
                    PathStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red
                }
            }

            RefreshPlayHero();
            RefreshFirstRunWizard();
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
                System.Windows.MessageBox.Show(
                    "Launch is unavailable right now (SPT may already be running, or an update is in progress).\n\n" +
                    "If SPT is already open, use Stop — or wait a moment and try again.",
                    "Can't launch yet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            
            // Validate path is provided
            if (string.IsNullOrWhiteSpace(LauncherPathTextBox.Text))
            {
                System.Diagnostics.Debug.WriteLine("[LaunchButton_Click] Path is empty");
                System.Windows.MessageBox.Show(
                    "No SPT launcher path is set.\n\n" +
                    "Use Auto-detect or Browse to select SPT.Launcher.exe in your SPT install folder.",
                    "Path needed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Validate file exists
            string launcherPath = LauncherPathTextBox.Text.Trim();
            System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Launcher path: {launcherPath}");
            
            if (!File.Exists(launcherPath))
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] File does not exist: {launcherPath}");
                System.Windows.MessageBox.Show(
                    "That launcher path doesn't exist anymore:\n\n" +
                    $"{launcherPath}\n\n" +
                    "Use Auto-detect or Browse to pick SPT.Launcher.exe again.",
                    "Path not found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Validate it's actually an executable
            if (!launcherPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] File is not an .exe: {launcherPath}");
                System.Windows.MessageBox.Show(
                    "That file isn't an executable.\n\n" +
                    "Browse to SPT.Launcher.exe (not a folder, shortcut without .exe, or config file).",
                    "Wrong file type",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (TryAttachToAlreadyRunningLauncher(launcherPath, out var alreadyRunningPid))
            {
                System.Windows.MessageBox.Show(
                    "SPT launcher is already running.\n\n" +
                    $"Process ID: {alreadyRunningPid}\n\n" +
                    "Use Stop if you need to close it before launching again.",
                    "Already running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
                    throw new Exception(
                        "Windows refused to start the executable (often antivirus, SmartScreen, or a blocked download).");
                }
                
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Process started, PID: {_launcherProcess.Id}");
                
                // Give it a moment to see if it exits immediately
                System.Threading.Thread.Sleep(100);
                
                if (_launcherProcess.HasExited)
                {
                    int exitCode = _launcherProcess.ExitCode;
                    System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Process exited immediately with code: {exitCode}");
                    throw new Exception(
                        $"SPT.Launcher started then closed immediately (exit code {exitCode}).\n\n" +
                        "Common fixes:\n" +
                        "• Right-click SPT.Launcher.exe → Properties → Unblock (if shown)\n" +
                        "• Allow it in Windows Defender / antivirus\n" +
                        "• Confirm you selected the real SPT.Launcher.exe, not a different tool");
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
                var (title, errorMsg) = DescribeLaunchWin32Failure(winEx, launcherPath);
                System.Windows.MessageBox.Show(errorMsg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Win32Exception: {winEx.Message} (Error Code: {winEx.NativeErrorCode})");
                
                LaunchButton.IsEnabled = true;
                if (StopButtonBorder != null)
                {
                    StopButtonBorder.Opacity = 0.6;
                }
            }
            catch (Exception ex)
            {
                string errorMsg =
                    "Couldn't start SPT.Launcher.\n\n" +
                    $"{ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\n\nDetails: {ex.InnerException.Message}";
                }
                errorMsg += $"\n\nPath:\n{launcherPath}";
                
                System.Windows.MessageBox.Show(errorMsg, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[LaunchButton_Click] Exception: {ex.Message}\nStack trace: {ex.StackTrace}");
                
                LaunchButton.IsEnabled = true;
                if (StopButtonBorder != null)
                {
                    StopButtonBorder.Opacity = 0.6;
                }
            }
        }

        private bool TryAttachToAlreadyRunningLauncher(string launcherPath, out int pid)
        {
            pid = 0;
            try
            {
                int currentProcessId = Process.GetCurrentProcess().Id;
                var existing = Process.GetProcessesByName("SPT.Launcher")
                    .Concat(Process.GetProcessesByName("Aki.Launcher"))
                    .Where(p => p.Id != currentProcessId && !p.HasExited)
                    .ToList();

                if (existing.Count == 0)
                {
                    return false;
                }

                Process? match = null;
                foreach (var process in existing)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) &&
                            string.Equals(path, launcherPath, StringComparison.OrdinalIgnoreCase))
                        {
                            match = process;
                            break;
                        }
                    }
                    catch
                    {
                        // Access denied reading MainModule — still treat as running
                    }
                }

                match ??= existing[0];
                pid = match.Id;
                _launcherProcess = match;
                _launcherPid = match.Id;
                _isLauncherRunning = true;
                _launcherPath = launcherPath;

                LaunchButton.IsEnabled = false;
                if (StopButtonBorder != null)
                {
                    StopButtonBorder.Opacity = 1.0;
                    StopButtonBorder.IsHitTestVisible = true;
                    StopButtonBorder.Visibility = Visibility.Visible;
                }

                _ = Task.Run(() => MonitorForServerProcess());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static (string Title, string Message) DescribeLaunchWin32Failure(
            System.ComponentModel.Win32Exception winEx,
            string launcherPath)
        {
            const string unblockHint =
                "Right-click SPT.Launcher.exe → Properties → check Unblock (if shown) → Apply.";
            const string antivirusHint =
                "Temporarily allow the file in Windows Defender / antivirus if it was quarantined.";

            return winEx.NativeErrorCode switch
            {
                2 => (
                    "Path not found",
                    "Windows couldn't find that executable.\n\n" +
                    "Browse again to SPT.Launcher.exe in your SPT folder.\n\n" +
                    $"Path:\n{launcherPath}"),
                3 => (
                    "Path not found",
                    "Part of that path is missing (folder moved or renamed).\n\n" +
                    "Use Auto-detect or Browse to locate SPT.Launcher.exe again.\n\n" +
                    $"Path:\n{launcherPath}"),
                5 => (
                    "Blocked or access denied",
                    "Windows blocked SPT.Launcher from starting (access denied).\n\n" +
                    $"Try:\n• {unblockHint}\n• {antivirusHint}\n• Run this app as Administrator only if your SPT folder needs elevated rights\n\n" +
                    $"Path:\n{launcherPath}"),
                1223 => (
                    "Blocked by Windows",
                    "Windows cancelled the launch (SmartScreen / UAC / security policy).\n\n" +
                    $"Try:\n• {unblockHint}\n• {antivirusHint}\n• If SmartScreen appears, choose More info → Run anyway\n\n" +
                    $"Path:\n{launcherPath}"),
                _ => (
                    "Launch failed",
                    $"Couldn't start SPT.Launcher.\n\n{winEx.Message} (code {winEx.NativeErrorCode})\n\n" +
                    $"If this file came from a download, try:\n• {unblockHint}\n• {antivirusHint}\n\n" +
                    $"Path:\n{launcherPath}")
            };
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
                int currentProcessId = Process.GetCurrentProcess().Id;
                string currentProcessName = Process.GetCurrentProcess().ProcessName;
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Current process: {currentProcessName} (PID: {currentProcessId})");

                var launcherProcesses = GetLauncherProcessesToStop(currentProcessId);
                LogLauncherProcessesToStop(launcherProcesses);

                var summary = new StopProcessSummary();
                foreach (var process in launcherProcesses)
                {
                    TryStopLauncherProcess(process, summary);
                }

                if (summary.FailedCount > 0)
                {
                    ShowStopFailureSummary(summary);
                }

                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Stopped {summary.StoppedCount} launcher processes, {summary.FailedCount} failed");
                CleanupTrackedLauncherAfterStop(summary.StoppedCount);

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

        private sealed class StopProcessSummary
        {
            public int StoppedCount { get; set; }
            public int FailedCount { get; set; }
            public List<string> FailedProcesses { get; } = new();
        }

        private List<Process> GetLauncherProcessesToStop(int currentProcessId)
        {
            var launcherProcesses = new List<Process>();

            if (_launcherProcess != null && !_launcherProcess.HasExited && _launcherProcess.Id != currentProcessId)
            {
                try
                {
                    var checkProcess = Process.GetProcessById(_launcherProcess.Id);
                    if (!checkProcess.HasExited)
                    {
                        launcherProcesses.Add(checkProcess);
                        System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Adding tracked launcher process: {_launcherProcess.ProcessName} (PID: {_launcherProcess.Id})");
                    }
                }
                catch (ArgumentException)
                {
                    System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Tracked launcher process no longer exists");
                }
            }

            if (launcherProcesses.Count == 0)
            {
                launcherProcesses.AddRange(Process.GetProcessesByName("SPT.Launcher")
                    .Where(p => p.Id != currentProcessId && !p.HasExited));
                launcherProcesses.AddRange(Process.GetProcessesByName("Aki.Launcher")
                    .Where(p => p.Id != currentProcessId && !p.HasExited));
            }

            return launcherProcesses;
        }

        private static void LogLauncherProcessesToStop(IEnumerable<Process> launcherProcesses)
        {
            var processes = launcherProcesses.ToList();
            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Found {processes.Count} SPT launcher process(es) to stop:");
            foreach (var proc in processes)
            {
                System.Diagnostics.Debug.WriteLine($"  - {proc.ProcessName} (PID: {proc.Id}, HasExited: {proc.HasExited})");
            }
        }

        private void TryStopLauncherProcess(Process process, StopProcessSummary summary)
        {
            try
            {
                if (process.HasExited)
                {
                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process {process.ProcessName} (PID: {process.Id}) already exited");
                    summary.StoppedCount++;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Attempting to stop {process.ProcessName} (PID: {process.Id})");
                if (TryCloseProcessGracefully(process))
                {
                    summary.StoppedCount++;
                    return;
                }

                if (TryKillProcess(process, summary))
                {
                    return;
                }

                summary.FailedCount++;
                summary.FailedProcesses.Add($"{process.ProcessName} (PID: {process.Id})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Error stopping {process.ProcessName} (PID: {process.Id}): {ex.Message}");
                summary.FailedCount++;
                summary.FailedProcesses.Add($"{process.ProcessName} (PID: {process.Id}) - {ex.Message}");
            }
        }

        private bool TryCloseProcessGracefully(Process process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Process has a window, attempting to close gracefully");
                process.CloseMainWindow();
                if (process.WaitForExit(2000))
                {
                    System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Process closed gracefully");
                    return true;
                }
            }
            catch (Exception closeEx)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Could not close window gracefully: {closeEx.Message}");
            }

            return false;
        }

        private bool TryKillProcess(Process process, StopProcessSummary summary)
        {
            try
            {
                process.Kill();
                if (process.WaitForExit(5000))
                {
                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Successfully killed {process.ProcessName} (PID: {process.Id})");
                    summary.StoppedCount++;
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 5)
            {
                return HandleAccessDeniedStop(process, summary);
            }

            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process did not exit within timeout");
            return false;
        }

        private bool HandleAccessDeniedStop(Process process, StopProcessSummary summary)
        {
            System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Access denied when killing {process.ProcessName} (PID: {process.Id})");

            int processId = process.Id;
            string processName = process.ProcessName;

            if (TryTerminateWithWmi(processId) || TryTerminateWithTaskKill(processId) || IsProcessTerminated(processId, "final check"))
            {
                summary.StoppedCount++;
                return true;
            }

            summary.FailedCount++;
            string errorMsg = $"{processName} (PID: {processId}) - Access Denied";
            if (!IsRunningAsAdministrator())
            {
                errorMsg += " (Try running this launcher as Administrator)";
            }
            summary.FailedProcesses.Add(errorMsg);
            return true;
        }

        private bool TryTerminateWithWmi(int processId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Attempting to use WMI to terminate process");
                if (TerminateProcessWithWmi(processId))
                {
                    System.Threading.Thread.Sleep(1000);
                    if (IsProcessTerminated(processId, "WMI"))
                    {
                        System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Successfully terminated using WMI");
                        return true;
                    }
                }
            }
            catch (Exception wmiEx)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] WMI termination failed: {wmiEx.Message}");
            }

            return false;
        }

        private bool TryTerminateWithTaskKill(int processId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Attempting to use taskkill as fallback");
                var taskkillInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /PID {processId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var taskkill = Process.Start(taskkillInfo);
                if (taskkill == null)
                {
                    return false;
                }

                taskkill.WaitForExit(5000);
                System.Threading.Thread.Sleep(500);
                if (IsProcessTerminated(processId, "taskkill"))
                {
                    System.Diagnostics.Debug.WriteLine("[StopSptProcessesAsync] Successfully killed using taskkill");
                    return true;
                }
            }
            catch (Exception taskkillEx)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] taskkill also failed: {taskkillEx.Message}");
            }

            return false;
        }

        private bool IsProcessTerminated(int processId, string context)
        {
            try
            {
                using var checkProcess = Process.GetProcessById(processId);
                if (checkProcess.HasExited)
                {
                    System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process exited ({context})");
                    return true;
                }
            }
            catch (ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"[StopSptProcessesAsync] Process doesn't exist anymore ({context})");
                return true;
            }

            return false;
        }

        private void ShowStopFailureSummary(StopProcessSummary summary)
        {
            string failedMessage = summary.StoppedCount > 0
                ? $"Successfully stopped {summary.StoppedCount} process(es).\n\n"
                : "";

            failedMessage += $"Failed to stop {summary.FailedCount} process(es):\n";
            failedMessage += string.Join("\n", summary.FailedProcesses);

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
                        }
                    }
                }
            });
        }

        private void CleanupTrackedLauncherAfterStop(int stoppedCount)
        {
            if (stoppedCount <= 0 || _launcherProcess == null)
            {
                return;
            }

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
                    RefreshPlayHero(hasTrackedLauncher || hasLauncherRunning, hasAnySptProcesses);
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
                    RefreshPlayHero();
                });
                System.Diagnostics.Debug.WriteLine($"[UpdateLauncherUI] Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private enum PlayHeroState
        {
            Idle,
            Ready,
            Running,
            Attention
        }

        private void RefreshPlayHero(bool? launcherRunning = null, bool? anySptRunning = null)
        {
            if (PlayStatusHeadline == null || PlayStatusDetail == null)
            {
                return;
            }

            var isLauncherRunning = launcherRunning ?? (_isLauncherRunning && _launcherProcess != null);
            var isAnySptRunning = anySptRunning ?? false;

            var path = LauncherPathTextBox?.Text?.Trim() ?? string.Empty;
            var hasValidPath = !string.IsNullOrWhiteSpace(path)
                               && File.Exists(path)
                               && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            var heroState = PlayHeroState.Idle;

            if (isLauncherRunning)
            {
                PlayStatusHeadline.Text = "SPT is running";
                PlayStatusDetail.Text = "Use Stop when you want to shut down the launcher and related processes.";
                heroState = PlayHeroState.Running;
            }
            else if (isAnySptRunning)
            {
                PlayStatusHeadline.Text = "SPT processes detected";
                PlayStatusDetail.Text = "You can launch again, or Stop to close running SPT/Tarkov processes.";
                heroState = PlayHeroState.Running;
            }
            else if (!hasValidPath)
            {
                PlayStatusHeadline.Text = "Set your SPT launcher path to begin";
                PlayStatusDetail.Text = "Browse to SPT.Launcher.exe below, then Launch when ready.";
                heroState = PlayHeroState.Attention;
            }
            else
            {
                var sptReady = SptVersionText?.Text != null
                               && !SptVersionText.Text.Equals("Not detected", StringComparison.OrdinalIgnoreCase)
                               && !SptVersionText.Text.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                if (!sptReady)
                {
                    PlayStatusHeadline.Text = "SPT not detected";
                    PlayStatusDetail.Text = "Install SPT or point the path at a valid SPT launcher.";
                    heroState = PlayHeroState.Attention;
                }
                else
                {
                    PlayStatusHeadline.Text = "Ready to launch";
                    PlayStatusDetail.Text = "Your SPT launcher path looks good. Launch when you're ready.";
                    heroState = PlayHeroState.Ready;
                }
            }

            // Attention from readiness (updates/patcher) takes priority over plain Ready.
            if (heroState == PlayHeroState.Ready &&
                TryGetReadinessAttention(out _, out _))
            {
                heroState = PlayHeroState.Attention;
            }

            ApplyPlayHeroAccent(heroState);
            RefreshReadinessSummary();
        }

        private void ApplyPlayHeroAccent(PlayHeroState state)
        {
            if (PlayHeroAccent == null)
            {
                return;
            }

            var resourceKey = state switch
            {
                PlayHeroState.Ready => "StatusSuccessColor",
                PlayHeroState.Running => "StatusInfoColor",
                PlayHeroState.Attention => "StatusWarningColor",
                _ => "ChromeMutedTextColor"
            };

            PlayHeroAccent.SetResourceReference(Border.BackgroundProperty, resourceKey);
        }

        private void ApplyStatusMessageAccent(Border? accent, bool? kind)
        {
            if (accent == null)
            {
                return;
            }

            var resourceKey = kind switch
            {
                true => "StatusSuccessColor",
                false => "StatusErrorColor",
                _ => "StatusInfoColor"
            };

            accent.SetResourceReference(Border.BackgroundProperty, resourceKey);
        }

        private void ReadinessDetailsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleReadinessDetails();
        }

        private void ReadinessSummaryStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks that originated on the action buttons inside the strip.
            if (e.OriginalSource is DependencyObject source &&
                FindAncestor<System.Windows.Controls.Button>(source) != null)
            {
                return;
            }

            ToggleReadinessDetails();
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void ToggleReadinessDetails()
        {
            var expanded = ReadinessDetailsCard?.Visibility == Visibility.Visible;
            if (expanded)
            {
                _readinessUserCollapsed = true;
                _readinessUserExpanded = false;
                SetReadinessDetailsExpanded(false);
            }
            else
            {
                _readinessUserCollapsed = false;
                _readinessUserExpanded = true;
                SetReadinessDetailsExpanded(true);
            }
        }

        private void SetReadinessDetailsExpanded(bool expanded)
        {
            if (ReadinessDetailsCard != null)
            {
                ReadinessDetailsCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ReadinessDetailsToggleButton != null)
            {
                ReadinessDetailsToggleButton.Content = expanded ? "Hide details" : "Details";
            }
        }

        private void RefreshReadinessSummary()
        {
            if (ReadinessSummaryTitle == null || ReadinessSummaryDetail == null || ReadinessSummaryDot == null)
            {
                return;
            }

            var needsAttention = TryGetReadinessAttention(out var reason, out var summaryLine);
            var readyBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            var attentionBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));

            if (needsAttention)
            {
                ReadinessSummaryTitle.Text = "Needs attention";
                ReadinessSummaryDetail.Text = reason;
                ReadinessSummaryDot.Background = attentionBrush;

                // Auto-open unless the user explicitly hid details.
                SetReadinessDetailsExpanded(!_readinessUserCollapsed || _readinessUserExpanded);
            }
            else
            {
                ReadinessSummaryTitle.Text = "Ready";
                ReadinessSummaryDetail.Text = summaryLine;
                ReadinessSummaryDot.Background = readyBrush;

                // Ready defaults to collapsed, but keep Details open if the user opened it.
                // (Previously every version/refresh pass forced-collapse after ~1s.)
                SetReadinessDetailsExpanded(_readinessUserExpanded);
            }
        }

        private bool TryGetReadinessAttention(out string reason, out string readySummary)
        {
            reason = "";
            var sptVersion = SptVersionText?.Text?.Trim() ?? "";
            var sptStatus = SptUpdateStatusText?.Text?.Trim() ?? "";
            var eftStatus = EftStatusText?.Text?.Trim() ?? "";
            var fikaStatus = FikaUpdateStatusText?.Text?.Trim() ?? "";
            var fikaVersion = FikaVersionText?.Text?.Trim() ?? "";

            var sptReady = !string.IsNullOrWhiteSpace(sptVersion)
                           && !sptVersion.Equals("Not detected", StringComparison.OrdinalIgnoreCase)
                           && !sptVersion.StartsWith("Error", StringComparison.OrdinalIgnoreCase);

            readySummary = sptReady
                ? $"SPT {sptVersion}" +
                  (string.IsNullOrWhiteSpace(EftVersionText?.Text) ||
                   EftVersionText!.Text.Equals("Not detected", StringComparison.OrdinalIgnoreCase)
                      ? ""
                      : $" · Tarkov {EftVersionText.Text}") +
                  (fikaVersion.Equals("Not detected", StringComparison.OrdinalIgnoreCase)
                      ? " · Fika optional"
                      : $" · Fika {fikaVersion}")
                : "Set up SPT to get ready.";

            if (_sptUpdateInProgress)
            {
                reason = "SPT update in progress.";
                return true;
            }

            if (UpdateVerifyPanel?.Visibility == Visibility.Visible)
            {
                var verifyTitle = UpdateVerifyTitleText?.Text?.Trim();
                reason = string.IsNullOrWhiteSpace(verifyTitle)
                    ? "Finish verifying the latest update."
                    : verifyTitle;
                return true;
            }

            if (!HasValidLauncherPath(out _))
            {
                reason = "Set your SPT.Launcher.exe path under setup.";
                return true;
            }

            if (!sptReady)
            {
                reason = "SPT is not detected at the current path.";
                return true;
            }

            if (UpdateNowButton?.Visibility == Visibility.Visible ||
                sptStatus.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
            {
                reason = string.IsNullOrWhiteSpace(sptStatus) || sptStatus == "—"
                    ? "An SPT update is available."
                    : $"SPT: {sptStatus}.";
                return true;
            }

            if (EftPatcherGuidancePanel?.Visibility == Visibility.Visible)
            {
                reason = "No downgrade patcher for your live Tarkov yet.";
                return true;
            }

            // Live Tarkov must match the patcher source before SPT can copy + downgrade.
            if (_currentEftInfo?.Status == EftCompatibilityStatus.UpdateRequired)
            {
                reason = string.IsNullOrWhiteSpace(eftStatus) || eftStatus == "—"
                    ? "Update live Tarkov so SPT can copy and run the patcher."
                    : $"Tarkov: {eftStatus}.";
                return true;
            }

            if (!IsSptDetectedAtCurrentPath() &&
                _currentEftInfo?.Status is EftCompatibilityStatus.NotDetected
                    or EftCompatibilityStatus.RequiredUnknown)
            {
                reason = string.IsNullOrWhiteSpace(eftStatus) || eftStatus == "—"
                    ? "Tarkov needs attention before install."
                    : $"Tarkov: {eftStatus}.";
                return true;
            }

            if (UpdateFikaButton?.Visibility == Visibility.Visible ||
                fikaStatus.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
            {
                reason = string.IsNullOrWhiteSpace(fikaStatus) || fikaStatus == "—"
                    ? "A Fika update is available."
                    : $"Fika: {fikaStatus}.";
                return true;
            }

            return false;
        }

        private void AdvancedToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SetSetupPanelExpanded(AdvancedPanel?.Visibility != Visibility.Visible);
        }

        private void SetSetupPanelExpanded(bool expanded)
        {
            if (AdvancedPanel == null || AdvancedToggleButton == null)
            {
                return;
            }

            AdvancedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            AdvancedToggleButton.Content = expanded ? "Hide setup" : "Show setup";
        }

        private bool HasValidLauncherPath(out string path)
        {
            path = LauncherPathTextBox?.Text?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(path)
                   && File.Exists(path)
                   && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSptDetectedAtCurrentPath()
        {
            if (!HasValidLauncherPath(out var path))
            {
                return false;
            }

            try
            {
                return SptDetectionService.Instance.IsSptInstalled(path);
            }
            catch
            {
                return false;
            }
        }

        private bool _firstRunAwaitingFinish;
        private bool _firstRunPreferLocateStep;

        private void RefreshFirstRunWizard()
        {
            if (FirstRunWizardPanel == null || MainLauncherContent == null)
            {
                return;
            }

            var hasValidPath = HasValidLauncherPath(out var path);
            var sptReady = IsSptDetectedAtCurrentPath();
            var dismissed = SettingsService.Instance.FirstRunWizardDismissed;
            var wizardWasVisible = FirstRunWizardPanel.Visibility == Visibility.Visible;

            if (dismissed && !_firstRunAwaitingFinish)
            {
                FirstRunWizardPanel.Visibility = Visibility.Collapsed;
                MainLauncherContent.Visibility = Visibility.Visible;
                return;
            }

            // Path + SPT ready.
            // If the user is mid-walkthrough, always advance to step 3.
            // Only silent-skip the wizard for returning users who never opened it.
            if (hasValidPath && sptReady)
            {
                var inWalkthrough = wizardWasVisible
                                    || _firstRunAwaitingFinish
                                    || _firstRunPreferLocateStep;

                if (inWalkthrough)
                {
                    AdvanceFirstRunToReadyStep(path);
                    return;
                }

                if (!dismissed)
                {
                    SettingsService.Instance.FirstRunWizardDismissed = true;
                    SettingsService.Instance.SaveSettings();
                    App.ClearForceFirstRun();
                }

                FirstRunWizardPanel.Visibility = Visibility.Collapsed;
                MainLauncherContent.Visibility = Visibility.Visible;
                return;
            }

            _firstRunAwaitingFinish = false;
            FirstRunWizardPanel.Visibility = Visibility.Visible;
            MainLauncherContent.Visibility = Visibility.Collapsed;
            SetSetupPanelExpanded(false);

            // Install first when there's nothing to point at yet.
            // After install / "I already have SPT", move to locate launcher.
            if (!hasValidPath && !_firstRunPreferLocateStep)
            {
                ShowFirstRunStep(1);
                return;
            }

            ShowFirstRunStep(2);
            if (FirstRunPathStatusText != null)
            {
                if (hasValidPath && !sptReady)
                {
                    FirstRunPathStatusText.Text =
                        $"Path set, but SPT was not detected there:\n{path}\nTry a different SPT.Launcher.exe.";
                    FirstRunPathStatusText.Foreground =
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
                }
                else if (string.IsNullOrWhiteSpace(FirstRunPathStatusText.Text) ||
                         FirstRunPathStatusText.Text.StartsWith("No launcher", StringComparison.OrdinalIgnoreCase) ||
                         FirstRunPathStatusText.Text.StartsWith("That path", StringComparison.OrdinalIgnoreCase) ||
                         FirstRunPathStatusText.Text.StartsWith("Could not auto-detect", StringComparison.OrdinalIgnoreCase) ||
                         FirstRunPathStatusText.Text.StartsWith("Still could", StringComparison.OrdinalIgnoreCase))
                {
                    FirstRunPathStatusText.Text =
                        "Click Auto-detect after the installer finishes, or Browse to SPT.Launcher.exe.";
                    FirstRunPathStatusText.Foreground =
                        (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                }
            }
        }

        private void AdvanceFirstRunToReadyStep(string? path = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                HasValidLauncherPath(out path);
            }

            _firstRunAwaitingFinish = true;
            _firstRunPreferLocateStep = true;

            if (FirstRunWizardPanel != null)
            {
                FirstRunWizardPanel.Visibility = Visibility.Visible;
            }

            if (MainLauncherContent != null)
            {
                MainLauncherContent.Visibility = Visibility.Collapsed;
            }

            ShowFirstRunStep(3);
            if (FirstRunReadyDetailText != null && !string.IsNullOrWhiteSpace(path))
            {
                FirstRunReadyDetailText.Text =
                    $"SPT looks good at:\n{path}\n\nYou can launch from the Play card, check Tarkov/Fika readiness, or tweak setup anytime.";
            }

            // Path was often empty on first Loaded pass — refresh dependent status now.
            UpdateSptVersionDisplay();
            UpdateEftVersionDisplay();
            UpdateFikaVersionDisplay();
        }

        private void ShowFirstRunStep(int step)
        {
            if (FirstRunStep1Panel != null)
            {
                FirstRunStep1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FirstRunStep2Panel != null)
            {
                FirstRunStep2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FirstRunStep3Panel != null)
            {
                FirstRunStep3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (FirstRunStepLabel != null)
            {
                FirstRunStepLabel.Text = step switch
                {
                    1 => "Step 1 of 3 — Install SPT",
                    2 => "Step 2 of 3 — Find SPT.Launcher.exe",
                    3 => "Step 3 of 3 — Ready",
                    _ => "Setup"
                };
            }
        }

        private void DismissFirstRunWizard()
        {
            _firstRunAwaitingFinish = false;
            _firstRunPreferLocateStep = false;
            SettingsService.Instance.FirstRunWizardDismissed = true;
            SettingsService.Instance.SaveSettings();
            App.ClearForceFirstRun();

            if (FirstRunWizardPanel != null)
            {
                FirstRunWizardPanel.Visibility = Visibility.Collapsed;
            }

            if (MainLauncherContent != null)
            {
                MainLauncherContent.Visibility = Visibility.Visible;
            }

            RefreshPlayHero();
            UpdateSptVersionDisplay();
            UpdateEftVersionDisplay();
            UpdateFikaVersionDisplay();
            RefreshReadinessSummary();
        }

        private void FirstRunInstallSptButton_Click(object sender, RoutedEventArgs e)
        {
            if (FirstRunInstallStatusText != null)
            {
                FirstRunInstallStatusText.Text =
                    "Downloading/launching the SPT installer. When it finishes, come back and find SPT.Launcher.exe.";
            }

            _firstRunPreferLocateStep = true;
            InstallSptButton_Click(sender, e);

            // Soft advance so Browse/Auto-detect is available after install starts.
            ShowFirstRunStep(2);
            if (FirstRunPathStatusText != null)
            {
                FirstRunPathStatusText.Text =
                    "Finish the SPT installer, then Auto-detect or Browse to SPT.Launcher.exe.";
                FirstRunPathStatusText.Foreground =
                    (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
            }
        }

        private void FirstRunAlreadyInstalledButton_Click(object sender, RoutedEventArgs e)
        {
            _firstRunPreferLocateStep = true;

            var detected = AutoDetectSptLauncher();
            if (!string.IsNullOrWhiteSpace(detected))
            {
                LauncherPathTextBox.Text = detected;
                SaveSettings();
                UpdatePathStatus();
                UpdateSptVersionDisplay();
                AdvanceFirstRunToReadyStep(detected);
                return;
            }

            ShowFirstRunStep(2);
            if (FirstRunPathStatusText != null)
            {
                FirstRunPathStatusText.Text =
                    "Could not auto-detect SPT.Launcher.exe. Browse to it in your SPT install folder.";
                FirstRunPathStatusText.Foreground =
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
            }
        }

        private void FirstRunBackToInstallButton_Click(object sender, RoutedEventArgs e)
        {
            _firstRunPreferLocateStep = false;
            ShowFirstRunStep(1);
        }

        private void FirstRunBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            _firstRunPreferLocateStep = true;
            BrowseButton_Click(sender, e);
            if (HasValidLauncherPath(out var path) && IsSptDetectedAtCurrentPath())
            {
                AdvanceFirstRunToReadyStep(path);
                return;
            }

            RefreshFirstRunWizard();
        }

        private void FirstRunDetectButton_Click(object sender, RoutedEventArgs e)
        {
            _firstRunPreferLocateStep = true;
            var detected = AutoDetectSptLauncher();
            if (string.IsNullOrWhiteSpace(detected))
            {
                if (FirstRunPathStatusText != null)
                {
                    FirstRunPathStatusText.Text =
                        "Still couldn’t find SPT.Launcher.exe. Use Browse and pick it from the folder you installed SPT into.";
                    FirstRunPathStatusText.Foreground =
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8));
                }

                return;
            }

            LauncherPathTextBox.Text = detected;
            SaveSettings();
            UpdatePathStatus();
            UpdateSptVersionDisplay();
            AdvanceFirstRunToReadyStep(detected);
        }

        private void FirstRunRecheckSptButton_Click(object sender, RoutedEventArgs e)
        {
            _firstRunPreferLocateStep = true;

            // Always re-scan after install — a stale/empty path should not block detection.
            if (TryAutoDetectAndApplySptLauncher(out var path))
            {
                UpdateSptVersionDisplay();
                AdvanceFirstRunToReadyStep(path);
                return;
            }

            UpdateSptVersionDisplay();
            RefreshFirstRunWizard();
            System.Windows.MessageBox.Show(
                "SPT.Launcher.exe still wasn’t found.\n\n" +
                "Finish the official installer, then use Auto-detect or Browse to the SPT.Launcher.exe in your install folder.",
                "SPT not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void FirstRunFinishButton_Click(object sender, RoutedEventArgs e)
        {
            DismissFirstRunWizard();
        }

        private void FirstRunSkipButton_Click(object sender, RoutedEventArgs e)
        {
            DismissFirstRunWizard();
            if (!HasValidLauncherPath(out _))
            {
                SetSetupPanelExpanded(true);
            }
        }

        private void OpenSptReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SptInstallUrls.ReleasesPageUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not open SPT releases page.\n\n{ex.Message}",
                    "Open SPT Releases",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Force refresh of the UI state
            UpdateLauncherUI();
        }

        private async void InstallSptButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                InstallSptButton.IsEnabled = false;
                InstallSptButton.Content = "Downloading...";

                var installerPath = Path.Combine(Path.GetTempPath(), SptInstallUrls.InstallerFileName);

                await SptUpdateService.Instance.DownloadInstallerAsync(
                    SptInstallUrls.InstallerDownloadUrl,
                    installerPath);

                InstallSptButton.Content = "Launching...";

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });

                InstallSptButton.Content = "Install SPT";
                InstallSptButton.IsEnabled = true;

                System.Windows.MessageBox.Show("SPT installer has been launched. Please follow the installation wizard.",
                    "Installer Launched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (HttpRequestException ex)
            {
                InstallSptButton.Content = "Install SPT";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"Failed to download the SPT installer.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.",
                    "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (TaskCanceledException ex)
            {
                InstallSptButton.Content = "Install SPT";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"Download timed out.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.",
                    "Download Timeout", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                InstallSptButton.Content = "Install SPT";
                InstallSptButton.IsEnabled = true;
                System.Windows.MessageBox.Show($"An error occurred while installing SPT.\n\nError: {ex.Message}",
                    "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void InstallFikaButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ShouldContinueWithFikaInstall())
            {
                return;
            }

            await RunFikaInstallerAsync(InstallFikaButton, ResetInstallFikaButton);
        }

        private async void UpdateFikaButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFikaInstallerAsync(UpdateFikaButton, ResetUpdateFikaButton);
        }

        private async Task RunFikaInstallerAsync(System.Windows.Controls.Button? progressButton, Action resetButton)
        {
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

            var installerPath = Path.Combine(sptPath, FikaInstallUrls.InstallerFileName);

            try
            {
                SetFikaButtonState(progressButton, "⏳ Downloading installer...", false);

                await DownloadFikaInstallerAsync(FikaInstallUrls.InstallerDownloadUrl, installerPath, progressButton);

                SetFikaButtonState(progressButton, "🚀 Launching installer...", false);
                LaunchFikaInstaller(installerPath, sptPath);

                resetButton();
                ShowFikaInstallerLaunchedMessage();
                var expectedFika = _currentFikaUpdateInfo?.LatestVersion;
                _ = VerifyFikaAfterInstallAsync(expectedFika);
            }
            catch (HttpRequestException ex)
            {
                resetButton();
                HandleFikaInstallHttpError(ex);
            }
            catch (TaskCanceledException ex)
            {
                resetButton();
                System.Windows.MessageBox.Show(
                    $"Download timed out.\n\nError: {ex.Message}\n\n" +
                    "Please check your internet connection and try again.",
                    "Download Timeout",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                resetButton();
                System.Windows.MessageBox.Show(
                    $"An error occurred while installing Fika.\n\nError: {ex.Message}",
                    "Installation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool ShouldContinueWithFikaInstall()
        {
            var fikaModPath = AutoDetectFikaMod();
            if (string.IsNullOrEmpty(fikaModPath))
            {
                return true;
            }

            var result = System.Windows.MessageBox.Show(
                $"Fika mod appears to be already installed at:\n{fikaModPath}\n\n" +
                "Do you want to reinstall it anyway?",
                "Fika Already Installed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        private void SetInstallFikaButtonState(string content, bool isEnabled)
        {
            SetFikaButtonState(InstallFikaButton, content, isEnabled);
        }

        private void ResetInstallFikaButton()
        {
            SetInstallFikaButtonState("Install Fika", true);
        }

        private void ResetUpdateFikaButton()
        {
            SetFikaButtonState(UpdateFikaButton, "Update Now", true);
        }

        private void SetFikaButtonState(System.Windows.Controls.Button? button, string content, bool isEnabled)
        {
            if (button == null)
            {
                return;
            }

            button.Content = content;
            button.IsEnabled = isEnabled;
        }

        private void ShowFikaManualDownloadPrompt(string title)
        {
            var result = System.Windows.MessageBox.Show(
                "Could not automatically download the Fika installer.\n\n" +
                "Would you like to open the GitHub releases page to download it manually?\n\n" +
                "After downloading, place the installer in your SPT directory and run it from there.",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = FikaInstallUrls.InstallerReleasesPageUrl,
                    UseShellExecute = true
                });
            }
        }

        private static bool IsGitHubRateLimitError(HttpRequestException ex)
        {
            return ex.Message.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
        }

        private async Task DownloadFikaInstallerAsync(string installerDownloadUrl, string installerPath, System.Windows.Controls.Button? progressButton = null)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            using var response = await client.GetAsync(installerDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalBytesRead += bytesRead;

                if (totalBytes > 0 && progressButton != null)
                {
                    var percent = (double)totalBytesRead / totalBytes * 100;
                    var button = progressButton;
                    Dispatcher.Invoke(() => button.Content = $"⏳ Downloading... {percent:F0}%");
                }
            }
        }

        private static void LaunchFikaInstaller(string installerPath, string sptPath)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = sptPath
            };

            Process.Start(processInfo);
        }

        private static void ShowFikaInstallerLaunchedMessage()
        {
            System.Windows.MessageBox.Show(
                "Fika installer has been launched. Please follow the installation wizard.\n\n" +
                "Make sure to install Fika to your SPT installation directory.\n\n" +
                "The version will be detected automatically after installation completes.",
                "Installer Launched",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ScheduleFikaVersionRefresh()
        {
            _ = VerifyFikaAfterInstallAsync(_currentFikaUpdateInfo?.LatestVersion);
        }

        private void UpdateVerifyDismissButton_Click(object sender, RoutedEventArgs e)
        {
            CancelUpdateVerify();
            if (UpdateVerifyPanel != null)
            {
                UpdateVerifyPanel.Visibility = Visibility.Collapsed;
            }

            RefreshReadinessSummary();
        }

        private void CancelUpdateVerify()
        {
            try
            {
                _updateVerifyCts?.Cancel();
            }
            catch
            {
                // Ignore cancel races
            }

            _updateVerifyCts?.Dispose();
            _updateVerifyCts = null;
        }

        private CancellationToken BeginUpdateVerify()
        {
            CancelUpdateVerify();
            _updateVerifyCts = new CancellationTokenSource();
            return _updateVerifyCts.Token;
        }

        private static bool IsInstallerProcessRunning()
        {
            string[] installerHints =
            [
                "SPTInstaller",
                "SPT.Installer",
                "SPT_Installer",
                "FikaInstaller",
                "Fika.Installer",
                "Fika_Installer",
                "Fika-Installer"
            ];

            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        foreach (var hint in installerHints)
                        {
                            if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }

                        if (name.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("SPTLauncher", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("SPT.Launcher", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Access denied / exited
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Best effort
            }

            return false;
        }

        private void ShowUpdateVerifyResult(bool? passed, string title, string detail)
        {
            InvokeOnUi(() =>
            {
                if (UpdateVerifyPanel == null || UpdateVerifyTitleText == null || UpdateVerifyDetailText == null)
                {
                    return;
                }

                UpdateVerifyPanel.Visibility = Visibility.Visible;
                UpdateVerifyTitleText.Text = title;
                UpdateVerifyDetailText.Text = detail;
                ApplyStatusMessageAccent(UpdateVerifyAccent, passed);

                UpdateVerifyTitleText.Foreground = passed switch
                {
                    true => (System.Windows.Media.Brush)FindResource("StatusSuccessColor"),
                    false => (System.Windows.Media.Brush)FindResource("StatusErrorColor"),
                    _ => (System.Windows.Media.Brush)FindResource("TextPrimaryColor")
                };

                // Verify results should surface in the compact readiness strip.
                _readinessUserCollapsed = false;
                _readinessUserExpanded = true;
                RefreshReadinessSummary();
            });
        }

        private static bool VersionsMatch(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var a = VersionStringHelper.Normalize(left);
            var b = VersionStringHelper.Normalize(right);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Allow prefix matches like 4.1.1 vs 4.1.1.0
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                   b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private async Task VerifySptAfterUpdateAsync(string? expectedVersion)
        {
            var ct = BeginUpdateVerify();
            ShowUpdateVerifyResult(
                passed: null,
                title: "Verifying SPT update…",
                detail: "Checking that the installed SPT version refreshed correctly.");

            string? detectedVersion = null;
            var installerSeen = false;

            try
            {
                for (var retry = 0; retry < 10; retry++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(1500, ct);

                    if (IsInstallerProcessRunning())
                    {
                        installerSeen = true;
                        ShowUpdateVerifyResult(
                            passed: null,
                            title: "Waiting for SPT installer…",
                            detail: "An installer is still running. Finish it, then this check will continue.");
                    }

                    UpdateSptVersionDisplay();
                    await Task.Delay(400, ct);

                    // Read the TextBox on the UI thread — Task.Run cannot touch DependencyObjects.
                    var launcherPath = InvokeOnUi(() =>
                        HasValidLauncherPath(out var path) ? path : string.Empty);

                    detectedVersion = string.IsNullOrWhiteSpace(launcherPath)
                        ? null
                        : await Task.Run(
                            () => SptDetectionService.Instance.GetSptVersion(launcherPath),
                            ct);

                    if (string.IsNullOrWhiteSpace(detectedVersion))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                        VersionsMatch(detectedVersion, expectedVersion))
                    {
                        ShowUpdateVerifyResult(
                            passed: true,
                            title: "SPT update verified",
                            detail: $"Installed version {detectedVersion} matches the expected update ({expectedVersion}).");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                        (IsInstallerProcessRunning() || (installerSeen && retry < 8)))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(expectedVersion))
                    {
                        ShowUpdateVerifyResult(
                            passed: true,
                            title: "SPT update verified",
                            detail: $"SPT is detected at version {detectedVersion}.");
                        return;
                    }
                }

                ct.ThrowIfCancellationRequested();

                if (IsInstallerProcessRunning() || installerSeen)
                {
                    ShowUpdateVerifyResult(
                        passed: null,
                        title: "SPT update still pending",
                        detail: string.IsNullOrWhiteSpace(detectedVersion)
                            ? "The installer may still be finishing. Complete it, then click Recheck."
                            : $"Detected {detectedVersion}" +
                              (string.IsNullOrWhiteSpace(expectedVersion) ? "." : $", expected {expectedVersion}.") +
                              " Finish the installer and click Recheck.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(detectedVersion))
                {
                    ShowUpdateVerifyResult(
                        passed: false,
                        title: "SPT update could not be verified",
                        detail: "SPT was not detected after the update. Confirm the installer finished, then click Recheck.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                    VersionsMatch(detectedVersion, expectedVersion))
                {
                    ShowUpdateVerifyResult(
                        passed: true,
                        title: "SPT update verified",
                        detail: $"Installed version {detectedVersion} matches the expected update ({expectedVersion}).");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(expectedVersion))
                {
                    ShowUpdateVerifyResult(
                        passed: false,
                        title: "SPT version mismatch",
                        detail: $"Detected {detectedVersion}, but expected {expectedVersion}. " +
                                "If the installer is still running, finish it and click Recheck.");
                    return;
                }

                ShowUpdateVerifyResult(
                    passed: true,
                    title: "SPT update verified",
                    detail: $"SPT is detected at version {detectedVersion}.");
            }
            catch (OperationCanceledException)
            {
                // Dismissed or superseded by a newer verify pass.
            }
        }

        private async Task VerifyFikaAfterInstallAsync(string? expectedVersion)
        {
            var ct = BeginUpdateVerify();
            ShowUpdateVerifyResult(
                passed: null,
                title: "Waiting for Fika installer…",
                detail: "Finish the Fika installer wizard. This launcher will recheck versions automatically.");

            string? clientVersion = null;
            string? serverVersion = null;
            FikaUpdateInfo? updateInfo = null;
            var installed = false;
            var installerSeen = false;

            try
            {
                for (var retry = 0; retry < 10; retry++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(retry == 0 ? 3000 : 2500, ct);

                    if (IsInstallerProcessRunning())
                    {
                        installerSeen = true;
                        ShowUpdateVerifyResult(
                            passed: null,
                            title: "Waiting for Fika installer…",
                            detail: "Installer is still running. Finish the wizard into your SPT folder.");
                    }

                    var sptPath = GetSptInstallPathOnUiThread();
                    var state = await DetectFikaStateAsync(sptPath);
                    installed = state.Installed;
                    clientVersion = state.ClientVersion;
                    serverVersion = state.ServerVersion;
                    updateInfo = await TryGetFikaUpdateInfoAsync(clientVersion, serverVersion);
                    InvokeOnUi(() => ApplyFikaUiState(installed, clientVersion, serverVersion, updateInfo));

                    if (!installed)
                    {
                        continue;
                    }

                    if (updateInfo?.IsUpdateAvailable != true)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                        (VersionsMatch(clientVersion, expectedVersion) || VersionsMatch(serverVersion, expectedVersion)))
                    {
                        break;
                    }

                    if (IsInstallerProcessRunning() || (installerSeen && retry < 8))
                    {
                        continue;
                    }
                }

                ct.ThrowIfCancellationRequested();

                if (!installed)
                {
                    if (IsInstallerProcessRunning() || installerSeen)
                    {
                        ShowUpdateVerifyResult(
                            passed: null,
                            title: "Fika install still pending",
                            detail: "Fika was not detected yet and an installer may still be finishing. " +
                                    "Complete the wizard into your SPT folder, then click Recheck.");
                        return;
                    }

                    ShowUpdateVerifyResult(
                        passed: false,
                        title: "Fika install not verified",
                        detail: "Fika was not detected yet. Finish the installer into your SPT folder, then click Recheck.");
                    return;
                }

                var detectedLabel = FormatFikaInstalledVersion(clientVersion, serverVersion);
                if (updateInfo?.IsUpdateAvailable == true &&
                    !string.IsNullOrWhiteSpace(expectedVersion) &&
                    !VersionsMatch(clientVersion, expectedVersion) &&
                    !VersionsMatch(serverVersion, expectedVersion))
                {
                    if (IsInstallerProcessRunning() || installerSeen)
                    {
                        ShowUpdateVerifyResult(
                            passed: null,
                            title: "Fika update still pending",
                            detail: $"Detected {detectedLabel}, expected around {expectedVersion}. " +
                                    "Finish the installer and click Recheck.");
                        return;
                    }

                    ShowUpdateVerifyResult(
                        passed: false,
                        title: "Fika may still need an update",
                        detail: $"Detected {detectedLabel}, but an update is still reported " +
                                $"(expected around {expectedVersion}). Confirm the installer completed, then Recheck.");
                    return;
                }

                ShowUpdateVerifyResult(
                    passed: true,
                    title: "Fika install verified",
                    detail: string.IsNullOrWhiteSpace(expectedVersion)
                        ? $"Fika detected: {detectedLabel}."
                        : $"Fika detected: {detectedLabel}. Expected update target was {expectedVersion}.");
            }
            catch (OperationCanceledException)
            {
                // Dismissed or superseded by a newer verify pass.
            }
        }

        private void HandleFikaInstallHttpError(HttpRequestException ex)
        {
            if (IsGitHubRateLimitError(ex))
            {
                var result = System.Windows.MessageBox.Show(
                    "GitHub rate limit exceeded. Cannot download the installer automatically.\n\n" +
                    "Would you like to open the GitHub releases page to download it manually?\n\n" +
                    "After downloading, place the installer in your SPT directory and run it from there.",
                    "Rate Limit Exceeded",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = FikaInstallUrls.InstallerReleasesPageUrl,
                        UseShellExecute = true
                    });
                }

                return;
            }

            System.Windows.MessageBox.Show(
                $"Failed to download the Fika installer.\n\nError: {ex.Message}\n\nPlease check your internet connection and try again.",
                "Download Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // Fika Co-op Configuration Methods
        
        /// <summary>
        /// Auto-detects SPT.Launcher.exe in common installation locations.
        /// Newer SPT installs place it under SPT_Runtime\ rather than the install root.
        /// </summary>
        private string AutoDetectSptLauncher()
        {
            try
            {
                var candidates = new List<string>();

                void Consider(string? path)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return;
                    }

                    try
                    {
                        path = Path.GetFullPath(path.Trim().Trim('"'));
                    }
                    catch
                    {
                        return;
                    }

                    if (!path.EndsWith("SPT.Launcher.exe", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith("Aki.Launcher.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (File.Exists(path) &&
                        !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        candidates.Add(path);
                    }
                }

                // Running process path
                foreach (var processName in new[] { "SPT.Launcher", "Aki.Launcher" })
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        try
                        {
                            Consider(TryGetProcessPath(process));
                        }
                        catch
                        {
                            // Continue searching
                        }
                    }
                }

                // Desktop / Start Menu shortcuts created by the SPT installer
                foreach (var shortcut in GetSptLauncherShortcutCandidates())
                {
                    Consider(TryResolveShortcutTarget(shortcut));
                }

                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();

                var folderNames = new[]
                {
                    "SPT", "SPT-AKI", "SPTarkov", "SinglePlayerTarkov", "spt", "SP-Tarkov"
                };

                // Common parent folders the official installer / users pick (e.g. C:\Games\SPT).
                var parentFolders = new List<string>(drives);
                foreach (var drive in drives)
                {
                    foreach (var parent in new[] { "Games", "Game", "SPTarkov", "Program Files", "Program Files (x86)" })
                    {
                        parentFolders.Add(Path.Combine(drive, parent));
                    }
                }

                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(userProfile))
                {
                    parentFolders.Add(Path.Combine(userProfile, "Games"));
                    parentFolders.Add(Path.Combine(userProfile, "SPT"));
                }

                // Explicit modern + legacy layouts under common folder names
                foreach (var parent in parentFolders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var folderName in folderNames)
                    {
                        var root = Path.Combine(parent, folderName);
                        foreach (var relative in new[]
                                 {
                                     "SPT.Launcher.exe",
                                     Path.Combine("SPT_Runtime", "SPT.Launcher.exe"),
                                     Path.Combine("SPT", "SPT.Launcher.exe"),
                                     Path.Combine("SPT", "SPT_Runtime", "SPT.Launcher.exe"),
                                     Path.Combine(folderName, "SPT.Launcher.exe"),
                                     Path.Combine(folderName, "SPT_Runtime", "SPT.Launcher.exe"),
                                     "Aki.Launcher.exe",
                                     Path.Combine("SPT_Runtime", "Aki.Launcher.exe")
                                 })
                        {
                            Consider(Path.Combine(root, relative));
                        }

                        // Installer often drops shortcuts in the chosen install folder.
                        Consider(TryResolveShortcutTarget(Path.Combine(root, "SPT.Launcher.lnk")));
                    }

                    // Also accept the parent itself when the user installed directly into Games\SPT_Runtime etc.
                    foreach (var relative in new[]
                             {
                                 "SPT.Launcher.exe",
                                 Path.Combine("SPT_Runtime", "SPT.Launcher.exe"),
                                 "Aki.Launcher.exe"
                             })
                    {
                        Consider(Path.Combine(parent, relative));
                    }
                }

                // Deeper scan under likely SPT roots (covers custom subfolders)
                foreach (var parent in parentFolders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var folderName in folderNames)
                    {
                        var root = Path.Combine(parent, folderName);
                        if (!Directory.Exists(root))
                        {
                            continue;
                        }

                        var found = SearchForLauncherRecursive(root, maxDepth: 4);
                        Consider(found);
                    }

                    if (Directory.Exists(parent) &&
                        folderNames.Any(n => parent.EndsWith(n, StringComparison.OrdinalIgnoreCase) ||
                                             parent.IndexOf($"\\{n}", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Consider(SearchForLauncherRecursive(parent, maxDepth: 4));
                    }
                }

                // Broader shallow scan across drives as a last resort
                if (candidates.Count == 0)
                {
                    foreach (var drive in drives)
                    {
                        var found = SearchForLauncherRecursive(drive, maxDepth: 3);
                        Consider(found);
                        if (candidates.Count > 0)
                        {
                            break;
                        }
                    }
                }

                if (candidates.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[AutoDetectSptLauncher] No launcher found");
                    return string.Empty;
                }

                // Prefer the newest launcher (most recent install/update).
                var best = candidates
                    .OrderByDescending(IsLikelyCurrentSptLauncher)
                    .ThenByDescending(p =>
                    {
                        try { return File.GetLastWriteTimeUtc(p); }
                        catch { return DateTime.MinValue; }
                    })
                    .First();

                System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Selected: {best}");
                return best;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoDetectSptLauncher] Error: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool IsLikelyCurrentSptLauncher(string path)
        {
            // Prefer modern SPT_Runtime layout and non-archive folders.
            var lower = path.ToLowerInvariant();
            if (lower.Contains("\\spt_version_archive\\") || lower.Contains("\\backup"))
            {
                return false;
            }

            return lower.Contains("\\spt_runtime\\") || lower.Contains("\\spt\\");
        }

        private static IEnumerable<string> GetSptLauncherShortcutCandidates()
        {
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
            };

            foreach (var dir in dirs.Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d)))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(dir, "*SPT*Launcher*.lnk", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        private static string? TryResolveShortcutTarget(string? shortcutPath)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            {
                return null;
            }

            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return null;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                var link = shell.CreateShortcut(shortcutPath);
                string? target = link.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TryResolveShortcutTarget] {shortcutPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Recursively searches for SPT.Launcher.exe (with depth limit for performance)
        /// </summary>
        private string SearchForLauncherRecursive(string directory, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth)
            {
                return string.Empty;
            }

            try
            {
                if (!Directory.Exists(directory))
                {
                    return string.Empty;
                }

                foreach (var exeName in new[] { "SPT.Launcher.exe", "Aki.Launcher.exe" })
                {
                    var launcherPath = Path.Combine(directory, exeName);
                    if (File.Exists(launcherPath))
                    {
                        return launcherPath;
                    }
                }

                if (currentDepth == maxDepth)
                {
                    return string.Empty;
                }

                var skipFolders = new[]
                {
                    "Windows", "Program Files", "Program Files (x86)", "ProgramData",
                    "$Recycle.Bin", "System Volume Information", "PerfLogs",
                    "Recovery", "Documents and Settings", "Windows.old",
                    "node_modules", ".git"
                };

                foreach (var dir in Directory.GetDirectories(directory))
                {
                    var dirName = Path.GetFileName(dir);
                    if (skipFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
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
                
                // Extract directory from launcher path
                // Modern SPT: D:\SPT\SPT_Runtime\SPT.Launcher.exe -> install root is D:\SPT
                var launcherDir = Path.GetDirectoryName(launcherPath);
                if (string.IsNullOrEmpty(launcherDir))
                {
                    return string.Empty;
                }

                var launcherDirName = Path.GetFileName(launcherDir);
                if (string.Equals(launcherDirName, "SPT_Runtime", StringComparison.OrdinalIgnoreCase))
                {
                    var runtimeParent = Path.GetDirectoryName(launcherDir);
                    if (!string.IsNullOrEmpty(runtimeParent) && Directory.Exists(runtimeParent))
                    {
                        return runtimeParent;
                    }
                }
                
                // Check if the parent directory exists and has more files/subdirectories than just the nested SPT folder
                // This handles cases where SPT is in a nested structure like D:\SPT\SPT\SPT.Launcher.exe
                // In this case, we want to back up the entire D:\SPT directory, not just D:\SPT\SPT
                var parentDir = Path.GetDirectoryName(launcherDir);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
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
                var fikaModNames = new[] { "Fika", "Fika-Coop", "FikaCoop", "FIKA", "fika", "fika-server" };
                var modsDirectories = new[]
                {
                    Path.Combine(sptPath, "user", "mods"),
                    Path.Combine(sptPath, "BepInEx", "plugins"),
                    Path.Combine(sptPath, "mods"),
                    Path.Combine(sptPath, "SPT", "user", "mods"),
                    Path.Combine(sptPath, "SPT", "BepInEx", "plugins"),
                    Path.Combine(sptPath, "SPT_Runtime", "user", "mods"),
                    Path.Combine(sptPath, "SPT_Runtime", "BepInEx", "plugins")
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

        private void UpdateEftVersionDisplay()
        {
            _ = UpdateEftVersionDisplayAsync();
        }

        private async Task UpdateEftVersionDisplayAsync()
        {
            try
            {
                string? preferredGamePath = null;
                try
                {
                    preferredGamePath = EftDetectionService.Instance.TryGetGamePathFromSptLauncherConfig(
                        GetLauncherConfigJsonPath());
                }
                catch
                {
                    // Ignore config read failures
                }

                var requiredLiveVersion = _currentUpdateInfo?.RequiredLiveEftVersion;
                var targetClientVersion = _currentUpdateInfo?.RequiredEftVersion;
                if (string.IsNullOrWhiteSpace(requiredLiveVersion) || string.IsNullOrWhiteSpace(targetClientVersion))
                {
                    var latestRelease = await SptDetectionService.Instance.GetLatestReleaseInfoAsync();
                    requiredLiveVersion ??= latestRelease?.RequiredLiveEftVersion;
                    targetClientVersion ??= latestRelease?.RequiredEftVersion;
                    if (_currentUpdateInfo == null && latestRelease != null)
                    {
                        _currentUpdateInfo = latestRelease;
                    }
                    else if (_currentUpdateInfo != null)
                    {
                        _currentUpdateInfo.RequiredLiveEftVersion ??= requiredLiveVersion;
                        _currentUpdateInfo.RequiredEftVersion ??= targetClientVersion;
                    }
                }

                var eftInfo = await Task.Run(() =>
                    EftDetectionService.Instance.EvaluateCompatibility(
                        requiredLiveVersion,
                        targetClientVersion,
                        preferredGamePath));
                await EftDetectionService.Instance.ResolveCurrentPatcherAvailabilityAsync(eftInfo);
                _currentEftInfo = eftInfo;

                InvokeOnUi(() => ApplyEftUiState(eftInfo));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateEftVersionDisplayAsync] Error: {ex.Message}");
                InvokeOnUi(() =>
                {
                    if (EftVersionText != null)
                    {
                        EftVersionText.Text = "Error detecting version";
                        EftVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    }

                    if (EftStatusText != null)
                    {
                        EftStatusText.Text = "Could not check Tarkov version";
                    }

                    if (UpdateTarkovButton != null)
                    {
                        UpdateTarkovButton.Visibility = Visibility.Visible;
                    }
                });
            }
        }

        private void ApplyEftUiState(EftCompatibilityInfo eftInfo)
        {
            var sptAlreadyInstalled = IsSptDetectedAtCurrentPath();
            // Patcher messaging is only useful before/during install. Once SPT is installed,
            // the live copy has already been downgraded into the SPT folder.
            var showPatcherDetails = !sptAlreadyInstalled &&
                                     eftInfo.Status == EftCompatibilityStatus.Compatible &&
                                     !string.IsNullOrWhiteSpace(eftInfo.AvailablePatcherUrl);

            if (EftVersionText != null)
            {
                EftVersionText.Text = string.IsNullOrWhiteSpace(eftInfo.InstalledVersion)
                    ? "Not detected"
                    : eftInfo.InstalledVersion;
                EftVersionText.Foreground = string.IsNullOrWhiteSpace(eftInfo.InstalledVersion)
                    ? (System.Windows.Media.Brush)FindResource("TextSecondaryColor")
                    : (System.Windows.Media.Brush)FindResource("TextPrimaryColor");
            }

            if (EftStatusText != null)
            {
                EftStatusText.Text = eftInfo.GetStatusText(sptAlreadyInstalled);
                EftStatusText.Foreground = (sptAlreadyInstalled, eftInfo.Status) switch
                {
                    (true, _) =>
                        (System.Windows.Media.Brush)FindResource("TextSecondaryColor"),
                    (_, EftCompatibilityStatus.Compatible) =>
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94)),
                    (_, EftCompatibilityStatus.UpdateRequired) =>
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)),
                    (_, EftCompatibilityStatus.NotDetected) =>
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)),
                    (_, EftCompatibilityStatus.NewerThanSupported) =>
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 179, 8)),
                    _ => (System.Windows.Media.Brush)FindResource("TextSecondaryColor")
                };
            }

            var isNewerThanPatcher = eftInfo.Status == EftCompatibilityStatus.NewerThanSupported;
            var needsLiveUpdate = eftInfo.Status == EftCompatibilityStatus.UpdateRequired;
            // Live Tarkov must be updated (via BSG) before SPT can copy it and run the downgrader.
            // Keep this visible even when SPT is already installed so users can prep for reinstall/update.
            var showUpdateButton =
                needsLiveUpdate ||
                (!sptAlreadyInstalled &&
                 eftInfo.Status is
                     EftCompatibilityStatus.NotDetected or
                     EftCompatibilityStatus.RequiredUnknown);

            if (EftRequiredVersionText != null)
            {
                if (isNewerThanPatcher && !needsLiveUpdate)
                {
                    // No-patcher warning uses the dedicated guidance panel.
                    EftRequiredVersionText.Visibility = Visibility.Collapsed;
                    EftRequiredVersionText.Text = string.Empty;
                }
                else if (needsLiveUpdate)
                {
                    EftRequiredVersionText.Visibility = Visibility.Visible;
                    var required = string.IsNullOrWhiteSpace(eftInfo.RequiredLiveVersion)
                        ? "the patcher source version"
                        : eftInfo.RequiredLiveVersion;
                    var target = string.IsNullOrWhiteSpace(eftInfo.TargetSptClientVersion)
                        ? "SPT client"
                        : eftInfo.TargetSptClientVersion;
                    EftRequiredVersionText.Text = !string.IsNullOrWhiteSpace(eftInfo.AvailablePatcherUrl)
                        ? $"Update live Tarkov to {required}, then SPT can copy it and run the patcher ({required} → {target})."
                        : $"Update live Tarkov to {required}" +
                          (string.IsNullOrWhiteSpace(eftInfo.TargetSptClientVersion)
                              ? " so SPT can copy and downgrade."
                              : $" → SPT {target}.");
                }
                else if (sptAlreadyInstalled)
                {
                    // Keep the home screen quiet once SPT is installed and live is not behind.
                    EftRequiredVersionText.Visibility = Visibility.Collapsed;
                    EftRequiredVersionText.Text = string.Empty;
                }
                else if (showPatcherDetails)
                {
                    EftRequiredVersionText.Visibility = Visibility.Visible;
                    var target = string.IsNullOrWhiteSpace(eftInfo.TargetSptClientVersion)
                        ? "SPT client"
                        : eftInfo.TargetSptClientVersion;
                    EftRequiredVersionText.Text =
                        $"Live Tarkov is ready. Install SPT to copy it and run the patcher ({eftInfo.InstalledVersion} → {target}).";
                }
                else if (showUpdateButton || eftInfo.Status == EftCompatibilityStatus.RequiredUnknown)
                {
                    EftRequiredVersionText.Visibility = Visibility.Visible;
                    if (!string.IsNullOrWhiteSpace(eftInfo.RequiredLiveVersion))
                    {
                        EftRequiredVersionText.Text =
                            $"Needs live Tarkov {eftInfo.RequiredLiveVersion}" +
                            (string.IsNullOrWhiteSpace(eftInfo.TargetSptClientVersion)
                                ? string.Empty
                                : $" → SPT {eftInfo.TargetSptClientVersion}");
                    }
                    else
                    {
                        EftRequiredVersionText.Text =
                            "Could not determine the live Tarkov version required by the SPT downgrader.";
                    }
                }
                else
                {
                    EftRequiredVersionText.Visibility = Visibility.Collapsed;
                    EftRequiredVersionText.Text = string.Empty;
                }
            }

            if (UpdateTarkovButton != null)
            {
                UpdateTarkovButton.Visibility = showUpdateButton ? Visibility.Visible : Visibility.Collapsed;
                UpdateTarkovButton.IsEnabled = !_sptUpdateInProgress;
            }

            if (EftPatcherGuidancePanel != null)
            {
                // Only crowd the UI when SPT still needs install/reinstall help.
                EftPatcherGuidancePanel.Visibility =
                    !sptAlreadyInstalled && isNewerThanPatcher
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            if (EftPatcherGuidanceText != null)
            {
                if (isNewerThanPatcher)
                {
                    var installed = string.IsNullOrWhiteSpace(eftInfo.InstalledVersion)
                        ? "unknown"
                        : eftInfo.InstalledVersion;
                    var target = string.IsNullOrWhiteSpace(eftInfo.TargetSptClientVersion)
                        ? "the SPT client version"
                        : eftInfo.TargetSptClientVersion;

                    EftPatcherGuidanceText.Text =
                        $"Your live Tarkov is {installed}. No downgrade patcher was found for " +
                        $"{installed} → {target} on the SPT patcher CDN yet.";
                }
                else
                {
                    EftPatcherGuidanceText.Text = string.Empty;
                }
            }

            RefreshPlayHero();
            RefreshReadinessSummary();
        }

        private void RefreshEftStatusButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshAllReadiness(forceSptRescan: true);
        }

        /// <summary>
        /// Re-checks SPT / Tarkov / Fika readiness. After a fresh SPT install the launcher path
        /// is often still empty, so Recheck must re-run auto-detect — not only refresh Tarkov.
        /// </summary>
        private void RefreshAllReadiness(bool forceSptRescan = false)
        {
            if (forceSptRescan || !IsSptDetectedAtCurrentPath())
            {
                TryAutoDetectAndApplySptLauncher(out _, forceRescan: forceSptRescan);
            }

            UpdatePathStatus();
            UpdateSptVersionDisplay();
            UpdateEftVersionDisplay();
            UpdateFikaVersionDisplay();
            RefreshReadinessSummary();
            RefreshFirstRunWizard();
            RefreshPlayHero();
        }

        /// <summary>
        /// Scans for SPT.Launcher.exe and applies it when found. Returns true when the current
        /// (or newly detected) path is a valid SPT install.
        /// </summary>
        private bool TryAutoDetectAndApplySptLauncher(out string path, bool forceRescan = false)
        {
            path = string.Empty;

            if (!forceRescan && IsSptDetectedAtCurrentPath() && HasValidLauncherPath(out path))
            {
                return true;
            }

            var detected = AutoDetectSptLauncher();
            if (string.IsNullOrWhiteSpace(detected))
            {
                HasValidLauncherPath(out path);
                return IsSptDetectedAtCurrentPath();
            }

            LauncherPathTextBox.Text = detected;
            SaveSettings();
            UpdatePathStatus();
            path = detected;
            return IsSptDetectedAtCurrentPath();
        }

        private void UpdateTarkovButton_Click(object sender, RoutedEventArgs e)
        {
            var requiredLive = _currentEftInfo?.RequiredLiveVersion;
            var targetClient = _currentEftInfo?.TargetSptClientVersion;
            var hasPatcherWaiting = !string.IsNullOrWhiteSpace(_currentEftInfo?.AvailablePatcherUrl);

            var launched = EftDetectionService.Instance.TryLaunchOfficialUpdater();
            if (!launched)
            {
                System.Windows.MessageBox.Show(
                    "Could not find the Battlestate Games launcher.\n\n" +
                    "Most Tarkov copies are updated through the BSG launcher (not Steam).\n" +
                    "Open your Battlestate Games launcher manually, update Escape From Tarkov" +
                    (string.IsNullOrWhiteSpace(requiredLive) ? "" : $" to {requiredLive}") +
                    ", then click Recheck.",
                    "Update Tarkov",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var detail = string.IsNullOrWhiteSpace(requiredLive)
                ? "Update Escape From Tarkov in the Battlestate Games launcher, then return here and click Recheck."
                : hasPatcherWaiting && !string.IsNullOrWhiteSpace(targetClient)
                    ? $"Update your live Tarkov install to {requiredLive}.\n\n" +
                      $"Once live matches, SPT can copy that install and run the downgrade patcher " +
                      $"({requiredLive} → {targetClient}) on the copy — your live game stays untouched.\n\n" +
                      "When the BSG update finishes, return here and Recheck."
                    : $"Update your live Tarkov install to {requiredLive}, then return here and click Recheck.";

            System.Windows.MessageBox.Show(
                detail,
                "Update Tarkov",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            UpdateEftVersionDisplay();
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

                var sptPath = GetSptInstallPathOnUiThread();
                System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] SPT Path from UI thread: {sptPath}");

                var fikaState = await DetectFikaStateAsync(sptPath);
                var updateInfo = await TryGetFikaUpdateInfoAsync(fikaState.ClientVersion, fikaState.ServerVersion);

                InvokeOnUi(() =>
                {
                    try
                    {
                        ApplyFikaUiState(fikaState.Installed, fikaState.ClientVersion, fikaState.ServerVersion, updateInfo);
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

        private string GetSptInstallPathOnUiThread()
        {
            return InvokeOnUi(GetSptInstallPath);
        }

        private async Task<(bool Installed, string? ClientVersion, string? ServerVersion)> DetectFikaStateAsync(string sptPath)
        {
            bool fikaInstalled = false;
            string? clientVersion = null;
            string? serverVersion = null;

            await Task.Run(() =>
            {
                try
                {
                    var clientModPath = DetectFikaClientModPath(sptPath);
                    var serverModPath = DetectFikaServerModPath(sptPath);
                    fikaInstalled = !string.IsNullOrEmpty(clientModPath) || !string.IsNullOrEmpty(serverModPath);

                    System.Diagnostics.Debug.WriteLine(
                        $"[UpdateFikaVersionDisplayAsync] Fika detected: {fikaInstalled}, clientPath: {clientModPath ?? "(none)"}, serverPath: {serverModPath ?? "(none)"}");

                    if (!string.IsNullOrEmpty(clientModPath))
                    {
                        clientVersion = GetFikaVersion(clientModPath);
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Client version: {clientVersion ?? "(null)"}");
                    }

                    if (!string.IsNullOrEmpty(serverModPath))
                    {
                        serverVersion = GetFikaVersion(serverModPath);
                        System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Server version: {serverVersion ?? "(null)"}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Error in background task: {ex.Message}");
                }
            });

            return (fikaInstalled, clientVersion, serverVersion);
        }

        private string? DetectFikaClientModPath(string sptPath)
        {
            if (string.IsNullOrEmpty(sptPath) || !Directory.Exists(sptPath))
            {
                return null;
            }

            var pluginRoots = new[]
            {
                Path.Combine(sptPath, "BepInEx", "plugins"),
                Path.Combine(sptPath, "SPT", "BepInEx", "plugins"),
                Path.Combine(sptPath, "SPT_Runtime", "BepInEx", "plugins")
            };

            foreach (var pluginsRoot in pluginRoots)
            {
                if (!Directory.Exists(pluginsRoot))
                {
                    continue;
                }

                // Folder installs: BepInEx/plugins/Fika/Fika.Core.dll
                foreach (var folderName in new[] { "Fika", "Fika.Core", "Fika-Core" })
                {
                    var folder = Path.Combine(pluginsRoot, folderName);
                    if (Directory.Exists(folder) &&
                        (File.Exists(Path.Combine(folder, "Fika.Core.dll")) ||
                         File.Exists(Path.Combine(folder, "Fika.dll"))))
                    {
                        return folder;
                    }
                }

                // Loose plugin installs: BepInEx/plugins/Fika.Core.dll
                var looseCore = Path.Combine(pluginsRoot, "Fika.Core.dll");
                if (File.Exists(looseCore))
                {
                    return pluginsRoot;
                }

                var looseFika = Path.Combine(pluginsRoot, "Fika.dll");
                if (File.Exists(looseFika))
                {
                    return pluginsRoot;
                }
            }

            return null;
        }

        private string? DetectFikaServerModPath(string sptPath)
        {
            if (string.IsNullOrEmpty(sptPath) || !Directory.Exists(sptPath))
            {
                return null;
            }

            var serverPaths = new[]
            {
                Path.Combine(sptPath, "user", "mods", "fika-server"),
                Path.Combine(sptPath, "SPT", "user", "mods", "fika-server"),
                Path.Combine(sptPath, "SPT_Runtime", "user", "mods", "fika-server")
            };

            foreach (var serverPath in serverPaths)
            {
                if (!Directory.Exists(serverPath))
                {
                    continue;
                }

                var packageJsonPath = Path.Combine(serverPath, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    // Prefer package.json match, but accept fika-server folder name as fallback.
                    if (CheckIfFikaPackageJson(packageJsonPath) ||
                        Path.GetFileName(serverPath).Contains("fika", StringComparison.OrdinalIgnoreCase))
                    {
                        return serverPath;
                    }
                }
                else if (Directory.EnumerateFileSystemEntries(serverPath).Any())
                {
                    // Installer layouts sometimes omit package.json until first server run.
                    return serverPath;
                }
            }

            // Broader search under known mods roots for folders/files that look like Fika server.
            var modsRoots = new[]
            {
                Path.Combine(sptPath, "user", "mods"),
                Path.Combine(sptPath, "SPT", "user", "mods"),
                Path.Combine(sptPath, "SPT_Runtime", "user", "mods")
            };

            foreach (var modsRoot in modsRoots)
            {
                if (!Directory.Exists(modsRoot))
                {
                    continue;
                }

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(modsRoot))
                    {
                        var name = Path.GetFileName(dir);
                        if (name.Contains("fika", StringComparison.OrdinalIgnoreCase))
                        {
                            var packageJsonPath = Path.Combine(dir, "package.json");
                            if (!File.Exists(packageJsonPath) || CheckIfFikaPackageJson(packageJsonPath))
                            {
                                return dir;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore enumeration failures
                }
            }

            return null;
        }

        private async Task<FikaUpdateInfo?> TryGetFikaUpdateInfoAsync(string? clientVersion, string? serverVersion)
        {
            if (string.IsNullOrEmpty(clientVersion) && string.IsNullOrEmpty(serverVersion))
            {
                return null;
            }

            try
            {
                var updateInfo = await SptDetectionService.Instance.CheckForFikaUpdatesAsync(clientVersion, serverVersion);
                _currentFikaUpdateInfo = updateInfo;
                System.Diagnostics.Debug.WriteLine(
                    $"[UpdateFikaVersionDisplayAsync] Update check result: {(updateInfo?.IsUpdateAvailable == true ? $"Update available: {updateInfo.LatestVersion}" : "Up to date")}");
                return updateInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateFikaVersionDisplayAsync] Error checking for updates: {ex.Message}");
                return null;
            }
        }

        private void ApplyFikaUiState(bool fikaInstalled, string? clientVersion, string? serverVersion, FikaUpdateInfo? updateInfo)
        {
            if (FikaVersionText == null)
            {
                return;
            }

            if (InstallFikaButton != null)
            {
                InstallFikaButton.Visibility = fikaInstalled ? Visibility.Collapsed : Visibility.Visible;
            }

            if (FikaUpdateStatusPanel != null)
            {
                FikaUpdateStatusPanel.Visibility = Visibility.Collapsed;
            }

            if (!fikaInstalled)
            {
                FikaVersionText.Text = "Not detected";
                FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                if (FikaUpdateStatusText != null)
                {
                    FikaUpdateStatusText.Text = "Optional";
                    FikaUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                }

                if (UpdateFikaButton != null)
                {
                    UpdateFikaButton.Visibility = Visibility.Collapsed;
                }

                RefreshReadinessSummary();
                return;
            }


            if (string.IsNullOrEmpty(clientVersion) && string.IsNullOrEmpty(serverVersion))
            {
                FikaVersionText.Text = "Installed (version unknown)";
                FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
            }
            else
            {
                FikaVersionText.Text = FormatFikaInstalledVersion(clientVersion, serverVersion);
                FikaVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryColor");
            }

            if (FikaUpdateStatusText != null)
            {
                if (updateInfo == null)
                {
                    FikaUpdateStatusText.Text = "Installed";
                    FikaUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    if (UpdateFikaButton != null)
                    {
                        UpdateFikaButton.Visibility = Visibility.Collapsed;
                    }
                }
                else if (updateInfo.IsUpdateAvailable)
                {
                    FikaUpdateStatusText.Text = $"Update {updateInfo.LatestVersion}";
                    FikaUpdateStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                    if (UpdateFikaButton != null)
                    {
                        UpdateFikaButton.Visibility = Visibility.Visible;
                        UpdateFikaButton.IsEnabled = true;
                    }
                }
                else
                {
                    FikaUpdateStatusText.Text = "Up to date";
                    FikaUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    if (UpdateFikaButton != null)
                    {
                        UpdateFikaButton.Visibility = Visibility.Collapsed;
                    }
                }
            }

            RefreshReadinessSummary();
        }

        private static string FormatFikaInstalledVersion(string? clientVersion, string? serverVersion)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(clientVersion))
            {
                parts.Add($"client {clientVersion}");
            }

            if (!string.IsNullOrEmpty(serverVersion))
            {
                parts.Add($"server {serverVersion}");
            }

            return parts.Count > 0 ? string.Join(" / ", parts) : "Installed (version unknown)";
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
                                version = VersionStringHelper.Normalize(version);
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
                            version = VersionStringHelper.Normalize(version);
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
                            version = VersionStringHelper.Normalize(version);
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

        private async Task UpdateSptVersionDisplayAsync()
        {
            try
            {
                if (SptVersionText == null)
                {
                    return;
                }

                // Get launcher path from text box or settings (TextBox must be read on the UI thread)
                var launcherPath = InvokeOnUi(() => LauncherPathTextBox?.Text);
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
                        SetSptStatusPill("Not installed");
                        if (InstallSptButton != null)
                        {
                            InstallSptButton.Visibility = Visibility.Visible;
                        }
                        if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                        SetSptUpdateActionButtonsVisible(false, false);
                        RefreshSptRecoveryPanel();
                        RefreshPlayHero();
                        RefreshFirstRunWizard();
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
                        SetSptStatusPill("Not installed");
                        if (InstallSptButton != null)
                        {
                            InstallSptButton.Visibility = Visibility.Visible;
                        }
                        if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                        SetSptUpdateActionButtonsVisible(false, false);
                        RefreshSptRecoveryPanel();
                        RefreshPlayHero();
                        RefreshFirstRunWizard();
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
                        SetSptStatusPill("Installed");
                        if (InstallSptButton != null)
                        {
                            InstallSptButton.Visibility = Visibility.Collapsed;
                        }
                        if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                        SetSptUpdateActionButtonsVisible(false, false);
                        RefreshSptRecoveryPanel();
                        RefreshPlayHero();
                        RefreshFirstRunWizard();
                    });
                    return;
                }

                // Update version display
                Dispatcher.Invoke(() =>
                {
                    SptVersionText.Text = version;
                    SptVersionText.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryColor");
                    SetSptStatusPill("Checking...");
                    if (InstallSptButton != null)
                    {
                        InstallSptButton.Visibility = Visibility.Collapsed;
                    }
                });

                // Check for updates asynchronously
                var updateInfo = await SptDetectionService.Instance.CheckForUpdatesAsync(version);
                _currentUpdateInfo = updateInfo;
                UpdateEftVersionDisplay();
                
                Dispatcher.Invoke(() =>
                {
                    if (SptUpdateStatusText == null)
                    {
                        return;
                    }

                    if (updateInfo == null)
                    {
                        SetSptStatusPill("—");
                        if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                        SetSptUpdateActionButtonsVisible(false, false);
                    }
                    else if (updateInfo.IsUpdateAvailable)
                    {
                        var hasInstaller = !string.IsNullOrWhiteSpace(updateInfo.InstallerDownloadUrl);
                        if (!hasInstaller)
                        {
                            SetSptStatusPill($"Update {updateInfo.LatestVersion} (manual)");
                            SetSptUpdateActionButtonsVisible(false, false);
                        }
                        else
                        {
                            SetSptStatusPill($"Update {updateInfo.LatestVersion}");
                            SetSptUpdateActionButtonsVisible(!_sptUpdateInProgress, !_sptUpdateInProgress);
                        }

                        SptUpdateStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                    }
                    else
                    {
                        SetSptStatusPill("Up to date");
                        if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                        {
                            SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                        }
                        SetSptUpdateActionButtonsVisible(false, false);
                    }

                    RefreshSptRecoveryPanel();
                    RefreshPlayHero();
                    RefreshFirstRunWizard();
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
                    SetSptStatusPill("Error");
                    if (!_sptUpdateInProgress && SptUpdateStatusPanel != null)
                    {
                        SptUpdateStatusPanel.Visibility = Visibility.Collapsed;
                    }
                    RefreshSptRecoveryPanel();
                    RefreshPlayHero();
                    RefreshFirstRunWizard();
                });
            }
        }

        private void SetSptStatusPill(string text)
        {
            if (SptUpdateStatusText == null)
            {
                return;
            }

            SptUpdateStatusText.Text = text;
            SptUpdateStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
        }

        private void SetSptUpdateActionButtonsVisible(bool showUpdateNow, bool showInstallerOnly)
        {
            if (UpdateNowButton != null)
            {
                UpdateNowButton.Visibility = showUpdateNow ? Visibility.Visible : Visibility.Collapsed;
                UpdateNowButton.IsEnabled = showUpdateNow && !_sptUpdateInProgress;
            }

            if (DownloadInstallerOnlyButton != null)
            {
                DownloadInstallerOnlyButton.Visibility = showInstallerOnly ? Visibility.Visible : Visibility.Collapsed;
                DownloadInstallerOnlyButton.IsEnabled = showInstallerOnly && !_sptUpdateInProgress;
            }

            // Progress / installer-only actions live in this panel.
            if (SptUpdateStatusPanel != null && !_sptUpdateInProgress)
            {
                SptUpdateStatusPanel.Visibility = showInstallerOnly ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshSptRecoveryPanel()
        {
            var sptPath = GetSptInstallPath();
            var hasSptPath = !string.IsNullOrWhiteSpace(sptPath) && Directory.Exists(sptPath);
            var lastBackup = SettingsService.Instance.LastSptBackupPath;
            var hasBackup = !string.IsNullOrWhiteSpace(lastBackup) && Directory.Exists(lastBackup);

            if (SptFolderActionsPanel != null)
            {
                SptFolderActionsPanel.Visibility = hasSptPath ? Visibility.Visible : Visibility.Collapsed;
            }

            if (OpenSptFolderButton != null)
            {
                OpenSptFolderButton.IsEnabled = hasSptPath && !_sptUpdateInProgress;
            }

            if (ReinstallSptButton != null)
            {
                ReinstallSptButton.IsEnabled = !_sptUpdateInProgress;
            }

            // Recovery (restore) only appears once a backup exists.
            if (SptRecoveryPanel != null)
            {
                SptRecoveryPanel.Visibility = hasBackup ? Visibility.Visible : Visibility.Collapsed;
            }

            if (RestoreBackupButton != null)
            {
                RestoreBackupButton.IsEnabled = hasBackup && hasSptPath && !_sptUpdateInProgress;
            }

            if (LastBackupPathText != null)
            {
                if (hasBackup)
                {
                    LastBackupPathText.Visibility = Visibility.Visible;
                    LastBackupPathText.Text = $"Last backup: {lastBackup}";
                }
                else
                {
                    LastBackupPathText.Visibility = Visibility.Collapsed;
                    LastBackupPathText.Text = string.Empty;
                }
            }
        }

        private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSptUpdateFlowAsync(installerOnly: false);
        }

        private async void DownloadInstallerOnlyButton_Click(object sender, RoutedEventArgs e)
        {
            await RunSptUpdateFlowAsync(installerOnly: true);
        }

        private void CancelUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            _sptUpdateCts?.Cancel();
            if (SptUpdateProgressText != null)
            {
                SptUpdateProgressText.Text = "Canceling...";
            }
        }

        private async Task RunSptUpdateFlowAsync(bool installerOnly)
        {
            if (_sptUpdateInProgress)
            {
                return;
            }

            if (_currentUpdateInfo == null || !_currentUpdateInfo.IsUpdateAvailable)
            {
                System.Windows.MessageBox.Show("No update information available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var sptPath = GetSptInstallPath();
            var downloadUrl = _currentUpdateInfo.InstallerDownloadUrl;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                ShowManualSptUpdatePrompt(_currentUpdateInfo.ReleaseUrl ?? SptInstallUrls.ReleasesPageUrl);
                return;
            }

            SptDownloadInfo? downloadInfo = null;
            try
            {
                downloadInfo = await SptUpdateService.Instance.GetDownloadInfoAsync(downloadUrl);
            }
            catch
            {
                downloadInfo = new SptDownloadInfo
                {
                    Url = downloadUrl,
                    FileName = SptInstallUrls.InstallerFileName
                };
            }

            var preferredGamePath = EftDetectionService.Instance.TryGetGamePathFromSptLauncherConfig(
                GetLauncherConfigJsonPath());
            var eftCompatibility = EftDetectionService.Instance.EvaluateCompatibility(
                _currentUpdateInfo.RequiredLiveEftVersion,
                _currentUpdateInfo.RequiredEftVersion,
                preferredGamePath);
            await EftDetectionService.Instance.ResolveCurrentPatcherAvailabilityAsync(eftCompatibility);
            _currentEftInfo = eftCompatibility;
            InvokeOnUi(() => ApplyEftUiState(eftCompatibility));

            var preflight = SptUpdatePreflight.Check(
                sptPath,
                downloadUrl,
                downloadInfo.ContentLength,
                requireBackupSpace: !installerOnly,
                requireNoRunningProcesses: !installerOnly,
                eftCompatibility: eftCompatibility,
                requireCompatibleEft: !installerOnly);
            if (!preflight.IsReady)
            {
                var result = System.Windows.MessageBox.Show(
                    "Update preflight checks failed:\n\n" + preflight.GetSummary() +
                    "\n\nWould you like to open the official Tarkov updater now?",
                    "Cannot Update Yet",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    UpdateTarkovButton_Click(sender: this, e: new RoutedEventArgs());
                }
                return;
            }

            if (preflight.Warnings.Count > 0)
            {
                var continueAnyway = System.Windows.MessageBox.Show(
                    preflight.GetSummary() + "\n\nContinue anyway?",
                    "Update Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (continueAnyway != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            bool createBackup = false;
            string? backupPath = null;
            if (!installerOnly)
            {
                var modeConfirm = System.Windows.MessageBox.Show(
                    "Full update will:\n" +
                    "1. Download and validate the official SPT installer\n" +
                    "2. Optionally back up your SPT folder\n" +
                    "3. Clean the SPT folder and run the installer\n\n" +
                    "Prefer a safer option? Use Download Installer Only instead.\n\n" +
                    "Continue with full update?",
                    "Confirm Full Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (modeConfirm != MessageBoxResult.Yes)
                {
                    return;
                }

                var backupResult = System.Windows.MessageBox.Show(
                    "Would you like to backup your current SPT folder before updating?\n\n" +
                    "This may take a long time and consume large amounts of storage space.\n\n" +
                    "Click Yes to create a backup, or No to skip backup.",
                    "Backup SPT Folder?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                createBackup = backupResult == MessageBoxResult.Yes;
                if (createBackup && !TrySelectBackupPath(out backupPath, out createBackup))
                {
                    return;
                }
            }

            _sptUpdateCts = new CancellationTokenSource();
            _sptUpdateInProgress = true;
            InvokeOnUi(RefreshReadinessSummary);
            var installerPath = Path.Combine(Path.GetTempPath(), downloadInfo.FileName);

            try
            {
                SetSptUpdateUiStarting(downloadInfo);

                var downloadProgress = CreateDownloadProgress(downloadInfo);
                await SptUpdateService.Instance.DownloadInstallerAsync(
                    downloadUrl,
                    installerPath,
                    downloadProgress,
                    _sptUpdateCts.Token);

                // Installer is validated before any destructive work.
                if (installerOnly)
                {
                    InvokeOnUi(() =>
                    {
                        if (SptUpdateProgressText != null)
                        {
                            SptUpdateProgressText.Text = "Launching installer (SPT folder will not be wiped)...";
                        }
                    });

                    await SptUpdateService.Instance.LaunchInstallerOnlyAsync(installerPath, sptPath);

                    ShowUpdateVerifyResult(
                        passed: null,
                        title: "SPT installer launched",
                        detail: "Your SPT folder was not modified by this launcher. Finish the installer wizard, then click Recheck on Readiness to verify the version.");

                    System.Windows.MessageBox.Show(
                        "Installer downloaded and launched.\n\n" +
                        "Your SPT folder was not modified by the launcher.\n" +
                        "Follow the installer wizard to finish updating.",
                        "Installer Launched",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var statusProgress = CreateStatusProgress();
                var progressProgress = CreatePercentProgress();

                await SptUpdateService.Instance.UpdateSptAsync(
                    sptPath,
                    installerPath,
                    createBackup,
                    backupPath,
                    statusProgress,
                    progressProgress);

                ShowSptUpdateSuccessUi();

                System.Windows.MessageBox.Show(
                    "SPT has been updated successfully!\n\nThe launcher will verify the installed version next.",
                    "Update Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                var expectedVersion = _currentUpdateInfo?.LatestVersion;
                try
                {
                    await VerifySptAfterUpdateAsync(expectedVersion);
                }
                catch (Exception verifyEx) when (verifyEx is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[RunSptUpdateFlowAsync] Post-update verify failed: {verifyEx}");
                    ShowUpdateVerifyResult(
                        passed: false,
                        title: "Could not verify SPT update",
                        detail: "The update finished, but automatic version check failed. Click Recheck on Readiness.");
                }
            }
            catch (OperationCanceledException)
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Text = "Update canceled.";
                        SptUpdateProgressText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    }
                });
            }
            catch (Exception ex)
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Text = $"Update failed: {ex.Message}";
                        SptUpdateProgressText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                    }
                });

                ShowSptUpdateFailureDialog(ex.Message, createBackup, backupPath);
            }
            finally
            {
                _sptUpdateInProgress = false;
                _sptUpdateCts?.Dispose();
                _sptUpdateCts = null;
                await FinalizeSptUpdateUiAndStateAsync();
                InvokeOnUi(RefreshSptRecoveryPanel);
                InvokeOnUi(RefreshReadinessSummary);
            }
        }

        private void ShowSptUpdateFailureDialog(string errorMessage, bool createBackup, string? backupPath)
        {
            var hasBackup = createBackup && !string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath);
            var message =
                $"Update failed: {errorMessage}\n\n" +
                (hasBackup
                    ? $"A backup is available at:\n{backupPath}\n\n"
                    : "No backup was created for this attempt.\n\n") +
                "Recovery options are available under the SPT Installation section.";

            System.Windows.MessageBox.Show(message, "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ShowManualSptUpdatePrompt(string releaseUrl)
        {
            var result = System.Windows.MessageBox.Show(
                "Automatic update is not available for this release.\n\n" +
                "The GitHub release does not include a downloadable installer.\n\n" +
                "You can download the update manually from the GitHub releases page.\n\n" +
                "Would you like to open the releases page now?",
                "Manual Update Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

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

        private bool TrySelectBackupPath(out string? backupPath, out bool createBackup)
        {
            backupPath = null;
            createBackup = true;
            var folderDialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select where to save the SPT backup",
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                backupPath = Path.Combine(folderDialog.SelectedPath, $"SPT_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                return true;
            }

            var continueResult = System.Windows.MessageBox.Show(
                "No backup location selected. Continue without backup?",
                "No Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (continueResult == MessageBoxResult.Yes)
            {
                createBackup = false;
                return true;
            }

            return false;
        }

        private void SetSptUpdateUiStarting(SptDownloadInfo downloadInfo)
        {
            InvokeOnUi(() =>
            {
                if (SptUpdateProgressBar != null)
                {
                    SptUpdateProgressBar.Visibility = Visibility.Visible;
                    SptUpdateProgressBar.Value = 0;
                }

                if (SptUpdateProgressText != null)
                {
                    SptUpdateProgressText.Visibility = Visibility.Visible;
                    SptUpdateProgressText.Text = "Starting download...";
                    SptUpdateProgressText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                }

                if (SptDownloadDetailsText != null)
                {
                    SptDownloadDetailsText.Visibility = Visibility.Visible;
                    SptDownloadDetailsText.Text =
                        $"File: {downloadInfo.FileName}\n" +
                        $"Size: {downloadInfo.DisplaySize}\n" +
                        $"URL: {downloadInfo.Url}";
                }

                SetSptUpdateActionButtonsVisible(false, false);

                if (CancelUpdateButton != null)
                {
                    CancelUpdateButton.Visibility = Visibility.Visible;
                    CancelUpdateButton.IsEnabled = true;
                }

                RefreshSptRecoveryPanel();
            });
        }

        private IProgress<double> CreateDownloadProgress(SptDownloadInfo downloadInfo)
        {
            return new Progress<double>(percent =>
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Value = percent;
                    }

                    if (SptUpdateProgressText != null)
                    {
                        var sizePart = downloadInfo.ContentLength.HasValue
                            ? $" ({downloadInfo.DisplaySize})"
                            : string.Empty;
                        SptUpdateProgressText.Text = $"Downloading {downloadInfo.FileName}{sizePart}... {percent:F0}%";
                    }
                });
            });
        }

        private IProgress<string> CreateStatusProgress()
        {
            return new Progress<string>(status =>
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Text = status;
                    }
                });
            });
        }

        private IProgress<double> CreatePercentProgress()
        {
            return new Progress<double>(percent =>
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Value = percent;
                    }
                });
            });
        }

        private void ShowSptUpdateSuccessUi()
        {
            InvokeOnUi(() =>
            {
                if (SptUpdateProgressText != null)
                {
                    SptUpdateProgressText.Text = "Update completed successfully!";
                    SptUpdateProgressText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                }
                if (SptUpdateProgressBar != null)
                {
                    SptUpdateProgressBar.Value = 100;
                }
            });
        }

        private async Task TryRefreshSptVersionAfterUpdateAsync()
        {
            for (int retry = 0; retry < 5; retry++)
            {
                await Task.Delay(2000);
                UpdateSptVersionDisplay();

                await Task.Delay(500);
                var currentVersion = InvokeOnUi(() => SptVersionText?.Text);
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

        private async Task FinalizeSptUpdateUiAndStateAsync()
        {
            await Task.Delay(2500);
            InvokeOnUi(() =>
            {
                if (SptUpdateProgressBar != null)
                {
                    SptUpdateProgressBar.Visibility = Visibility.Collapsed;
                }

                if (SptUpdateProgressText != null)
                {
                    SptUpdateProgressText.Visibility = Visibility.Collapsed;
                }

                if (SptDownloadDetailsText != null)
                {
                    SptDownloadDetailsText.Visibility = Visibility.Collapsed;
                }

                if (CancelUpdateButton != null)
                {
                    CancelUpdateButton.Visibility = Visibility.Collapsed;
                    CancelUpdateButton.IsEnabled = false;
                }

                var hasInstaller = _currentUpdateInfo?.IsUpdateAvailable == true &&
                                   !string.IsNullOrWhiteSpace(_currentUpdateInfo.InstallerDownloadUrl);
                SetSptUpdateActionButtonsVisible(hasInstaller, hasInstaller);
                RefreshSptRecoveryPanel();
            });

            await Task.Delay(1500);
            UpdateSptVersionDisplay();
        }

        private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sptUpdateInProgress)
            {
                return;
            }

            var sptPath = GetSptInstallPath();
            var backupPath = SettingsService.Instance.LastSptBackupPath;
            if (string.IsNullOrWhiteSpace(sptPath) || !Directory.Exists(sptPath))
            {
                System.Windows.MessageBox.Show("SPT installation directory not found.", "Restore Backup",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
            {
                System.Windows.MessageBox.Show("No valid backup path is saved yet.", "Restore Backup",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var running = SptUpdatePreflight.GetRunningSptProcessNames();
            if (running.Length > 0)
            {
                System.Windows.MessageBox.Show(
                    "Stop SPT-related processes before restoring:\n" + string.Join(", ", running),
                    "Restore Backup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                $"This will replace your current SPT folder with the backup:\n{backupPath}\n\nContinue?",
                "Restore Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            _sptUpdateInProgress = true;
            InvokeOnUi(RefreshReadinessSummary);
            try
            {
                InvokeOnUi(() =>
                {
                    if (SptUpdateStatusPanel != null)
                    {
                        SptUpdateStatusPanel.Visibility = Visibility.Visible;
                    }

                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Visibility = Visibility.Visible;
                        SptUpdateProgressBar.Value = 0;
                    }

                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Visibility = Visibility.Visible;
                        SptUpdateProgressText.Text = "Restoring backup...";
                        SptUpdateProgressText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryColor");
                    }

                    SetSptUpdateActionButtonsVisible(false, false);
                    RefreshSptRecoveryPanel();
                });

                await SptUpdateService.Instance.RestoreBackupAsync(
                    sptPath,
                    backupPath,
                    CreateStatusProgress(),
                    CreatePercentProgress());

                System.Windows.MessageBox.Show("Backup restored successfully.", "Restore Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateSptVersionDisplay();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Restore failed: {ex.Message}", "Restore Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _sptUpdateInProgress = false;
                InvokeOnUi(() =>
                {
                    RefreshReadinessSummary();
                    if (SptUpdateProgressBar != null)
                    {
                        SptUpdateProgressBar.Visibility = Visibility.Collapsed;
                    }

                    if (SptUpdateProgressText != null)
                    {
                        SptUpdateProgressText.Visibility = Visibility.Collapsed;
                    }

                    RefreshSptRecoveryPanel();
                });
            }
        }

        private void OpenSptFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var sptPath = GetSptInstallPath();
            if (string.IsNullOrWhiteSpace(sptPath) || !Directory.Exists(sptPath))
            {
                System.Windows.MessageBox.Show("SPT installation directory not found.", "Open Folder",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = sptPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open folder: {ex.Message}", "Open Folder",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReinstallSptButton_Click(object sender, RoutedEventArgs e)
        {
            InstallSptButton_Click(sender, e);
        }

        private void InvokeOnUi(Action action)
        {
            Dispatcher.Invoke(action);
        }

        private T InvokeOnUi<T>(Func<T> func)
        {
            return Dispatcher.Invoke(func);
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



}
