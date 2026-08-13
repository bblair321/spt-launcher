using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;

namespace SptLauncherWpf.Services
{
    public sealed class RequiredModsPackService
    {
        private static RequiredModsPackService? _instance;
        public static RequiredModsPackService Instance => _instance ??= new RequiredModsPackService();

        public const int DefaultSptHttpsPort = 6969;
        public const int LanAgentHttpPort = 17865;
        public const string DefaultPackPath = "/mod-pack";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;

        private RequiredModsPackService()
        {
            // SPT Kestrel serves HTTPS with a self-signed cert — same trust model as talking to the game server.
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SPTLauncher/4.2 (+https://github.com/bblair321/spt-launcher)");
        }

        /// <summary>
        /// Builds the pack fetch URL from Settings: explicit URL wins; otherwise derive from host.
        /// Bare host → https://{host}:6969/mod-pack. Port 17865 → http (LAN agent fallback).
        /// </summary>
        public static string GetConfiguredPackUrl()
        {
            var explicitUrl = SettingsService.Instance.RequiredModsPackUrl?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(explicitUrl))
            {
                return NormalizePackUrl(explicitUrl) ?? explicitUrl;
            }

            var host = SettingsService.Instance.RequiredModsServerHost?.Trim() ?? "";
            return TryResolvePackUrl(host) ?? "";
        }

        /// <summary>
        /// Accepts a full http(s) URL, or host / host:port, and returns a pack URL.
        /// </summary>
        public static string? TryResolvePackUrl(string? hostOrUrl)
        {
            if (string.IsNullOrWhiteSpace(hostOrUrl))
            {
                return null;
            }

            var input = hostOrUrl.Trim();

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePackUrl(input);
            }

            // host or host:port (IPv4 / hostname; bracketed IPv6 with port also supported)
            string host;
            int? port = null;

            if (input.StartsWith('[') && input.Contains(']'))
            {
                var close = input.IndexOf(']');
                host = input[..(close + 1)];
                if (close + 1 < input.Length && input[close + 1] == ':' &&
                    int.TryParse(input[(close + 2)..], out var p6))
                {
                    port = p6;
                }
            }
            else
            {
                var colon = input.LastIndexOf(':');
                // Treat as host:port only when a single colon and numeric port (avoid IPv6 without brackets)
                if (colon > 0 && input.IndexOf(':') == colon &&
                    int.TryParse(input[(colon + 1)..], out var p))
                {
                    host = input[..colon];
                    port = p;
                }
                else
                {
                    host = input;
                }
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                return null;
            }

            port ??= DefaultSptHttpsPort;

            // LAN agent fallback (plain HTTP on 17865)
            if (port == LanAgentHttpPort)
            {
                return $"http://{host}:{LanAgentHttpPort}{DefaultPackPath}";
            }

            return $"https://{host}:{port}{DefaultPackPath}";
        }

        /// <summary>
        /// Ensures an absolute pack URL has a path (defaults to /mod-pack).
        /// </summary>
        public static string? NormalizePackUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
            {
                var builder = new UriBuilder(uri) { Path = DefaultPackPath };
                return builder.Uri.ToString().TrimEnd('/');
            }

