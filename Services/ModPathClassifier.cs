namespace SptLauncherWpf.Services
{
    public enum ModInstallKind
    {
        Unknown,
        ServerOnly,
        ClientOnly,
        Mixed
    }

    public sealed class ModPathClassification
    {
        public ModInstallKind Kind { get; init; } = ModInstallKind.Unknown;
        public bool HasServerPaths { get; init; }
        public bool HasClientPaths { get; init; }
        public bool HasExtraRootFiles { get; init; }
        public bool CanAutoInstall => Kind is not ModInstallKind.Unknown;
        public IReadOnlyList<string> InstallableRelativePaths { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> SkippedPaths { get; init; } = Array.Empty<string>();
        public string Summary { get; init; } = "";
    }

    /// <summary>
    /// Classifies Forge archive paths into server (user/mods) vs client (BepInEx) targets.
    /// </summary>
    public static class ModPathClassifier
    {
        public static ModPathClassification Classify(
            IEnumerable<string> archivePaths,
            bool installHasSptRuntime)
        {
            var normalized = archivePaths
                .Select(NormalizeArchivePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var installable = new List<string>();
            var skipped = new List<string>();
            var hasServer = false;
            var hasClient = false;
            var hasExtraRoot = false;

            foreach (var path in normalized)
            {
                if (IsDirectoryMarker(path))
                {
                    continue;
                }

                if (TryMapToInstallRelative(path, installHasSptRuntime, out var mapped, out var kind))
                {
                    installable.Add(mapped);
                    if (kind == ModInstallKind.ServerOnly)
                    {
                        hasServer = true;
                    }
                    else if (kind == ModInstallKind.ClientOnly)
                    {
                        hasClient = true;
                    }
                }
                else if (IsRootLevelFile(path))
                {
                    // Root extras (e.g. SVM's Greed.exe) install at SPT root only when we also
                    // have recognized mod paths; otherwise the archive is ambiguous.
                    hasExtraRoot = true;
                    skipped.Add(path);
                }
                else
                {
                    skipped.Add(path);
                }
            }

            // If we have known install targets, also place root-level extras at install root.
            if ((hasServer || hasClient) && hasExtraRoot)
            {
                foreach (var extra in skipped.Where(IsRootLevelFile).ToList())
                {
                    installable.Add(extra);
                    skipped.RemoveAll(p => string.Equals(p, extra, StringComparison.OrdinalIgnoreCase));
                }
            }

            installable = installable
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var kindResult = (hasServer, hasClient) switch
            {
                (true, true) => ModInstallKind.Mixed,
                (true, false) => ModInstallKind.ServerOnly,
                (false, true) => ModInstallKind.ClientOnly,
                _ => ModInstallKind.Unknown
            };

            var summary = kindResult switch
            {
                ModInstallKind.ServerOnly => "Server mod (user/mods)",
                ModInstallKind.ClientOnly => "Client mod (BepInEx)",
                ModInstallKind.Mixed => "Client + server package",
                _ => "Unknown layout — open on Forge instead of auto-install"
            };

            return new ModPathClassification
            {
                Kind = kindResult,
                HasServerPaths = hasServer,
                HasClientPaths = hasClient,
                HasExtraRootFiles = hasExtraRoot,
                InstallableRelativePaths = installable,
                SkippedPaths = skipped,
                Summary = summary
            };
        }

        /// <summary>
        /// Keeps only paths that install under BepInEx (for required-client pack sync).
        /// </summary>
        public static IReadOnlyList<string> FilterClientInstallPaths(IEnumerable<string> installRelativePaths)
        {
            return installRelativePaths
                .Select(NormalizeArchivePath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && StartsWithSegment(p, "BepInEx"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Maps a single archive path to a destination relative to the SPT install root.
        /// </summary>
        public static bool TryMapToInstallRelative(
            string archivePath,
            bool installHasSptRuntime,
            out string installRelativePath,
            out ModInstallKind kind)
        {
            installRelativePath = "";
            kind = ModInstallKind.Unknown;
            var path = NormalizeArchivePath(archivePath);
            if (string.IsNullOrWhiteSpace(path) || IsDirectoryMarker(path))
            {
                return false;
            }

            // Strip a single leading folder if the zip wraps everything (common).
            // Only strip when the wrap is NOT already a known root.
            path = MaybeUnwrapSingleRoot(path);

            // Archives often nest under a literal "SPT/" folder (Forge file trees do this too).
            if (StartsWithSegment(path, "SPT") && !StartsWithSegment(path, "SPT_Runtime"))
            {
                var afterSpt = TrimPrefix(path, "SPT/");
                if (StartsWithSegment(afterSpt, "BepInEx") ||
                    StartsWithSegment(afterSpt, "user/mods") ||
                    StartsWithSegment(afterSpt, "user\\mods") ||
                    StartsWithSegment(afterSpt, "SPT_Runtime"))
                {
                    path = afterSpt;
                }
            }

            if (StartsWithSegment(path, "BepInEx"))
            {
                installRelativePath = path;
                kind = ModInstallKind.ClientOnly;
                return true;
            }

            if (StartsWithSegment(path, "SPT_Runtime"))
            {
                var afterRuntime = TrimPrefix(path, "SPT_Runtime/");
                if (StartsWithSegment(afterRuntime, "user/mods") ||
                    StartsWithSegment(afterRuntime, "user\\mods"))
                {
                    kind = ModInstallKind.ServerOnly;
                    installRelativePath = installHasSptRuntime
                        ? path
                        : afterRuntime.Replace('\\', '/');
                    return true;
                }

                // Other SPT_Runtime content (rare) — keep if runtime exists, else strip.
                kind = ModInstallKind.ServerOnly;
                installRelativePath = installHasSptRuntime ? path : afterRuntime.Replace('\\', '/');
                return StartsWithSegment(afterRuntime, "user");
            }

            if (StartsWithSegment(path, "user/mods") || StartsWithSegment(path, "user\\mods"))
            {
                kind = ModInstallKind.ServerOnly;
                installRelativePath = installHasSptRuntime
                    ? "SPT_Runtime/" + path.Replace('\\', '/')
                    : path.Replace('\\', '/');
                return true;
            }

            return false;
        }

        public static string NormalizeArchivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            var normalized = path.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            return normalized.TrimStart('/');
        }

        private static bool IsDirectoryMarker(string path) =>
            path.EndsWith("/", StringComparison.Ordinal);

        private static bool IsRootLevelFile(string path)
        {
            var normalized = NormalizeArchivePath(path);
            return !string.IsNullOrWhiteSpace(normalized)
                   && !normalized.Contains('/')
                   && !IsDirectoryMarker(normalized);
        }

        private static bool StartsWithSegment(string path, string prefix)
        {
            var p = path.Replace('\\', '/');
            var pre = prefix.Replace('\\', '/');
            return p.Equals(pre, StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith(pre.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimPrefix(string path, string prefix)
        {
            var p = path.Replace('\\', '/');
            var pre = prefix.Replace('\\', '/');
            return p.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
                ? p[pre.Length..]
                : p;
        }

        private static string MaybeUnwrapSingleRoot(string path)
        {
            // Do not unwrap known roots.
            if (StartsWithSegment(path, "BepInEx")
                || StartsWithSegment(path, "SPT_Runtime")
                || StartsWithSegment(path, "user")
                || (StartsWithSegment(path, "SPT") && !StartsWithSegment(path, "SPT_Runtime")))
            {
                return path;
            }

            var slash = path.IndexOf('/');
            if (slash <= 0)
            {
                return path;
            }

            var remainder = path[(slash + 1)..];
            if (StartsWithSegment(remainder, "BepInEx")
                || StartsWithSegment(remainder, "SPT_Runtime")
                || StartsWithSegment(remainder, "user/mods")
                || (StartsWithSegment(remainder, "SPT") && !StartsWithSegment(remainder, "SPT_Runtime")))
            {
                return remainder;
            }

            return path;
        }
    }
}
