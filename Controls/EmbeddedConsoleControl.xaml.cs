using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SptLauncherWpf.Controls
{
    public partial class EmbeddedConsoleControl : UserControl
    {
        private Process process;

        public EmbeddedConsoleControl()
        {
            InitializeComponent();
        }

        public async void StartProcess(string exePath, string args = "")
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    AppendLine("Process already running.");
                    return;
                }

                AppendLine($"[Launching] {exePath}");

                // For SPT server, we need to let it run in its own console to avoid "Unable to get console mode" error
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true, // Let it use its own console
                    CreateNoWindow = false  // Allow console window
                };

                process = Process.Start(psi);
                if (process != null)
                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (s, e) => AppendLine("[Process exited]");
                    
                    AppendLine("✅ Server started in its own console window");
                    AppendLine("📋 Check the server console window for detailed output");
                    AppendLine("🔄 Monitoring log files for status updates...");
                    
                    // Start monitoring log files for output since we can't redirect directly
                    _ = Task.Run(() => MonitorLogFiles(System.IO.Path.GetDirectoryName(exePath)));
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

        private async Task MonitorLogFiles(string serverDirectory)
        {
            try
            {
                // Wait a moment for log files to be created
                await Task.Delay(2000);
                
                var logFiles = Directory.GetFiles(serverDirectory, "*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(3)
                    .ToList();

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
                                
                                string line;
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
                }
            }
            catch (Exception ex)
            {
                AppendLine($"❌ Log monitoring failed: {ex.Message}");
            }
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

        public void StopProcess()
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill();
                    AppendLine("[Process killed]");
                }
                catch (Exception ex)
                {
                    AppendLine($"Failed to kill process: {ex.Message}");
                }
            }
        }

        private void AppendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            Dispatcher.Invoke(() =>
            {
                OutputBlock.Text += line + Environment.NewLine;
                ScrollViewer.ScrollToEnd();
            });
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            SendInput(InputBox.Text);
            InputBox.Clear();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendInput(InputBox.Text);
                InputBox.Clear();
                e.Handled = true;
            }
        }

        private void SendInput(string input)
        {
            if (process == null || process.HasExited)
            {
                AppendLine("[Cannot send input — process not running]");
                return;
            }

            process.StandardInput.WriteLine(input);
            process.StandardInput.Flush();
        }
    }
}
