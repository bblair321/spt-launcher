using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SptLauncherWpf
{
    public partial class SimpleWpfLauncher : Window
    {
        private Process? _serverProcess;
        private System.Windows.Controls.TextBox _consoleOutput = null!;
        private System.Windows.Controls.Button _startButton = null!;
        private System.Windows.Controls.Button _stopButton = null!;
        private System.Windows.Controls.TextBox _serverPathTextBox = null!;
        private System.Windows.Controls.Label _statusLabel = null!;

        public SimpleWpfLauncher()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Title = "SPT Launcher - WPF Version";
            Width = 800;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MinWidth = 600;
            MinHeight = 400;

            // Main grid
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Controls
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Console
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); // Status

            // Controls panel
            var controlsPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Center
            };

            var pathLabel = new System.Windows.Controls.Label { Content = "Server Path:", VerticalAlignment = VerticalAlignment.Center };
            _serverPathTextBox = new System.Windows.Controls.TextBox
            {
                Text = "",
                Width = 400,
                Margin = new Thickness(5, 0, 5, 0)
            };

            _startButton = new System.Windows.Controls.Button
            {
                Content = "Start Server",
                Width = 100,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(Colors.Green),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _startButton.Click += StartButton_Click;

            _stopButton = new System.Windows.Controls.Button
            {
                Content = "Stop Server",
                Width = 100,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(Colors.Red),
                Foreground = new SolidColorBrush(Colors.White),
                IsEnabled = false
            };
            _stopButton.Click += StopButton_Click;

            controlsPanel.Children.Add(pathLabel);
            controlsPanel.Children.Add(_serverPathTextBox);
            controlsPanel.Children.Add(_startButton);
            controlsPanel.Children.Add(_stopButton);

            Grid.SetRow(controlsPanel, 0);
            mainGrid.Children.Add(controlsPanel);

            // Console output
            var consolePanel = new Grid();
            consolePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            consolePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var consoleLabel = new System.Windows.Controls.Label
            {
                Content = "Server Output:",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 5, 10, 5)
            };
            Grid.SetRow(consoleLabel, 0);
            consolePanel.Children.Add(consoleLabel);

            _consoleOutput = new System.Windows.Controls.TextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Background = new SolidColorBrush(Colors.Black),
                Foreground = new SolidColorBrush(Colors.LimeGreen),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 10)
            };
            Grid.SetRow(_consoleOutput, 1);
            consolePanel.Children.Add(_consoleOutput);

            Grid.SetRow(consolePanel, 1);
            mainGrid.Children.Add(consolePanel);

            // Status bar
            _statusLabel = new System.Windows.Controls.Label
            {
                Content = "Ready",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 5, 10, 5)
            };
            Grid.SetRow(_statusLabel, 2);
            mainGrid.Children.Add(_statusLabel);

            Content = mainGrid;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_serverPathTextBox.Text))
            {
                System.Windows.MessageBox.Show("Please enter a server path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _startButton.IsEnabled = false;
            _statusLabel.Content = "Starting server...";
            _consoleOutput.Clear();

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _serverPathTextBox.Text,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(_serverPathTextBox.Text) ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false, // This is crucial for console mode
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _serverProcess = Process.Start(processInfo);
                if (_serverProcess == null)
                {
                    throw new Exception("Failed to start server process");
                }

                _statusLabel.Content = $"Server started (PID: {_serverProcess.Id})";
                _stopButton.IsEnabled = true;

                // Start output capture
                _ = Task.Run(() => CaptureOutput(_serverProcess));

                AddOutput("Server started successfully!", Colors.Green);
            }
            catch (Exception ex)
            {
                AddOutput($"Error starting server: {ex.Message}", Colors.Red);
                _statusLabel.Content = "Failed to start server";
                _startButton.IsEnabled = true;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit(5000);
                    AddOutput("Server stopped by user", Colors.Orange);
                }
                catch (Exception ex)
                {
                    AddOutput($"Error stopping server: {ex.Message}", Colors.Red);
                }
            }

            _serverProcess = null;
            _startButton.IsEnabled = true;
            _stopButton.IsEnabled = false;
            _statusLabel.Content = "Ready";
        }

        private void CaptureOutput(Process process)
        {
            Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var output = await process.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrEmpty(output))
                        {
                            AddOutput(output, Colors.LimeGreen);
                        }

                        var error = await process.StandardError.ReadLineAsync();
                        if (!string.IsNullOrEmpty(error))
                        {
                            AddOutput(error, Colors.Red);
                        }

                        await Task.Delay(10);
                    }
                }
                catch (Exception ex)
                {
                    AddOutput($"Output capture error: {ex.Message}", Colors.Red);
                }
            });
        }

        private void AddOutput(string text, System.Windows.Media.Color color)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                _consoleOutput.AppendText($"[{timestamp}] {text}\n");
                _consoleOutput.ScrollToEnd();
            });
        }
    }
}
