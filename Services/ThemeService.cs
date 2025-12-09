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

        private ThemeService()
        {
            // Apply saved theme on startup
            ApplyTheme(SettingsService.Instance.Theme);
        }

        public void ApplyTheme(string themeName)
        {
            try
            {
                Console.WriteLine($"Applying theme: {themeName}");
                
                switch (themeName.ToLower())
                {
                    case "light":
                        ApplyLightTheme();
                        break;
                    case "dark":
                        ApplyDarkTheme();
                        break;
                    case "system":
                        ApplySystemTheme();
                        break;
                    default:
                        ApplyDarkTheme();
                        break;
                }

                // Save theme preference
                SettingsService.Instance.Theme = themeName;
                SettingsService.Instance.SaveSettings();

                Console.WriteLine($"Theme {themeName} applied successfully");

                // Force UI refresh
                ForceCompleteUIRefresh();

                // Notify theme change
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
                foreach (Window window in Application.Current.Windows)
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Force all windows to refresh
                    foreach (Window window in Application.Current.Windows)
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
                        if (child is Control control)
                        {
                            control.InvalidateProperty(Control.BackgroundProperty);
                            control.InvalidateProperty(Control.ForegroundProperty);
                            control.InvalidateProperty(Control.BorderBrushProperty);
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
            Application.Current.Resources["BackgroundColor"] = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // Pure white
            Application.Current.Resources["SurfaceColor"] = new SolidColorBrush(Color.FromRgb(240, 240, 240)); // Light gray
            Application.Current.Resources["PrimaryColor"] = new SolidColorBrush(Color.FromRgb(0, 100, 200)); // Bright blue
            Application.Current.Resources["TextPrimaryColor"] = new SolidColorBrush(Color.FromRgb(0, 0, 0)); // Pure black
            Application.Current.Resources["TextSecondaryColor"] = new SolidColorBrush(Color.FromRgb(100, 100, 100)); // Dark gray
            Application.Current.Resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // Light gray border
            Application.Current.Resources["CardBackgroundColor"] = new SolidColorBrush(Color.FromRgb(250, 250, 250)); // Very light gray
            Application.Current.Resources["HoverColor"] = new SolidColorBrush(Color.FromRgb(220, 220, 220)); // Medium gray
            
            // Force resource refresh
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary());
            
            // Force a complete UI refresh
            ForceCompleteUIRefresh();
            
            Console.WriteLine("Light theme colors applied");
        }

        private void ApplyDarkTheme()
        {
            Console.WriteLine("Applying dark theme colors...");
            
            // Dark theme colors - replace entire brushes
            Application.Current.Resources["BackgroundColor"] = new SolidColorBrush(Color.FromRgb(17, 24, 39));
            Application.Current.Resources["SurfaceColor"] = new SolidColorBrush(Color.FromRgb(31, 41, 55));
            Application.Current.Resources["PrimaryColor"] = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            Application.Current.Resources["TextPrimaryColor"] = new SolidColorBrush(Color.FromRgb(249, 250, 251));
            Application.Current.Resources["TextSecondaryColor"] = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            Application.Current.Resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(55, 65, 81));
            Application.Current.Resources["CardBackgroundColor"] = new SolidColorBrush(Color.FromRgb(31, 41, 55));
            Application.Current.Resources["HoverColor"] = new SolidColorBrush(Color.FromRgb(55, 65, 81));
            
            // Force resource refresh
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary());
            
            // Force a complete UI refresh
            ForceCompleteUIRefresh();
            
            Console.WriteLine("Dark theme colors applied");
        }

        private void ApplySystemTheme()
        {
            // Check system theme preference
            var isDarkMode = IsSystemDarkMode();
            if (isDarkMode)
            {
                ApplyDarkTheme();
            }
            else
            {
                ApplyLightTheme();
            }
        }

        private bool IsSystemDarkMode()
        {
            try
            {
                // Check Windows theme preference
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int intValue && intValue == 0;
            }
            catch
            {
                // Default to dark theme if we can't detect system preference
                return true;
            }
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
