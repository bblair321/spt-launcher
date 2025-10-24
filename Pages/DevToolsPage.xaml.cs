using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SptLauncherWpf.Pages
{
    public partial class DevToolsPage : Page
    {
        private List<ProcessInfo> _processes = new();

        public DevToolsPage()
        {
            InitializeComponent();
            LoadSystemInfo();
            LoadPerformanceInfo();
            LoadNetworkInfo();
        }

        private void LoadSystemInfo()
        {
            try
            {
                SystemInfoPanel.Children.Clear();

                // OS Information
                var osInfo = Environment.OSVersion;
                AddInfoItem("OS", $"{osInfo.VersionString}");
                AddInfoItem("Platform", $"{osInfo.Platform}");
                AddInfoItem("Version", $"{osInfo.Version}");

                // .NET Information
                AddInfoItem(".NET Version", Environment.Version.ToString());
                AddInfoItem("Machine Name", Environment.MachineName);
                AddInfoItem("User Domain", Environment.UserDomainName);
                AddInfoItem("User Name", Environment.UserName);

                // System Directories
                AddInfoItem("System Directory", Environment.SystemDirectory);
                AddInfoItem("Current Directory", Environment.CurrentDirectory);
            }
            catch (Exception ex)
            {
                AddInfoItem("Error", $"Failed to load system info: {ex.Message}");
            }
        }

        private void LoadPerformanceInfo()
        {
            try
            {
                PerformanceInfoPanel.Children.Clear();

                // Memory Information
                var workingSet = Environment.WorkingSet;
                AddInfoItem("Working Set", FormatBytes(workingSet));
                AddInfoItem("GC Memory", FormatBytes(GC.GetTotalMemory(false)));

                // Processor Information
                AddInfoItem("Processor Count", Environment.ProcessorCount.ToString());
                AddInfoItem("64-bit Process", Environment.Is64BitProcess.ToString());
                AddInfoItem("64-bit OS", Environment.Is64BitOperatingSystem.ToString());

                // System Uptime
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount);
                AddInfoItem("System Uptime", $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m");
            }
            catch (Exception ex)
            {
                AddInfoItem("Error", $"Failed to load performance info: {ex.Message}");
            }
        }

        private void LoadNetworkInfo()
        {
            try
            {
                var networkInfo = new StringBuilder();
                
                // Get network interfaces
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        networkInfo.AppendLine($"Interface: {ni.Name}");
                        networkInfo.AppendLine($"  Type: {ni.NetworkInterfaceType}");
                        networkInfo.AppendLine($"  Status: {ni.OperationalStatus}");
                        
                        var ipProps = ni.GetIPProperties();
                        foreach (var addr in ipProps.UnicastAddresses)
                        {
                            networkInfo.AppendLine($"  IP: {addr.Address}");
                        }
                        networkInfo.AppendLine();
                    }
                }

                NetworkInfoTextBlock.Text = networkInfo.ToString();
            }
            catch (Exception ex)
            {
                NetworkInfoTextBlock.Text = $"Failed to load network info: {ex.Message}";
            }
        }

        private void AddInfoItem(string label, string value)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var labelText = new TextBlock
            {
                Text = $"{label}:",
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                Width = 120,
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(labelText);

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(valueText);

            SystemInfoPanel.Children.Add(panel);
        }

        private void AddInfoItem(string label, string value, StackPanel targetPanel)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var labelText = new TextBlock
            {
                Text = $"{label}:",
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                Width = 120,
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(labelText);

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(valueText);

            targetPanel.Children.Add(panel);
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private async void RefreshProcessesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshProcessesButton.IsEnabled = false;
                RefreshProcessesButton.Content = "Refreshing...";

                await Task.Run(() =>
                {
                    _processes.Clear();
                    var processes = Process.GetProcesses()
                        .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                        .OrderBy(p => p.ProcessName)
                        .Take(100); // Limit to 100 processes for performance

                    foreach (var process in processes)
                    {
                        try
                        {
                            var processInfo = new ProcessInfo
                            {
                                Id = process.Id,
                                ProcessName = process.ProcessName,
                                CPU = "0%", // CPU usage would require more complex calculation
                                Memory = FormatBytes(process.WorkingSet64)
                            };
                            _processes.Add(processInfo);
                        }
                        catch
                        {
                            // Skip processes that can't be accessed
                        }
                    }
                });

                ProcessListView.ItemsSource = _processes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh processes: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshProcessesButton.IsEnabled = true;
                RefreshProcessesButton.Content = "Refresh";
            }
        }

        private void KillProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessInfo selectedProcess)
            {
                var result = MessageBox.Show($"Are you sure you want to kill process '{selectedProcess.ProcessName}' (PID: {selectedProcess.Id})?", 
                                           "Confirm Kill", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var process = Process.GetProcessById(selectedProcess.Id);
                        process.Kill();
                        RefreshProcessesButton_Click(sender, e); // Refresh the list
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to kill process: {ex.Message}", "Error", 
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a process to kill.", "No Selection", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SPT-Launcher", "logs");
                if (!Directory.Exists(logDirectory))
                {
                    LogTextBlock.Text = "No log directory found.";
                    return;
                }

                var logFiles = Directory.GetFiles(logDirectory, "*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(5); // Load last 5 log files

                var logContent = new StringBuilder();
                foreach (var logFile in logFiles)
                {
                    logContent.AppendLine($"=== {Path.GetFileName(logFile)} ===");
                    logContent.AppendLine(File.ReadAllText(logFile));
                    logContent.AppendLine();
                }

                LogTextBlock.Text = logContent.ToString();
                LogScrollViewer.ScrollToEnd();
            }
            catch (Exception ex)
            {
                LogTextBlock.Text = $"Failed to load logs: {ex.Message}";
            }
        }

        private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBlock.Text = "";
        }

        private void ExportLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Export Logs",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"spt-launcher-logs-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveFileDialog.FileName, LogTextBlock.Text);
                    MessageBox.Show("Logs exported successfully!", "Export Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export logs: {ex.Message}", "Export Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ScanPortsButton_Click(object sender, RoutedEventArgs e)
        {
            var host = PortScannerTextBox.Text.Trim();
            if (string.IsNullOrEmpty(host))
            {
                MessageBox.Show("Please enter a host to scan.", "Invalid Input", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ScanPortsButton.IsEnabled = false;
                ScanPortsButton.Content = "Scanning...";

                var openPorts = new List<int>();
                var commonPorts = new[] { 22, 23, 25, 53, 80, 110, 143, 443, 993, 995, 3389, 6969 };

                await Task.Run(() =>
                {
                    foreach (var port in commonPorts)
                    {
                        try
                        {
                            using var client = new System.Net.Sockets.TcpClient();
                            var result = client.BeginConnect(host, port, null, null);
                            var success = result.AsyncWaitHandle.WaitOne(1000, true);
                            if (success && client.Connected)
                            {
                                openPorts.Add(port);
                                client.EndConnect(result);
                            }
                        }
                        catch
                        {
                            // Port is closed or filtered
                        }
                    }
                });

                var resultText = $"Port scan results for {host}:\n\n";
                if (openPorts.Count > 0)
                {
                    resultText += "Open ports:\n";
                    foreach (var port in openPorts)
                    {
                        resultText += $"  Port {port} - Open\n";
                    }
                }
                else
                {
                    resultText += "No open ports found on common ports.";
                }

                MessageBox.Show(resultText, "Port Scan Results", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Port scan failed: {ex.Message}", "Scan Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanPortsButton.IsEnabled = true;
                ScanPortsButton.Content = "Scan Ports";
            }
        }

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Testing SPT connection...\n\nThis would test connectivity to SPT-AKI servers and validate configuration.", 
                          "Connection Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Validating configuration...\n\nThis would check all configuration files for syntax errors and missing values.", 
                          "Config Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to clear the application cache?", 
                                       "Confirm Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SPT-Launcher", "cache");
                    if (Directory.Exists(cacheDirectory))
                    {
                        Directory.Delete(cacheDirectory, true);
                    }
                    MessageBox.Show("Cache cleared successfully!", "Cache Cleared", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to clear cache: {ex.Message}", "Clear Cache Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = "";
        public string CPU { get; set; } = "";
        public string Memory { get; set; } = "";
    }
}
