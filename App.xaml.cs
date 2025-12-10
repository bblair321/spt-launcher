using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SptLauncherWpf.Services;

namespace SptLauncherWpf
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Set DPI awareness for better text rendering
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Ignore if DPI awareness setting fails
            }
            
            // Handle unhandled exceptions
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            try
            {
                // Load and apply saved theme BEFORE creating main window
                var savedTheme = SettingsService.Instance.Theme;
                if (string.IsNullOrEmpty(savedTheme) || (savedTheme != "light" && savedTheme != "dark"))
                {
                    savedTheme = "dark"; // Default to dark
                }
                ThemeService.Instance.ApplyTheme(savedTheme);
                
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
        
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Unhandled exception: {e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}", 
                "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
        
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Fatal exception: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Fatal Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