            return uri.ToString();
        }

        public async Task<RequiredModsPack> FetchAsync(
            string url,
            string? agentToken = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    "No pack URL configured. Enter the game server host (→ https://host:6969/mod-pack) or a full pack URL.");
            }

            var normalized = NormalizePackUrl(url) ?? url.Trim();
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    "Pack URL must be an absolute http(s) address, e.g. https://SERVER:6969/mod-pack");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (!string.IsNullOrWhiteSpace(agentToken))
                {
                    request.Headers.TryAddWithoutValidation("X-Agent-Token", agentToken.Trim());
                }

                using var response = await _http.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Pack request to {uri} failed ({(int)response.StatusCode} {response.ReasonPhrase}). " +
                        "Confirm the server manager is publishing /mod-pack on the SPT HTTPS port.");
                }

                if (string.IsNullOrWhiteSpace(body))
                {
                    throw new InvalidOperationException($"Pack response from {uri} was empty.");
                }

                return Parse(body);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Timed out fetching pack from {uri}. Is the game server reachable on that host/port?",
                    ex);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach pack at {uri}. " +
                    "Use HTTPS on port 6969 (self-signed SPT cert is accepted). " +
                    "LAN fallback: http://host:17865/mod-pack. " +
                    $"Details: {ex.Message}",
                    ex);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException)
            {
                throw new InvalidOperationException(
                    $"TLS/network error fetching pack from {uri}: {ex.Message}",
                    ex);
            }
        }

        public RequiredModsPack Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Pack JSON is empty.");
            }

            RequiredModsPack? pack;
            try
            {
                pack = JsonSerializer.Deserialize<RequiredModsPack>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Could not parse pack JSON: {ex.Message}", ex);
            }

            if (pack == null)
            {
                throw new InvalidOperationException("Pack JSON deserialized to null.");
            }

            pack.Mods ??= new List<RequiredModEntry>();
            return pack;
        }

        public RequiredModsDiffResult Diff(RequiredModsPack pack, IEnumerable<InstalledModInfo> installedMods)
        {
            var clientMods = installedMods
                .Where(m => m.Kind == InstalledModKind.Client)
                .ToList();

            var items = new List<RequiredModDiffItem>();
            var matchedLocal = new HashSet<InstalledModInfo>();

            foreach (var entry in pack.Mods ?? Enumerable.Empty<RequiredModEntry>())
            {
                if (!entry.CanAutoInstall)
                {
                    items.Add(new RequiredModDiffItem
                    {
                        Status = RequiredModDiffStatus.ManualFix,
                        PackEntry = entry,
                        Message =
                            $"{entry.DisplayName}: missing forgeModId — cannot auto-download from Forge" +
                            (string.IsNullOrWhiteSpace(entry.Guid) ? "." : $" (guid {entry.Guid}).")
                    });
                    continue;
                }

                var local = FindLocalMatch(entry, clientMods);
                if (local == null)
                {
                    items.Add(new RequiredModDiffItem
                    {
                        Status = RequiredModDiffStatus.Missing,
                        PackEntry = entry,
                        Message = $"{entry.DisplayName} {entry.Version ?? ""}".Trim() + " — missing"
                    });
                    continue;
                }

                matchedLocal.Add(local);
                var requiredVersion = (entry.Version ?? "").Trim();
                var localVersion = (local.VersionHint ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(requiredVersion) &&
                    (string.IsNullOrWhiteSpace(localVersion) || !VersionsEqual(requiredVersion, localVersion)))
                {
                    items.Add(new RequiredModDiffItem
                    {
                        Status = RequiredModDiffStatus.WrongVersion,
                        PackEntry = entry,
                        Installed = local,
                        Message =
                            $"{entry.DisplayName}: have {localVersion.IfEmpty("unknown")}, need {requiredVersion}"
                    });
                    continue;
                }

                items.Add(new RequiredModDiffItem
                {
                    Status = RequiredModDiffStatus.Ok,
                    PackEntry = entry,
                    Installed = local,
                    Message = $"{entry.DisplayName} OK" +
                              (string.IsNullOrWhiteSpace(localVersion) ? "" : $" ({localVersion})")
                });
            }

            foreach (var local in clientMods.Where(m => !matchedLocal.Contains(m)))
            {
                items.Add(new RequiredModDiffItem
                {
                    Status = RequiredModDiffStatus.Extra,
                    Installed = local,
                    Message = $"{local.DisplayName} — installed, not required by pack"
                });
            }

            return new RequiredModsDiffResult
            {
                Pack = pack,
                Items = items
            };
        }

        public async Task<RequiredModsSyncReport> SyncMissingAsync(
            RequiredModsPack pack,
            string sptRoot,
            IProgress<RequiredModsSyncProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var installed = InstalledModsService.ScanInstalledMods(sptRoot);
            var before = Diff(pack, installed);
            var toSync = before.Items
                .Where(i => i.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion)
                .Where(i => i.PackEntry != null)
                .Select(i => i.PackEntry!)
                .ToList();

            if (toSync.Count == 0)
            {
                return new RequiredModsSyncReport
                {
                    Success = true,
                    Message = before.ManualFixCount > 0
                        ? "Nothing to auto-install. Some mods are missing forgeModId and need a manual fix."
                        : "All required client mods are already installed.",
                    DiffAfter = before
                };
            }

            var errors = new List<string>();
            var installedCount = 0;
            var skippedServerOnly = 0;
            var failed = 0;

            for (var i = 0; i < toSync.Count; i++)
            {
                var entry = toSync[i];
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new RequiredModsSyncProgress
                {
                    Current = i + 1,
                    Total = toSync.Count,
                    Message = $"Syncing {entry.DisplayName} ({i + 1}/{toSync.Count})…"
                });

                try
                {
                    var result = await InstallPackEntryAsync(entry, sptRoot, progress, cancellationToken);
                    if (result.SkippedServerOnly)
                    {
                        skippedServerOnly++;
                        errors.Add($"{entry.DisplayName}: server-only package — skipped for client sync.");
                    }
                    else if (result.Success)
                    {
                        // Re-scan this mod immediately — InstallAsync used to report success
                        // even when 0 files extracted / marker stayed on the old version.
                        var midScan = InstalledModsService.ScanInstalledMods(sptRoot);
                        var midDiff = Diff(
                            new RequiredModsPack { Mods = new List<RequiredModEntry> { entry } },
                            midScan);
                        var stillWrong = midDiff.Items.Any(i =>
                            i.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion);
                        if (stillWrong)
                        {
                            failed++;
                            var detail = midDiff.Items.FirstOrDefault(i =>
                                i.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion);
                            errors.Add(
                                $"{entry.DisplayName}: install reported OK but still " +
                                $"{detail?.Message ?? "not matching pack version"}. " +
                                "Close EscapeFromTarkov/SPT if running, then sync again — or install 1.6.0 manually from Forge.");
                        }
                        else
                        {
                            installedCount++;
                        }
                    }
                    else
                    {
                        failed++;
                        errors.Add($"{entry.DisplayName}: {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{entry.DisplayName}: {ex.Message}");
                }
            }

            var afterInstalled = InstalledModsService.ScanInstalledMods(sptRoot);
            var after = Diff(pack, afterInstalled);
            var success = !after.NeedsSync;

            var parts = new List<string>();
            if (installedCount > 0)
            {
                parts.Add($"Installed/updated {installedCount}");
            }

            if (skippedServerOnly > 0)
            {
                parts.Add($"skipped {skippedServerOnly} server-only");
            }

            if (failed > 0)
            {
                parts.Add($"{failed} failed");
            }

            if (after.NeedsSync)
            {
                parts.Add($"still missing {after.MissingCount}, wrong version {after.WrongVersionCount}");
            }
            else if (after.ManualFixCount > 0)
            {
                parts.Add($"{after.ManualFixCount} need manual install");
            }
            else
            {
                parts.Add("client pack ready");
            }

            return new RequiredModsSyncReport
            {
                Success = success,
                Message = string.Join(" · ", parts),
                InstalledCount = installedCount,
                SkippedServerOnlyCount = skippedServerOnly,
                FailedCount = failed,
                Errors = errors,
                DiffAfter = after
            };
        }

        private async Task<(bool Success, bool SkippedServerOnly, string Error)> InstallPackEntryAsync(
            RequiredModEntry entry,
            string sptRoot,
            IProgress<RequiredModsSyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (entry.ForgeModId is not int forgeId || forgeId <= 0)
            {
                return (false, false,
                    "Missing forgeModId — cannot download from Forge. Install this client mod manually.");
            }

            var mod = await ResolveModAsync(entry, cancellationToken);
            if (mod == null)
            {
                return (false, false,
                    $"Could not find Forge mod id {forgeId} on sp-mod.com.");
            }

            var versions = await ForgeApiService.Instance.GetModVersionsAsync(
                mod.Id,
                sptVersion: null,
                cancellationToken: cancellationToken);

            if (versions.Count == 0)
            {
                versions = mod.Versions ?? new List<ForgeModVersion>();
            }

            var version = PickVersion(versions, entry.Version);
            if (version == null || string.IsNullOrWhiteSpace(version.Link))
            {
                return (false, false,
                    string.IsNullOrWhiteSpace(entry.Version)
                        ? "No downloadable version found on Forge."
                        : $"Version {entry.Version} not found on sp-mod.com for mod id {forgeId}.");
            }

            ForgeFileTree? tree = null;
            try
            {
                tree = await ForgeApiService.Instance.GetFileTreeAsync(mod.Id, version.Id, cancellationToken);
            }
            catch
            {
                tree = null;
            }

            var hasRuntime = Directory.Exists(Path.Combine(sptRoot, "SPT_Runtime"));
            // Don't reject on Forge file-tree alone — the real zip/DLL layout is authoritative.
            if (tree?.Files is { Count: > 0 })
            {
                var classification = ModPathClassifier.Classify(tree.Files, hasRuntime);
                if (classification.Kind == ModInstallKind.ServerOnly)
                {
                    return (false, true, "Server-only package");
                }
            }

            var installProgress = new Progress<ModInstallProgress>(p =>
            {
                progress?.Report(new RequiredModsSyncProgress
                {
                    Message = $"{entry.DisplayName}: {p.Message}"
                });
            });

            var report = await ModInstallService.Instance.InstallAsync(
                mod,
                version,
                sptRoot,
                tree?.Files,
                installProgress,
                cancellationToken,
                clientPathsOnly: true);

            if (!report.Success)
            {
                return (false, false, report.Message);
            }

            // Pack Diff keys off .forge-mod.json sidecars. Always stamp the matched
            // local plugin(s) to the version we just installed so upgrades can't leave
            // a stale "have 1.1.0, need 1.6.0" marker on an older DLL name.
            StampMatchedClientMarkers(sptRoot, entry, mod, version);

            return (true, false, "");
        }

        private static void StampMatchedClientMarkers(
            string sptRoot,
            RequiredModEntry entry,
            ForgeModSummary mod,
            ForgeModVersion version)
        {
            var marker = new ForgeModMarker
            {
                ForgeModId = mod.Id,
                Guid = !string.IsNullOrWhiteSpace(entry.Guid) ? entry.Guid : mod.Guid,
                Slug = !string.IsNullOrWhiteSpace(entry.Slug) ? entry.Slug : mod.Slug,
                Name = !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : mod.Name,
                Version = version.Version,
                VersionId = version.Id,
                InstalledAtUtc = DateTime.UtcNow
            };

            var scanned = InstalledModsService.ScanInstalledMods(sptRoot);
            var clientMods = scanned.Where(m => m.Kind == InstalledModKind.Client).ToList();
            var match = FindLocalMatch(entry, clientMods);

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (match != null)
            {
                foreach (var path in match.AllPaths)
                {
                    targets.Add(path);
                }
            }

            // Also stamp any plugin whose existing marker already points at this Forge mod.
            foreach (var local in clientMods)
            {
                if (local.ForgeModId == mod.Id ||
                    (!string.IsNullOrWhiteSpace(marker.Guid) &&
                     string.Equals(local.ForgeGuid, marker.Guid, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var path in local.AllPaths)
                    {
                        targets.Add(path);
                    }
                }
            }

            foreach (var path in targets)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        ForgeModMarker.Write(path, isDirectory: true, marker);
                    }
                    else if (File.Exists(path))
                    {
                        ForgeModMarker.Write(path, isDirectory: false, marker);
                    }
                }
                catch
                {
                    // best-effort
                }
            }
        }

        private static async Task<ForgeModSummary?> ResolveModAsync(
            RequiredModEntry entry,
            CancellationToken cancellationToken)
        {
            if (entry.ForgeModId is int id and > 0)
            {
                try
                {
                    return await ForgeApiService.Instance.GetModAsync(id, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Forge lookup failed for mod id {id}: {ex.Message}", ex);
                }
            }

            if (string.IsNullOrWhiteSpace(entry.Slug) && string.IsNullOrWhiteSpace(entry.Name))
            {
                return null;
            }

            var query = !string.IsNullOrWhiteSpace(entry.Slug) ? entry.Slug! : entry.Name!;
            try
            {
                var page = await ForgeApiService.Instance.SearchModsAsync(
                    query: query,
                    sptVersion: null,
                    page: 1,
                    perPage: 25,
                    cancellationToken: cancellationToken);

                var slug = (entry.Slug ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    var bySlug = page.Mods.FirstOrDefault(m =>
                        string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
                    if (bySlug != null)
                    {
                        return bySlug;
                    }
                }

                var nameKey = InstalledModsService.NormalizeModKey(entry.Name);
                return page.Mods.FirstOrDefault(m =>
                    InstalledModsService.NormalizeModKey(m.Name) == nameKey);
            }
            catch
            {
                return null;
            }
        }

        private static ForgeModVersion? PickVersion(IReadOnlyList<ForgeModVersion> versions, string? required)
        {
            if (versions.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(required))
            {
                return versions[0];
            }

            var exact = versions.FirstOrDefault(v =>
                string.Equals(v.Version?.Trim(), required.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            var normalizedRequired = NormalizeVersionLabel(required);
            return versions.FirstOrDefault(v =>
                       NormalizeVersionLabel(v.Version) == normalizedRequired)
                   ?? null;
        }

        internal static InstalledModInfo? FindLocalMatch(
            RequiredModEntry entry,
            IReadOnlyList<InstalledModInfo> clientMods)
        {
            // Prefer guid, then forgeModId, then slug, then name/folder.
            if (!string.IsNullOrWhiteSpace(entry.Guid))
            {
                var byGuid = clientMods.FirstOrDefault(m =>
                    string.Equals(m.ForgeGuid, entry.Guid, StringComparison.OrdinalIgnoreCase));
                if (byGuid != null)
                {
                    return byGuid;
                }
            }

            if (entry.ForgeModId is int id and > 0)
            {
                var byId = clientMods.FirstOrDefault(m => m.ForgeModId == id);
                if (byId != null)
                {
                    return byId;
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Slug))
            {
                var bySlug = clientMods.FirstOrDefault(m =>
                    string.Equals(m.ForgeSlug, entry.Slug, StringComparison.OrdinalIgnoreCase));
                if (bySlug != null)
                {
                    return bySlug;
                }
            }

            var nameKey = InstalledModsService.NormalizeModKey(entry.Name);
            var slugKey = InstalledModsService.NormalizeModKey(entry.Slug?.Replace('-', ' '));
            return clientMods.FirstOrDefault(m =>
            {
                var displayKey = InstalledModsService.NormalizeModKey(m.DisplayName);
                var folderKey = InstalledModsService.NormalizeModKey(
                    Path.GetFileName(m.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                return (!string.IsNullOrEmpty(nameKey) && (displayKey == nameKey || folderKey == nameKey))
                       || (!string.IsNullOrEmpty(slugKey) && (displayKey == slugKey || folderKey == slugKey));
            });
        }

        public static bool VersionsEqual(string a, string b)
        {
            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var na = NormalizeVersionLabel(a);
            var nb = NormalizeVersionLabel(b);
            if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Version.TryParse(StripPrerelease(na), out var va) &&
                   Version.TryParse(StripPrerelease(nb), out var vb) &&
                   va == vb;
        }

        private static string NormalizeVersionLabel(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                v = v[1..].Trim();
            }

            return v;
        }

        private static string StripPrerelease(string version)
        {
            var cut = version.IndexOfAny(new[] { '-', '+' });
            return cut >= 0 ? version[..cut] : version;
        }
    }

    internal static class RequiredModsStringExtensions
    {
        public static string IfEmpty(this string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
