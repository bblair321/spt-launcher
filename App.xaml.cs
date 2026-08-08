using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SptLauncherWpf.Services;

namespace SptLauncherWpf
{
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// When true, skip SPT auto-detect and show the first-run walkthrough.
        /// Enabled with --force-first-run (used by scripts/Test-AsNewUser.ps1).
        /// Cleared once the user finishes or skips the walkthrough.
        /// </summary>
        public static bool ForceFirstRun { get; private set; }

        public static void ClearForceFirstRun() => ForceFirstRun = false;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // P/Invoke for unblocking files
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        /// <summary>
        /// Unblocks the current application executable if it was downloaded from the internet
        /// </summary>
        private static void UnblockCurrentExecutable()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return;
                }

                string zoneIdentifier = $"{exePath}:Zone.Identifier";
                bool unblocked = DeleteFile(zoneIdentifier);
                System.Diagnostics.Debug.WriteLine($"[App] Unblocked current executable: {unblocked}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Failed to unblock current executable: {ex.Message}");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ForceFirstRun = e.Args.Any(a =>
                string.Equals(a, "--force-first-run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-force-first-run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/force-first-run", StringComparison.OrdinalIgnoreCase));

            base.OnStartup(e);
            
            // Unblock the current executable if it was downloaded from the internet
            // This is important because a blocked app may not be able to launch other processes
            UnblockCurrentExecutable();
            
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
                
                // Start periodic update checking if enabled
                if (SettingsService.Instance.AutoUpdate)
                {
                    UpdateService.Instance.StartPeriodicCheck();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start application: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }
        
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show($"Unhandled exception: {e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}", 
                "Unhandled Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
        
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.Windows.MessageBox.Show($"Fatal exception: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", 
                    "Fatal Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
