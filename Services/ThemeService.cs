using System;
using System.Windows;
using System.Windows.Media;

namespace SptLauncherWpf.Services
{
    public class ThemeService
    {
        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
        
        // Store current theme to avoid reading stale values from SettingsService
        private string _currentTheme = "dark";

        public string CurrentTheme => _currentTheme;

        private ThemeService()
        {
            // Initialize current theme from settings, but don't apply yet
            // Theme will be applied explicitly by App.xaml.cs on startup
            var savedTheme = SettingsService.Instance.Theme;
            
            // Normalize theme - convert old "system" theme to "dark", ensure only light/dark
            if (savedTheme != "light" && savedTheme != "dark")
            {
                savedTheme = "dark";
                SettingsService.Instance.Theme = savedTheme;
                SettingsService.Instance.SaveSettings();
            }
            
            _currentTheme = savedTheme;
        }

        public void ApplyTheme(string themeName)
        {
            try
            {
                // Normalize theme name - only support light or dark
                themeName = themeName.ToLower();
                if (themeName != "light" && themeName != "dark")
                {
                    themeName = "dark"; // Default to dark
                }

                if (themeName == "light")
                {
                    ApplyLightTheme();
                }
                else
                {
                    ApplyDarkTheme();
                }

                _currentTheme = themeName;

                SettingsService.Instance.Theme = themeName;
                SettingsService.Instance.SaveSettings();

                // DynamicResource bindings pick up brush replacements; listeners refresh chrome.
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(themeName));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply theme '{themeName}': {ex.Message}");
            }
        }

        private void ApplyLightTheme()
        {
            var resources = System.Windows.Application.Current.Resources;
            resources["BackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            resources["SurfaceColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            resources["PrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 100, 200));
            resources["TextPrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0));
            resources["TextSecondaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));
            resources["BorderColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            resources["CardBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
            resources["HoverColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220));
            resources["ChromeBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
            resources["ChromeTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            resources["ChromeMutedTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            resources["StatusSuccessColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            resources["StatusWarningColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
            resources["StatusErrorColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
            resources["StatusInfoColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
        }

        private void ApplyDarkTheme()
        {
            var resources = System.Windows.Application.Current.Resources;
            resources["BackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
            resources["SurfaceColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            resources["PrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
            resources["TextPrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 250, 251));
            resources["TextSecondaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            resources["BorderColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81));
            resources["CardBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            resources["HoverColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81));
            resources["ChromeBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
            resources["ChromeTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));
            resources["ChromeMutedTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            resources["StatusSuccessColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            resources["StatusWarningColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            resources["StatusErrorColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            resources["StatusInfoColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        }

    }

    public class ThemeChangedEventArgs : EventArgs
    {
        public string ThemeName { get; }

        public ThemeChangedEventArgs(string themeName)
        {
            ThemeName = themeName;
        }
    }
}
