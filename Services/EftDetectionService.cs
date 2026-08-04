using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SptLauncherWpf.Services
{
    public enum EftCompatibilityStatus
    {
        NotDetected,
        Compatible,
        UpdateRequired,
        NewerThanSupported,
        RequiredUnknown
    }

    public class EftCompatibilityInfo
    {
        public string? InstallPath { get; set; }
        public string? InstalledVersion { get; set; }
        /// <summary>Live Tarkov version mentioned in SPT release notes (often lags behind current patchers).</summary>
        public string? RequiredLiveVersion { get; set; }
        /// <summary>SPT client version after downgrade (for display only).</summary>
        public string? TargetSptClientVersion { get; set; }
        /// <summary>Confirmed patcher URL for installed live → SPT target, when found.</summary>
        public string? AvailablePatcherUrl { get; set; }
        public EftCompatibilityStatus Status { get; set; } = EftCompatibilityStatus.NotDetected;
        public string StatusText => GetStatusText(sptAlreadyInstalled: false);

        public string GetStatusText(bool sptAlreadyInstalled) => Status switch
        {
            EftCompatibilityStatus.Compatible =>
                sptAlreadyInstalled
                    ? "Live install detected"
                    : string.IsNullOrWhiteSpace(AvailablePatcherUrl)
                        ? "Ready for SPT installer"
                        : "Patcher available for install",
            EftCompatibilityStatus.UpdateRequired => "Update Tarkov (for downgrader)",
            EftCompatibilityStatus.NewerThanSupported => "No patcher for this Tarkov version yet",
            EftCompatibilityStatus.RequiredUnknown => "Required live version unknown",
            _ => "Not detected"
        };
    }

    public class EftDetectionService
    {
        private static EftDetectionService? _instance;
        public static EftDetectionService Instance => _instance ??= new EftDetectionService();

        private const string EftExeName = "EscapeFromTarkov.exe";
        private const string EftOfficialSiteUrl = "https://www.escapefromtarkov.com/";
        private const string PatcherHost = "https://slugma.waffle-lord.net";

        private static readonly HttpClient PatcherHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private EftDetectionService()
        {
        }

        public EftCompatibilityInfo EvaluateCompatibility(
            string? requiredLiveEftVersion,
            string? targetSptClientVersion = null,
            string? preferredGamePath = null)
        {
            var info = new EftCompatibilityInfo
            {
                RequiredLiveVersion = string.IsNullOrWhiteSpace(requiredLiveEftVersion)
                    ? null
                    : NormalizeEftVersion(requiredLiveEftVersion),
                TargetSptClientVersion = string.IsNullOrWhiteSpace(targetSptClientVersion)
                    ? null
                    : NormalizeEftVersion(targetSptClientVersion)
            };

            // Prefer registry/Steam/common live installs. SPT GamePath often points at the
            // already-downpatched SPT client copy, which is not the live Tarkov install.
            info.InstallPath = FindEftInstallPath(preferredGamePath);
            if (string.IsNullOrWhiteSpace(info.InstallPath))
            {
                info.Status = EftCompatibilityStatus.NotDetected;
                return info;
            }

            info.InstalledVersion = GetInstalledEftVersion(info.InstallPath);
            if (string.IsNullOrWhiteSpace(info.InstalledVersion))
            {
                info.Status = EftCompatibilityStatus.NotDetected;
                return info;
            }

            if (string.IsNullOrWhiteSpace(info.TargetSptClientVersion) &&
                string.IsNullOrWhiteSpace(info.RequiredLiveVersion))
            {
                info.Status = EftCompatibilityStatus.RequiredUnknown;
                return info;
            }

            // Provisional status from release-note source version only:
            // - too old  -> UpdateRequired
            // - equal/newer/unknown -> Compatible until the CDN patcher probe runs
            // Never set NewerThanSupported here; that is reserved for "no patcher for this live build".
            if (!string.IsNullOrWhiteSpace(info.RequiredLiveVersion))
            {
                var comparison = CompareNormalizedVersions(info.InstalledVersion, info.RequiredLiveVersion);
                info.Status = comparison < 0
                    ? EftCompatibilityStatus.UpdateRequired
                    : EftCompatibilityStatus.Compatible;
            }
            else
            {
                info.Status = EftCompatibilityStatus.Compatible;
            }

            return info;
        }

        /// <summary>
        /// Confirms whether a Patcher_{live}_to_{target}.7z exists.
        /// The warning panel is only used when live Tarkov has no matching patcher yet.
        /// </summary>
        public async Task ResolveCurrentPatcherAvailabilityAsync(EftCompatibilityInfo info)
        {
            if (info == null)
            {
                return;
            }

            if (info.Status is EftCompatibilityStatus.NotDetected or EftCompatibilityStatus.UpdateRequired)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(info.InstalledVersion) ||
                string.IsNullOrWhiteSpace(info.TargetSptClientVersion))
            {
                return;
            }

            var probe = await ProbeAvailablePatcherUrlAsync(
                info.InstalledVersion,
                info.TargetSptClientVersion);

            if (probe.Result == PatcherProbeResult.Exists)
            {
                info.AvailablePatcherUrl = probe.Url;
                info.Status = EftCompatibilityStatus.Compatible;
                Debug.WriteLine($"[EftDetectionService] Found current patcher: {probe.Url}");
                return;
            }

            if (probe.Result == PatcherProbeResult.Error)
            {
                // Network/CDN glitch — don't scare the user with a false "no patcher" warning.
                Debug.WriteLine("[EftDetectionService] Patcher probe errored; leaving provisional status.");
                return;
            }

            // Confirmed missing: live Tarkov has no downgrade patcher for the SPT target yet.
            info.AvailablePatcherUrl = null;
            info.Status = EftCompatibilityStatus.NewerThanSupported;
            Debug.WriteLine(
                $"[EftDetectionService] No patcher for live {info.InstalledVersion} -> {info.TargetSptClientVersion}");
        }

        public async Task<string?> FindAvailablePatcherUrlAsync(string liveVersion, string targetVersion)
        {
            var probe = await ProbeAvailablePatcherUrlAsync(liveVersion, targetVersion);
            return probe.Result == PatcherProbeResult.Exists ? probe.Url : null;
        }

        private async Task<(PatcherProbeResult Result, string? Url)> ProbeAvailablePatcherUrlAsync(
            string liveVersion,
            string targetVersion)
        {
            var live = NormalizeEftVersion(liveVersion);
            var target = NormalizeEftVersion(targetVersion);
            if (string.IsNullOrWhiteSpace(live) || string.IsNullOrWhiteSpace(target))
            {
                return (PatcherProbeResult.Error, null);
            }

            var sawMissing = false;
            foreach (var targetVariant in GetPatcherTargetVariants(target))
            {
                var url = BuildPatcherUrl(live!, targetVariant);
                var result = await ProbeUrlAsync(url);
                if (result == PatcherProbeResult.Exists)
                {
                    return (PatcherProbeResult.Exists, url);
                }

                if (result == PatcherProbeResult.Missing)
                {
                    sawMissing = true;
                }
                else
                {
                    return (PatcherProbeResult.Error, null);
                }
            }

            return (sawMissing ? PatcherProbeResult.Missing : PatcherProbeResult.Error, null);
        }

        private enum PatcherProbeResult
        {
            Exists,
            Missing,
            Error
        }

        public static string BuildPatcherUrl(string liveVersion, string targetVariant) =>
            $"{PatcherHost}/Patcher_{liveVersion}_to_{targetVariant}.7z";

        /// <summary>
        /// CDN patcher filenames sometimes drop a leading "0." from SPT target versions.
        /// </summary>
        public static IReadOnlyList<string> GetPatcherTargetVariants(string targetVersion)
        {
            var seen = new List<string>();
            void Add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (!seen.Exists(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                {
                    seen.Add(value);
                }
            }

            Add(targetVersion);

            // Release notes use 0.16.9.5.40743; patcher files use 16.9.5.40743.
            if (targetVersion.StartsWith("0.", StringComparison.Ordinal))
            {
                Add(targetVersion.Substring(2));
            }
            else if (targetVersion.StartsWith("16.", StringComparison.Ordinal))
            {
                Add("0." + targetVersion);
            }

            return seen;
        }

        private static async Task<PatcherProbeResult> ProbeUrlAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await PatcherHttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return PatcherProbeResult.Exists;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return PatcherProbeResult.Missing;
                }

                // Some hosts dislike HEAD; fall back to a ranged GET.
                if (response.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed
                    or System.Net.HttpStatusCode.Forbidden)
                {
                    using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    using var getResponse = await PatcherHttpClient.SendAsync(
                        getRequest,
                        HttpCompletionOption.ResponseHeadersRead);
                    if (getResponse.IsSuccessStatusCode ||
                        getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
                    {
                        return PatcherProbeResult.Exists;
                    }

                    if (getResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return PatcherProbeResult.Missing;
                    }
                }

                return PatcherProbeResult.Error;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftDetectionService] Patcher probe failed for {url}: {ex.Message}");
                return PatcherProbeResult.Error;
            }
        }

        public string? FindEftInstallPath(string? preferredGamePath = null)
        {
            var fromRegistry = FindEftPathFromRegistry();
            if (!string.IsNullOrWhiteSpace(fromRegistry))
            {
                return fromRegistry;
            }

            var fromSteam = FindEftPathFromSteamLibraries();
            if (!string.IsNullOrWhiteSpace(fromSteam))
            {
                return fromSteam;
            }

            foreach (var candidate in GetCommonEftPaths())
            {
                var resolved = ResolveEftDirectory(candidate);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            // Last resort only: SPT launcher GamePath (may be SPT's local game copy).
            if (!string.IsNullOrWhiteSpace(preferredGamePath))
            {
                return ResolveEftDirectory(preferredGamePath);
            }

            return null;
        }

        public string? GetInstalledEftVersion(string? installPath = null)
        {
            installPath ??= FindEftInstallPath();
            if (string.IsNullOrWhiteSpace(installPath))
            {
                return null;
            }

            var candidates = new List<string?>();

            // BSG uninstall DisplayVersion is the authoritative live client string
            // (e.g. 1.1.0.0.46624) and avoids exe FileVersion dropping a segment.
            var registryVersion = FindEftDisplayVersionFromRegistry(installPath);
            if (!string.IsNullOrWhiteSpace(registryVersion))
            {
                candidates.Add(registryVersion);
            }

            var exePath = Path.Combine(installPath, EftExeName);
            if (File.Exists(exePath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(exePath);

                    // ProductVersion looks like: 1.1.0.0-46624-f8702c22
                    // FileVersion often drops a middle segment: 1.1.0.46624
                    candidates.Add(versionInfo.ProductVersion);
                    candidates.Add(versionInfo.FileVersion);
                    candidates.Add(
                        $"{versionInfo.ProductMajorPart}.{versionInfo.ProductMinorPart}." +
                        $"{versionInfo.ProductBuildPart}.{versionInfo.ProductPrivatePart}");
                    candidates.Add(
                        $"{versionInfo.FileMajorPart}.{versionInfo.FileMinorPart}." +
                        $"{versionInfo.FileBuildPart}.{versionInfo.FilePrivatePart}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EftDetectionService] Failed to read EFT exe version: {ex.Message}");
                }
            }

            return PickBestEftVersion(candidates);
        }

        /// <summary>
        /// Extracts a numeric dotted EFT version from strings like "1.1.0.0-46624-f8702c22",
        /// "1.0.4.1-44236-749fe27f", or "0.16.9-40087".
        /// </summary>
        public static string? NormalizeEftVersion(string? rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            var cleaned = VersionStringHelper.Normalize(rawVersion).Trim();

            // Tarkov ProductVersion: "<client>-<build>-<git>" -> "1.1.0.0.46624"
            var dashParts = cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (dashParts.Length >= 2 &&
                Regex.IsMatch(dashParts[0], @"^\d+(?:\.\d+)+$") &&
                Regex.IsMatch(dashParts[1], @"^\d+$"))
            {
                return $"{dashParts[0]}.{dashParts[1]}";
            }

            cleaned = cleaned.Replace('-', '.');
            var match = Regex.Match(cleaned, @"(\d+(?:\.\d+){1,6})");
            if (!match.Success)
            {
                return null;
            }

            return match.Groups[1].Value;
        }

        private static string? PickBestEftVersion(IEnumerable<string?> candidates)
        {
            string? best = null;
            var bestScore = -1;

            foreach (var candidate in candidates)
            {
                var normalized = NormalizeEftVersion(candidate);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var parts = GetNumericVersionParts(normalized);
                // Prefer fuller Tarkov strings (5-part live versions beat truncated 4-part FileVersion).
                var score = (parts.Length * 1000) + (parts.Length > 0 ? parts[^1] : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = normalized;
                }
            }

            return best;
        }

        private static string? FindEftDisplayVersionFromRegistry(string installPath)
        {
            string[] keyPaths =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\WOW6432Node\Battlestate Games\EscapeFromTarkov",
                @"SOFTWARE\Battlestate Games\EscapeFromTarkov"
            };

            foreach (var keyPath in keyPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath)
                                   ?? Registry.CurrentUser.OpenSubKey(keyPath);
                    if (key == null)
                    {
                        continue;
                    }

                    var location = key.GetValue("InstallLocation")?.ToString()
                                   ?? key.GetValue("InstallPath")?.ToString();
                    var resolved = ResolveEftDirectory(location);
                    if (!string.IsNullOrWhiteSpace(resolved) &&
                        !string.Equals(resolved, installPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                    var normalized = NormalizeEftVersion(displayVersion);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        return normalized;
                    }
                }
                catch
                {
                    // Continue searching
                }
            }

            return null;
        }

        public string? TryGetGamePathFromSptLauncherConfig(string? launcherConfigJsonPath)
        {
            if (string.IsNullOrWhiteSpace(launcherConfigJsonPath) || !File.Exists(launcherConfigJsonPath))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(launcherConfigJsonPath));
                if (doc.RootElement.TryGetProperty("GamePath", out var gamePathElement))
                {
                    var gamePath = gamePathElement.GetString();
                    return string.IsNullOrWhiteSpace(gamePath) ? null : gamePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftDetectionService] Failed reading GamePath: {ex.Message}");
            }

            return null;
        }

        public bool TryLaunchOfficialUpdater()
        {
            // Most Tarkov copies use the Battlestate Games launcher, not Steam.
            if (TryLaunchBsgLauncher())
            {
                return true;
            }

            // Only use Steam when we can identify an actual Escape from Tarkov appmanifest.
            // Never hardcode a Steam app id — wrong ids can launch unrelated games.
            if (TryLaunchSteamEftIfInstalled())
            {
                return true;
            }

            // Last resort: open the official site so the user can update via BSG launcher.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = EftOfficialSiteUrl,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftDetectionService] Failed opening EFT site: {ex.Message}");
            }

            return false;
        }

        private static bool TryLaunchBsgLauncher()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Battlestate Games", "BsgLauncher", "BsgLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Battlestate Games", "BsgLauncher", "BsgLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Battlestate Games", "BsgLauncher", "BsgLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "BSG Launcher", "BsgLauncher.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Battlestate Games", "BsgLauncher", "BsgLauncher.exe")
            };

            // Also check beside the live EFT install / registry uninstall entries.
            var eftPath = Instance.FindEftInstallPath();
            if (!string.IsNullOrWhiteSpace(eftPath))
            {
                candidates.Add(Path.Combine(eftPath, "BsgLauncher.exe"));
                candidates.Add(Path.Combine(Directory.GetParent(eftPath)?.FullName ?? string.Empty, "BsgLauncher", "BsgLauncher.exe"));
            }

            foreach (var path in candidates.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EftDetectionService] Failed launching BSG launcher at {path}: {ex.Message}");
                }
            }

            return false;
        }

        private static bool TryLaunchSteamEftIfInstalled()
        {
            try
            {
                var steamPath = Registry.CurrentUser
                    .OpenSubKey(@"Software\Valve\Steam")
                    ?.GetValue("SteamPath")
                    ?.ToString()
                    ?.Replace('/', Path.DirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                {
                    return false;
                }

                var libraryFolders = new List<string> { steamPath };
                var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFile))
                {
                    foreach (Match match in Regex.Matches(
                                 File.ReadAllText(libraryFile),
                                 "\"path\"\\s+\"([^\"]+)\"",
                                 RegexOptions.IgnoreCase))
                    {
                        var path = match.Groups[1].Value.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
                        if (Directory.Exists(path))
                        {
                            libraryFolders.Add(path);
                        }
                    }
                }

                foreach (var library in libraryFolders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var steamApps = Path.Combine(library, "steamapps");
                    if (!Directory.Exists(steamApps))
                    {
                        continue;
                    }

                    foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
                    {
                        var content = File.ReadAllText(manifest);
                        if (!Regex.IsMatch(content, "\"name\"\\s+\"Escape from Tarkov\"", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(content, "\"installdir\"\\s+\"[^\"]*Escape from Tarkov[^\"]*\"", RegexOptions.IgnoreCase))
                        {
                            continue;
                        }

                        var appIdMatch = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase);
                        if (!appIdMatch.Success)
                        {
                            var fromFile = Regex.Match(Path.GetFileNameWithoutExtension(manifest), @"appmanifest_(\d+)");
                            if (!fromFile.Success)
                            {
                                continue;
                            }

                            appIdMatch = fromFile;
                        }

                        var appId = appIdMatch.Groups[1].Value;
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = $"steam://run/{appId}",
                            UseShellExecute = true
                        });
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftDetectionService] Steam EFT launch failed: {ex.Message}");
            }

            return false;
        }

        private static string? FindEftPathFromRegistry()
        {
            string[] keyPaths =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\WOW6432Node\Battlestate Games\EscapeFromTarkov",
                @"SOFTWARE\Battlestate Games\EscapeFromTarkov"
            };

            foreach (var keyPath in keyPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath)
                                   ?? Registry.CurrentUser.OpenSubKey(keyPath);
                    if (key == null)
                    {
                        continue;
                    }

                    foreach (var valueName in new[] { "InstallLocation", "InstallPath", "DisplayIcon", "UninstallString" })
                    {
                        var value = key.GetValue(valueName)?.ToString();
                        var resolved = ResolveEftDirectory(value);
                        if (!string.IsNullOrWhiteSpace(resolved))
                        {
                            return resolved;
                        }
                    }
                }
                catch
                {
                    // Continue searching
                }
            }

            return null;
        }

        private static string? FindEftPathFromSteamLibraries()
        {
            try
            {
                var steamPath = Registry.CurrentUser
                    .OpenSubKey(@"Software\Valve\Steam")
                    ?.GetValue("SteamPath")
                    ?.ToString()
                    ?.Replace('/', Path.DirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                {
                    return null;
                }

                var libraryFolders = new List<string> { steamPath };
                var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFile))
                {
                    foreach (Match match in Regex.Matches(
                                 File.ReadAllText(libraryFile),
                                 "\"path\"\\s+\"([^\"]+)\"",
                                 RegexOptions.IgnoreCase))
                    {
                        var path = match.Groups[1].Value.Replace(@"\\", @"\").Replace('/', Path.DirectorySeparatorChar);
                        if (Directory.Exists(path))
                        {
                            libraryFolders.Add(path);
                        }
                    }
                }

                foreach (var library in libraryFolders.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(library, "steamapps", "common", "Escape from Tarkov");
                    var resolved = ResolveEftDirectory(candidate);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        return resolved;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftDetectionService] Steam library scan failed: {ex.Message}");
            }

            return null;
        }

        private static IEnumerable<string> GetCommonEftPaths()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"C:\",
                @"D:\",
                @"E:\",
                @"F:\",
                @"G:\"
            };

            foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                yield return Path.Combine(root, "Battlestate Games", "Escape from Tarkov");
                yield return Path.Combine(root, "Escape from Tarkov");
                yield return Path.Combine(root, "Games", "Escape from Tarkov");
            }
        }

        private static string? ResolveEftDirectory(string? pathOrFile)
        {
            if (string.IsNullOrWhiteSpace(pathOrFile))
            {
                return null;
            }

            try
            {
                var cleaned = pathOrFile.Trim().Trim('"');
                if (File.Exists(cleaned) &&
                    cleaned.EndsWith(EftExeName, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(cleaned);
                }

                if (Directory.Exists(cleaned) && File.Exists(Path.Combine(cleaned, EftExeName)))
                {
                    return cleaned;
                }
            }
            catch
            {
                // Ignore bad paths
            }

            return null;
        }

        private static int CompareNormalizedVersions(string left, string right)
        {
            var leftParts = GetNumericVersionParts(left);
            var rightParts = GetNumericVersionParts(right);
            if (leftParts.Length == 0 || rightParts.Length == 0)
            {
                return 0;
            }

            var length = Math.Max(leftParts.Length, rightParts.Length);
            for (var i = 0; i < length; i++)
            {
                var l = i < leftParts.Length ? leftParts[i] : 0;
                var r = i < rightParts.Length ? rightParts[i] : 0;
                if (l != r)
                {
                    return l.CompareTo(r);
                }
            }

            return 0;
        }

        private static int[] GetNumericVersionParts(string version)
        {
            var normalized = NormalizeEftVersion(version);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Array.Empty<int>();
            }

            return normalized
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out var value) ? value : -1)
                .Where(value => value >= 0)
                .ToArray();
        }

        /// <summary>
        /// Live Tarkov version required as downgrader input, from patcher links like:
        /// Patcher_1.0.6.5.46221_to_16.9.5.40743.7z
        /// </summary>
        public static string? ParseLiveEftVersionFromReleaseBody(string? releaseBody)
        {
            if (string.IsNullOrWhiteSpace(releaseBody))
            {
                return null;
            }

            var patterns = new[]
            {
                @"Patcher_([0-9]+(?:\.[0-9]+)+)_to_",
                @"Patchers?/([0-9]+(?:\.[0-9]+)+)_to_",
                @"from[_ ]([0-9]+(?:\.[0-9]+)+)_to_"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(releaseBody, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    return NormalizeEftVersion(match.Groups[1].Value);
                }
            }

            return null;
        }

        /// <summary>
        /// SPT client target after downgrade, from "Requires EFT ..." lines.
        /// </summary>
        public static string? ParseTargetEftVersionFromReleaseBody(string? releaseBody)
        {
            if (string.IsNullOrWhiteSpace(releaseBody))
            {
                return null;
            }

            var patterns = new[]
            {
                @"Requires\s+EFT\s*[`'""]?\s*([0-9]+(?:[.\-][0-9]+)+)",
                @"_to_([0-9]+(?:\.[0-9]+)+)\.7z",
                @"EFT\s*(?:client\s*)?version\s*[`'""]?\s*([0-9]+(?:[.\-][0-9]+)+)"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(releaseBody, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var normalized = NormalizeEftVersion(match.Groups[1].Value);
                    // Patcher targets often omit the leading 0 (16.9.5.40743 -> 0.16.9.5.40743).
                    if (!string.IsNullOrWhiteSpace(normalized) &&
                        normalized.StartsWith("16.", StringComparison.Ordinal))
                    {
                        normalized = "0." + normalized;
                    }

                    return normalized;
                }
            }

            return null;
        }
    }
}
