using System.Diagnostics;
using System.IO;

namespace SptLauncherWpf.Services
{
    public enum InstalledModKind
    {
        Server,
        Client
    }

    public sealed class InstalledModInfo
    {
        public string DisplayName { get; init; } = "";
        public string Path { get; init; } = "";
        public InstalledModKind Kind { get; init; }
        public bool IsEnabled { get; init; } = true;
        public bool IsDirectory { get; init; } = true;
        public string? VersionHint { get; init; }
        public int? ForgeModId { get; init; }
        public string? ForgeGuid { get; init; }
        public string? ForgeSlug { get; init; }
        public string? ForgeName { get; init; }
        public ForgeRecommendedVersion? AvailableUpdate { get; set; }

        /// <summary>
        /// All plugin files in this install unit (multi-DLL Forge packages).
        /// Empty means only <see cref="Path"/> applies.
        /// </summary>
        public IReadOnlyList<string> RelatedPaths { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> AllPaths =>
            RelatedPaths.Count > 0 ? RelatedPaths : new[] { Path };
    }

    /// <summary>
    /// Scans and manages installed SPT server/client mods under an install root.
    /// Disable/enable uses a ".disabled" name suffix (folders and loose plugin files).
    /// </summary>
    public static class InstalledModsService
    {
        public const string DisabledSuffix = ".disabled";

        public static IReadOnlyList<string> GetServerModsDirectories(string sptRoot)
        {
            var dirs = new List<string>();
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                return dirs;
            }

            var runtimeMods = Path.Combine(sptRoot, "SPT_Runtime", "user", "mods");
            var rootMods = Path.Combine(sptRoot, "user", "mods");

            if (Directory.Exists(runtimeMods))
            {
                dirs.Add(runtimeMods);
            }

            // Avoid listing the same folder twice when layouts overlap.
            if (Directory.Exists(rootMods) &&
                !dirs.Any(d => string.Equals(d, rootMods, StringComparison.OrdinalIgnoreCase)))
            {
                dirs.Add(rootMods);
            }

            return dirs;
        }

        public static IReadOnlyList<string> GetClientPluginDirectories(string sptRoot)
        {
            var dirs = new List<string>();
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                return dirs;
            }

            var plugins = Path.Combine(sptRoot, "BepInEx", "plugins");
            if (Directory.Exists(plugins))
            {
                dirs.Add(plugins);
            }

            return dirs;
        }

        public static List<InstalledModInfo> ScanInstalledMods(string sptRoot)
        {
            var results = new List<InstalledModInfo>();
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                return results;
            }

