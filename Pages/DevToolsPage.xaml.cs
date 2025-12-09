using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace SptLauncherWpf.Pages
{
    public partial class DevToolsPage : Page
    {
        private List<ProcessInfo> _processes = new();
        private static DevToolsPage? _currentInstance;
        private Border? _selectedProcessCard = null;

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
                    // Update process count
                    var processCountText = this.FindName("ProcessCountText") as TextBlock;
                    if (processCountText != null)
                    {
                        processCountText.Text = $"{_processes.Count} process{(_processes.Count != 1 ? "es" : "")}";
                    }
                    
                    // Clear selection when refreshing
                    _selectedProcessCard = null;
                    
                    // Get the process list panel
                    var processListPanel = this.FindName("ProcessListPanel") as StackPanel;
                    if (processListPanel != null)
                    {
                        processListPanel.Children.Clear();
                        
                        // Create cards for each process
                        foreach (var process in _processes)
                        {
                            var card = CreateProcessCard(process);
                            processListPanel.Children.Add(card);
                        }
                    }
                    
                    // Show/hide empty state
                    var emptyStatePanel = this.FindName("EmptyStatePanel") as StackPanel;
                    if (emptyStatePanel != null)
                    {
                        emptyStatePanel.Visibility = _processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
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
            // Get the selected process from the selected card
            ProcessInfo? selectedProcess = null;
            if (_selectedProcessCard != null && _selectedProcessCard.Tag is ProcessInfo processInfo)
            {
                selectedProcess = processInfo;
            }
            
            if (selectedProcess != null)
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
                        
                        // Clear selection
                        _selectedProcessCard = null;
                        
                        // Force immediate refresh
                        RefreshProcesses();
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

        private Border CreateProcessCard(ProcessInfo process)
        {
            var card = new Border
            {
                Style = (Style)FindResource("ModernCardStyle"),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16),
                Tag = process,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stackPanel = new StackPanel();

            // Header with process name and status indicator
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var statusEllipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)FindResource("PrimaryColor"),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(statusEllipse);

            var nameText = new TextBlock
            {
                Text = process.ProcessName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerPanel.Children.Add(nameText);

            stackPanel.Children.Add(headerPanel);

            // Process details in a grid
            var detailsGrid = new Grid();
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // PID
            var pidLabel = new TextBlock
            {
                Text = "PID:",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryColor"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(pidLabel, 0);
            detailsGrid.Children.Add(pidLabel);

            var pidValue = new TextBlock
            {
                Text = process.Id.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                FontWeight = FontWeights.Medium
            };
            Grid.SetColumn(pidValue, 1);
            detailsGrid.Children.Add(pidValue);

            // Memory
            var memoryLabel = new TextBlock
            {
                Text = "Memory:",
                FontSize = 12,
                Foreground = (Brush)FindResource("TextSecondaryColor"),
                Margin = new Thickness(16, 0, 8, 0)
            };
            Grid.SetColumn(memoryLabel, 2);
            detailsGrid.Children.Add(memoryLabel);

            var memoryValue = new TextBlock
            {
                Text = process.Memory,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = (Brush)FindResource("TextPrimaryColor"),
                FontWeight = FontWeights.Medium
            };
            Grid.SetColumn(memoryValue, 3);
            detailsGrid.Children.Add(memoryValue);

            stackPanel.Children.Add(detailsGrid);

            // Add hover effect (only if not selected)
            card.MouseEnter += (s, e) =>
            {
                if (card != _selectedProcessCard)
                {
                    card.Background = (Brush)FindResource("HoverColor");
                }
            };
            card.MouseLeave += (s, e) =>
            {
                // Only reset background if this card is not selected
                if (card != _selectedProcessCard)
                {
                    card.Background = (Brush)FindResource("CardBackgroundColor");
                }
                else
                {
                    // Keep selected card highlighted
                    card.Background = (Brush)FindResource("HoverColor");
                }
            };

            // Add click handler for selection
            card.MouseLeftButtonDown += (s, e) =>
            {
                // Deselect previous card
                if (_selectedProcessCard != null && _selectedProcessCard != card)
                {
                    _selectedProcessCard.Background = (Brush)FindResource("CardBackgroundColor");
                }
                
                // Select this card
                _selectedProcessCard = card;
                card.Background = (Brush)FindResource("HoverColor");
            };

            card.Child = stackPanel;
            return card;
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
