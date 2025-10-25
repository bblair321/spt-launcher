using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SptLauncherWpf.Pages
{
    public partial class DevToolsPage : Page
    {
        private List<ProcessInfo> _processes = new();
        private static DevToolsPage _currentInstance;

        public DevToolsPage()
        {
            InitializeComponent();
            _currentInstance = this;
            SetupAutoRefresh();
        }

        private void DevToolsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _currentInstance = this;
            RefreshProcesses();
        }

        private void DevToolsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Keep timer running even when leaving tab for continuous refresh
            // Only stop timer when page is actually destroyed
        }

        ~DevToolsPage()
        {
            // Don't stop global timer - let it continue running
        }

        private void SetupAutoRefresh()
        {
            // Initial refresh
            RefreshProcesses();
        }



        private void RefreshProcesses()
        {
            try
            {
                _processes.Clear();
                
                // Look for SPT-related processes
                var sptProcessNames = new[] { "SPT.Server", "SPT.Launcher", "Aki.Server", "Aki.Launcher" };
                var allProcesses = Process.GetProcesses();
                
                foreach (var processName in sptProcessNames)
                {
                    try
                    {
                        var processes = allProcesses.Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
                        var processList = processes.ToList();
                        
                        foreach (var process in processList)
                        {
                            try
                            {
                                // Check if process is still running
                                if (!process.HasExited)
                                {
                                    var processInfo = new ProcessInfo
                                    {
                                        Id = process.Id,
                                        ProcessName = process.ProcessName,
                                        CPU = "0%", // CPU usage would require more complex calculation
                                        Memory = $"{process.WorkingSet64 / 1024 / 1024:F1} MB"
                                    };
                                    _processes.Add(processInfo);
                                }
                            }
                            catch
                            {
                                // Silently handle individual process access errors
                            }
                        }
                    }
                    catch
                    {
                        // Silently handle process search errors
                    }
                }

                // Update UI on UI thread
                Dispatcher.Invoke(() =>
                {
                    // Force complete UI refresh
                    ProcessListView.ItemsSource = null;
                    ProcessListView.Items.Clear();
                    ProcessListView.UpdateLayout();
                    
                    // Create a new collection to force binding refresh
                    var newProcesses = new List<ProcessInfo>(_processes);
                    ProcessListView.ItemsSource = newProcesses;
                    
                    // Force visual update
                    ProcessListView.InvalidateVisual();
                });
            }
            catch
            {
                // Silently handle refresh errors
            }
        }

        private void RefreshProcessesButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcesses();
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
                        
                        // Wait for the process to actually terminate
                        System.Threading.Thread.Sleep(500);
                        
                        // Force immediate refresh
                        RefreshProcesses();
                        
                        // Clear the selection after killing
                        ProcessListView.SelectedItem = null;
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



    }

    public class ProcessInfo
    {
        public int Id { get; set; }
        public string ProcessName { get; set; } = "";
        public string CPU { get; set; } = "";
        public string Memory { get; set; } = "";
    }
}
