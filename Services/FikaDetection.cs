using System.IO;
using System.Text.Json;

namespace SptLauncherWpf.Services
{
    internal static class FikaDetection
    {
        public static bool IsFikaCoreOrServerDll(string? fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^".disabled".Length];
            }

            return name.Equals("Fika.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Core.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Dedicated.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Server.dll", StringComparison.OrdinalIgnoreCase);
        }

        public static bool FolderHasFikaClientDll(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            return File.Exists(Path.Combine(folder, "Fika.Core.dll"))
                || File.Exists(Path.Combine(folder, "Fika.dll"))
                || File.Exists(Path.Combine(folder, "Fika.Dedicated.dll"));
        }

        public static bool FolderHasCompleteFikaServer(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            var packageJson = Path.Combine(folder, "package.json");
            if (File.Exists(packageJson) && PackageJsonLooksLikeFika(packageJson))
            {
                return true;
            }

            try
            {
                return Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories)
                    .Any(IsFikaCoreOrServerDll);
            }
            catch
            {
                return false;
            }
        }

        public static bool PackageJsonLooksLikeFika(string packageJsonPath)
        {
            try
            {
                var json = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
                if (json.RootElement.TryGetProperty("name", out var nameElement))
                {
                    var name = nameElement.GetString() ?? "";
                    if (name.Contains("fika", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (json.RootElement.TryGetProperty("id", out var idElement))
                {
                    var id = idElement.GetString() ?? "";
                    if (id.Contains("fika", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore unreadable json
            }

            return false;
        }
    }
}
