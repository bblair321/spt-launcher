using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SptLauncherWpf.Services
{
    public class SptUpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public bool IsUpdateAvailable { get; set; }
        public string? ReleaseUrl { get; set; }
        public string? InstallerDownloadUrl { get; set; }
    }

    public class FikaUpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public bool IsUpdateAvailable { get; set; }
        public string? ReleaseUrl { get; set; }
        public string? InstallerDownloadUrl { get; set; }
        public bool IsClientUpdateAvailable { get; set; }
        public bool IsServerUpdateAvailable { get; set; }
        public string? LatestClientVersion { get; set; }
        public string? LatestServerVersion { get; set; }
    }

    public class SptGitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
        
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
        
        [JsonPropertyName("assets")]
        public List<SptGitHubAsset> Assets { get; set; } = new();
    }

    public class SptGitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class SptDetectionService
    {
        private static SptDetectionService? _instance;
        public static SptDetectionService Instance => _instance ??= new SptDetectionService();

        private const string SptReleasesApiUrl = "https://api.github.com/repos/sp-tarkov/build/releases/latest";
        private const string ForgeInstallerPageUrl = "https://forge.sp-tarkov.com/installer";
        private const string FikaPluginReleasesApiUrl = "https://api.github.com/repos/project-fika/Fika-Plugin/releases/latest";
        private const string FikaServerReleasesApiUrl = "https://api.github.com/repos/project-fika/Fika-Server/releases/latest";
        private HttpClient? _httpClient;

        private SptDetectionService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SPT-Launcher-WPF");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Checks if SPT is installed by verifying the launcher path exists and is valid
        /// </summary>
        public bool IsSptInstalled(string launcherPath)
        {
            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                return false;
            }

            try
            {
                return File.Exists(launcherPath) && launcherPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the SPT version from exe properties (primary) or package.json (fallback)
        /// </summary>
        public string GetSptVersion(string launcherPath)
        {
            if (!IsSptInstalled(launcherPath))
            {
                return string.Empty;
            }

            try
            {
                var launcherDir = Path.GetDirectoryName(launcherPath);
                if (string.IsNullOrEmpty(launcherDir))
                {
                    return string.Empty;
                }

                // Determine the actual SPT root directory (handles nested structures like D:\SPT\SPT\SPT.Launcher.exe)
                var sptPath = DetermineSptRootDirectory(launcherDir);
                if (string.IsNullOrEmpty(sptPath))
                {
                    sptPath = launcherDir; // Fallback to launcher directory if detection fails
                }

                // Try SPT.Server.exe first (more likely to have the actual version)
                // Check both the detected root path and the launcher directory
                var serverExePath = Path.Combine(sptPath, "SPT.Server.exe");
                if (File.Exists(serverExePath))
                {
                    var versionFromServer = ReadVersionFromExe(serverExePath);
                    if (!string.IsNullOrEmpty(versionFromServer))
                    {
                        return versionFromServer;
                    }
                }

                // Also try in the launcher directory (in case it's in a nested structure)
                if (!string.Equals(sptPath, launcherDir, StringComparison.OrdinalIgnoreCase))
                {
                    serverExePath = Path.Combine(launcherDir, "SPT.Server.exe");
                    if (File.Exists(serverExePath))
                    {
                        var versionFromServer = ReadVersionFromExe(serverExePath);
                        if (!string.IsNullOrEmpty(versionFromServer))
                        {
                            return versionFromServer;
                        }
                    }
                }

                // Try launcher exe
                var versionFromLauncher = ReadVersionFromExe(launcherPath);
                if (!string.IsNullOrEmpty(versionFromLauncher))
                {
                    return versionFromLauncher;
                }

                // Fallback to package.json (try both paths)
                var versionFromPackage = ReadVersionFromPackageJson(sptPath);
                if (!string.IsNullOrEmpty(versionFromPackage))
                {
                    return versionFromPackage;
                }

                // Last resort: try package.json in launcher directory
                if (!string.Equals(sptPath, launcherDir, StringComparison.OrdinalIgnoreCase))
                {
                    return ReadVersionFromPackageJson(launcherDir);
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Determines the actual SPT root directory, handling nested structures like D:\SPT\SPT\
        /// </summary>
        private string DetermineSptRootDirectory(string launcherDir)
        {
            try
            {
                // Check if the parent directory contains SPT-related files
                // This handles cases where SPT is in a nested structure like D:\SPT\SPT\SPT.Launcher.exe
                var parentDir = Path.GetDirectoryName(launcherDir);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    // Get the name of the launcher directory (e.g., "SPT" from "D:\SPT\SPT")
                    var launcherDirName = Path.GetFileName(launcherDir);
                    // Get the name of the parent directory (e.g., "SPT" from "D:\SPT")
                    var parentDirName = Path.GetFileName(parentDir);

                    // If the parent and launcher directories have the same name (e.g., both are "SPT"),
                    // this suggests a nested structure like D:\SPT\SPT\ where we should use the parent
                    if (string.Equals(launcherDirName, parentDirName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Also check if parent has multiple items (not just the nested directory)
                        var parentItems = Directory.GetFileSystemEntries(parentDir);
                        if (parentItems.Length > 1)
                        {
                            // Parent directory is the root SPT directory
                            return parentDir;
                        }
                    }
                    else
                    {
                        // Check if parent directory contains SPT-related files
                        var serverExePath = Path.Combine(parentDir, "SPT.Server.exe");
                        var sptDataPath = Path.Combine(parentDir, "SPT_Data");
                        if (File.Exists(serverExePath) || Directory.Exists(sptDataPath))
                        {
                            // Parent directory contains SPT files, so use it as the root SPT directory
                            return parentDir;
                        }
                    }
                }

                return launcherDir;
            }
            catch
            {
                return launcherDir;
            }
        }

        /// <summary>
        /// Reads version from package.json in SPT root directory
        /// </summary>
        private string ReadVersionFromPackageJson(string sptPath)
        {
            try
            {
                var packageJsonPath = Path.Combine(sptPath, "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    return string.Empty;
                }

                var jsonContent = File.ReadAllText(packageJsonPath);
                using var document = JsonDocument.Parse(jsonContent);
                
                if (document.RootElement.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    if (string.IsNullOrEmpty(version))
                    {
                        return string.Empty;
                    }
                    return VersionStringHelper.Normalize(version);
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads version from SPT.Launcher.exe file properties
        /// </summary>
        private string ReadVersionFromExe(string exePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                
                string? version = null;
                
                // Try ProductVersion first (more reliable for SPT)
                if (!string.IsNullOrEmpty(versionInfo.ProductVersion))
                {
                    version = versionInfo.ProductVersion;
                }
                // Fallback to FileVersion
                else if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                {
                    version = versionInfo.FileVersion;
                }

                if (string.IsNullOrEmpty(version))
                {
                    return string.Empty;
                }
                return VersionStringHelper.Normalize(version);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks for SPT updates by comparing current version with latest GitHub release
        /// </summary>
        public async Task<SptUpdateInfo?> CheckForUpdatesAsync(string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                return null;
            }

            try
            {
                var response = await _httpClient!.GetStringAsync(SptReleasesApiUrl);
                var release = JsonSerializer.Deserialize<SptGitHubRelease>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                });

                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    return null;
                }

                // Extract version from tag (remove 'v' prefix if present)
                var latestVersion = release.TagName.TrimStart('v', 'V');
                
                // Normalize both versions for comparison
                var normalizedLatest = NormalizeVersion(latestVersion);
                var normalizedCurrent = NormalizeVersion(currentVersion);

                // Compare versions
                var isUpdateAvailable = IsNewerVersion(normalizedLatest, normalizedCurrent);

                // Find installer download URL from release assets
                string? installerUrl = null;
                if (release.Assets != null && release.Assets.Count > 0)
                {
                    // Debug: Log available assets
                    System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Found {release.Assets.Count} assets in release:");
                    foreach (var asset in release.Assets)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {asset.Name} ({asset.Size} bytes)");
                    }

                    // Look for installer/setup exe files first
                    var installerAsset = release.Assets.FirstOrDefault(a => 
                        (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                         (a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase) ||
                          a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                          a.Name.Contains("install", StringComparison.OrdinalIgnoreCase))) ||
                        a.Name.Equals("SPTInstaller.exe", StringComparison.OrdinalIgnoreCase));

                    // Fallback to any .exe file
                    if (installerAsset == null)
                    {
                        installerAsset = release.Assets.FirstOrDefault(a => 
                            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    }

                    // Fallback to any Windows executable-like file
                    if (installerAsset == null)
                    {
                        installerAsset = release.Assets.FirstOrDefault(a => 
                            a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                    }

                    // Fallback to largest asset (likely the installer)
                    if (installerAsset == null && release.Assets.Count > 0)
                    {
                        installerAsset = release.Assets.OrderByDescending(a => a.Size).FirstOrDefault();
                        System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Using largest asset as fallback: {installerAsset?.Name}");
                    }

                    if (installerAsset != null && !string.IsNullOrEmpty(installerAsset.BrowserDownloadUrl))
                    {
                        installerUrl = installerAsset.BrowserDownloadUrl;
                        System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Found installer URL: {installerUrl}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[SptDetectionService] No installer URL found in assets");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[SptDetectionService] No assets found in release");
                }

                // If no installer URL from GitHub, try to get it from Forge
                if (string.IsNullOrWhiteSpace(installerUrl))
                {
                    System.Diagnostics.Debug.WriteLine("[SptDetectionService] Attempting to get installer URL from Forge...");
                    installerUrl = await GetInstallerUrlFromForgeAsync();
                }

                return new SptUpdateInfo
                {
                    LatestVersion = latestVersion,
                    IsUpdateAvailable = isUpdateAvailable,
                    ReleaseUrl = release.HtmlUrl,
                    InstallerDownloadUrl = installerUrl
                };
            }
            catch (HttpRequestException)
            {
                // Network error - return null to indicate check failed
                return null;
            }
            catch (Exception)
            {
                // Other errors - return null
                return null;
            }
        }

        /// <summary>
        /// Gets the installer download URL from the Forge installer page
        /// </summary>
        private async Task<string?> GetInstallerUrlFromForgeAsync()
        {
            try
            {
                var html = await _httpClient!.GetStringAsync(ForgeInstallerPageUrl);
                
                // Look for download links in the HTML
                // Common patterns: href="...installer.exe" or data-download-url="..." or download="..."
                var patterns = new[]
                {
                    @"href=[""']([^""']*installer[^""']*\.exe[^""']*)[""']",  // href="...installer.exe"
                    @"href=[""']([^""']*\.exe[^""']*)[""']",  // href="...something.exe"
                    @"data-download-url=[""']([^""']*)[""']",  // data-download-url="..."
                    @"download=[""']([^""']*\.exe[^""']*)[""']"  // download="...installer.exe"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var url = match.Groups[1].Value;
                        
                        // Make absolute URL if relative
                        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
                        {
                            if (!uri.IsAbsoluteUri)
                            {
                                uri = new Uri(new Uri(ForgeInstallerPageUrl), uri);
                            }
                            
                            var absoluteUrl = uri.ToString();
                            System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Found installer URL from Forge: {absoluteUrl}");
                            return absoluteUrl;
                        }
                    }
                }

                // Fallback: Look for any .exe link
                var exeMatch = Regex.Match(html, @"href=[""']([^""']*\.exe[^""']*)[""']", RegexOptions.IgnoreCase);
                if (exeMatch.Success && exeMatch.Groups.Count > 1)
                {
                    var url = exeMatch.Groups[1].Value;
                    if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
                    {
                        if (!uri.IsAbsoluteUri)
                        {
                            uri = new Uri(new Uri(ForgeInstallerPageUrl), uri);
                        }
                        var absoluteUrl = uri.ToString();
                        System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Found .exe URL from Forge: {absoluteUrl}");
                        return absoluteUrl;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[SptDetectionService] No installer URL found on Forge page");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Error getting installer URL from Forge: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Normalizes a version string by removing prefixes and suffixes
        /// </summary>
        private string NormalizeVersion(string version)
        {
            return VersionStringHelper.Normalize(version);
        }

        /// <summary>
        /// Compares two version strings to determine if remoteVersion is newer than currentVersion
        /// </summary>
        public bool IsNewerVersion(string remoteVersion, string currentVersion)
        {
            try
            {
                // Normalize both versions
                var normalizedRemote = NormalizeVersion(remoteVersion);
                var normalizedCurrent = NormalizeVersion(currentVersion);

                // Try to parse as Version objects for reliable comparison
                if (Version.TryParse(normalizedRemote, out var remoteVer) && 
                    Version.TryParse(normalizedCurrent, out var currentVer))
                {
                    return remoteVer > currentVer;
                }

                // Fallback to string comparison if parsing fails
                return string.Compare(normalizedRemote, normalizedCurrent, StringComparison.OrdinalIgnoreCase) > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks for Fika updates by comparing installed client/server versions with their GitHub release streams.
        /// </summary>
        public async Task<FikaUpdateInfo?> CheckForFikaUpdatesAsync(string? clientVersion, string? serverVersion)
        {
            if (string.IsNullOrWhiteSpace(clientVersion) && string.IsNullOrWhiteSpace(serverVersion))
            {
                return null;
            }

            try
            {
                var clientReleaseTask = string.IsNullOrWhiteSpace(clientVersion)
                    ? Task.FromResult<SptGitHubRelease?>(null)
                    : FetchLatestGitHubReleaseAsync(FikaPluginReleasesApiUrl);
                var serverReleaseTask = string.IsNullOrWhiteSpace(serverVersion)
                    ? Task.FromResult<SptGitHubRelease?>(null)
                    : FetchLatestGitHubReleaseAsync(FikaServerReleasesApiUrl);

                await Task.WhenAll(clientReleaseTask, serverReleaseTask);

                var clientRelease = clientReleaseTask.Result;
                var serverRelease = serverReleaseTask.Result;

                if (clientRelease == null && serverRelease == null)
                {
                    return null;
                }

                string? latestClientVersion = null;
                string? latestServerVersion = null;
                var isClientUpdateAvailable = false;
                var isServerUpdateAvailable = false;

                if (clientRelease != null && !string.IsNullOrWhiteSpace(clientRelease.TagName))
                {
                    latestClientVersion = ExtractReleaseVersion(clientRelease.TagName);
                    isClientUpdateAvailable = IsNewerVersion(latestClientVersion, clientVersion!);
                    System.Diagnostics.Debug.WriteLine(
                        $"[SptDetectionService] Fika client: installed={clientVersion}, latest={latestClientVersion}, update={isClientUpdateAvailable}");
                }

                if (serverRelease != null && !string.IsNullOrWhiteSpace(serverRelease.TagName))
                {
                    latestServerVersion = ExtractReleaseVersion(serverRelease.TagName);
                    isServerUpdateAvailable = IsNewerVersion(latestServerVersion, serverVersion!);
                    System.Diagnostics.Debug.WriteLine(
                        $"[SptDetectionService] Fika server: installed={serverVersion}, latest={latestServerVersion}, update={isServerUpdateAvailable}");
                }

                var isUpdateAvailable = isClientUpdateAvailable || isServerUpdateAvailable;

                return new FikaUpdateInfo
                {
                    LatestVersion = BuildFikaLatestVersionSummary(latestClientVersion, latestServerVersion, isClientUpdateAvailable, isServerUpdateAvailable),
                    IsUpdateAvailable = isUpdateAvailable,
                    ReleaseUrl = FikaInstallUrls.InstallerReleasesPageUrl,
                    InstallerDownloadUrl = isUpdateAvailable ? FikaInstallUrls.InstallerDownloadUrl : null,
                    IsClientUpdateAvailable = isClientUpdateAvailable,
                    IsServerUpdateAvailable = isServerUpdateAvailable,
                    LatestClientVersion = latestClientVersion,
                    LatestServerVersion = latestServerVersion
                };
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Error checking Fika updates: {ex.Message}");
                return null;
            }
        }

        private async Task<SptGitHubRelease?> FetchLatestGitHubReleaseAsync(string apiUrl)
        {
            System.Diagnostics.Debug.WriteLine($"[SptDetectionService] Fetching latest release from: {apiUrl}");
            var response = await _httpClient!.GetStringAsync(apiUrl);
            return JsonSerializer.Deserialize<SptGitHubRelease>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            });
        }

        private static string ExtractReleaseVersion(string tagName)
        {
            return tagName.TrimStart('v', 'V');
        }

        private static string BuildFikaLatestVersionSummary(
            string? latestClientVersion,
            string? latestServerVersion,
            bool isClientUpdateAvailable,
            bool isServerUpdateAvailable)
        {
            var parts = new List<string>();
            if (isClientUpdateAvailable && !string.IsNullOrWhiteSpace(latestClientVersion))
            {
                parts.Add($"client {latestClientVersion}");
            }

            if (isServerUpdateAvailable && !string.IsNullOrWhiteSpace(latestServerVersion))
            {
                parts.Add($"server {latestServerVersion}");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : latestClientVersion ?? latestServerVersion ?? "";
        }
    }
}
