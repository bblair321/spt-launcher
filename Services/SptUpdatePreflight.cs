using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SptLauncherWpf.Services
{
    public class SptUpdatePreflightResult
    {
        public bool IsReady { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string[] RunningSptProcesses { get; set; } = Array.Empty<string>();
        public long EstimatedSptSizeBytes { get; set; }
        public long AvailableDiskBytes { get; set; }

        public string GetSummary()
        {
            var parts = new List<string>();
            if (Errors.Count > 0)
            {
                parts.Add(string.Join(Environment.NewLine, Errors));
            }

            if (Warnings.Count > 0)
            {
                parts.Add(string.Join(Environment.NewLine, Warnings));
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }
    }

    public static class SptUpdatePreflight
    {
        private static readonly string[] SptProcessNames =
        {
            "SPT.Server",
            "SPT.Launcher",
            "EscapeFromTarkov",
            "EscapeFromTarkov_BE"
        };

        public static SptUpdatePreflightResult Check(
            string sptPath,
            string? installerDownloadUrl,
            long? expectedInstallerBytes = null,
            bool requireBackupSpace = true,
            bool requireNoRunningProcesses = true,
            EftCompatibilityInfo? eftCompatibility = null,
            bool requireCompatibleEft = false)
        {
            var result = new SptUpdatePreflightResult();

            if (string.IsNullOrWhiteSpace(sptPath) || !Directory.Exists(sptPath))
            {
                result.Errors.Add("SPT installation directory was not found. Set the SPT launcher path first.");
            }

            if (string.IsNullOrWhiteSpace(installerDownloadUrl))
            {
                result.Errors.Add("No installer download URL is available for this update.");
            }
            else if (!Uri.TryCreate(installerDownloadUrl, UriKind.Absolute, out var uri) ||
                     (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                result.Errors.Add("Installer download URL is invalid.");
            }

            if (eftCompatibility != null)
            {
                switch (eftCompatibility.Status)
                {
                    case EftCompatibilityStatus.NotDetected:
                        var missingMessage =
                            "Live Escape From Tarkov was not detected. SPT install/update needs a valid up-to-date EFT install.";
                        if (requireCompatibleEft)
                        {
                            result.Errors.Add(missingMessage);
                        }
                        else
                        {
                            result.Warnings.Add(missingMessage);
                        }
                        break;

                    case EftCompatibilityStatus.UpdateRequired:
                        var updateMessage =
                            $"Live Tarkov is too old for the SPT downgrader " +
                            $"(installed: {eftCompatibility.InstalledVersion}, needed: {eftCompatibility.RequiredLiveVersion}). " +
                            "Update Tarkov through the official launcher before continuing.";
                        if (requireCompatibleEft)
                        {
                            result.Errors.Add(updateMessage);
                        }
                        else
                        {
                            result.Warnings.Add(updateMessage);
                        }
                        break;

                    case EftCompatibilityStatus.NewerThanSupported:
                        var newerMessage =
                            $"No downgrade patcher was found yet for live Tarkov {eftCompatibility.InstalledVersion} " +
                            $"(SPT target {eftCompatibility.TargetSptClientVersion ?? "unknown"}). " +
                            "Wait for SPT to publish a patcher for this Tarkov version, then try again. " +
                            "You can also use Download Installer Only and check the official installer.";
                        if (requireCompatibleEft)
                        {
                            result.Errors.Add(newerMessage);
                        }
                        else
                        {
                            result.Warnings.Add(newerMessage);
                        }
                        break;

                    case EftCompatibilityStatus.RequiredUnknown:
                        result.Warnings.Add(
                            "Could not determine the live Tarkov version required by the SPT downgrader. " +
                            "Make sure live EFT is fully updated before installing/updating SPT.");
                        break;
                }
            }

            result.RunningSptProcesses = GetRunningSptProcessNames();
            if (requireNoRunningProcesses && result.RunningSptProcesses.Length > 0)
            {
                result.Errors.Add(
                    "SPT-related processes are still running: " +
                    string.Join(", ", result.RunningSptProcesses) +
                    ". Stop them before updating.");
            }
            else if (!requireNoRunningProcesses && result.RunningSptProcesses.Length > 0)
            {
                result.Warnings.Add(
                    "SPT-related processes are running: " +
                    string.Join(", ", result.RunningSptProcesses) +
                    ". Close them before running the installer.");
            }

            if (!string.IsNullOrWhiteSpace(sptPath) && Directory.Exists(sptPath))
            {
                try
                {
                    result.EstimatedSptSizeBytes = EstimateDirectorySize(sptPath);
                    var root = Path.GetPathRoot(Path.GetTempPath());
                    if (!string.IsNullOrEmpty(root))
                    {
                        var drive = new DriveInfo(root);
                        result.AvailableDiskBytes = drive.AvailableFreeSpace;

                        var requiredBytes = Math.Max(expectedInstallerBytes ?? 200L * 1024 * 1024, 100L * 1024 * 1024);
                        if (requireBackupSpace)
                        {
                            // Full update may also create a backup of the SPT folder.
                            var sptRoot = Path.GetPathRoot(sptPath);
                            if (!string.IsNullOrEmpty(sptRoot) &&
                                string.Equals(sptRoot, root, StringComparison.OrdinalIgnoreCase))
                            {
                                requiredBytes += result.EstimatedSptSizeBytes;
                            }
                        }

                        if (drive.AvailableFreeSpace < requiredBytes)
                        {
                            result.Errors.Add(
                                $"Not enough free disk space on {root}. " +
                                $"Need about {FormatBytes(requiredBytes)} free " +
                                $"(available: {FormatBytes(drive.AvailableFreeSpace)}).");
                        }
                        else if (drive.AvailableFreeSpace < requiredBytes + (2L * 1024 * 1024 * 1024))
                        {
                            result.Warnings.Add(
                                $"Disk space is getting low on {root} " +
                                $"({FormatBytes(drive.AvailableFreeSpace)} free).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Could not fully check disk space: {ex.Message}");
                }
            }

            result.IsReady = result.Errors.Count == 0;
            return result;
        }

        public static string[] GetRunningSptProcessNames()
        {
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in SptProcessNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                    {
                        running.Add(name);
                    }

                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
                catch
                {
                    // Ignore process enumeration failures
                }
            }

            return running.OrderBy(n => n).ToArray();
        }

        public static long EstimateDirectorySize(string path)
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Skip inaccessible files
                    }
                }
            }
            catch
            {
                // Best effort
            }

            return size;
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]}";
        }
    }
}
