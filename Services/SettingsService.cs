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

        public bool FikaEnabled
        {
            get => GetValue("FikaEnabled", false);
            set => SetValue("FikaEnabled", value);
        }

        public string FikaIpAddress
        {
            get => GetValue("FikaIpAddress", "127.0.0.1");
            set => SetValue("FikaIpAddress", value);
        }
    }
}
