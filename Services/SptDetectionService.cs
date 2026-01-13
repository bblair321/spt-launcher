using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SptLauncherWpf.Services
{
    public class SptDetectionService
    {
        private static SptDetectionService? _instance;
        public static SptDetectionService Instance => _instance ??= new SptDetectionService();

        private SptDetectionService()
        {
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
                var sptPath = Path.GetDirectoryName(launcherPath);
                if (string.IsNullOrEmpty(sptPath))
                {
                    return string.Empty;
                }

                // Try SPT.Server.exe first (more likely to have the actual version)
                var serverExePath = Path.Combine(sptPath, "SPT.Server.exe");
                if (File.Exists(serverExePath))
                {
                    var versionFromServer = ReadVersionFromExe(serverExePath);
                    if (!string.IsNullOrEmpty(versionFromServer))
                    {
                        return versionFromServer;
                    }
                }

                // Try launcher exe
                var versionFromLauncher = ReadVersionFromExe(launcherPath);
                if (!string.IsNullOrEmpty(versionFromLauncher))
                {
                    return versionFromLauncher;
                }

                // Fallback to package.json
                return ReadVersionFromPackageJson(sptPath);
            }
            catch
            {
                return string.Empty;
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

                    // Strip commit hash if present (format: "version+commithash")
                    // Take only the part before the '+' sign
                    var plusIndex = version.IndexOf('+');
                    if (plusIndex > 0)
                    {
                        version = version.Substring(0, plusIndex);
                    }

                    // Strip common suffixes like "-RELEASE", "-DEV", "-ALPHA", "-BETA"
                    var dashIndex = version.IndexOf('-');
                    if (dashIndex > 0)
                    {
                        var suffix = version.Substring(dashIndex).ToUpperInvariant();
                        if (suffix == "-RELEASE" || suffix == "-DEV" || suffix == "-ALPHA" || suffix == "-BETA" || suffix.StartsWith("-RC"))
                        {
                            version = version.Substring(0, dashIndex);
                        }
                    }

                    return version;
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

                // Strip commit hash if present (format: "version+commithash")
                var plusIndex = version.IndexOf('+');
                if (plusIndex > 0)
                {
                    version = version.Substring(0, plusIndex);
                }

                // Strip common suffixes like "-RELEASE", "-DEV", "-ALPHA", "-BETA"
                var dashIndex = version.IndexOf('-');
                if (dashIndex > 0)
                {
                    var suffix = version.Substring(dashIndex).ToUpperInvariant();
                    if (suffix == "-RELEASE" || suffix == "-DEV" || suffix == "-ALPHA" || suffix == "-BETA" || suffix.StartsWith("-RC"))
                    {
                        version = version.Substring(0, dashIndex);
                    }
                }

                return version;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
