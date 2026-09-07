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
        private const string LatestReleasePageUrl = "https://github.com/bblair321/spt-launcher/releases/latest";
        private const string ExeAssetName = "SPTLauncher.exe";

        private readonly HttpClient _httpClient;
        private readonly HttpClient _pageClient;
        private readonly HttpClient _downloadClient;
        private System.Windows.Threading.DispatcherTimer? _checkTimer;

        public event EventHandler<UpdateInfo>? UpdateAvailable;
        public event EventHandler? UpdateCheckCompleted;

        /// <summary>Last update-check failure message, or null when the check completed cleanly.</summary>
        public string? LastCheckError { get; private set; }

        /// <summary>Last self-update download/apply failure, or null when the last attempt succeeded.</summary>
        public string? LastDownloadError { get; private set; }

        private UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SPT-Launcher-WPF");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);

            // Binary downloads must not use the GitHub API Accept header or a 15s timeout.
            _downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _downloadClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SPT-Launcher-WPF");

            // Website latest-release redirect is not the 60/hour REST API quota.
            _pageClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _pageClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SPT-Launcher-WPF");
            _pageClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html");
            _pageClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public Version GetCurrentVersion()
        {
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileVersion) &&
                        Version.TryParse(info.FileVersion, out var fileVersion))
                    {
                        return fileVersion;
                    }
                }
            }
            catch
            {
                // fall through to assembly version
            }

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version ?? new Version(3, 0, 0, 0);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync(bool forceCheck = false)
        {
            LastCheckError = null;
            try
            {
                // Only skip if auto-update is disabled AND this is not a forced check
                if (!forceCheck && !SettingsService.Instance.AutoUpdate)
                {
                    return null;
                }

                Console.WriteLine("Checking for updates from GitHub...");

                var (info, upToDate, error) = await TryCheckViaApiAsync();
                if (error != null)
                {
                    Console.WriteLine($"{error} Falling back to GitHub releases page…");
                    (info, upToDate, error) = await TryCheckViaReleasePageAsync();
                }

                if (error != null)
                {
                    LastCheckError = error;
                    Console.WriteLine(LastCheckError);
                    UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                if (upToDate || info == null)
                {
                    Console.WriteLine("Already on latest version");
                    UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                Console.WriteLine($"Update available: {info.Version}");
                UpdateAvailable?.Invoke(this, info);
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return info;
            }
            catch (HttpRequestException ex)
            {
                LastCheckError = $"Network error checking for updates: {ex.Message}";
                Console.WriteLine(LastCheckError);
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                LastCheckError = $"Timed out checking for updates: {ex.Message}";
                Console.WriteLine(LastCheckError);
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return null;
            }
            catch (Exception ex)
            {
                LastCheckError = $"Failed to check for updates: {ex.Message}";
                Console.WriteLine(LastCheckError);
                UpdateCheckCompleted?.Invoke(this, EventArgs.Empty);
                return null;
            }
        }

        private async Task<(UpdateInfo? Info, bool UpToDate, string? Error)> TryCheckViaApiAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync(UpdateCheckUrl);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return (null, false,
                        $"GitHub update check failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
                }

                var release = JsonSerializer.Deserialize<GitHubRelease>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    return (null, false, "Failed to parse GitHub release information.");
                }

                var remoteVersion = release.TagName.TrimStart('v', 'V');
                Console.WriteLine($"Current version: {GetCurrentVersion()}, Remote version: {remoteVersion}");
                if (!IsNewerVersion(remoteVersion, GetCurrentVersion()))
                {
                    return (null, true, null);
                }

                var installerAsset = release.Assets.FirstOrDefault(a =>
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));

                installerAsset ??= release.Assets.FirstOrDefault();
                if (installerAsset == null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl))
                {
                    return (null, false, "No downloadable .exe asset found on the latest GitHub release.");
                }

                return (new UpdateInfo
                {
                    Version = remoteVersion,
                    DownloadUrl = installerAsset.BrowserDownloadUrl,
                    ReleaseNotes = release.Body,
                    ReleaseDate = release.PublishedAt
                }, false, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (null, false, ex.Message);
            }
        }

        private async Task<(UpdateInfo? Info, bool UpToDate, string? Error)> TryCheckViaReleasePageAsync()
        {
            try
            {
                using var response = await _pageClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, LatestReleasePageUrl),
                    HttpCompletionOption.ResponseHeadersRead);

                Uri? tagUri = null;
                if (response.Headers.Location != null)
                {
                    tagUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(new Uri(LatestReleasePageUrl), response.Headers.Location);
                }

                tagUri ??= response.RequestMessage?.RequestUri;
                var tag = TryParseTagFromLatestReleaseUrl(tagUri);
                if (string.IsNullOrWhiteSpace(tag) && response.IsSuccessStatusCode)
                {
                    return (null, false, "Could not read the latest release tag from GitHub.");
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    return (null, false,
                        $"GitHub releases page failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
                }

                var remoteVersion = tag.TrimStart('v', 'V');
                Console.WriteLine($"Current version: {GetCurrentVersion()}, Remote version: {remoteVersion} (releases page)");
                if (!IsNewerVersion(remoteVersion, GetCurrentVersion()))
                {
                    return (null, true, null);
                }

                return (new UpdateInfo
                {
                    Version = remoteVersion,
                    DownloadUrl = DirectExeDownloadUrl(tag),
                    ReleaseNotes = "",
                    ReleaseDate = DateTime.MinValue
                }, false, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (null, false, ex.Message);
            }
        }

        internal static string? TryParseTagFromLatestReleaseUrl(Uri? uri)
        {
            if (uri == null)
            {
                return null;
            }

            const string marker = "/releases/tag/";
            var path = uri.AbsolutePath.TrimEnd('/');
            var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var tag = Uri.UnescapeDataString(path[(idx + marker.Length)..]);
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }

        internal static string DirectExeDownloadUrl(string tag)
        {
            var t = (tag ?? "").Trim().Trim('/');
            return $"https://github.com/bblair321/spt-launcher/releases/download/{t}/{ExeAssetName}";
        }

        public bool IsNewerVersion(string remoteVersion, Version currentVersion)
        {
            try
            {
                var remoteText = (remoteVersion ?? "").Trim().TrimStart('v', 'V');
                if (!Version.TryParse(remoteText, out var remoteVer))
                {
                    return false;
                }

                // Compare major.minor.build only. .NET treats Version("4.2.7") as earlier than
                // Version(4,2,7,0) because the undefined revision counts as -1.
                var remoteNorm = new Version(
                    remoteVer.Major,
                    remoteVer.Minor,
                    Math.Max(remoteVer.Build, 0));
                var currentNorm = new Version(
                    currentVersion.Major,
                    currentVersion.Minor,
                    Math.Max(currentVersion.Build, 0));
                return remoteNorm > currentNorm;
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

            // Check shortly after startup
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(2000);
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
            LastDownloadError = null;
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
            {
                LastDownloadError = "Could not determine the running launcher path.";
                Console.WriteLine(LastDownloadError);
                return false;
            }

            var updatePath = Path.Combine(
                Path.GetTempPath(),
                $"SPT-Launcher-Update-{updateInfo.Version}.exe");

            try
            {
                if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    LastDownloadError = "The update has no download URL.";
                    return false;
                }

                await DownloadUpdateFileAsync(updateInfo.DownloadUrl, updatePath, progress);

                if (!File.Exists(updatePath) || new FileInfo(updatePath).Length == 0)
                {
                    LastDownloadError = "Downloaded update file is missing or empty.";
                    Console.WriteLine(LastDownloadError);
                    TryDeleteFile(updatePath);
                    return false;
                }

                if (!UpdateApplyHelper.LooksLikeWindowsExecutable(updatePath))
                {
                    LastDownloadError =
                        "Downloaded file is not a Windows executable (GitHub may have returned an error page).";
                    Console.WriteLine(LastDownloadError);
                    TryDeleteFile(updatePath);
                    return false;
                }

                MarkPendingSelfUpdate(updateInfo.Version);
                return ApplyUpdateAndRestart(currentExePath, updatePath);
            }
            catch (Exception ex)
            {
                LastDownloadError = $"Failed to download update: {ex.Message}";
                Console.WriteLine(LastDownloadError);
                ClearPendingSelfUpdate();
                TryDeleteFile(updatePath);
                return false;
            }
        }

        /// <summary>
        /// After restart: remove .old.exe when the update succeeded, and report whether to show a success banner.
        /// </summary>
        public SelfUpdateCompletionResult? CompleteSelfUpdateIfNeeded()
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
            {
                return null;
            }

            var currentVersion = GetCurrentVersion();
            var displayVersion = UpdateApplyHelper.FormatDisplayVersion(currentVersion);
            var pending = SettingsService.Instance.PendingSelfUpdateVersion?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(pending))
            {
                // Orphaned leftover from an older update path — safe once we're running.
                UpdateApplyHelper.TryRemoveBackup(currentExePath);
                return null;
            }

            var matched = UpdateApplyHelper.VersionsLookEqual(pending, currentVersion.ToString()) ||
                          UpdateApplyHelper.VersionsLookEqual(pending, displayVersion);

            if (matched)
            {
                var removed = UpdateApplyHelper.TryRemoveBackup(currentExePath);
                ClearPendingSelfUpdate();
                return new SelfUpdateCompletionResult
                {
                    ShowSuccessBanner = true,
                    DisplayVersion = displayVersion,
                    ExpectedVersion = pending,
                    BackupRemoved = removed
                };
            }

            // Keep .old.exe for possible manual recovery; clear the marker so we don't nag forever.
            ClearPendingSelfUpdate();
            return new SelfUpdateCompletionResult
            {
                ShowFailureBanner = true,
                DisplayVersion = displayVersion,
                ExpectedVersion = pending,
                BackupRemoved = false
            };
        }

        private static void MarkPendingSelfUpdate(string version)
        {
            SettingsService.Instance.PendingSelfUpdateVersion = version ?? "";
            SettingsService.Instance.SaveSettings();
        }

        private static void ClearPendingSelfUpdate()
        {
            SettingsService.Instance.PendingSelfUpdateVersion = "";
            SettingsService.Instance.SaveSettings();
        }

        private async Task DownloadUpdateFileAsync(
            string downloadUrl,
            string destinationPath,
            IProgress<double>? progress)
        {
            using var response = await _downloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)downloadedBytes / totalBytes * 100);
                }
            }
        }

        private bool ApplyUpdateAndRestart(string currentExePath, string downloadedUpdatePath)
        {
            try
            {
                var appDir = Path.GetDirectoryName(currentExePath)!;
                var backupPath = UpdateApplyHelper.GetBackupPath(currentExePath);
                var scriptPath = Path.Combine(Path.GetTempPath(), $"spt-launcher-update-{Guid.NewGuid():N}.cmd");
                var processName = Path.GetFileName(currentExePath);

                var script = UpdateApplyHelper.BuildReplaceInPlaceScript(
                    processName,
                    currentExePath,
                    downloadedUpdatePath,
                    backupPath,
                    scriptPath,
                    processId: Environment.ProcessId);

                File.WriteAllText(scriptPath, script);

                var comspec = Environment.GetEnvironmentVariable("ComSpec");
                if (string.IsNullOrWhiteSpace(comspec))
                {
                    comspec = "cmd.exe";
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = comspec,
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = appDir
                });

                System.Windows.Application.Current.Dispatcher.Invoke(System.Windows.Application.Current.Shutdown);
                return true;
            }
            catch (Exception ex)
            {
                LastDownloadError = $"Failed to apply update: {ex.Message}";
                Console.WriteLine(LastDownloadError);
                ClearPendingSelfUpdate();
                TryDeleteFile(downloadedUpdatePath);
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only
            }
        }

        public void Dispose()
        {
            StopPeriodicCheck();
            _httpClient.Dispose();
            _pageClient.Dispose();
            _downloadClient.Dispose();
        }
    }
}

