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
        /// SPT HTTPS (6969) may 404 pack mirrors while the LAN agent still has the zip.
        /// </summary>
        internal static string? AgentMirrorFallbackUrl(string zipUrl)
        {
            if (!Uri.TryCreate(zipUrl.Trim(), UriKind.Absolute, out var uri) ||
                !uri.AbsolutePath.StartsWith("/mod-pack/mirror/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (uri.Port == LanAgentHttpPort)
            {
                return null;
            }

            return new UriBuilder(Uri.UriSchemeHttp, uri.Host, LanAgentHttpPort, uri.AbsolutePath)
                .Uri.ToString();
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
            foreach (var entry in pack.Mods)
            {
                TryFillKnownWorkshopDownload(entry);
            }

            return pack;
        }

        /// <summary>
        /// Workshop-only mods (BattlePass, PatchCRC32) are not on Forge. Older host packs
        /// omit downloadUrl, which made every Check show "cannot auto-download" even when
        /// the launcher already knows the Workshop API.
        /// </summary>
        internal static bool TryFillKnownWorkshopDownload(RequiredModEntry entry)
        {
            if (entry.CanAutoInstall)
            {
                return false;
            }

            var guid = (entry.Guid ?? "").Trim();
            var slug = (entry.Slug ?? "").Trim();
            var name = (entry.Name ?? "").Trim();
            var key = $"{guid} {slug} {name}".ToLowerInvariant();

            if (guid.Equals("com.bblai.battlepass", StringComparison.OrdinalIgnoreCase) ||
                slug.Equals("tarkov-battlepass", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("battlepass"))
            {
                entry.Guid = string.IsNullOrWhiteSpace(entry.Guid) ? "com.bblai.battlepass" : entry.Guid;
                // Version ids under /api/download/{id} go stale. /api/mods/{slug} always
                // returns the current latestVersion.downloadUrl.
                entry.DownloadUrl = "https://blairsworkshop.com/api/mods/tarkov-battlepass";
                entry.DownloadKind = "blairsWorkshopJson";
                entry.PageUrl ??= "https://blairsworkshop.com/mods/tarkov-battlepass";
                if (string.IsNullOrWhiteSpace(entry.Slug))
                {
                    entry.Slug = "tarkov-battlepass";
                }

                return true;
            }

            if (guid.Equals("com.s8.sptpatchcrc32", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("patchcrc32") ||
                key.Contains("crc32-patch"))
            {
                entry.Guid = string.IsNullOrWhiteSpace(entry.Guid) ? "com.s8.sptpatchcrc32" : entry.Guid;
                entry.DownloadUrl = "https://blairsworkshop.com/api/mods/crc32-patch";
                entry.DownloadKind = "blairsWorkshopJson";
                entry.PageUrl ??= "https://blairsworkshop.com/mods/crc32-patch";
                return true;
            }

            return false;
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
                TryFillKnownWorkshopDownload(entry);

                var copies = FindAllLocalMatches(entry, clientMods);
                foreach (var copy in copies)
                {
                    matchedLocal.Add(copy);
                }

                var local = FindLocalMatch(entry, clientMods) ?? copies.FirstOrDefault();
                if (local == null && !entry.CanAutoInstall)
                {
                    items.Add(new RequiredModDiffItem
                    {
                        Status = RequiredModDiffStatus.ManualFix,
                        PackEntry = entry,
                        Message =
                            $"{entry.DisplayName}: missing forgeModId and downloadUrl — cannot auto-download" +
                            (string.IsNullOrWhiteSpace(entry.Guid) ? "." : $" (guid {entry.Guid}).")
                    });
                    continue;
                }
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

                var localVersion = (local.VersionHint ?? "").Trim();
                var staleCopies = StaleLocalCopies(entry, local, copies);
                var leftover = staleCopies.Count > 0;
                if (!IsLocalVersionSatisfied(entry, local) || leftover)
                {
                    var requiredVersion = (entry.Version ?? "").Trim();
                    var message = leftover && IsLocalVersionSatisfied(entry, local)
                        ? $"{entry.DisplayName}: leftover older copy will be removed on sync"
                        : $"{entry.DisplayName}: have {localVersion.IfEmpty("unknown")}, need {requiredVersion}";
                    if (leftover && !IsLocalVersionSatisfied(entry, local))
                    {
                        message += " (leftover copies will be deleted)";
                    }

                    items.Add(new RequiredModDiffItem
                    {
                        Status = RequiredModDiffStatus.WrongVersion,
                        PackEntry = entry,
                        Installed = local,
                        Message = message
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
            var prunedCount = 0;
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
                    var clientMods = InstalledModsService.ScanInstalledMods(sptRoot)
                        .Where(m => m.Kind == InstalledModKind.Client)
                        .ToList();
                    var copies = FindAllLocalMatches(entry, clientMods);
                    var local = FindLocalMatch(entry, clientMods);
                    if (local != null &&
                        IsLocalVersionSatisfied(entry, local) &&
                        StaleLocalCopies(entry, local, copies).Count > 0)
                    {
                        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var copy in copies)
                        {
                            if (SameInstallFamily(copy, local) && IsLocalVersionSatisfied(entry, copy))
                            {
                                foreach (var path in KeepPaths(copy))
                                {
                                    keep.Add(path);
                                }
                            }
                        }

                        prunedCount += RemoveLocalCopies(sptRoot, entry, keep);
                        installedCount++;
                        continue;
                    }

                    // Replace leftover plugins (old folder vs new zip layout) before extract.
                    TryUninstallLocalMatch(sptRoot, entry);
                    var result = await InstallPackEntryAsync(entry, sptRoot, progress, cancellationToken);
                    if (result.SkippedServerOnly)
                    {
                        skippedServerOnly++;
                        errors.Add($"{entry.DisplayName}: server-only package — skipped for client sync.");
                    }
                    else if (result.Success)
                    {
                        PruneAfterPackInstall(sptRoot, entry, result.ExtractedFiles);
                        StampMatchedClientMarkers(
                            sptRoot,
                            entry,
                            forgeMod: null,
                            versionLabel: entry.Version);

                        var midScan = InstalledModsService.ScanInstalledMods(sptRoot);
                        var midDiff = Diff(pack, midScan);
                        var stillWrong = midDiff.Items.Any(item =>
                            ReferenceEquals(item.PackEntry, entry) &&
                            item.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion);

                        if (stillWrong)
                        {
                            TryUninstallLocalMatch(sptRoot, entry);
                            var retry = await InstallPackEntryAsync(entry, sptRoot, progress, cancellationToken);
                            if (retry.Success)
                            {
                                PruneAfterPackInstall(sptRoot, entry, retry.ExtractedFiles);
                            }

                            midScan = InstalledModsService.ScanInstalledMods(sptRoot);
                            midDiff = Diff(pack, midScan);
                            stillWrong = midDiff.Items.Any(item =>
                                ReferenceEquals(item.PackEntry, entry) &&
                                item.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion);

                            if (!retry.Success || stillWrong)
                            {
                                failed++;
                                var detail = midDiff.Items.FirstOrDefault(item =>
                                    ReferenceEquals(item.PackEntry, entry) &&
                                    item.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion);
                                errors.Add(
                                    $"{entry.DisplayName}: {detail?.Message ?? retry.Error ?? "version still mismatch"} after reinstall. " +
                                    "Close the game if it is running, then sync again so leftover DLLs can be deleted.");
                            }
                            else
                            {
                                installedCount++;
                            }
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

            if (prunedCount > 0)
            {
                parts.Add($"removed {prunedCount} leftover cop{(prunedCount == 1 ? "y" : "ies")}");
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
                foreach (var item in after.Items
                             .Where(i => i.Status is RequiredModDiffStatus.Missing or RequiredModDiffStatus.WrongVersion)
                             .Take(5))
                {
                    errors.Add(item.Message);
                }
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

        internal static bool DownloadErrorLooksGone(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            var l = error.ToLowerInvariant();
            return l.Contains("404 (not found)")
                   || l.Contains("410 (gone)")
                   || l.Contains("http 404")
                   || l.Contains("http 410")
                   || l.Contains("status: 404")
                   || l.Contains("status: 410")
                   || l.Contains("status code does not indicate success: 404")
                   || l.Contains("status code does not indicate success: 410")
                   || l.Contains("404 not found")
                   || l.Contains("410 gone")
                   || l.Contains("no published workshop mod")
                   || l.Contains("could not resolve latest hosted")
                   || l.Contains("isn't a supported")
                   || l.Contains("not a supported")
                   || l.Contains(".zip/.7z");
        }

        internal static bool CanResolveForge(RequiredModEntry entry) =>
            entry.ForgeModId is > 0 ||
            !string.IsNullOrWhiteSpace(entry.Slug) ||
            !string.IsNullOrWhiteSpace(entry.Name);

        internal static bool IsKnownWorkshopOnly(RequiredModEntry entry)
        {
            var guid = (entry.Guid ?? "").Trim();
            var slug = (entry.Slug ?? "").Trim();
            var name = (entry.Name ?? "").Trim();
            var key = $"{guid} {slug} {name}".ToLowerInvariant();
            return guid.Equals("com.bblai.battlepass", StringComparison.OrdinalIgnoreCase) ||
                   guid.Equals("com.s8.sptpatchcrc32", StringComparison.OrdinalIgnoreCase) ||
                   slug.Equals("tarkov-battlepass", StringComparison.OrdinalIgnoreCase) ||
                   key.Contains("battlepass") ||
                   key.Contains("patchcrc32") ||
                   key.Contains("crc32-patch");
        }

        internal static bool ShouldFallbackHostedDownloadToForge(RequiredModEntry entry, string? hostedError) =>
            !IsKnownWorkshopOnly(entry) &&
            CanResolveForge(entry) &&
            DownloadErrorLooksGone(hostedError);

        internal static bool KeepExistingHostedInstall(string sptRoot, RequiredModEntry entry)
        {
            try
            {
                var scanned = InstalledModsService.ScanInstalledMods(sptRoot);
                var local = FindLocalMatch(
                    entry,
                    scanned.Where(m => m.Kind == InstalledModKind.Client).ToList());
                if (local == null)
                {
                    return false;
                }

                var required = (entry.Version ?? "").Trim();
                var have = (local.VersionHint ?? "").Trim();
                if (LooksLikePlaceholderVersion(required) || string.IsNullOrWhiteSpace(required))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(have) || LooksLikePlaceholderVersion(have))
                {
                    return false;
                }

                return VersionsEqual(required, have) || CompareVersionRank(have, required) >= 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool HostedInstallIsOlderThanPack(
            RequiredModEntry entry,
            IReadOnlyList<string> extractedFiles)
        {
            var packVersion = (entry.Version ?? "").Trim();
            if (string.IsNullOrWhiteSpace(packVersion) || LooksLikePlaceholderVersion(packVersion))
            {
                return false;
            }

            var extractedVersion = ReadExtractedPluginVersion(entry, extractedFiles);
            if (string.IsNullOrWhiteSpace(extractedVersion) || LooksLikePlaceholderVersion(extractedVersion))
            {
                return false;
            }

            return CompareVersionRank(extractedVersion, packVersion) < 0;
        }

        internal static string? ReadExtractedPluginVersion(
            RequiredModEntry entry,
            IReadOnlyList<string> extractedFiles)
        {
            foreach (var path in extractedFiles)
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!PathStrictlyMatchesPackEntry(path, entry) &&
                    !PathBelongsToPackEntry(path, entry))
                {
                    continue;
                }

                var version = InstalledModsService.TryReadDllVersion(path);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }

            return null;
        }

        private readonly record struct PackInstallResult(
            bool Success,
            bool SkippedServerOnly,
            string Error,
            IReadOnlyList<string> ExtractedFiles)
        {
            public static PackInstallResult Ok(IReadOnlyList<string>? extracted = null) =>
                new(true, false, "", extracted ?? Array.Empty<string>());

            public static PackInstallResult Fail(string error) =>
                new(false, false, error ?? "", Array.Empty<string>());

            public static PackInstallResult SkipServer(string error) =>
                new(false, true, error ?? "", Array.Empty<string>());
        }

        private async Task<PackInstallResult> InstallPackEntryAsync(
            RequiredModEntry entry,
            string sptRoot,
            IProgress<RequiredModsSyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(entry.DownloadUrl))
            {
                PackInstallResult hosted;
                try
                {
                    hosted = await InstallHostedPackEntryAsync(entry, sptRoot, progress, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    hosted = PackInstallResult.Fail(ex.Message);
                }

                if (hosted.Success || hosted.SkippedServerOnly ||
                    !ShouldFallbackHostedDownloadToForge(entry, hosted.Error))
                {
                    return hosted;
                }

                progress?.Report(new RequiredModsSyncProgress
                {
                    Message = $"{entry.DisplayName}: hosted download is gone — trying Forge…"
                });
                var forge = await InstallForgePackEntryAsync(entry, sptRoot, progress, cancellationToken);
                if (forge.Success || forge.SkippedServerOnly)
                {
                    return forge;
                }

                return PackInstallResult.Fail($"{hosted.Error} Forge fallback: {forge.Error}");
            }

            return await InstallForgePackEntryAsync(entry, sptRoot, progress, cancellationToken);
        }

        private async Task<PackInstallResult> InstallForgePackEntryAsync(
            RequiredModEntry entry,
            string sptRoot,
            IProgress<RequiredModsSyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            var mod = await ResolveModAsync(entry, cancellationToken);
            if (mod == null)
            {
                return PackInstallResult.Fail(
                    entry.ForgeModId is int missingId && missingId > 0
                        ? $"Could not find Forge mod id {missingId} on sp-mod.com."
                        : $"Could not find \"{entry.DisplayName}\" on Forge. Install this client mod manually.");
            }

            var forgeId = mod.Id;

            // Always load the full versions list for installs. GetModAsync's include=versions
            // can be incomplete when `fields` omits version columns, which made exact picks
            // like 1.5.0 / 1.8.0 fail even though Forge has them.
            var versions = await ForgeApiService.Instance.GetModVersionsAsync(
                mod.Id,
                sptVersion: null,
                cancellationToken: cancellationToken);
            if (versions.Count == 0 && mod.Versions is { Count: > 0 })
            {
                versions = mod.Versions;
            }

            var toTry = ForgeVersionsToTry(versions, entry.Version, TryDetectSptVersion(sptRoot));
            if (toTry.Count == 0)
            {
                return PackInstallResult.Fail(
                    string.IsNullOrWhiteSpace(entry.Version)
                        ? "No downloadable version found on Forge."
                        : $"Version {entry.Version} not found on sp-mod.com for mod id {forgeId} " +
                          $"(Forge returned {versions.Count} version(s)).");
            }

            string? lastGone = null;
            foreach (var version in toTry)
            {
                EnsureVersionDownloadLink(mod, version);
                if (string.IsNullOrWhiteSpace(version.Link))
                {
                    continue;
                }

                if (lastGone != null)
                {
                    progress?.Report(new RequiredModsSyncProgress
                    {
                        Message = $"{entry.DisplayName}: {entry.Version} is gone — trying Forge {version.Version}…"
                    });
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
                        return PackInstallResult.SkipServer("Server-only package");
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

                if (report.Success)
                {
                    // Pack Diff keys off .forge-mod.json sidecars. Always stamp the matched
                    // local plugin(s) to the version we just installed so upgrades can't leave
                    // a stale "have 1.1.0, need 1.6.0" marker on an older DLL name.
                    StampMatchedClientMarkers(sptRoot, entry, mod, version.Version, version.Id);
                    return PackInstallResult.Ok(report.ExtractedFiles);
                }

                if (!DownloadErrorLooksGone(report.Message))
                {
                    return PackInstallResult.Fail(report.Message);
                }

                lastGone = report.Message;
            }

            return PackInstallResult.Fail(
                lastGone ?? $"Version {entry.Version} not found on sp-mod.com for mod id {forgeId}.");
        }

        private async Task<PackInstallResult> InstallHostedPackEntryAsync(
            RequiredModEntry entry,
            string sptRoot,
            IProgress<RequiredModsSyncProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new RequiredModsSyncProgress
            {
                Message = $"{entry.DisplayName}: resolving hosted download…"
            });

            string zipUrl;
            try
            {
                zipUrl = await ResolveHostedDownloadUrlAsync(entry, cancellationToken);
            }
            catch (Exception ex)
            {
                return PackInstallResult.Fail(
                    $"Hosted download failed for {entry.DisplayName}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                return PackInstallResult.Fail($"Hosted download URL resolved empty for {entry.DisplayName}.");
            }

            var mod = new ForgeModSummary
            {
                Id = entry.ForgeModId is > 0 ? entry.ForgeModId.Value : 0,
                Name = entry.DisplayName,
                Slug = entry.Slug ?? "",
                Guid = entry.Guid
            };
            var version = new ForgeModVersion
            {
                Id = 0,
                Version = string.IsNullOrWhiteSpace(entry.Version) ? "0" : entry.Version!.Trim(),
                Link = zipUrl
            };

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
                preferredFileTree: entry.ClientFiles,
                installProgress,
                cancellationToken,
                clientPathsOnly: true);

            if (!report.Success)
            {
                var agentMirror = AgentMirrorFallbackUrl(zipUrl);
                if (!string.IsNullOrWhiteSpace(agentMirror))
                {
                    progress?.Report(new RequiredModsSyncProgress
                    {
                        Message = $"{entry.DisplayName}: SPT HTTPS mirror missing — trying agent…"
                    });
                    version.Link = agentMirror;
                    report = await ModInstallService.Instance.InstallAsync(
                        mod,
                        version,
                        sptRoot,
                        preferredFileTree: entry.ClientFiles,
                        installProgress,
                        cancellationToken,
                        clientPathsOnly: true);
                }
            }

            if (!report.Success)
            {
                if (DownloadErrorLooksGone(report.Message) &&
                    KeepExistingHostedInstall(sptRoot, entry))
                {
                    return PackInstallResult.Ok();
                }

                return PackInstallResult.Fail(report.Message);
            }

            if (CanResolveForge(entry) &&
                HostedInstallIsOlderThanPack(entry, report.ExtractedFiles))
            {
                progress?.Report(new RequiredModsSyncProgress
                {
                    Message = $"{entry.DisplayName}: hosted zip is older than pack {entry.Version} — trying Forge…"
                });
                var forge = await InstallForgePackEntryAsync(entry, sptRoot, progress, cancellationToken);
                if (forge.Success || forge.SkippedServerOnly)
                {
                    return forge;
                }

                return PackInstallResult.Fail(
                    $"Hosted download is older than pack {entry.Version}. Forge fallback: {forge.Error}");
            }

            StampMatchedClientMarkers(sptRoot, entry, mod, version.Version, versionId: null);
            return PackInstallResult.Ok(report.ExtractedFiles);
        }

        private async Task<string> ResolveHostedDownloadUrlAsync(
            RequiredModEntry entry,
            CancellationToken cancellationToken)
        {
            var url = entry.DownloadUrl?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("downloadUrl is empty.");
            }

            // Pack-relative mirrors: use the LAN agent (plain HTTP). SPT HTTPS 6969
            // is a self-signed cert and older listeners 404 /mod-pack/mirror/….
            if (url.StartsWith('/'))
            {
                var packUrl = GetConfiguredPackUrl();
                if (string.IsNullOrWhiteSpace(packUrl) ||
                    !Uri.TryCreate(packUrl, UriKind.Absolute, out var packUri))
                {
                    throw new InvalidOperationException(
                        "Relative downloadUrl requires a configured pack URL (server host).");
                }

                if (url.StartsWith("/mod-pack/mirror/", StringComparison.OrdinalIgnoreCase))
                {
                    url = new UriBuilder(Uri.UriSchemeHttp, packUri.Host, LanAgentHttpPort, url)
                        .Uri.ToString();
                }
                else
                {
                    var builder = new UriBuilder(packUri.Scheme, packUri.Host, packUri.Port, url);
                    url = builder.Uri.ToString();
                }
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("downloadUrl must be an absolute http(s) URL.");
            }

            var kind = (entry.DownloadKind ?? "").Trim();
            var looksLikeWorkshopApi =
                url.Contains("/api/download/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/api/mods/", StringComparison.OrdinalIgnoreCase) ||
                LooksLikeWorkshopModPage(url);
            if (string.IsNullOrWhiteSpace(kind))
            {
                kind = looksLikeWorkshopApi ? "blairsWorkshopJson" : "direct";
            }

            // Packs have stamped `direct` on Blair's JSON APIs. Treat those as a hop
            // or clients save `{"url":"..."}` and report "isn't a supported archive".
            if ((kind.Equals("direct", StringComparison.OrdinalIgnoreCase) ||
                kind.Equals("zip", StringComparison.OrdinalIgnoreCase)) &&
                !looksLikeWorkshopApi)
            {
                return url;
            }

            var modsApi = WorkshopModsApiUrl(entry, url);
            if (!string.IsNullOrWhiteSpace(modsApi))
            {
                var latest = await TryHopWorkshopModsApiAsync(modsApi, cancellationToken);
                if (!string.IsNullOrWhiteSpace(latest))
                {
                    return latest;
                }
            }

            return await HopWorkshopDownloadApiAsync(url, cancellationToken);
        }

        internal static bool LooksLikeWorkshopModPage(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host;
            if (string.IsNullOrWhiteSpace(host) ||
                !host.Contains("blairsworkshop.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var path = uri.AbsolutePath.Trim('/');
            return path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("api/mods/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// `/api/download/{slug}` 404s — slugs are not version ids. Resolve latest via
        /// `/api/mods/{slug}` instead.
        /// </summary>
        internal static string? WorkshopModsApiUrl(RequiredModEntry entry, string downloadUrl)
        {
            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
            {
                var segs = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 2 &&
                    segs[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                    segs[1].Equals("mods", StringComparison.OrdinalIgnoreCase))
                {
                    return downloadUrl;
                }

                if (segs.Length >= 2 && segs[0].Equals("mods", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{uri.GetLeftPart(UriPartial.Authority)}/api/mods/{segs[1]}";
                }

                if (segs.Length >= 3 &&
                    segs[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                    segs[1].Equals("download", StringComparison.OrdinalIgnoreCase) &&
                    segs[2].Contains('-'))
                {
                    return $"{uri.GetLeftPart(UriPartial.Authority)}/api/mods/{segs[2]}";
                }
            }

            var slug = (entry.Slug ?? "").Trim();
            if (slug.Equals("tarkov-battlepass", StringComparison.OrdinalIgnoreCase) ||
                (entry.Guid ?? "").Equals("com.bblai.battlepass", StringComparison.OrdinalIgnoreCase))
            {
                slug = "tarkov-battlepass";
            }
            else if ((entry.Guid ?? "").Equals("com.s8.sptpatchcrc32", StringComparison.OrdinalIgnoreCase) ||
                     slug.Contains("crc32", StringComparison.OrdinalIgnoreCase))
            {
                slug = string.IsNullOrWhiteSpace(slug) ? "crc32-patch" : slug;
            }

            if (string.IsNullOrWhiteSpace(slug))
            {
                return null;
            }

            return $"https://blairsworkshop.com/api/mods/{slug}";
        }

        internal static string? TryLatestDownloadUrlFromWorkshopModJson(string json)
        {
            try
            {
                var meta = JsonSerializer.Deserialize<BlairWorkshopModMeta>(json, JsonOptions);
                var hop = meta?.LatestVersion?.DownloadUrl?.Trim();
                return string.IsNullOrWhiteSpace(hop) ? null : hop;
            }
            catch
            {
                return null;
            }
        }

        private async Task<string?> TryHopWorkshopModsApiAsync(string modsApiUrl, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(modsApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var versionDownload = TryLatestDownloadUrlFromWorkshopModJson(json);
            if (string.IsNullOrWhiteSpace(versionDownload))
            {
                return null;
            }

            return await HopWorkshopDownloadApiAsync(versionDownload, cancellationToken);
        }

        private async Task<string> HopWorkshopDownloadApiAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<BlairWorkshopDownloadResponse>(
                stream,
                JsonOptions,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.Url))
            {
                throw new InvalidOperationException(
                    "Download API did not return a zip url field.");
            }

            return payload.Url.Trim();
        }

        private sealed class BlairWorkshopDownloadResponse
        {
            public string? Url { get; set; }
            public int? ExpiresIn { get; set; }
        }

        private sealed class BlairWorkshopModMeta
        {
            public string? Slug { get; set; }
            public string? Title { get; set; }
            public BlairWorkshopLatestVersion? LatestVersion { get; set; }
        }

        private sealed class BlairWorkshopLatestVersion
        {
            public string? Id { get; set; }
            public string? Version { get; set; }
            public string? DownloadUrl { get; set; }
        }

        private static void StampMatchedClientMarkers(
            string sptRoot,
            RequiredModEntry entry,
            ForgeModSummary? forgeMod,
            string? versionLabel,
            int? versionId = null)
        {
            var versionText = !string.IsNullOrWhiteSpace(versionLabel)
                ? versionLabel!.Trim()
                : (entry.Version ?? "").Trim();
            if (string.IsNullOrWhiteSpace(versionText))
            {
                return;
            }

            var marker = new ForgeModMarker
            {
                ForgeModId = forgeMod?.Id > 0
                    ? forgeMod.Id
                    : entry.ForgeModId ?? 0,
                Guid = !string.IsNullOrWhiteSpace(entry.Guid) ? entry.Guid : forgeMod?.Guid,
                Slug = !string.IsNullOrWhiteSpace(entry.Slug) ? entry.Slug : forgeMod?.Slug,
                Name = !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : forgeMod?.Name,
                Version = versionText,
                VersionId = versionId,
                InstalledAtUtc = DateTime.UtcNow
            };

            var scanned = InstalledModsService.ScanInstalledMods(sptRoot);
            var clientMods = scanned.Where(m => m.Kind == InstalledModKind.Client).ToList();

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Only stamp paths that clearly belong to this pack entry (slug/name/clientFiles).
            // Matching by Forge id/guid alone retagged SAIN as WTT CommonLib when the pack
            // GUID was wrongly com.fika.core.
            foreach (var local in clientMods)
            {
                foreach (var path in local.AllPaths)
                {
                    if (PathStrictlyMatchesPackEntry(path, entry))
                    {
                        targets.Add(path);
                    }
                }
            }

            if (targets.Count == 0 && entry.ClientFiles is { Count: > 0 })
            {
                foreach (var rel in entry.ClientFiles)
                {
                    if (string.IsNullOrWhiteSpace(rel))
                    {
                        continue;
                    }

                    var full = Path.GetFullPath(Path.Combine(
                        sptRoot,
                        rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                    if (File.Exists(full) || Directory.Exists(full))
                    {
                        targets.Add(full);
                    }
                }
            }

            foreach (var path in targets)
            {
                if (IsProtectedCorePluginPath(path))
                {
                    continue;
                }

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

        private static void TryUninstallLocalMatch(string sptRoot, RequiredModEntry entry)
        {
            try
            {
                RemoveLocalCopies(sptRoot, entry, keepPaths: null);
            }
            catch
            {
                // best-effort cleanup before retry install
            }
        }

        internal static bool IsLocalVersionSatisfied(RequiredModEntry entry, InstalledModInfo local)
        {
            var requiredVersion = (entry.Version ?? "").Trim();
            var localVersion = (local.VersionHint ?? "").Trim();

            // Re-download only when the install is known to be *older* than the pack.
            // Missing sidecars and DLL FileVersion ahead of the Forge tag used to
            // look like WrongVersion forever, so every launcher open re-synced.
            if (string.IsNullOrWhiteSpace(requiredVersion) ||
                LooksLikePlaceholderVersion(requiredVersion) ||
                string.IsNullOrWhiteSpace(localVersion))
            {
                return true;
            }

            return VersionsEqual(requiredVersion, localVersion) ||
                   CompareVersionRank(localVersion, requiredVersion) >= 0;
        }

        internal static IEnumerable<string> EntryGuids(RequiredModEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.Guid))
            {
                yield return entry.Guid.Trim();
            }

            if (entry.ExtraGuids == null)
            {
                yield break;
            }

            foreach (var guid in entry.ExtraGuids)
            {
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    yield return guid.Trim();
                }
            }
        }

        internal static HashSet<string> KeepPaths(InstalledModInfo local)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in local.AllPaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    keep.Add(Path.GetFullPath(path));
                }
            }

            return keep;
        }

        internal static List<InstalledModInfo> FindAllLocalMatches(
            RequiredModEntry entry,
            IReadOnlyList<InstalledModInfo> clientMods)
        {
            var guids = new HashSet<string>(EntryGuids(entry), StringComparer.OrdinalIgnoreCase);
            var matches = new List<InstalledModInfo>();

            foreach (var mod in clientMods)
            {
                if (mod.Kind != InstalledModKind.Client)
                {
                    continue;
                }

                if (!IsReplaceTarget(mod, entry, guids))
                {
                    continue;
                }

                matches.Add(mod);
            }

            return matches
                .GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        internal static List<InstalledModInfo> StaleLocalCopies(
            RequiredModEntry entry,
            InstalledModInfo primary,
            IReadOnlyList<InstalledModInfo> copies)
        {
            return copies
                .Where(copy =>
                    !string.Equals(copy.Path, primary.Path, StringComparison.OrdinalIgnoreCase) &&
                    (!SameInstallFamily(copy, primary) || !IsLocalVersionSatisfied(entry, copy)))
                .ToList();
        }

        /// <summary>
        /// True when two installs are the same mod layout (folder + files inside, or
        /// companion DLLs sitting together). False for an old folder next to a new one,
        /// or a leftover loose DLL beside a replacement folder.
        /// </summary>
        internal static bool SameInstallFamily(InstalledModInfo a, InstalledModInfo b)
        {
            if (string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (a.IsDirectory && b.AllPaths.Any(p => IsSameOrUnder(p, a.Path)))
            {
                return true;
            }

            if (b.IsDirectory && a.AllPaths.Any(p => IsSameOrUnder(p, b.Path)))
            {
                return true;
            }

            if (!a.IsDirectory && !b.IsDirectory)
            {
                var dirA = Path.GetDirectoryName(a.Path) ?? "";
                var dirB = Path.GetDirectoryName(b.Path) ?? "";
                return string.Equals(dirA, dirB, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Paths that should be deleted when replacing this pack mod.
        /// Tighter than <see cref="FindLocalMatch"/>: core GUIDs like com.fika.core
        /// must not delete Fika.Core when CommonLib was mis-tagged.
        /// </summary>
        internal static bool IsReplaceTarget(
            InstalledModInfo mod,
            RequiredModEntry entry,
            HashSet<string>? guids = null)
        {
            if (IsProtectedCorePluginPath(mod.Path) ||
                mod.AllPaths.Any(IsProtectedCorePluginPath))
            {
                return false;
            }

            guids ??= new HashSet<string>(EntryGuids(entry), StringComparer.OrdinalIgnoreCase);

            bool PathOk(string path) =>
                PathBelongsToPackEntry(path, entry) &&
                !IsCoreGuidOnlyCollision(path, entry);

            var anyPathOk = PathOk(mod.Path) || mod.AllPaths.Any(PathOk);
            if (!anyPathOk)
            {
                return false;
            }

            if (mod.AllPaths.Any(p => PathStrictlyMatchesPackEntry(p, entry)) ||
                PathStrictlyMatchesPackEntry(mod.Path, entry))
            {
                return !IsCoreGuidOnlyCollision(mod.Path, entry);
            }

            if (entry.ForgeModId is int id and > 0 && mod.ForgeModId == id)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entry.Slug) &&
                string.Equals(mod.ForgeSlug, entry.Slug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(mod.ForgeGuid) && guids.Contains(mod.ForgeGuid) &&
                entry.ForgeModId is > 0 && mod.ForgeModId == entry.ForgeModId)
            {
                return true;
            }

            return false;
        }

        internal static bool IsCoreGuidOnlyCollision(string path, RequiredModEntry entry)
        {
            var guid = (entry.Guid ?? "").Trim();
            if (!IsCorePluginGuid(guid))
            {
                return false;
            }

            var leaf = PathMatchLeaf(path);
            if (InstalledModsService.IdentityOverlapsPath(leaf, entry.Slug, entry.Name))
            {
                return false;
            }

            if (entry.ClientFiles is { Count: > 0 })
            {
                foreach (var rel in entry.ClientFiles)
                {
                    if (string.IsNullOrWhiteSpace(rel))
                    {
                        continue;
                    }

                    var relLeaf = PathMatchLeaf(rel.Replace('\\', '/'));
                    if (!string.IsNullOrWhiteSpace(relLeaf) &&
                        (leaf == relLeaf || leaf.Contains(relLeaf) || relLeaf.Contains(leaf)))
                    {
                        return false;
                    }
                }
            }

            return InstalledModsService.IdentityOverlapsPath(leaf, guid);
        }

        internal static bool IsCorePluginGuid(string? guid)
        {
            var g = (guid ?? "").Trim().ToLowerInvariant();
            return g is "com.fika.core"
                or "com.fika.dedicated"
                or "com.spt.core"
                or "com.spt.custom"
                or "com.spt.singleplayer"
                or "com.spt.reflection";
        }

        /// <summary>
        /// Pack sync must never delete Fika/SPT cores. Companion GUIDs like
        /// <c>com.20fpsguy.LootNet.fika</c> otherwise match a folder named <c>Fika</c>.
        /// </summary>
        internal static bool IsProtectedCorePluginPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var file = Path.GetFileName(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (IsFikaOrSptCoreFileName(file))
            {
                return true;
            }

            var leaf = PathMatchLeaf(path);
            return leaf is "fika"
                or "fikacore"
                or "fikacoop"
                or "fikaserver"
                or "fikadedicated"
                or "fikaheadless"
                or "sptcore"
                or "sptcustom"
                or "sptsingleplayer"
                or "sptreflection";
        }

        internal static bool IsFikaOrSptCoreFileName(string? fileName)
        {
            var name = Path.GetFileName(fileName ?? "");
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^".disabled".Length];
            }

            return name.Equals("Fika.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Core.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Dedicated.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Fika.Server.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("spt-core.dll", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Last meaningful GUID segment, skipping generic suffixes like fika/kills/core.
        /// </summary>
        internal static string? IdentifyingGuidToken(string? guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string[] generic =
            [
                "com", "eft", "spt", "mod", "mods", "plugin", "plugins", "client", "server",
                "core", "main", "fika", "kills", "communitytab", "dedicated", "headless"
            ];

            var parts = guid.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                var token = InstalledModsService.NormalizeModKey(parts[i]);
                if (token.Length >= 4 && !generic.Contains(token))
                {
                    return token;
                }
            }

            return null;
        }

        internal static int RemoveLocalCopies(
            string sptRoot,
            RequiredModEntry entry,
            IReadOnlyCollection<string>? keepPaths)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keepPaths != null)
            {
                foreach (var path in keepPaths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    try
                    {
                        keep.Add(Path.GetFullPath(path));
                    }
                    catch
                    {
                        keep.Add(path);
                    }
                }
            }

            var scanned = InstalledModsService.ScanInstalledMods(sptRoot);
            var matches = FindAllLocalMatches(
                entry,
                scanned.Where(m => m.Kind == InstalledModKind.Client).ToList());

            var removed = 0;
            foreach (var match in matches)
            {
                if (ShouldKeepInstall(match, keep))
                {
                    continue;
                }

                try
                {
                    InstalledModsService.Uninstall(match);
                    removed++;
                }
                catch
                {
                    // continue removing other copies
                }
            }

            return removed;
        }

        internal static void PruneAfterPackInstall(
            string sptRoot,
            RequiredModEntry entry,
            IReadOnlyList<string>? extractedFiles)
        {
            try
            {
                if (extractedFiles is { Count: > 0 })
                {
                    PruneUnlistedPluginFiles(sptRoot, entry, extractedFiles);
                    var keep = extractedFiles
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p =>
                        {
                            try
                            {
                                return Path.GetFullPath(p);
                            }
                            catch
                            {
                                return p;
                            }
                        })
                        .ToList();
                    foreach (var file in keep.ToList())
                    {
                        var dir = Path.GetDirectoryName(file);
                        if (!string.IsNullOrWhiteSpace(dir) &&
                            !IsPluginsRoot(sptRoot, dir))
                        {
                            keep.Add(dir);
                        }
                    }

                    RemoveLocalCopies(sptRoot, entry, keep);
                }
                else
                {
                    var local = FindLocalMatch(
                        entry,
                        InstalledModsService.ScanInstalledMods(sptRoot)
                            .Where(m => m.Kind == InstalledModKind.Client)
                            .ToList());
                    if (local != null)
                    {
                        RemoveLocalCopies(sptRoot, entry, KeepPaths(local));
                    }
                }
            }
            catch
            {
                // best-effort
            }
        }

        internal static int PruneUnlistedPluginFiles(
            string sptRoot,
            RequiredModEntry entry,
            IReadOnlyList<string> extractedFiles)
        {
            var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in extractedFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                try
                {
                    extracted.Add(Path.GetFullPath(file));
                }
                catch
                {
                    extracted.Add(file);
                }
            }

            if (extracted.Count == 0)
            {
                return 0;
            }

            var removed = 0;
            var dirs = extracted
                .Select(f => Path.GetDirectoryName(f))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var pluginsRoot = IsPluginsRoot(sptRoot, dir!);
                if (!pluginsRoot && !PathStrictlyMatchesPackEntry(dir!, entry))
                {
                    continue;
                }

                foreach (var dll in SafePluginFilesIn(dir!))
                {
                    string full;
                    try
                    {
                        full = Path.GetFullPath(dll);
                    }
                    catch
                    {
                        full = dll;
                    }

                    if (extracted.Contains(full))
                    {
                        continue;
                    }

                    if (IsProtectedCorePluginPath(full))
                    {
                        continue;
                    }

                    if (pluginsRoot && !PathStrictlyMatchesPackEntry(full, entry))
                    {
                        continue;
                    }

                    if (pluginsRoot && IsCoreGuidOnlyCollision(full, entry))
                    {
                        continue;
                    }

                    try
                    {
                        InstalledModsService.Uninstall(new InstalledModInfo
                        {
                            DisplayName = entry.DisplayName,
                            Path = full,
                            Kind = InstalledModKind.Client,
                            IsDirectory = false
                        });
                        removed++;
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }

            return removed;
        }

        private static bool IsPluginsRoot(string sptRoot, string directory)
        {
            try
            {
                var plugins = Path.GetFullPath(Path.Combine(sptRoot, "BepInEx", "plugins"));
                return string.Equals(
                    Path.GetFullPath(directory),
                    plugins,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> SafePluginFilesIn(string directory)
        {
            try
            {
                return Directory.GetFiles(directory)
                    .Where(f =>
                    {
                        var name = Path.GetFileName(f);
                        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                               name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
                    });
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool ShouldKeepInstall(InstalledModInfo match, HashSet<string> keep)
        {
            if (keep.Count == 0)
            {
                return false;
            }

            foreach (var path in match.AllPaths)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(path);
                }
                catch
                {
                    full = path;
                }

                if (keep.Contains(full))
                {
                    return true;
                }

                foreach (var kept in keep)
                {
                    if (IsSameOrUnder(full, kept) || IsSameOrUnder(kept, full))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsSameOrUnder(string path, string root)
        {
            var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = trimmedRoot + Path.DirectorySeparatorChar;
            return (trimmedPath + Path.DirectorySeparatorChar)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<ForgeModSummary?> ResolveModAsync(
            RequiredModEntry entry,
            CancellationToken cancellationToken)
        {
            foreach (var id in ForgeIdsToTry(entry))
            {
                try
                {
                    return await ForgeApiService.Instance.GetModAsync(id, cancellationToken);
                }
                catch
                {
                    // try search next
                }
            }

            var queries = new List<string>();
            void Offer(string? value)
            {
                var q = (value ?? "").Trim();
                if (q.Length == 0)
                {
                    return;
                }

                if (!queries.Exists(existing => string.Equals(existing, q, StringComparison.OrdinalIgnoreCase)))
                {
                    queries.Add(q);
                }
            }

            Offer(entry.Slug);
            Offer(entry.Name);

            foreach (var query in queries)
            {
                try
                {
                    var page = await ForgeApiService.Instance.SearchModsAsync(
                        query: query,
                        sptVersion: null,
                        page: 1,
                        perPage: 25,
                        cancellationToken: cancellationToken);

                    var match = MatchSearchedMod(entry, page.Mods);
                    if (match != null)
                    {
                        return match;
                    }
                }
                catch
                {
                    // try the next query
                }
            }

            return null;
        }

        internal static IEnumerable<int> ForgeIdsToTry(RequiredModEntry entry)
        {
            if (entry.ForgeModId is int packId and > 0)
            {
                yield return packId;
            }

            var fromPage = TryParseForgeModIdFromPageUrl(entry.PageUrl);
            if (fromPage is int pageId && pageId > 0 && pageId != entry.ForgeModId)
            {
                yield return pageId;
            }
        }

        internal static int? TryParseForgeModIdFromPageUrl(string? pageUrl)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) ||
                !Uri.TryCreate(pageUrl.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("mod", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(parts[i + 1], out var id) &&
                    id > 0)
                {
                    return id;
                }
            }

            return null;
        }

        internal static ForgeModSummary? MatchSearchedMod(
            RequiredModEntry entry,
            IEnumerable<ForgeModSummary> mods)
        {
            var slug = (entry.Slug ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(slug))
            {
                var bySlug = mods.FirstOrDefault(m =>
                    string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
                if (bySlug != null)
                {
                    return bySlug;
                }
            }

            var nameKey = InstalledModsService.NormalizeModKey(entry.Name);
            if (!string.IsNullOrWhiteSpace(nameKey))
            {
                var byName = mods.FirstOrDefault(m =>
                    InstalledModsService.NormalizeModKey(m.Name) == nameKey);
                if (byName != null)
                {
                    return byName;
                }
            }

            return null;
        }

        private static ForgeModVersion? PickVersion(IReadOnlyList<ForgeModVersion> versions, string? required)
        {
            if (versions.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(required))
            {
                return PickNewestVersion(versions);
            }

            var exact = versions.FirstOrDefault(v =>
                string.Equals(v.Version?.Trim(), required.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            var normalizedRequired = NormalizeVersionLabel(required);
            return versions.FirstOrDefault(v =>
                       NormalizeVersionLabel(v.Version) == normalizedRequired);
        }

        private static ForgeModVersion? PickNewestVersion(IReadOnlyList<ForgeModVersion> versions)
        {
            if (versions.Count == 0)
            {
                return null;
            }

            return versions
                .OrderByDescending(v => ParseVersionRank(v.Version))
                .ThenByDescending(v => v.Id)
                .FirstOrDefault();
        }

        /// <summary>
        /// Pack version first, then newer Forge builds — but when the install's SPT version
        /// is known, skip builds tagged for a different minor line (Armory 2.1.1 is ~4.0.13;
        /// SPT 4.1.5 needs 3.0.0 ~4.1.5).
        /// </summary>
        internal static List<ForgeModVersion> ForgeVersionsToTry(
            IReadOnlyList<ForgeModVersion> versions,
            string? required,
            string? installedSpt = null)
        {
            IReadOnlyList<ForgeModVersion> pool = versions;
            if (!string.IsNullOrWhiteSpace(installedSpt))
            {
                var compatible = versions
                    .Where(v => SptConstraintAllows(v.SptVersionConstraint, installedSpt))
                    .ToList();
                if (compatible.Count > 0)
                {
                    pool = compatible;
                }
            }

            var usable = pool
                .Where(v => !LooksLikePlaceholderVersion(v.Version))
                .ToList();
            if (usable.Count > 0)
            {
                pool = usable;
            }

            var packVersion = LooksLikePlaceholderVersion(required) ? null : required;

            var result = new List<ForgeModVersion>();
            var primary = PickVersion(pool, packVersion) ?? PickNewestVersion(pool);
            if (primary == null)
            {
                return result;
            }

            result.Add(primary);
            foreach (var newer in pool
                         .Where(v => v.Id != primary.Id)
                         .Where(v => CompareVersionRank(v.Version ?? "", primary.Version ?? "") > 0)
                         .OrderBy(v => ParseVersionRank(v.Version))
                         .ThenBy(v => v.Id))
            {
                result.Add(newer);
            }

            return result;
        }

        /// <summary>
        /// SPT 4.1.x builds are compatible with each other. A ~4.0.13 / &lt;4.1.0 tag is not.
        /// </summary>
        internal static bool SptConstraintAllows(string? constraint, string? installedSpt)
        {
            if (string.IsNullOrWhiteSpace(installedSpt) || string.IsNullOrWhiteSpace(constraint))
            {
                return true;
            }

            if (!TryParseSptVersion(installedSpt, out var installed))
            {
                return true;
            }

            foreach (var raw in constraint.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!SptConstraintTokenAllows(raw.Trim(), installed))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string? TryDetectSptVersion(string sptRoot)
        {
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                return null;
            }

            foreach (var relative in new[]
                     {
                         Path.Combine("SPT_Runtime", "SPT.Launcher.exe"),
                         "SPT.Launcher.exe"
                     })
            {
                var path = Path.Combine(sptRoot, relative);
                if (!File.Exists(path))
                {
                    continue;
                }

                var version = SptDetectionService.Instance.GetSptVersion(path);
                if (!string.IsNullOrWhiteSpace(version) &&
                    !version.Equals("Not detected", StringComparison.OrdinalIgnoreCase) &&
                    !version.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return version;
                }
            }

            return null;
        }

        private static bool SptConstraintTokenAllows(string token, Version installed)
        {
            if (token.StartsWith("~", StringComparison.Ordinal))
            {
                if (!TryParseSptVersion(token[1..], out var tilde))
                {
                    return true;
                }

                return installed.Major == tilde.Major && installed.Minor == tilde.Minor;
            }

            if (token.StartsWith("^", StringComparison.Ordinal))
            {
                if (!TryParseSptVersion(token[1..], out var caret))
                {
                    return true;
                }

                return installed.Major == caret.Major;
            }

            if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                return TryParseSptVersion(token[2..], out var max) && CompareSpt3(installed, max) <= 0;
            }

            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                return TryParseSptVersion(token[2..], out var min) && CompareSpt3(installed, min) >= 0;
            }

            if (token.StartsWith("<", StringComparison.Ordinal))
            {
                return TryParseSptVersion(token[1..], out var max) && CompareSpt3(installed, max) < 0;
            }

            if (token.StartsWith(">", StringComparison.Ordinal))
            {
                return TryParseSptVersion(token[1..], out var min) && CompareSpt3(installed, min) > 0;
            }

            if (TryParseSptVersion(token, out var exact))
            {
                // Forge authors often tag "4.1.5" without ~. SPT hotfixes (4.1.6)
                // still need that 4.1.x build — same rule as ~4.1.5.
                return installed.Major == exact.Major && installed.Minor == exact.Minor;
            }

            return true;
        }

        private static bool TryParseSptVersion(string? value, out Version version)
        {
            var normalized = VersionStringHelper.Normalize(value ?? "");
            if (Version.TryParse(normalized, out version!))
            {
                return true;
            }

            if (Version.TryParse(normalized + ".0", out version!))
            {
                return true;
            }

            version = new Version(0, 0);
            return false;
        }

        private static int CompareSpt3(Version a, Version b)
        {
            var left = new Version(a.Major, a.Minor, Math.Max(a.Build, 0));
            var right = new Version(b.Major, b.Minor, Math.Max(b.Build, 0));
            return left.CompareTo(right);
        }

        /// <summary>
        /// Some Forge version rows omit `link`; the download route is still predictable.
        /// </summary>
        private static void EnsureVersionDownloadLink(ForgeModSummary mod, ForgeModVersion version)
        {
            if (!string.IsNullOrWhiteSpace(version.Link))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(mod.Slug) || string.IsNullOrWhiteSpace(version.Version))
            {
                return;
            }

            version.Link =
                $"{ForgeApiService.WebsiteBaseUrl}/mod/download/{mod.Id}/{mod.Slug.Trim()}/{version.Version.Trim()}";
        }

        internal static InstalledModInfo? FindLocalMatch(
            RequiredModEntry entry,
            IReadOnlyList<InstalledModInfo> clientMods)
        {
            var candidates = new List<InstalledModInfo>();

            if (!string.IsNullOrWhiteSpace(entry.Guid))
            {
                candidates.AddRange(clientMods.Where(m =>
                    string.Equals(m.ForgeGuid, entry.Guid, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var extra in EntryGuids(entry).Skip(string.IsNullOrWhiteSpace(entry.Guid) ? 0 : 1))
            {
                candidates.AddRange(clientMods.Where(m =>
                    string.Equals(m.ForgeGuid, extra, StringComparison.OrdinalIgnoreCase)));
            }

            if (entry.ForgeModId is int id and > 0)
            {
                candidates.AddRange(clientMods.Where(m => m.ForgeModId == id));
            }

            if (!string.IsNullOrWhiteSpace(entry.Slug))
            {
                candidates.AddRange(clientMods.Where(m =>
                    string.Equals(m.ForgeSlug, entry.Slug, StringComparison.OrdinalIgnoreCase)));
            }

            candidates.AddRange(clientMods.Where(m =>
                PathStrictlyMatchesPackEntry(m.Path, entry) ||
                m.AllPaths.Any(p => PathStrictlyMatchesPackEntry(p, entry))));

            // Name matching is a last resort only when the pack entry has no id/guid/slug.
            // Otherwise "LootNET" / "Use Loose Loot" style collisions can pick the wrong mod
            // and stamp the wrong version onto its sidecar.
            var hasStrongId = entry.ForgeModId is > 0 ||
                              !string.IsNullOrWhiteSpace(entry.Guid) ||
                              !string.IsNullOrWhiteSpace(entry.Slug);
            if (!hasStrongId)
            {
                var nameKey = InstalledModsService.NormalizeModKey(entry.Name);
                candidates.AddRange(clientMods.Where(m =>
                {
                    var displayKey = InstalledModsService.NormalizeModKey(m.DisplayName);
                    var folderKey = InstalledModsService.NormalizeModKey(
                        Path.GetFileName(m.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                    return !string.IsNullOrEmpty(nameKey) && (displayKey == nameKey || folderKey == nameKey);
                }));
            }

            // Drop path mismatches (e.g. Gaylatea-UseLooseLoot.dll wrongly tagged as LootNET).
            // Hosted pack entries (no Forge id) must match strictly — otherwise a bad sidecar
            // GUID on every DLL would make one hosted mod "match" the whole plugins folder.
            candidates = entry.ForgeModId is > 0
                ? candidates.Where(m => PathBelongsToPackEntry(m.Path, entry)).ToList()
                : candidates.Where(m => PathStrictlyMatchesPackEntry(m.Path, entry)).ToList();

            var unique = candidates
                .GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (unique.Count == 0)
            {
                return null;
            }

            var required = (entry.Version ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(required))
            {
                var exact = unique.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.VersionHint) &&
                    VersionsEqual(required, m.VersionHint!));
                if (exact != null)
                {
                    return exact;
                }
            }

            return unique
                .OrderByDescending(m => ParseVersionRank(m.VersionHint))
                .ThenByDescending(m => m.IsEnabled)
                .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        /// <summary>
        /// Last path segment for pack matching. Do not use GetFileNameWithoutExtension on
        /// plugin folders — <c>com.bblai.battlepass</c> would become <c>com.bblai</c>.
        /// </summary>
        internal static string PathMatchLeaf(string path)
        {
            var name = Path.GetFileName(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            while (true)
            {
                if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^".disabled".Length];
                    continue;
                }

                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^4];
                    continue;
                }

                break;
            }

            return InstalledModsService.NormalizeModKey(name);
        }

        /// <summary>
        /// True when the install path is allowed to represent this pack entry.
        /// Rejects known cross-tagging (Use Loose Loot DLL stamped as LootNET), but still
        /// allows generic plugin folder names matched purely by Forge id/guid.
        /// </summary>
        internal static bool PathBelongsToPackEntry(string path, RequiredModEntry entry)
        {
            var leaf = PathMatchLeaf(path);
            if (string.IsNullOrWhiteSpace(leaf))
            {
                return false;
            }

            var slugCompact = InstalledModsService.NormalizeModKey(entry.Slug);
            var nameKey = InstalledModsService.NormalizeModKey(entry.Name);

            if (!string.IsNullOrWhiteSpace(slugCompact) && leaf.Contains(slugCompact))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(nameKey) && leaf.Contains(nameKey))
            {
                return true;
            }

            var entryIsLootNet = (!string.IsNullOrWhiteSpace(slugCompact) && slugCompact.Contains("lootnet"))
                                 || (!string.IsNullOrWhiteSpace(nameKey) && nameKey.Contains("lootnet"));
            var entryIsLooseLoot = (!string.IsNullOrWhiteSpace(slugCompact) && slugCompact.Contains("uselooseloot"))
                                   || (!string.IsNullOrWhiteSpace(nameKey) && nameKey.Contains("uselooseloot"))
                                   || (!string.IsNullOrWhiteSpace(nameKey) && nameKey.Contains("looseloot"));
            var pathIsLooseLoot = leaf.Contains("uselooseloot") || leaf.Contains("gaylatea");
            var pathIsLootNet = leaf.Contains("lootnet") && !pathIsLooseLoot;

            if (entryIsLootNet && pathIsLooseLoot)
            {
                return false;
            }

            if (entryIsLooseLoot && pathIsLootNet)
            {
                return false;
            }

            // Generic path (GuidMod, etc.) — allow Forge id/guid matching.
            return true;
        }

        /// <summary>
        /// Hosted / no-Forge-id installs must only touch paths that clearly match the entry
        /// name, slug, or listed clientFiles — never every DLL under BepInEx/plugins.
        /// </summary>
        internal static bool PathStrictlyMatchesPackEntry(string path, RequiredModEntry entry)
        {
            var normalizedPath = path.Replace('\\', '/');
            var leaf = PathMatchLeaf(path);
            if (string.IsNullOrWhiteSpace(leaf))
            {
                return false;
            }

            if (InstalledModsService.IdentityOverlapsPath(leaf, entry.Slug, entry.Name, entry.Guid))
            {
                return true;
            }

            if (entry.ExtraGuids is { Count: > 0 })
            {
                foreach (var extra in entry.ExtraGuids)
                {
                    // Use the identifying token (LootNet), not the full GUID.
                    // "...LootNet.fika" contains "fika" and would delete BepInEx/plugins/Fika.
                    var extraToken = IdentifyingGuidToken(extra);
                    if (string.IsNullOrWhiteSpace(extraToken))
                    {
                        continue;
                    }

                    if (InstalledModsService.IdentityOverlapsPath(leaf, extraToken))
                    {
                        return true;
                    }
                }
            }

            if (entry.ClientFiles is { Count: > 0 })
            {
                foreach (var rel in entry.ClientFiles)
                {
                    if (string.IsNullOrWhiteSpace(rel))
                    {
                        continue;
                    }

                    var normRel = rel.Replace('\\', '/').Trim('/');
                    var fullRel = "/" + normRel;
                    if (normalizedPath.EndsWith(fullRel, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    var relLeaf = PathMatchLeaf(normRel);
                    if (!string.IsNullOrWhiteSpace(relLeaf) && leaf == relLeaf)
                    {
                        return true;
                    }

                    // BepInEx/plugins/<folder>/... matches that plugin folder only —
                    // never a short prefix like BepInEx/plugins.
                    var parts = normRel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    var pluginsIdx = Array.FindIndex(
                        parts,
                        p => p.Equals("plugins", StringComparison.OrdinalIgnoreCase));
                    if (pluginsIdx >= 0 && pluginsIdx + 1 < parts.Length)
                    {
                        var pluginFolder = InstalledModsService.NormalizeModKey(parts[pluginsIdx + 1]);
                        if (!string.IsNullOrWhiteSpace(pluginFolder) && pluginFolder == leaf)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// csproj / BepInPlugin template leftover. Semver treats 1.0.0 as newer than 0.2.3.
        /// </summary>
        internal static bool LooksLikePlaceholderVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var n = NormalizeVersionLabel(version);
            var cut = n.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                n = n[..cut];
            }

            return n is "1.0.0" or "1.0" or "0.1.0" or "0.0.1";
        }

        private static int ParseVersionRank(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return -1;
            }

            var trimmed = NormalizeVersionLabel(version);
            var cut = trimmed.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
            {
                trimmed = trimmed[..cut];
            }

            return Version.TryParse(trimmed, out var parsed)
                ? (parsed.Major * 1_000_000) + (parsed.Minor * 1_000) + Math.Max(parsed.Build, 0)
                : 0;
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

            return CompareVersionRank(na, nb) == 0 && ParseVersionRank(na) >= 0;
        }

        /// <summary>Negative if a is older than b. Equal trailing zeros (2.0.1 vs 2.0.1.0).</summary>
        internal static int CompareVersionRank(string a, string b) =>
            ParseVersionRank(a).CompareTo(ParseVersionRank(b));

        private static string NormalizeVersionLabel(string? value)
        {
            var v = (value ?? "").Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                v = v[1..].Trim();
            }

            return v;
        }
    }

    internal static class RequiredModsStringExtensions
    {
        public static string IfEmpty(this string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
