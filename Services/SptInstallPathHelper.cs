using System.IO;

namespace SptLauncherWpf.Services
{
    /// <summary>
    /// Resolves the SPT install root and detected version from the configured launcher path.
    /// </summary>
    public static class SptInstallPathHelper
    {
        public static bool TryResolveFromLauncherPath(
            string? launcherPath,
            out string sptRoot,
            out string? sptVersion,
            out string error)
        {
            sptRoot = "";
            sptVersion = null;
            error = "";

            if (string.IsNullOrWhiteSpace(launcherPath))
            {
                error = "No SPT.Launcher.exe path is set. Use the Launcher tab to Auto-detect or Browse.";
                return false;
            }

            if (!File.Exists(launcherPath))
            {
                error = $"Launcher path does not exist:\n{launcherPath}";
                return false;
            }

            var launcherDir = Path.GetDirectoryName(launcherPath);
            if (string.IsNullOrWhiteSpace(launcherDir))
            {
                error = "Could not determine the folder for SPT.Launcher.exe.";
                return false;
            }

            sptRoot = SptDetectionService.ResolveSptRootDirectory(launcherDir);
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                error = "Could not resolve the SPT install root from the launcher path.";
                return false;
            }

            try
            {
                sptVersion = SptDetectionService.Instance.GetSptVersion(launcherPath);
                if (string.IsNullOrWhiteSpace(sptVersion) ||
                    sptVersion.Equals("Not detected", StringComparison.OrdinalIgnoreCase))
                {
                    sptVersion = null;
                }
            }
            catch
            {
                sptVersion = null;
            }

            return true;
        }

        public static bool InstallHasSptRuntime(string sptRoot) =>
            !string.IsNullOrWhiteSpace(sptRoot)
            && Directory.Exists(Path.Combine(sptRoot, "SPT_Runtime"));
    }
}
