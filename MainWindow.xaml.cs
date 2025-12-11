using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SptLauncherWpf.Pages;
using SptLauncherWpf.Services;
using System.Threading.Tasks;

namespace SptLauncherWpf
{
    public partial class MainWindow : Window
    {
        private string _currentTab = "launcher";
        private bool _isMaximized = false;

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                
                // Set window icon from resource
                try
                {
                    var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "spt_rpg_icon.ico");
                    if (File.Exists(iconPath))
                    {
                        using (var iconStream = new FileStream(iconPath, FileMode.Open))
                        {
                            this.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconStream);
                        }
                    }
                }
                catch
                {
                    // Icon loading failed, continue without it
                }
                
                InitializeStyles();
                SetActiveTab("launcher");
                SetupDragFunctionality();
                SetVersionFromAssembly();
                
                // Subscribe to theme changes to update icon BEFORE loading
                ThemeService.Instance.ThemeChanged += OnThemeChanged;
                
                // Subscribe to update service events
                UpdateService.Instance.UpdateAvailable += OnUpdateAvailable;
                
                // Update icon to reflect current theme
                UpdateThemeIcon();
                
                // Extend window frame into client area to eliminate white border
                Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize MainWindow: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        
        private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
        {
            // Update icon on UI thread
            Dispatcher.Invoke(() =>
            {
                UpdateThemeIcon();
            });
        }
        
        private void UpdateThemeIcon()
        {
            try
            {
                var currentTheme = ThemeService.Instance.CurrentTheme;
                // Show opposite icon: if light mode, show moon (to switch to dark), if dark mode, show sun (to switch to light)
                ThemeToggleIcon.Text = currentTheme == "light" ? "🌙" : "🌞";
            }
            catch
            {
                // Fallback if update fails
                ThemeToggleIcon.Text = "🌙";
            }
        }
        
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button temporarily to prevent rapid clicking
                ThemeToggleButton.IsEnabled = false;
                
                var currentTheme = ThemeService.Instance.CurrentTheme;
                var newTheme = currentTheme == "light" ? "dark" : "light";
                
                // Apply theme synchronously
                ThemeService.Instance.ApplyTheme(newTheme);
                
                // Re-enable button after a short delay
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    ThemeToggleButton.IsEnabled = true;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                // Re-enable button on error
                ThemeToggleButton.IsEnabled = true;
                MessageBox.Show($"Failed to toggle theme: {ex.Message}", "Theme Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                DwmExtendFrameIntoClientArea(helper.Handle, ref margins);
            }
            catch
            {
                // Ignore if DWM extension fails
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
                button.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // #3B82F6 - brighter blue
                button.FontWeight = FontWeights.SemiBold;
                button.Opacity = 1.0;
            }
            else
            {
                button.Background = Brushes.Transparent;
                button.FontWeight = FontWeights.Normal;
                button.Opacity = 0.9;
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

        private UpdateInfo? _availableUpdate;

        private void OnUpdateAvailable(object? sender, UpdateInfo updateInfo)
        {
            Dispatcher.Invoke(() =>
            {
                _availableUpdate = updateInfo;
                UpdateNotificationText.Text = $"A new version is available!";
                UpdateVersionText.Text = $"Version {updateInfo.Version} - Click Download to update";
                UpdateNotificationBanner.Visibility = Visibility.Visible;
            });
        }

        private async void UpdateDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null) return;

            try
            {
                UpdateDownloadButton.IsEnabled = false;
                UpdateDownloadButton.Content = "Downloading...";

                var progress = new Progress<double>(percent =>
                {
                    UpdateDownloadButton.Content = $"Downloading... {percent:F0}%";
                });

                var success = await UpdateService.Instance.DownloadUpdateAsync(_availableUpdate, progress);

                if (success)
                {
                    MessageBox.Show("Update downloaded successfully. The installer will launch shortly.", 
                        "Update Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateNotificationBanner.Visibility = Visibility.Collapsed;
                }
                else
                {
                    MessageBox.Show("Failed to download the update. Please try again later.", 
                        "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateDownloadButton.IsEnabled = true;
                    UpdateDownloadButton.Content = "Download";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading update: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateDownloadButton.IsEnabled = true;
                UpdateDownloadButton.Content = "Download";
            }
        }

        private void UpdateDismissButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateNotificationBanner.Visibility = Visibility.Collapsed;
        }
    }
}
