using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SptLauncherWpf.Controls;
using SptLauncherWpf.Pages;
using SptLauncherWpf.Services;
using System.Threading;
using System.Threading.Tasks;

namespace SptLauncherWpf
{
    public partial class MainWindow : Window
    {
        private string _currentTab = "launcher";
        private bool _isMaximized = false;

        // Keep pages alive so tab switches don't reset first-run / UI state.
        private LauncherPage? _launcherPage;
        private ServersPage? _serversPage;
        private ModsPage? _modsPage;
        private SettingsPage? _settingsPage;
        private OfficialSptLauncherHost? _officialLauncherHost;
        private nint _embeddedOfficialHwnd;
        private NativeWindowStyleSnapshot? _embeddedOfficialStyles;
        private CancellationTokenSource? _embedCts;
        private bool _officialLauncherOverlayMinimized;

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
                
                Closing += MainWindow_Closing;
                StateChanged += MainWindow_StateChanged;

                RestoreWindowBounds();

                // Handle post-restart self-update confirmation / .old.exe cleanup
                Loaded += (_, _) =>
                {
                    EnsureOfficialLauncherHost();
                    TryShowSelfUpdateCompletion();
                };
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
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateThemeIcon();
                    RefreshChromeTabStyles();
                }));
                return;
            }

            UpdateThemeIcon();
            RefreshChromeTabStyles();
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
                ThemeToggleButton.IsEnabled = false;

                var currentTheme = ThemeService.Instance.CurrentTheme;
                var newTheme = currentTheme == "light" ? "dark" : "light";
                ThemeService.Instance.ApplyTheme(newTheme);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to toggle theme: {ex.Message}", "Theme Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ThemeToggleButton.IsEnabled = true;
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            _isMaximized = WindowState == WindowState.Maximized;
            if (MaximizeButton != null)
            {
                MaximizeButton.Content = _isMaximized ? "❐" : "□";
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                DetachOfficialLauncher(restoreAsStandalone: true);
            }
            catch
            {
                // Don't block close on detach failure
            }

            try
            {
                SaveWindowBounds();
            }
            catch
            {
                // Don't block close on settings write failure
            }
        }

        private void RestoreWindowBounds()
        {
            var settings = SettingsService.Instance;
            var width = settings.WindowWidth;
            var height = settings.WindowHeight;
            if (width < MinWidth || height < MinHeight)
            {
                return;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = width;
            Height = height;

            var left = settings.WindowLeft;
            var top = settings.WindowTop;
            if (!double.IsNaN(left) && !double.IsNaN(top) && IsOnAnyScreen(left, top, width, height))
            {
                Left = left;
                Top = top;
            }

            if (settings.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
                _isMaximized = true;
                MaximizeButton.Content = "❐";
            }
        }

        private void SaveWindowBounds()
        {
            var settings = SettingsService.Instance;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.WindowMaximized = WindowState == WindowState.Maximized || _isMaximized;
            settings.SaveSettings();
        }

        private static bool IsOnAnyScreen(double left, double top, double width, double height)
        {
            var vsLeft = SystemParameters.VirtualScreenLeft;
            var vsTop = SystemParameters.VirtualScreenTop;
            var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
            const double margin = 48;

            return left < vsRight - margin
                   && left + width > vsLeft + margin
                   && top < vsBottom - margin
                   && top + margin > vsTop;
        }

        private void InitializeStyles()
        {
            // Apply base styles to all tab buttons
            ApplyTabButtonStyle(LauncherTabButton);
            ApplyTabButtonStyle(ServersTabButton);
            ApplyTabButtonStyle(ModsTabButton);
            ApplyTabButtonStyle(SettingsTabButton);
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
        }

        private void SetActiveTab(string tabId)
        {
            try
            {
                _currentTab = tabId;
                
                // Update button styles
                RefreshChromeTabStyles();

                // Navigate to a cached page instance (recreating LauncherPage restarted first-run setup).
                switch (tabId)
                {
                    case "launcher":
                        _launcherPage ??= new LauncherPage();
                        ContentFrame.Navigate(_launcherPage);
                        break;
                    case "servers":
                        _serversPage ??= new ServersPage();
                        ContentFrame.Navigate(_serversPage);
                        break;
                    case "mods":
                        _modsPage ??= new ModsPage();
                        ContentFrame.Navigate(_modsPage);
                        break;
                    case "settings":
                        _settingsPage ??= new SettingsPage();
                        ContentFrame.Navigate(_settingsPage);
                        break;
                }

                SyncOfficialLauncherOverlay();
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
                var version = UpdateService.Instance.GetCurrentVersion();
                VersionText.Text = UpdateApplyHelper.FormatDisplayVersion(version);
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
                    var detail = string.IsNullOrWhiteSpace(UpdateService.Instance.LastDownloadError)
                        ? "Please try again later."
                        : UpdateService.Instance.LastDownloadError;
                    System.Windows.MessageBox.Show(
                        $"Failed to download the update.\n\n{detail}",
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

        public bool IsOfficialLauncherEmbedded => _embeddedOfficialHwnd != 0;

        public bool IsOfficialLauncherOverlayShowing =>
            IsOfficialLauncherEmbedded && !_officialLauncherOverlayMinimized;

        public async Task<bool> TryEmbedOfficialLauncherAsync(int processId)
        {
            DetachOfficialLauncher(restoreAsStandalone: false);
            _embedCts?.Dispose();
            _embedCts = new CancellationTokenSource();
            var token = _embedCts.Token;
            var excludePid = Environment.ProcessId;

            nint hwnd;
            try
            {
                hwnd = await NativeWindowEmbed.WaitForMainWindowAsync(
                    [processId],
                    excludePid,
                    TimeSpan.FromSeconds(20),
                    token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (hwnd == 0)
            {
                return false;
            }

            EnsureOfficialLauncherHost();
            if (_officialLauncherHost == null || OfficialLauncherOverlay == null)
            {
                return false;
            }

            OfficialLauncherOverlay.Visibility = Visibility.Visible;
            OfficialLauncherOverlay.UpdateLayout();
            var hostHandle = await WaitForHostHandleAsync(token);
            if (hostHandle == 0)
            {
                OfficialLauncherOverlay.Visibility = Visibility.Hidden;
                return false;
            }

            _embeddedOfficialStyles = NativeWindowEmbed.CaptureStyles(hwnd);
            if (!NativeWindowEmbed.EmbedAsChild(hwnd, hostHandle))
            {
                _embeddedOfficialStyles = null;
                OfficialLauncherOverlay.Visibility = Visibility.Hidden;
                return false;
            }

            _embeddedOfficialHwnd = hwnd;
            _officialLauncherOverlayMinimized = false;
            _officialLauncherHost.Attach(hwnd);
            SyncOfficialLauncherOverlay();
            _officialLauncherHost.FitChild();
            return true;
        }

        public void HideOfficialLauncherOverlay()
        {
            if (!IsOfficialLauncherEmbedded)
            {
                return;
            }

            _officialLauncherOverlayMinimized = true;
            SyncOfficialLauncherOverlay();
            _launcherPage?.OnOfficialLauncherOverlayHidden();
        }

        public void ShowOfficialLauncherOverlay()
        {
            if (!IsOfficialLauncherEmbedded)
            {
                return;
            }

            _officialLauncherOverlayMinimized = false;
            SyncOfficialLauncherOverlay();
            _launcherPage?.SetOfficialLauncherStatus("Official SPT launcher is docked in this tab.");
        }

        public void DetachOfficialLauncher(bool restoreAsStandalone)
        {
            _embedCts?.Cancel();
            _officialLauncherOverlayMinimized = false;

            var hwnd = _embeddedOfficialHwnd;
            var styles = _embeddedOfficialStyles;
            _embeddedOfficialHwnd = 0;
            _embeddedOfficialStyles = null;
            _officialLauncherHost?.ClearChild();

            if (hwnd != 0)
            {
                NativeWindowEmbed.Detach(hwnd, styles, restoreAsStandalone);
            }

            if (OfficialLauncherOverlay != null)
            {
                OfficialLauncherOverlay.Visibility = Visibility.Hidden;
            }
        }

        internal void SyncOfficialLauncherOverlay()
        {
            if (OfficialLauncherOverlay == null)
            {
                return;
            }

            var show = IsOfficialLauncherEmbedded
                       && _currentTab == "launcher"
                       && !_officialLauncherOverlayMinimized;
            OfficialLauncherOverlay.Visibility = show ? Visibility.Visible : Visibility.Hidden;
            NativeWindowEmbed.SetVisible(_embeddedOfficialHwnd, show);
            if (show)
            {
                _officialLauncherHost?.FitChild();
            }
        }

        private async Task<nint> WaitForHostHandleAsync(CancellationToken token)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var handle = _officialLauncherHost?.Handle ?? 0;
                    if (handle != 0)
                    {
                        return handle;
                    }
                }
                catch
                {
                    // HwndHost is not created until it has been loaded.
                }

                OfficialLauncherOverlay?.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                await Task.Delay(50, token);
            }

            return 0;
        }

        private void EnsureOfficialLauncherHost()
        {
            if (_officialLauncherHost != null || OfficialLauncherHostSlot == null)
            {
                return;
            }

            _officialLauncherHost = new OfficialSptLauncherHost
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };
            OfficialLauncherHostSlot.Children.Add(_officialLauncherHost);
            OfficialLauncherHostSlot.SizeChanged += (_, _) => _officialLauncherHost?.FitChild();
        }

        private async void OfficialLauncherBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_launcherPage != null)
            {
                await _launcherPage.StopOfficialLauncherAsync();
            }
        }

        private void OfficialLauncherPopOutButton_Click(object sender, RoutedEventArgs e)
        {
            DetachOfficialLauncher(restoreAsStandalone: true);
            _launcherPage?.SetOfficialLauncherStatus(
                "Official SPT launcher opened in its own window.");
        }

        private async void OfficialLauncherStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_launcherPage != null)
            {
                await _launcherPage.StopOfficialLauncherAsync();
            }
        }
    }
}
