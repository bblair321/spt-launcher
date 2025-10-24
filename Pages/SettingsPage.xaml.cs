using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
            AutoStartCheckBox.IsChecked = SettingsService.Instance.AutoStart;
            MinimizeToTrayCheckBox.IsChecked = SettingsService.Instance.MinimizeToTray;
            AutoUpdateCheckBox.IsChecked = SettingsService.Instance.AutoUpdate;
            
            // Set theme
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == SettingsService.Instance.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }

            // Load launcher settings
            DefaultLauncherPathTextBox.Text = SettingsService.Instance.DefaultLauncherPath ?? "D:\\SPT\\SPT.Launcher.exe";
            AutoSaveLogsCheckBox.IsChecked = SettingsService.Instance.AutoSaveLogs;
            LogRetentionTextBox.Text = SettingsService.Instance.LogRetentionDays.ToString();

            // Load server settings
            DefaultPortTextBox.Text = SettingsService.Instance.DefaultPort;
            AutoStartServersCheckBox.IsChecked = SettingsService.Instance.AutoStartServers;
            ServerTimeoutTextBox.Text = SettingsService.Instance.ServerTimeoutSeconds.ToString();

        }

        private void SaveSettings()
        {
            try
            {
                // Save general settings
                SettingsService.Instance.AutoStart = AutoStartCheckBox.IsChecked ?? false;
                SettingsService.Instance.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
                SettingsService.Instance.AutoUpdate = AutoUpdateCheckBox.IsChecked ?? false;
                
                if (ThemeComboBox.SelectedItem is ComboBoxItem selectedTheme)
                {
                    SettingsService.Instance.Theme = selectedTheme.Tag?.ToString() ?? "dark";
                }

                // Save launcher settings
                SettingsService.Instance.DefaultLauncherPath = DefaultLauncherPathTextBox.Text;
                SettingsService.Instance.AutoSaveLogs = AutoSaveLogsCheckBox.IsChecked ?? false;
                
                if (int.TryParse(LogRetentionTextBox.Text, out int logRetention))
                {
                    SettingsService.Instance.LogRetentionDays = logRetention;
                }

                // Save server settings
                SettingsService.Instance.DefaultPort = DefaultPortTextBox.Text;
                SettingsService.Instance.AutoStartServers = AutoStartServersCheckBox.IsChecked ?? false;
                
                if (int.TryParse(ServerTimeoutTextBox.Text, out int timeout))
                {
                    SettingsService.Instance.ServerTimeoutSeconds = timeout;
                }


                SettingsService.Instance.SaveSettings();
                MessageBox.Show("Settings saved successfully!", "Success", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseDefaultPathButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Default Launcher Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                InitialDirectory = Path.GetDirectoryName(DefaultLauncherPathTextBox.Text) ?? "C:\\"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                DefaultLauncherPathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all settings to their default values?", 
                                       "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Reset to default values
                SettingsService.Instance.AutoStart = false;
                SettingsService.Instance.MinimizeToTray = false;
                SettingsService.Instance.AutoUpdate = true;
                SettingsService.Instance.Theme = "dark";
                SettingsService.Instance.DefaultLauncherPath = "";
                SettingsService.Instance.AutoSaveLogs = false;
                SettingsService.Instance.LogRetentionDays = 30;
                SettingsService.Instance.DefaultPort = "6969";
                SettingsService.Instance.AutoStartServers = false;
                SettingsService.Instance.ServerTimeoutSeconds = 30;
                SettingsService.Instance.DebugMode = false;
                SettingsService.Instance.VerboseLogging = false;
                SettingsService.Instance.SaveSettings();
                
                LoadSettings();
                MessageBox.Show("Settings have been reset to defaults.", "Reset Complete", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
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
                    AutoStart = SettingsService.Instance.AutoStart,
                    MinimizeToTray = SettingsService.Instance.MinimizeToTray,
                    AutoUpdate = SettingsService.Instance.AutoUpdate,
                    Theme = SettingsService.Instance.Theme,
                    DefaultLauncherPath = SettingsService.Instance.DefaultLauncherPath,
                    AutoSaveLogs = SettingsService.Instance.AutoSaveLogs,
                    LogRetentionDays = SettingsService.Instance.LogRetentionDays,
                    DefaultPort = SettingsService.Instance.DefaultPort,
                    AutoStartServers = SettingsService.Instance.AutoStartServers,
                    ServerTimeoutSeconds = SettingsService.Instance.ServerTimeoutSeconds,
                    DebugMode = SettingsService.Instance.DebugMode,
                    VerboseLogging = SettingsService.Instance.VerboseLogging
                };

                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(saveFileDialog.FileName, json);
                    
                    MessageBox.Show("Settings exported successfully!", "Export Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export settings: {ex.Message}", "Export Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
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
                    if (settings.TryGetProperty("AutoStart", out var autoStart))
                        SettingsService.Instance.AutoStart = autoStart.GetBoolean();
                    
                    if (settings.TryGetProperty("MinimizeToTray", out var minimizeToTray))
                        SettingsService.Instance.MinimizeToTray = minimizeToTray.GetBoolean();
                    
                    if (settings.TryGetProperty("AutoUpdate", out var autoUpdate))
                        SettingsService.Instance.AutoUpdate = autoUpdate.GetBoolean();
                    
                    if (settings.TryGetProperty("Theme", out var theme))
                        SettingsService.Instance.Theme = theme.GetString() ?? "dark";
                    
                    if (settings.TryGetProperty("DefaultLauncherPath", out var defaultPath))
                        SettingsService.Instance.DefaultLauncherPath = defaultPath.GetString() ?? "";
                    
                    if (settings.TryGetProperty("AutoSaveLogs", out var autoSaveLogs))
                        SettingsService.Instance.AutoSaveLogs = autoSaveLogs.GetBoolean();
                    
                    if (settings.TryGetProperty("LogRetentionDays", out var logRetention))
                        SettingsService.Instance.LogRetentionDays = logRetention.GetInt32();
                    
                    if (settings.TryGetProperty("DefaultPort", out var defaultPort))
                        SettingsService.Instance.DefaultPort = defaultPort.GetString() ?? "6969";
                    
                    if (settings.TryGetProperty("AutoStartServers", out var autoStartServers))
                        SettingsService.Instance.AutoStartServers = autoStartServers.GetBoolean();
                    
                    if (settings.TryGetProperty("ServerTimeoutSeconds", out var serverTimeout))
                        SettingsService.Instance.ServerTimeoutSeconds = serverTimeout.GetInt32();
                    
                    if (settings.TryGetProperty("DebugMode", out var debugMode))
                        SettingsService.Instance.DebugMode = debugMode.GetBoolean();
                    
                    if (settings.TryGetProperty("VerboseLogging", out var verboseLogging))
                        SettingsService.Instance.VerboseLogging = verboseLogging.GetBoolean();

                    SettingsService.Instance.SaveSettings();
                    LoadSettings();
                    
                    MessageBox.Show("Settings imported successfully!", "Import Complete", 
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import settings: {ex.Message}", "Import Error", 
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