            foreach (var modsDir in GetServerModsDirectories(sptRoot))
            {
                foreach (var dir in SafeEnumerateDirectories(modsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name) || IsIgnoredServerFolder(name))
                    {
                        continue;
                    }

                    var enabled = !IsDisabledName(name);
                    var marker = ForgeModMarker.TryRead(dir, isDirectory: true);
                    results.Add(new InstalledModInfo
                    {
                        DisplayName = marker?.Name ?? StripDisabledSuffix(name),
                        Path = dir,
                        Kind = InstalledModKind.Server,
                        IsEnabled = enabled,
                        IsDirectory = true,
                        VersionHint = marker?.Version ?? TryReadServerModVersion(dir),
                        ForgeModId = marker is { ForgeModId: > 0 } ? marker.ForgeModId : null,
                        ForgeGuid = marker?.Guid,
                        ForgeSlug = marker?.Slug,
                        ForgeName = marker?.Name
                    });
                }
            }

            foreach (var pluginsDir in GetClientPluginDirectories(sptRoot))
            {
                foreach (var dir in SafeEnumerateDirectories(pluginsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(name) || IsIgnoredClientFolder(name))
                    {
                        continue;
                    }

                    var marker = ForgeModMarker.TryRead(dir, isDirectory: true);
                    results.Add(new InstalledModInfo
                    {
                        DisplayName = marker?.Name ?? StripDisabledSuffix(name),
                        Path = dir,
                        Kind = InstalledModKind.Client,
                        IsEnabled = !IsDisabledName(name),
                        IsDirectory = true,
                        VersionHint = marker?.Version,
                        ForgeModId = marker is { ForgeModId: > 0 } ? marker.ForgeModId : null,
                        ForgeGuid = marker?.Guid,
                        ForgeSlug = marker?.Slug,
                        ForgeName = marker?.Name
                    });
                }

                foreach (var file in SafeEnumerateFiles(pluginsDir))
                {
                    var name = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(name) || !IsPluginFile(name))
                    {
                        continue;
                    }

                    var marker = ForgeModMarker.TryRead(file, isDirectory: false);
                    var display = marker?.Name;
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        var baseName = IsDisabledName(name) ? StripDisabledSuffix(name) : name;
                        display = Path.GetFileNameWithoutExtension(baseName);
                    }

                    results.Add(new InstalledModInfo
                    {
                        DisplayName = display!,
                        Path = file,
                        Kind = InstalledModKind.Client,
                        IsEnabled = !IsDisabledName(name),
                        IsDirectory = false,
                        VersionHint = marker?.Version,
                        ForgeModId = marker is { ForgeModId: > 0 } ? marker.ForgeModId : null,
                        ForgeGuid = marker?.Guid,
                        ForgeSlug = marker?.Slug,
                        ForgeName = marker?.Name
                    });
                }
            }

            return CollapseLinkedClientPlugins(results)
                .OrderBy(m => m.Kind)
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Multi-DLL Forge installs write the same marker beside each loose plugin
        /// (e.g. Tyfon.UIFixes.dll + Tyfon.UIFixes.Net.dll). Collapse those into one row.
        /// </summary>
        internal static List<InstalledModInfo> CollapseLinkedClientPlugins(List<InstalledModInfo> mods)
        {
            var keep = new List<InstalledModInfo>();
            var loose = new List<InstalledModInfo>();

            foreach (var mod in mods)
            {
                if (mod.Kind == InstalledModKind.Client && !mod.IsDirectory)
                {
                    loose.Add(mod);
                }
                else
                {
                    keep.Add(mod);
                }
            }

            var groups = new Dictionary<string, List<InstalledModInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in loose)
            {
                var key = ClientPluginGroupKey(mod);
                if (key == null)
                {
                    keep.Add(WithRelatedPaths(mod, new[] { mod.Path }));
                    continue;
                }

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<InstalledModInfo>();
                    groups[key] = list;
                }

                list.Add(mod);
            }

            foreach (var group in groups.Values)
            {
                var ordered = group
                    .OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primary = ordered[0];
                var paths = ordered.Select(m => m.Path).ToList();
                keep.Add(new InstalledModInfo
                {
                    DisplayName = primary.DisplayName,
                    Path = primary.Path,
                    Kind = InstalledModKind.Client,
                    // Mixed enable state is rare; show enabled if any member is enabled.
                    IsEnabled = ordered.Any(m => m.IsEnabled),
                    IsDirectory = false,
                    VersionHint = ordered.Select(m => m.VersionHint).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                                  ?? primary.VersionHint,
                    ForgeModId = primary.ForgeModId,
                    ForgeGuid = primary.ForgeGuid,
                    ForgeSlug = primary.ForgeSlug,
                    ForgeName = primary.ForgeName,
                    AvailableUpdate = primary.AvailableUpdate,
                    RelatedPaths = paths
                });
            }

            return keep;
        }

        private static string? ClientPluginGroupKey(InstalledModInfo mod)
        {
            if (mod.ForgeModId is int id and > 0)
            {
                return "id:" + id;
            }

            if (!string.IsNullOrWhiteSpace(mod.ForgeGuid))
            {
                return "guid:" + mod.ForgeGuid.Trim();
            }

            return null;
        }

        private static InstalledModInfo WithRelatedPaths(InstalledModInfo mod, IReadOnlyList<string> paths) =>
            new()
            {
                DisplayName = mod.DisplayName,
                Path = mod.Path,
                Kind = mod.Kind,
                IsEnabled = mod.IsEnabled,
                IsDirectory = mod.IsDirectory,
                VersionHint = mod.VersionHint,
                ForgeModId = mod.ForgeModId,
                ForgeGuid = mod.ForgeGuid,
                ForgeSlug = mod.ForgeSlug,
                ForgeName = mod.ForgeName,
                AvailableUpdate = mod.AvailableUpdate,
                RelatedPaths = paths
            };

        public static bool IsInstalledMatch(InstalledModInfo installed, ForgeModSummary forge)
        {
            if (installed.ForgeModId is int id && id == forge.Id)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(installed.ForgeGuid) &&
                !string.IsNullOrWhiteSpace(forge.Guid) &&
                string.Equals(installed.ForgeGuid, forge.Guid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(installed.ForgeSlug) &&
                string.Equals(installed.ForgeSlug, forge.Slug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var installedKey = NormalizeModKey(installed.DisplayName);
            var forgeNameKey = NormalizeModKey(forge.Name);
            var forgeSlugKey = NormalizeModKey(forge.Slug.Replace('-', ' '));
            var folderKey = NormalizeModKey(Path.GetFileName(installed.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

            return (!string.IsNullOrEmpty(installedKey) && installedKey == forgeNameKey)
                   || (!string.IsNullOrEmpty(folderKey) && (folderKey == forgeNameKey || folderKey == forgeSlugKey));
        }

        public static string NormalizeModKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            var chars = value
                .Where(ch => char.IsLetterOrDigit(ch))
                .Select(char.ToLowerInvariant)
                .ToArray();
            return new string(chars);
        }

        public static IEnumerable<string> BuildUpdateQueryPairs(IEnumerable<InstalledModInfo> mods)
        {
            foreach (var mod in mods)
            {
                if (string.IsNullOrWhiteSpace(mod.VersionHint))
                {
                    continue;
                }

                if (mod.ForgeModId is int id and > 0)
                {
                    yield return $"{id}:{mod.VersionHint}";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(mod.ForgeGuid))
                {
                    yield return $"{mod.ForgeGuid}:{mod.VersionHint}";
                }
            }
        }

        public static bool IsDisabledName(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);

        public static string StripDisabledSuffix(string name)
        {
            if (IsDisabledName(name))
            {
                return name[..^DisabledSuffix.Length];
            }

            return name;
        }

        public static string GetDisabledPath(string path)
        {
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            if (IsDisabledName(name))
            {
                return full;
            }

            var parent = Path.GetDirectoryName(full) ?? "";
            return Path.Combine(parent, name + DisabledSuffix);
        }

        public static string GetEnabledPath(string path)
        {
            var full = Path.GetFullPath(path);
            var name = Path.GetFileName(full);
            if (!IsDisabledName(name))
            {
                return full;
            }

            var parent = Path.GetDirectoryName(full) ?? "";
            return Path.Combine(parent, StripDisabledSuffix(name));
        }

        public static InstalledModInfo SetEnabled(InstalledModInfo mod, bool enabled)
        {
            if (mod.IsEnabled == enabled)
            {
                return mod;
            }

            if (mod.IsDirectory || mod.AllPaths.Count <= 1)
            {
                var source = mod.Path;
                var destination = enabled ? GetEnabledPath(source) : GetDisabledPath(source);
                MoveInstalledPath(mod.IsDirectory, source, destination, mod.DisplayName, enabled);
                return CloneWithPath(mod, destination, enabled, new[] { destination });
            }

            // Multi-DLL package: enable/disable every related loose plugin together.
            var moved = new List<string>();
            foreach (var source in mod.AllPaths)
            {
                var destination = enabled ? GetEnabledPath(source) : GetDisabledPath(source);
                MoveInstalledPath(isDirectory: false, source, destination, mod.DisplayName, enabled);
                moved.Add(destination);
            }

            moved.Sort(StringComparer.OrdinalIgnoreCase);
            return CloneWithPath(mod, moved[0], enabled, moved);
        }

        private static void MoveInstalledPath(
            bool isDirectory,
            string source,
            string destination,
            string displayName,
            bool enabled)
        {
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new IOException(
                    $"Cannot {(enabled ? "enable" : "disable")} \"{displayName}\" because \"{Path.GetFileName(destination)}\" already exists.");
            }

            if (isDirectory)
            {
                Directory.Move(source, destination);
                return;
            }

            File.Move(source, destination);
            MoveLoosePluginMarker(source, destination);
        }

        private static void MoveLoosePluginMarker(string sourceFile, string destinationFile)
        {
            var srcMarker = ForgeModMarker.GetMarkerPathForFile(sourceFile);
            if (!File.Exists(srcMarker))
            {
                return;
            }

            var dstMarker = ForgeModMarker.GetMarkerPathForFile(destinationFile);
            try
            {
                if (File.Exists(dstMarker))
                {
                    File.Delete(dstMarker);
                }

                File.Move(srcMarker, dstMarker);
            }
            catch
            {
                // Best-effort; plugin rename already succeeded.
            }
        }

        private static InstalledModInfo CloneWithPath(
            InstalledModInfo mod,
            string path,
            bool enabled,
            IReadOnlyList<string> relatedPaths) =>
            new()
            {
                DisplayName = mod.DisplayName,
                Path = path,
                Kind = mod.Kind,
                IsEnabled = enabled,
                IsDirectory = mod.IsDirectory,
                VersionHint = mod.VersionHint,
                ForgeModId = mod.ForgeModId,
                ForgeGuid = mod.ForgeGuid,
                ForgeSlug = mod.ForgeSlug,
                ForgeName = mod.ForgeName,
                AvailableUpdate = mod.AvailableUpdate,
                RelatedPaths = relatedPaths
            };

        public static void Uninstall(InstalledModInfo mod)
        {
            if (mod.IsDirectory)
            {
                if (Directory.Exists(mod.Path))
                {
                    Directory.Delete(mod.Path, recursive: true);
                }

                return;
            }

            foreach (var path in mod.AllPaths)
            {
                TryDeleteLoosePlugin(path);
            }
        }

        private static void TryDeleteLoosePlugin(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var markerPath = ForgeModMarker.GetMarkerPathForFile(path);
            if (File.Exists(markerPath))
            {
                try
                {
                    File.Delete(markerPath);
                }
                catch
                {
                    // Best-effort; the plugin itself is already gone.
                }
            }
        }

        public static void OpenInExplorer(InstalledModInfo mod)
        {
            if (!mod.IsDirectory && mod.AllPaths.Count > 1)
            {
                var dir = Path.GetDirectoryName(mod.Path);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            var target = mod.Path;
            if (mod.IsDirectory)
            {
                if (!Directory.Exists(target))
                {
                    throw new DirectoryNotFoundException(target);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{target}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (!File.Exists(target))
            {
                throw new FileNotFoundException("Mod file not found.", target);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{target}\"",
                UseShellExecute = true
            });
        }

        private static bool IsPluginFile(string fileName)
        {
            var working = IsDisabledName(fileName) ? StripDisabledSuffix(fileName) : fileName;
            return working.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIgnoredServerFolder(string name) =>
            name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);

        private static bool IsIgnoredClientFolder(string name) =>
            name.Equals("cache", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try
            {
                return Directory.EnumerateDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string? TryReadServerModVersion(string modDir)
        {
            try
            {
                var packageJson = Path.Combine(modDir, "package.json");
                if (!File.Exists(packageJson))
                {
                    return null;
                }

                var text = File.ReadAllText(packageJson);
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
                }
            }
            catch
            {
                // ignore version probe failures
            }

            return null;
        }
    }
}
