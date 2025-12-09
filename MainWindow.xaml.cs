using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SptLauncherWpf.Pages;

namespace SptLauncherWpf
{
    public partial class MainWindow : Window
    {
        private string _currentTab = "launcher";
        private bool _isMaximized = false;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                InitializeStyles();
                SetActiveTab("launcher");
                SetupDragFunctionality();
                SetVersionFromAssembly();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize MainWindow: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void InitializeStyles()
        {
            // Apply base styles to all tab buttons
            ApplyTabButtonStyle(LauncherTabButton);
            ApplyTabButtonStyle(ServersTabButton);
            ApplyTabButtonStyle(ModsTabButton);
            ApplyTabButtonStyle(SettingsTabButton);
            ApplyTabButtonStyle(DevToolsTabButton);
        }

        private void ApplyTabButtonStyle(Button button)
        {
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(20, 12, 20, 12);
            button.Margin = new Thickness(0, 0, 2, 0);
            button.Cursor = Cursors.Hand;
            button.FontSize = 14.0;
            button.FontWeight = FontWeights.Normal;
            button.Foreground = Brushes.White; // White text for better visibility on blue background
        }

        private void SetActiveTab(string tabId)
        {
            try
            {
                _currentTab = tabId;
                
                // Update button styles
                UpdateTabButtonStyle(LauncherTabButton, tabId == "launcher");
                UpdateTabButtonStyle(ServersTabButton, tabId == "servers");
                UpdateTabButtonStyle(ModsTabButton, tabId == "mods");
                UpdateTabButtonStyle(SettingsTabButton, tabId == "settings");
                UpdateTabButtonStyle(DevToolsTabButton, tabId == "devtools");

                // Navigate to appropriate page
                switch (tabId)
                {
                    case "launcher":
                        ContentFrame.Navigate(new LauncherPage());
                        break;
                    case "servers":
                        ContentFrame.Navigate(new ServersPage());
                        break;
                    case "mods":
                        ContentFrame.Navigate(new ModsPage());
                        break;
                    case "settings":
                        ContentFrame.Navigate(new SettingsPage());
                        break;
                    case "devtools":
                        ContentFrame.Navigate(new DevToolsPage());
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set active tab '{tabId}': {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTabButtonStyle(Button button, bool isActive)
        {
            if (isActive)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // #2563EB
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.FontWeight = FontWeights.Normal;
            }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tabId)
            {
                SetActiveTab(tabId);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMaximized)
            {
                WindowState = WindowState.Normal;
                _isMaximized = false;
                MaximizeButton.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                _isMaximized = true;
                MaximizeButton.Content = "❐";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetupDragFunctionality()
        {
            // Drag functionality is handled by the title bar MouseLeftButtonDown event
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Drag the window when clicking on the title bar
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void SetVersionFromAssembly()
        {
            try
            {
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch
            {
                // Fallback to default version if assembly version can't be read
                VersionText.Text = "v3.0.0";
            }
        }
    }
}
