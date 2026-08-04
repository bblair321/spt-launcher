using System;
using System.IO;

namespace SptLauncherWpf.Services
{
    public sealed class SelfUpdateCompletionResult
    {
        public bool ShowSuccessBanner { get; init; }
        public bool ShowFailureBanner { get; init; }
        public string DisplayVersion { get; init; } = "";
        public string? ExpectedVersion { get; init; }
        public bool BackupRemoved { get; init; }
    }

    /// <summary>
    /// Shared helpers for replace-in-place self-updates (validated download + update script).
    /// </summary>
    public static class UpdateApplyHelper
    {
        public static string GetBackupPath(string currentExePath)
        {
            var appDir = Path.GetDirectoryName(currentExePath) ?? "";
            var name = Path.GetFileNameWithoutExtension(currentExePath);
            return Path.Combine(appDir, $"{name}.old.exe");
        }

        public static bool TryRemoveBackup(string currentExePath)
        {
            try
            {
                var backupPath = GetBackupPath(currentExePath);
                if (!File.Exists(backupPath))
                {
                    return false;
                }

                File.Delete(backupPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool VersionsLookEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (Version.TryParse(left.TrimStart('v', 'V'), out var leftVersion) &&
                Version.TryParse(right.TrimStart('v', 'V'), out var rightVersion))
            {
                return leftVersion.Major == rightVersion.Major &&
                       leftVersion.Minor == rightVersion.Minor &&
                       Math.Max(leftVersion.Build, 0) == Math.Max(rightVersion.Build, 0);
            }

            return string.Equals(
                VersionStringHelper.Normalize(left),
                VersionStringHelper.Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatDisplayVersion(Version version) =>
            $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        public static string FormatDisplayVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return "";
            }

            if (Version.TryParse(version.TrimStart('v', 'V'), out var parsed))
            {
                return FormatDisplayVersion(parsed);
            }

            var normalized = VersionStringHelper.Normalize(version);
            return normalized.StartsWith('v') || normalized.StartsWith('V')
                ? normalized
                : "v" + normalized;
        }

        public static bool LooksLikeWindowsExecutable(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length < 64)
                {
                    return false;
                }

                using var stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[2];
                var read = stream.Read(header);
                return read == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a cmd script that waits for the launcher to exit, swaps the exe, and restores on failure.
        /// </summary>
        public static string BuildReplaceInPlaceScript(
            string processName,
            string currentExePath,
            string downloadedUpdatePath,
            string backupPath,
            string scriptPath,
            int maxWaitSeconds = 60)
        {
            if (maxWaitSeconds < 1)
            {
                maxWaitSeconds = 1;
            }

            return $"""
                @echo off
                setlocal EnableExtensions
                set "MAX_WAIT={maxWaitSeconds}"
                set "WAITED=0"
                :wait_for_exit
                tasklist /FI "IMAGENAME eq {processName}" 2>NUL | find /I /N "{processName}" >NUL
                if "%ERRORLEVEL%"=="0" (
                    if %WAITED% GEQ %MAX_WAIT% goto fail
                    timeout /t 1 /nobreak > nul
                    set /a WAITED+=1
                    goto wait_for_exit
                )
                del /f /q "{backupPath}" 2>nul
                move /y "{currentExePath}" "{backupPath}" >nul
                if errorlevel 1 goto fail
                move /y "{downloadedUpdatePath}" "{currentExePath}" >nul
                if errorlevel 1 (
                    move /y "{backupPath}" "{currentExePath}" >nul
                    goto fail
                )
                start "" "{currentExePath}"
                del /f /q "{scriptPath}" 2>nul
                exit /b 0
                :fail
                exit /b 1
                """;
        }
    }
}
