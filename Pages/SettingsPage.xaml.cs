using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load general settings
            AutoUpdateCheckBox.IsChecked = SettingsService.Instance.AutoUpdate;
            RequiredModsServerHostTextBox.Text = SettingsService.Instance.RequiredModsServerHost ?? "";
            RequiredModsPackUrlTextBox.Text = SettingsService.Instance.RequiredModsPackUrl ?? "";
            AutoCheckRequiredModsCheckBox.IsChecked = SettingsService.Instance.AutoCheckRequiredModsOnLaunch;
        }

        private void SaveSettings()
        {
            try
            {
                // Save general settings
                var autoUpdateEnabled = AutoUpdateCheckBox.IsChecked ?? false;
                SettingsService.Instance.AutoUpdate = autoUpdateEnabled;

                SettingsService.Instance.RequiredModsServerHost =
                    RequiredModsServerHostTextBox.Text?.Trim() ?? "";
                SettingsService.Instance.RequiredModsPackUrl =
                    RequiredModsPackUrlTextBox.Text?.Trim() ?? "";
                SettingsService.Instance.AutoCheckRequiredModsOnLaunch =
                    AutoCheckRequiredModsCheckBox.IsChecked ?? true;
                
                // Start or stop periodic update checking based on setting
                if (autoUpdateEnabled)
                {
                    UpdateService.Instance.StartPeriodicCheck();
                }
                else
                {
                    UpdateService.Instance.StopPeriodicCheck();
                }
                
                SettingsService.Instance.SaveSettings();
                System.Windows.MessageBox.Show("Settings saved successfully!", "Success", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CheckForUpdatesButton.IsEnabled = false;
                CheckForUpdatesButton.Content = "Checking...";
                
                // Force check regardless of auto-update setting
                var updateInfo = await UpdateService.Instance.CheckForUpdatesAsync(forceCheck: true);
                
                if (updateInfo != null)
                {
                    // Update is available - the banner will be shown automatically via the event
                    System.Windows.MessageBox.Show(
                        $"A new version ({updateInfo.Version}) is available!\n\n" +
                        $"Current version: {UpdateService.Instance.GetCurrentVersion()}\n\n" +
                        $"The update notification banner has been displayed at the top of the window.",
                        "Update Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("You are running the latest version.", 
                        "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to check for updates: {ex.Message}", 
                    "Update Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CheckForUpdatesButton.IsEnabled = true;
                CheckForUpdatesButton.Content = "Check for Updates Now";
            }
        }


        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Are you sure you want to reset all settings to their default values?", 
                                       "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Reset to default values
                SettingsService.Instance.AutoUpdate = true;
                SettingsService.Instance.Theme = "dark";
                SettingsService.Instance.DebugMode = false;
                SettingsService.Instance.VerboseLogging = false;
                SettingsService.Instance.SaveSettings();
                
                LoadSettings();
                System.Windows.MessageBox.Show("Settings have been reset to defaults.", "Reset Complete", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Settings",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = "spt-launcher-settings.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                var settings = new
                {
                    AutoUpdate = SettingsService.Instance.AutoUpdate,
                    Theme = SettingsService.Instance.Theme,
                    DebugMode = SettingsService.Instance.DebugMode,
                    VerboseLogging = SettingsService.Instance.VerboseLogging
                };

                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveFileDialog.FileName, json);
                    
                    System.Windows.MessageBox.Show("Settings exported successfully!", "Export Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to export settings: {ex.Message}", "Export Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Settings",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(openFileDialog.FileName);
                    var settings = JsonSerializer.Deserialize<JsonElement>(json);

                    // Import settings
                    if (settings.TryGetProperty("AutoUpdate", out var autoUpdate))
                        SettingsService.Instance.AutoUpdate = autoUpdate.GetBoolean();
                    
                    if (settings.TryGetProperty("Theme", out var theme))
                        SettingsService.Instance.Theme = theme.GetString() ?? "dark";
                    
                    
                    
                    if (settings.TryGetProperty("DebugMode", out var debugMode))
                        SettingsService.Instance.DebugMode = debugMode.GetBoolean();
                    
                    if (settings.TryGetProperty("VerboseLogging", out var verboseLogging))
                        SettingsService.Instance.VerboseLogging = verboseLogging.GetBoolean();

                    SettingsService.Instance.SaveSettings();
                    LoadSettings();
                    
                    System.Windows.MessageBox.Show("Settings imported successfully!", "Import Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to import settings: {ex.Message}", "Import Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings(); // Reload original settings
        }

    }
}
