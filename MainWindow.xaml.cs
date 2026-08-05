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
                RefreshChromeTabStyles();
                
                // Extend window frame into client area to eliminate white border
                Loaded += MainWindow_Loaded;

                // Handle post-restart self-update confirmation / .old.exe cleanup
                Loaded += (_, _) => TryShowSelfUpdateCompletion();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to initialize MainWindow: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
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
                RefreshChromeTabStyles();
            });
        }
        
        private void UpdateThemeIcon()
        {
            try
            {
                var currentTheme = ThemeService.Instance.CurrentTheme;
                // Label shows the theme you can switch TO.
                ThemeToggleIcon.Text = currentTheme == "light" ? "Dark" : "Light";
            }
            catch
            {
                ThemeToggleIcon.Text = "Dark";
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
                System.Windows.MessageBox.Show($"Failed to toggle theme: {ex.Message}", "Theme Error", 
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

        private void ApplyTabButtonStyle(System.Windows.Controls.Button button)
        {
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.FontSize = 13.0;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = (System.Windows.Media.Brush)FindResource("ChromeMutedTextColor");
        }

        private void RefreshChromeTabStyles()
        {
            UpdateTabButtonStyle(LauncherTabButton, _currentTab == "launcher");
            UpdateTabButtonStyle(ServersTabButton, _currentTab == "servers");
            UpdateTabButtonStyle(ModsTabButton, _currentTab == "mods");
            UpdateTabButtonStyle(SettingsTabButton, _currentTab == "settings");
            UpdateTabButtonStyle(DevToolsTabButton, _currentTab == "devtools");
        }

        private void SetActiveTab(string tabId)
        {
            try
            {
                _currentTab = tabId;
                
                // Update button styles
                RefreshChromeTabStyles();

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
                System.Windows.MessageBox.Show($"Failed to set active tab '{tabId}': {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTabButtonStyle(System.Windows.Controls.Button button, bool isActive)
        {
            if (isActive)
            {
                button.Background = (System.Windows.Media.Brush)FindResource("HoverColor");
                button.Foreground = (System.Windows.Media.Brush)FindResource("ChromeTextColor");
                button.FontWeight = FontWeights.SemiBold;
                button.Opacity = 1.0;
            }
            else
            {
                button.Background = System.Windows.Media.Brushes.Transparent;
                button.Foreground = (System.Windows.Media.Brush)FindResource("ChromeMutedTextColor");
                button.FontWeight = FontWeights.SemiBold;
                button.Opacity = 1.0;
            }
        }

        private void ApplyStatusBannerKind(bool? success)
        {
            var accent = success switch
            {
                true => (System.Windows.Media.Brush)FindResource("StatusSuccessColor"),
                false => (System.Windows.Media.Brush)FindResource("StatusWarningColor"),
                _ => (System.Windows.Media.Brush)FindResource("StatusInfoColor")
            };

            if (UpdateBannerAccent != null)
            {
                UpdateBannerAccent.Background = accent;
            }

            UpdateNotificationBanner.SetResourceReference(
                System.Windows.Controls.Border.BackgroundProperty,
                "HoverColor");
            UpdateNotificationBanner.SetResourceReference(
                System.Windows.Controls.Border.BorderBrushProperty,
                "BorderColor");
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string tabId)
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
        private System.Windows.Threading.DispatcherTimer? _updateBannerAutoHideTimer;
        private bool _showingSelfUpdateResult;

        private void TryShowSelfUpdateCompletion()
        {
            try
            {
                var result = UpdateService.Instance.CompleteSelfUpdateIfNeeded();
                if (result == null)
                {
                    return;
                }

                if (result.ShowSuccessBanner)
                {
                    ShowSelfUpdateResultBanner(
                        title: "Launcher updated",
                        detail: $"You're now on {result.DisplayVersion}.",
                        success: true);
                    return;
                }

                if (result.ShowFailureBanner)
                {
                    var expected = UpdateApplyHelper.FormatDisplayVersion(result.ExpectedVersion);
                    ShowSelfUpdateResultBanner(
                        title: "Launcher update may not have applied",
                        detail: string.IsNullOrWhiteSpace(expected)
                            ? $"Still running {result.DisplayVersion}. A .old.exe backup may still be next to the app."
                            : $"Expected {expected}, but this build is {result.DisplayVersion}. A .old.exe backup may still be next to the app.",
                        success: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to complete self-update handling: {ex.Message}");
            }
        }

        private void ShowSelfUpdateResultBanner(string title, string detail, bool success)
        {
            _showingSelfUpdateResult = true;
            _availableUpdate = null;

            UpdateNotificationText.Text = title;
            UpdateVersionText.Text = detail;
            UpdateDownloadButton.Visibility = Visibility.Collapsed;
            ApplyStatusBannerKind(success);
            UpdateNotificationBanner.Visibility = Visibility.Visible;

            _updateBannerAutoHideTimer?.Stop();
            if (success)
            {
                _updateBannerAutoHideTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(8)
                };
                _updateBannerAutoHideTimer.Tick += (_, _) =>
                {
                    _updateBannerAutoHideTimer.Stop();
                    if (_showingSelfUpdateResult)
                    {
                        UpdateNotificationBanner.Visibility = Visibility.Collapsed;
                        _showingSelfUpdateResult = false;
                        ResetUpdateBannerChrome();
                    }
                };
                _updateBannerAutoHideTimer.Start();
            }
        }

        private void ResetUpdateBannerChrome()
        {
            UpdateDownloadButton.Visibility = Visibility.Visible;
            UpdateDownloadButton.IsEnabled = true;
            UpdateDownloadButton.Content = "Download";
            ApplyStatusBannerKind(null);
        }

        private void OnUpdateAvailable(object? sender, UpdateInfo updateInfo)
        {
            Dispatcher.Invoke(() =>
            {
                if (_showingSelfUpdateResult)
                {
                    return;
                }

                _availableUpdate = updateInfo;
                ResetUpdateBannerChrome();
                UpdateNotificationText.Text = "A new version is available";
                UpdateVersionText.Text = $"Version {updateInfo.Version} — click Download to update";
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
                    UpdateDownloadButton.Content = "Restarting...";
                    // App shuts down to let the update script replace the executable.
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to download the update. Please try again later.", 
                        "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateDownloadButton.IsEnabled = true;
                    UpdateDownloadButton.Content = "Download";
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error downloading update: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateDownloadButton.IsEnabled = true;
                UpdateDownloadButton.Content = "Download";
            }
        }

        private void UpdateDismissButton_Click(object sender, RoutedEventArgs e)
        {
            _updateBannerAutoHideTimer?.Stop();
            _showingSelfUpdateResult = false;
            UpdateNotificationBanner.Visibility = Visibility.Collapsed;
            ResetUpdateBannerChrome();
        }
    }
}
