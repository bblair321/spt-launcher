using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace SptLauncherWpf.Services
{
    public class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SPT-Launcher",
            "settings.json"
        );

        private static SettingsService? _instance;
        public static SettingsService Instance => _instance ??= new SettingsService();

        private Dictionary<string, object> _settings = new();

        private SettingsService()
        {
            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public T GetValue<T>(string key, T defaultValue = default!)
        {
            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is JsonElement element)
                    {
                        return JsonSerializer.Deserialize<T>(element.GetRawText()) ?? defaultValue;
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        public void SetValue<T>(string key, T value)
        {
            _settings[key] = value!;
        }

        // Convenience properties
        public string LauncherPath
        {
            get => GetValue("LauncherPath", "");
            set => SetValue("LauncherPath", value);
        }

        public string ServersJson
        {
            get => GetValue("ServersJson", "");
            set => SetValue("ServersJson", value);
        }

        public string AuthToken
        {
            get => GetValue("AuthToken", "");
            set => SetValue("AuthToken", value);
        }

        public string UserName
        {
            get => GetValue("UserName", "");
            set => SetValue("UserName", value);
        }

        public bool AutoStart
        {
            get => GetValue("AutoStart", false);
            set => SetValue("AutoStart", value);
        }

        public bool MinimizeToTray
        {
            get => GetValue("MinimizeToTray", false);
            set => SetValue("MinimizeToTray", value);
        }

        public bool AutoUpdate
        {
            get => GetValue("AutoUpdate", true);
            set => SetValue("AutoUpdate", value);
        }

        public string RequiredModsPackUrl
        {
            get => GetValue("RequiredModsPackUrl", "");
            set => SetValue("RequiredModsPackUrl", value);
        }

        /// <summary>
        /// Game server host or host:port. Used to derive https://{host}:6969/mod-pack when PackUrl is empty.
        /// </summary>
        public string RequiredModsServerHost
        {
            get => GetValue("RequiredModsServerHost", "");
            set => SetValue("RequiredModsServerHost", value);
        }

        public string RequiredModsAgentToken
        {
            get => GetValue("RequiredModsAgentToken", "");
            set => SetValue("RequiredModsAgentToken", value);
        }

        public bool AutoCheckRequiredModsOnLaunch
        {
            get => GetValue("AutoCheckRequiredModsOnLaunch", true);
            set => SetValue("AutoCheckRequiredModsOnLaunch", value);
        }

        public string Theme
        {
            get => GetValue("Theme", "dark");
            set => SetValue("Theme", value);
        }

        public string DefaultLauncherPath
        {
            get => GetValue("DefaultLauncherPath", "");
            set => SetValue("DefaultLauncherPath", value);
        }

        public bool AutoSaveLogs
        {
            get => GetValue("AutoSaveLogs", false);
            set => SetValue("AutoSaveLogs", value);
        }

        public int LogRetentionDays
        {
            get => GetValue("LogRetentionDays", 30);
            set => SetValue("LogRetentionDays", value);
        }

        public string DefaultPort
        {
            get => GetValue("DefaultPort", "6969");
            set => SetValue("DefaultPort", value);
        }

        public bool AutoStartServers
        {
            get => GetValue("AutoStartServers", false);
            set => SetValue("AutoStartServers", value);
        }

        public int ServerTimeoutSeconds
        {
            get => GetValue("ServerTimeoutSeconds", 30);
            set => SetValue("ServerTimeoutSeconds", value);
        }

        public bool DebugMode
        {
            get => GetValue("DebugMode", false);
            set => SetValue("DebugMode", value);
        }

        public bool VerboseLogging
        {
            get => GetValue("VerboseLogging", false);
            set => SetValue("VerboseLogging", value);
        }

        public string LastSptBackupPath
        {
            get => GetValue("LastSptBackupPath", "");
            set => SetValue("LastSptBackupPath", value);
        }

        /// <summary>
        /// Target launcher version written before replace-in-place; cleared after restart handling.
        /// </summary>
        public string PendingSelfUpdateVersion
        {
            get => GetValue("PendingSelfUpdateVersion", "");
            set => SetValue("PendingSelfUpdateVersion", value);
        }

        public bool FirstRunWizardDismissed
        {
            get => GetValue("FirstRunWizardDismissed", false);
            set => SetValue("FirstRunWizardDismissed", value);
        }

        public double WindowLeft
        {
            get => GetValue("WindowLeft", double.NaN);
            set => SetValue("WindowLeft", value);
        }

        public double WindowTop
        {
            get => GetValue("WindowTop", double.NaN);
            set => SetValue("WindowTop", value);
        }

        public double WindowWidth
        {
            get => GetValue("WindowWidth", 0d);
            set => SetValue("WindowWidth", value);
        }

        public double WindowHeight
        {
            get => GetValue("WindowHeight", 0d);
            set => SetValue("WindowHeight", value);
        }

        public bool WindowMaximized
        {
            get => GetValue("WindowMaximized", false);
            set => SetValue("WindowMaximized", value);
        }
    }
}
