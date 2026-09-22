using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SptLauncherWpf.Services
{
    public enum OfficialSptServerSyncStatus
    {
        Added,
        AlreadyPresent,
        Skipped,
        NoLauncherInstall,
        Failed
    }

    /// <summary>
    /// Mirrors the join host into the official SPT launcher so Fika / remote play
    /// does not require pasting the URL by hand.
    /// SPT 4.x: SPT_Runtime/user/Launcher/LauncherSettings.json Servers[].IpAddress
    /// SPT 3.x: user/launcher/config.json Server.Url (+ IsDevMode)
    /// </summary>
    public static class OfficialSptLauncherSync
    {
        public const string LocalServerId = "1721162719";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static OfficialSptServerSyncStatus TryAddGameServer(string? launcherExePath, string? hostOrUrl)
        {
            try
            {
                var address = TryNormalizeGameServerAddress(hostOrUrl);
                if (address == null)
                {
                    return OfficialSptServerSyncStatus.Skipped;
                }

                var launcherPath = launcherExePath?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
                {
                    return OfficialSptServerSyncStatus.NoLauncherInstall;
                }

                var status = OfficialSptServerSyncStatus.Skipped;
                var settingsPath = GetLauncherSettingsPath(launcherPath);
                var settingsStatus = UpsertLauncherSettingsFile(settingsPath, address);
                if (settingsStatus is OfficialSptServerSyncStatus.Added
                    or OfficialSptServerSyncStatus.AlreadyPresent)
                {
                    status = settingsStatus;
                }

                foreach (var legacyPath in EnumerateDistinctExistingLegacyConfigPaths(launcherPath))
                {
                    var legacyStatus = UpsertLegacyConfigFile(legacyPath, ToHttpsBackendUrl(address));
                    if (status != OfficialSptServerSyncStatus.Added &&
                        legacyStatus is OfficialSptServerSyncStatus.Added
                            or OfficialSptServerSyncStatus.AlreadyPresent)
                    {
                        status = legacyStatus;
                    }
                }

                return status;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficialSptLauncherSync] Failed: {ex.Message}");
                return OfficialSptServerSyncStatus.Failed;
            }
        }

        /// <summary>
        /// Official SPT 4.x server list stores host:port with no scheme
        /// (e.g. 50.39.145.123:6969). Built-in local and the LAN pack agent are skipped.
        /// </summary>
        public static string? TryNormalizeGameServerAddress(string? hostOrUrl)
        {
            var packUrl = RequiredModsPackService.TryResolvePackUrl(hostOrUrl);
            if (string.IsNullOrWhiteSpace(packUrl) ||
                !Uri.TryCreate(packUrl, UriKind.Absolute, out var uri) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                return null;
            }

            if (uri.Port == RequiredModsPackService.LanAgentHttpPort)
            {
                return null;
            }

            if (IsBuiltInLocalServer(uri.Host, uri.Port))
            {
                return null;
            }

            return $"{uri.Host}:{uri.Port}";
        }

        public static string ToHttpsBackendUrl(string address) =>
            $"https://{address.Trim().TrimEnd('/')}";

        internal static bool AddressesMatch(string? left, string? right)
        {
            var a = NormalizeForCompare(left);
            var b = NormalizeForCompare(right);
            return a != null && b != null &&
                   string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryUpsertLauncherSettingsJson(
            string? existingJson,
            string ipAddress,
            string name,
            string serverId,
            out string updatedJson)
        {
            var obj = ParseObjectOrEmpty(existingJson);
            var serversKey = FindPropertyName(obj, "Servers") ?? "Servers";
            var servers = obj[serversKey] as JsonArray;
            if (servers == null)
            {
                servers = new JsonArray();
                obj[serversKey] = servers;
            }

            foreach (var item in servers)
            {
                if (item is not JsonObject server)
                {
                    continue;
                }

                var existing = ReadString(server, "IpAddress");
                if (AddressesMatch(existing, ipAddress))
                {
                    updatedJson = existingJson ?? string.Empty;
                    return false;
                }
            }

            servers.Add(new JsonObject
            {
                ["Name"] = name,
                ["IpAddress"] = ipAddress,
                ["ServerId"] = serverId
            });

            updatedJson = obj.ToJsonString(JsonOptions);
            return true;
        }

        internal static bool TryUpsertLegacyConfigJson(
            string existingJson,
            string httpsUrl,
            out string updatedJson)
        {
            var obj = ParseObjectOrEmpty(existingJson);
            var serverKey = FindPropertyName(obj, "Server") ?? "Server";
            var server = obj[serverKey] as JsonObject;
            if (server == null)
            {
                server = new JsonObject();
                obj[serverKey] = server;
            }

            var existingUrl = ReadString(server, "Url");
            var urlKey = FindPropertyName(server, "Url") ?? "Url";
            var urlChanged = !UrlsMatch(existingUrl, httpsUrl);
            if (urlChanged)
            {
                server[urlKey] = httpsUrl;
            }

            var devKey = FindPropertyName(obj, "IsDevMode");
            var changedDevMode = false;
            if (devKey != null)
            {
                var current = obj[devKey]?.GetValue<bool>() ?? false;
                if (!current)
                {
                    obj[devKey] = true;
                    changedDevMode = true;
                }
            }
            else
            {
                obj["IsDevMode"] = true;
                changedDevMode = true;
            }

            if (!urlChanged && !changedDevMode)
            {
                updatedJson = existingJson;
                return false;
            }

            updatedJson = obj.ToJsonString(JsonOptions);
            return true;
        }

        internal static string GetLauncherSettingsPath(string launcherExePath)
        {
            var launcherDir = Path.GetDirectoryName(launcherExePath)
                ?? throw new InvalidOperationException("Launcher path has no directory.");
            return Path.Combine(launcherDir, "user", "Launcher", "LauncherSettings.json");
        }

        private static OfficialSptServerSyncStatus UpsertLauncherSettingsFile(string path, string ipAddress)
        {
            string? existing = null;
            if (File.Exists(path))
            {
                existing = File.ReadAllText(path);
            }

            var serverId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            if (!TryUpsertLauncherSettingsJson(existing, ipAddress, ipAddress, serverId, out var updated))
            {
                return OfficialSptServerSyncStatus.AlreadyPresent;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, updated);
            Debug.WriteLine($"[OfficialSptLauncherSync] Added {ipAddress} to {path}");
            return OfficialSptServerSyncStatus.Added;
        }

        private static OfficialSptServerSyncStatus UpsertLegacyConfigFile(string path, string httpsUrl)
        {
            if (!File.Exists(path))
            {
                return OfficialSptServerSyncStatus.Skipped;
            }

            var existing = File.ReadAllText(path);
            if (!TryUpsertLegacyConfigJson(existing, httpsUrl, out var updated))
            {
                return OfficialSptServerSyncStatus.AlreadyPresent;
            }

            File.WriteAllText(path, updated);
            Debug.WriteLine($"[OfficialSptLauncherSync] Set Server.Url {httpsUrl} in {path}");
            return OfficialSptServerSyncStatus.Added;
        }

        private static IEnumerable<string> EnumerateDistinctExistingLegacyConfigPaths(string launcherExePath)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in EnumerateLegacyConfigPathCandidates(launcherExePath))
            {
                if (seen.Add(candidate) && File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> EnumerateLegacyConfigPathCandidates(string launcherExePath)
        {
            var launcherDir = Path.GetDirectoryName(launcherExePath);
            if (!string.IsNullOrWhiteSpace(launcherDir))
            {
                yield return Path.Combine(launcherDir, "user", "launcher", "config.json");
            }

            if (SptInstallPathHelper.TryResolveFromLauncherPath(launcherExePath, out var sptRoot, out _, out _))
            {
                yield return Path.Combine(sptRoot, "user", "launcher", "config.json");
                yield return Path.Combine(sptRoot, "SPT_Runtime", "user", "launcher", "config.json");
            }
        }

        private static bool IsBuiltInLocalServer(string host, int port)
        {
            if (port != RequiredModsPackService.DefaultSptHttpsPort)
            {
                return false;
            }

            return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeForCompare(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return TryNormalizeGameServerAddress(trimmed);
            }

            // IpAddress fields are host:port with no scheme; still accept a missing port.
            return TryNormalizeGameServerAddress(trimmed);
        }

        private static bool UrlsMatch(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                left.Trim().TrimEnd('/'),
                right.Trim().TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static JsonObject ParseObjectOrEmpty(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        private static string? FindPropertyName(JsonObject obj, string name)
        {
            foreach (var property in obj)
            {
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Key;
                }
            }

            return null;
        }

        private static string? ReadString(JsonObject obj, string name)
        {
            var key = FindPropertyName(obj, name);
            if (key == null || obj[key] is not JsonValue value)
            {
                return null;
            }

            try
            {
                return value.GetValue<string>();
            }
            catch
            {
                return value.ToString();
            }
        }
    }
}
