using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace SptLauncherWpf.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public DateTime ReleaseDate { get; set; }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("body")]
        public string Body { get; set; } = "";
        
        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }
        
        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class UpdateService
    {
        private static UpdateService? _instance;
        public static UpdateService Instance => _instance ??= new UpdateService();

        private const string UpdateCheckUrl = "https://api.github.com/repos/bblair321/spt-launcher/releases/latest";
        
        private HttpClient? _httpClient;
        private System.Windows.Threading.DispatcherTimer? _checkTimer;

        public event EventHandler<UpdateInfo>? UpdateAvailable;
        public event EventHandler? UpdateCheckCompleted;

        private UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SPT-Launcher-WPF");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public Version GetCurrentVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version ?? new Version(3, 0, 0, 0);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false)
        {
            try
            {
                // Only skip if auto-update is disabled AND this is not a forced check
                if (!forceCheck && !SettingsService.Instance.AutoUpdate)
                {
                    return null;
                }

                Console.WriteLine("Checking for updates from GitHub...");
                
                var response = await _httpClient!.GetStringAsync(UpdateCheckUrl);
                var release = JsonSerializer.Deserialize<GitHubRelease>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                });

                if (release == null)
                {
                    Console.WriteLine("Failed to parse release information");
                    UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                // Extract version from tag (remove 'v' prefix if present)
                var remoteVersion = release.TagName.TrimStart('v', 'V');
                var currentVersion = GetCurrentVersion();

                Console.WriteLine($"Current version: {currentVersion}, Remote version: {remoteVersion}");

                // Check if remote version is newer
                // Version comparison works correctly between 3-part (3.0.0) and 4-part (3.0.0.0) versions
                if (!IsNewerVersion(remoteVersion, currentVersion))
                {
                    Console.WriteLine("Already on latest version");
                    UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                // Find the installer/exe asset
                var installerAsset = release.Assets.FirstOrDefault(a => 
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));

                if (installerAsset == null)
                {
                    // Fallback to first asset if no installer found
                    installerAsset = release.Assets.FirstOrDefault();
                }

                if (installerAsset == null)
                {
                    Console.WriteLine("No download asset found in release");
                    UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                var updateInfo = new UpdateInfo
                {
                    Version = remoteVersion,
                    DownloadUrl = installerAsset.BrowserDownloadUrl,
                    ReleaseNotes = release.Body,
                    ReleaseDate = release.PublishedAt
                };

                Console.WriteLine($"Update available: {updateInfo.Version}");
                
                // Notify listeners about the update
                UpdateAvailable?.Invoke(this, updateInfo);
                
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Network error checking for updates: {ex.Message}");
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to check for updates: {ex.Message}");
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return null;
            }
        }

        public bool IsNewerVersion(string remoteVersion, Version currentVersion)
        {
            try
            {
                // Normalize versions - handle both 3-part (3.0.0) and 4-part (3.0.0.0) versions
                // GitHub typically uses 3-part versions, but .NET Version can handle both
                if (Version.TryParse(remoteVersion, out var remoteVer))
                {
                    // Compare versions - Version comparison works correctly between 3-part and 4-part
                    // e.g., Version("3.0.1") > Version("3.0.0.0") returns true
                    return remoteVer > currentVersion;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void StartPeriodicCheck()
        {
            if (!SettingsService.Instance.AutoUpdate)
            {
                return;
            }

            // Stop existing timer if any
            StopPeriodicCheck();

            // Check immediately on startup (after a short delay)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(5000); // Wait 5 seconds after startup
                await CheckForUpdatesAsync();
            }), System.Windows.Threading.DispatcherPriority.Background);

            // Then check every 24 hours
            _checkTimer = new System.Windows.Threading.DispatcherTimer();
            _checkTimer.Interval = TimeSpan.FromHours(24);
            _checkTimer.Tick += async (s, e) =>
            {
                if (SettingsService.Instance.AutoUpdate)
                {
                    await CheckForUpdatesAsync();
                }
            };
            _checkTimer.Start();
        }

        public void StopPeriodicCheck()
        {
            _checkTimer?.Stop();
            _checkTimer = null;
        }

        public async Task<bool> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
        {
            try
            {
                if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    return false;
                }

                var tempPath = Path.Combine(Path.GetTempPath(), $"SPT-Launcher-Update-{updateInfo.Version}.exe");
                
                using (var response = await _httpClient!.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;

                            if (totalBytes > 0 && progress != null)
                            {
                                var percent = (double)downloadedBytes / totalBytes * 100;
                                progress.Report(percent);
                            }
                        }
                    }
                }

                // Launch the installer
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download update: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            StopPeriodicCheck();
            _httpClient?.Dispose();
        }
    }
}

