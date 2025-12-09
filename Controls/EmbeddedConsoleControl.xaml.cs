using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SptLauncherWpf.Controls
{
    public partial class EmbeddedConsoleControl : UserControl
    {
        private Process? process;
        private bool isProcessRunning = false;
        private bool _userHasScrolled = false;
        
        // Windows API declarations for console embedding
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out System.Drawing.Rectangle lpRect);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CHILD = 0x40000000;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 1;

        public EmbeddedConsoleControl()
        {
            InitializeComponent();
        }

        public void StartProcess(string exePath, string args = "")
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    AppendLine("Process already running.");
                    return;
                }

                AppendLine($"[Launching] {exePath}");

                var workingDir = System.IO.Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(workingDir))
                {
                    AppendLine("❌ Invalid executable path");
                    return;
                }
                
                // Launch server in background (hidden) and monitor log files
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process = Process.Start(psi);
                if (process != null)
                {
                    isProcessRunning = true;
                    process.EnableRaisingEvents = true;
                    process.Exited += (s, e) => 
                    {
                        isProcessRunning = false;
                        AppendLine($"[Process exited with code: {process.ExitCode}]");
                    };
                    
                    AppendLine("✅ Server started successfully");
                    AppendLine("📋 Server is running in the background (hidden)");
                    AppendLine("📋 Monitoring log files for output...");
                    
                    // Monitor log files for output
                    _ = Task.Run(async () => await MonitorLogFiles(workingDir));
                }
                else
                {
                    AppendLine("❌ Failed to start server process");
                }
            }
            catch (Exception ex)
            {
                AppendLine($"❌ Failed to start: {ex.Message}");
            }
        }

        private Task CaptureConsoleOutput()
        {
            try
            {
                if (process == null)
                {
                    AppendLine("❌ Cannot capture output: process is null");
                    return Task.CompletedTask;
                }

                AppendLine("📋 Starting console output capture...");
                
                // Start reading from standard output
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (process.StandardOutput == null) return;
                        using var reader = process.StandardOutput;
                        string? line;
                        while (process != null && !process.HasExited && (line = await reader.ReadLineAsync()) != null)
                        {
                            Dispatcher.Invoke(() => AppendLine(CleanOutputLine(line)));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLine($"Output capture error: {ex.Message}"));
                    }
                });
                
                // Start reading from standard error
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (process.StandardError == null) return;
                        using var reader = process.StandardError;
                        string? line;
                        while (process != null && !process.HasExited && (line = await reader.ReadLineAsync()) != null)
                        {
                            Dispatcher.Invoke(() => AppendLine($"[ERROR] {CleanOutputLine(line)}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppendLine($"Error capture error: {ex.Message}"));
                    }
                });
                
                AppendLine("✅ Console output capture started successfully");
            }
            catch (Exception ex)
            {
                AppendLine($"❌ Failed to start console output capture: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private IntPtr FindWindowByProcessId(int processId)
        {
            IntPtr foundWindow = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out int windowProcessId);
                if (windowProcessId == processId)
                {
                    foundWindow = hWnd;
                    return false; // Stop enumeration
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return foundWindow;
        }

        private async Task MonitorLogFiles(string serverDirectory)
        {
            try
            {
                // Wait a moment for log files to be created
                await Task.Delay(2000);
                
                // Look for various log file patterns
                var logPatterns = new[] { "*.log", "*.txt", "user/logs/*.log", "logs/*.log" };
                var logFiles = new List<string>();
                
                foreach (var pattern in logPatterns)
                {
                    try
                    {
                        var files = Directory.GetFiles(serverDirectory, pattern, SearchOption.AllDirectories)
                            .OrderByDescending(f => File.GetLastWriteTime(f))
                            .Take(3);
                        logFiles.AddRange(files);
                    }
                    catch { }
                }
                
                logFiles = logFiles.Distinct().OrderByDescending(f => File.GetLastWriteTime(f)).Take(3).ToList();

                if (logFiles.Any())
                {
                    AppendLine($"📁 Monitoring log files: {string.Join(", ", logFiles.Select(Path.GetFileName))}");
                    
                    // Monitor the most recent log file
                    var latestLog = logFiles.First();
                    var lastPosition = new FileInfo(latestLog).Length;
                    
                    while (process != null && !process.HasExited)
                    {
                        try
                        {
                            var currentLength = new FileInfo(latestLog).Length;
                            if (currentLength > lastPosition)
                            {
                                using var fs = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                fs.Seek(lastPosition, SeekOrigin.Begin);
                                using var reader = new StreamReader(fs);
                                
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        AppendLine(CleanOutputLine(line));
                                    }
                                }
                                
                                lastPosition = currentLength;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLine($"Log monitoring error: {ex.Message}");
                        }
                        
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    AppendLine("⚠️ No log files found to monitor");
                    AppendLine("📋 Server console output will appear in the external console window");
                    AppendLine("📋 The server is running normally - check the external console for output");
                    
                    // Show a periodic status update since we can't monitor logs
                    var statusCounter = 0;
                    while (process != null && !process.HasExited)
                    {
                        await Task.Delay(5000); // Check every 5 seconds
                        statusCounter++;
                        
                        if (statusCounter % 6 == 0) // Every 30 seconds
                        {
                            AppendLine($"📋 Server still running... ({statusCounter * 5}s elapsed)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLine($"❌ Log monitoring failed: {ex.Message}");
            }
        }

        private string CleanAnsiCodes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Remove ANSI escape codes
            var ansiPattern = @"\x1b\[[0-9;]*[mK]";
            text = System.Text.RegularExpressions.Regex.Replace(text, ansiPattern, "");

            return text;
        }

        private string CleanOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return line;

            // Remove server ID prefix like [1bd36d39-dc2b-4c30-8dce-820af4fb6221]
            var serverIdPattern = @"^\[[a-f0-9-]{36}\]\s*";
            line = System.Text.RegularExpressions.Regex.Replace(line, serverIdPattern, "");

            // Remove timestamp prefix like [2025-10-23 18:37:48.387]
            var timestampPattern = @"^\[\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3}\]\s*";
            line = System.Text.RegularExpressions.Regex.Replace(line, timestampPattern, "");

            // Remove log level prefixes like [Info], [Error], [Fatal]
            var logLevelPattern = @"^\[(Info|Error|Fatal|Debug|Warn|Trace)\]\[[^\]]+\]\s*";
            line = System.Text.RegularExpressions.Regex.Replace(line, logLevelPattern, "");

            // Remove any remaining empty brackets
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^\[\]\s*", "");

            return line.Trim();
        }

        public Process? Process => process;
        
        public bool IsProcessRunning => isProcessRunning;
        
        public void StopProcess()
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill();
                    isProcessRunning = false;
                    AppendLine("[Process killed]");
                }
                catch (Exception ex)
                {
                    AppendLine($"Failed to kill process: {ex.Message}");
                }
            }
        }

        public void ClearConsole()
        {
            Dispatcher.Invoke(() =>
            {
                OutputBlock.Text = "";
                _userHasScrolled = false; // Reset scroll state when clearing
            });
        }

        public void SetOutput(string output)
        {
            Dispatcher.Invoke(() =>
            {
                OutputBlock.Text = output;
                _userHasScrolled = false; // Reset scroll state when setting output
                OutputScrollViewer.ScrollToEnd();
            });
        }

        public string GetOutput()
        {
            return OutputBlock.Text;
        }

        private void AppendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            Dispatcher.Invoke(() =>
            {
                OutputBlock.Text += line + Environment.NewLine;
                
                // Only auto-scroll if user hasn't manually scrolled up
                if (!_userHasScrolled)
                {
                    OutputScrollViewer.ScrollToEnd();
                }
            });
        }

        private void OutputBlock_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // This method is called when the user selects text in the output
            // The TextBox is already read-only, so users can select and copy text
        }

        private void OutputScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Check if user has scrolled up from the bottom
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                // If user is at the bottom (within 10 pixels), allow auto-scroll
                // If user has scrolled up, disable auto-scroll
                _userHasScrolled = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight - 10;
            }
        }
    }
}
