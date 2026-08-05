using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Shapes;
using SptLauncherWpf.Services;

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
                
                // Always apply the theme (even if same) to ensure it's properly set
                Console.WriteLine($"Applying theme: {themeName} (current: {_currentTheme})");
                
                if (themeName == "light")
                {
                    ApplyLightTheme();
                }
                else
                {
                    ApplyDarkTheme();
                }

                // Update current theme tracking BEFORE saving
                _currentTheme = themeName;

                // Save theme preference immediately
                SettingsService.Instance.Theme = themeName;
                SettingsService.Instance.SaveSettings();

                Console.WriteLine($"Theme {themeName} applied and saved successfully");

                // Force UI refresh
                ForceCompleteUIRefresh();

                // Notify theme change AFTER everything is applied
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(themeName));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to apply theme '{themeName}': {ex.Message}");
            }
        }

        private void ForceCompleteUIRefresh()
        {
            try
            {
                // Force refresh of all windows
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window != null)
                    {
                        // Force the window to refresh its resources
                        window.UpdateLayout();
                        
                        // Force refresh of all visual elements
                        RefreshVisualTree(window);
                        
                        // Force the window to re-render
                        window.InvalidateVisual();
                        window.InvalidateArrange();
                        window.InvalidateMeasure();
                    }
                }
                
                // Force application-level refresh
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // Force all windows to refresh
                    foreach (Window window in System.Windows.Application.Current.Windows)
                    {
                        if (window != null)
                        {
                            window.UpdateLayout();
                            window.InvalidateVisual();
                        }
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
                
                Console.WriteLine("Complete UI refresh completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh UI: {ex.Message}");
            }
        }

        private void RefreshVisualTree(DependencyObject parent)
        {
            try
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is FrameworkElement element)
                    {
                        // Force the element to re-evaluate its resources
                        element.UpdateLayout();
                        
                        // Force re-evaluation of StaticResource bindings for common properties
                        if (child is System.Windows.Controls.Control control)
                        {
                            control.InvalidateProperty(System.Windows.Controls.Control.BackgroundProperty);
                            control.InvalidateProperty(System.Windows.Controls.Control.ForegroundProperty);
                            control.InvalidateProperty(System.Windows.Controls.Control.BorderBrushProperty);
                        }
                        else if (child is Border border)
                        {
                            border.InvalidateProperty(Border.BackgroundProperty);
                            border.InvalidateProperty(Border.BorderBrushProperty);
                        }
                        else if (child is TextBlock textBlock)
                        {
                            textBlock.InvalidateProperty(TextBlock.ForegroundProperty);
                        }
                        
                        // Force visual refresh
                        element.InvalidateVisual();
                        element.InvalidateArrange();
                        element.InvalidateMeasure();
                    }
                    RefreshVisualTree(child);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh visual tree: {ex.Message}");
            }
        }

        private void ApplyLightTheme()
        {
            Console.WriteLine("Applying light theme colors...");
            
            // Light theme colors - make them very different from dark theme
            System.Windows.Application.Current.Resources["BackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)); // Pure white
            System.Windows.Application.Current.Resources["SurfaceColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)); // Light gray
            System.Windows.Application.Current.Resources["PrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 100, 200)); // Bright blue
            System.Windows.Application.Current.Resources["TextPrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0)); // Pure black
            System.Windows.Application.Current.Resources["TextSecondaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)); // Dark gray
            System.Windows.Application.Current.Resources["BorderColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)); // Light gray border
            System.Windows.Application.Current.Resources["CardBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)); // Very light gray
            System.Windows.Application.Current.Resources["HoverColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)); // Medium gray
            System.Windows.Application.Current.Resources["ChromeBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246));
            System.Windows.Application.Current.Resources["ChromeTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            System.Windows.Application.Current.Resources["ChromeMutedTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
            System.Windows.Application.Current.Resources["StatusSuccessColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            System.Windows.Application.Current.Resources["StatusWarningColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
            System.Windows.Application.Current.Resources["StatusErrorColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
            System.Windows.Application.Current.Resources["StatusInfoColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
            
            // Force resource refresh
            System.Windows.Application.Current.Resources.MergedDictionaries.Clear();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary());
            
            // Force a complete UI refresh
            ForceCompleteUIRefresh();
            
            Console.WriteLine("Light theme colors applied");
        }

        private void ApplyDarkTheme()
        {
            Console.WriteLine("Applying dark theme colors...");
            
            // Dark theme colors - replace entire brushes
            System.Windows.Application.Current.Resources["BackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(17, 24, 39));
            System.Windows.Application.Current.Resources["SurfaceColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            System.Windows.Application.Current.Resources["PrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
            System.Windows.Application.Current.Resources["TextPrimaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 250, 251));
            System.Windows.Application.Current.Resources["TextSecondaryColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175));
            System.Windows.Application.Current.Resources["BorderColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81));
            System.Windows.Application.Current.Resources["CardBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 41, 55));
            System.Windows.Application.Current.Resources["HoverColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 65, 81));
            System.Windows.Application.Current.Resources["ChromeBackgroundColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 23, 42));
            System.Windows.Application.Current.Resources["ChromeTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));
            System.Windows.Application.Current.Resources["ChromeMutedTextColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
            System.Windows.Application.Current.Resources["StatusSuccessColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            System.Windows.Application.Current.Resources["StatusWarningColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            System.Windows.Application.Current.Resources["StatusErrorColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            System.Windows.Application.Current.Resources["StatusInfoColor"] = new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
            
            // Force resource refresh
            System.Windows.Application.Current.Resources.MergedDictionaries.Clear();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary());
            
            // Force a complete UI refresh
            ForceCompleteUIRefresh();
            
            Console.WriteLine("Dark theme colors applied");
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
