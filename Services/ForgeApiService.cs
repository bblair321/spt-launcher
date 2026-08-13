using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SptLauncherWpf.Services
{
    public sealed class ForgeApiService
    {
        private static ForgeApiService? _instance;
        public static ForgeApiService Instance => _instance ??= new ForgeApiService();

        public const string BaseUrl = "https://sp-mod.com/api/v0";
        public const string WebsiteBaseUrl = "https://sp-mod.com";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<int, (DateTime ExpiresUtc, ForgeModSummary Mod)> _modCache = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTime _nextAllowedUtc = DateTime.MinValue;

        private ForgeApiService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SPTLauncher/4.2 (+https://github.com/bblair321/spt-launcher)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<ForgeModsPageResult> SearchModsAsync(
            string? query = null,
            string? sptVersion = null,
            int page = 1,
            int perPage = 12,
            CancellationToken cancellationToken = default)
        {
            var qs = new StringBuilder();
            AppendQuery(qs, "page", Math.Max(1, page).ToString());
            AppendQuery(qs, "per_page", Math.Clamp(perPage, 1, 50).ToString());
            AppendQuery(qs, "include", "category,versions");
            AppendQuery(qs, "sort", "-downloads");
            AppendQuery(qs, "fields", "id,guid,name,slug,teaser,thumbnail,downloads,detail_url,fika_compatibility,category_id");

            if (!string.IsNullOrWhiteSpace(query))
            {
                AppendQuery(qs, "query", query.Trim());
            }

            if (!string.IsNullOrWhiteSpace(sptVersion))
            {
                // Forge expects a SemVer constraint; match the SPT minor line (4.1.x).
                var constraint = BuildSptVersionFilter(sptVersion);
                if (!string.IsNullOrWhiteSpace(constraint))
                {
                    AppendQuery(qs, "filter[spt_version]", constraint);
                }
            }

            var url = $"{BaseUrl}/mods?{qs}";
            using var response = await SendGetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ForgeApiResponse<List<ForgeModSummary>>>(
                stream, JsonOptions, cancellationToken);

            if (parsed?.Data == null)
            {
                throw new InvalidOperationException("Forge returned an empty mods response.");
            }

            return new ForgeModsPageResult
            {
                Mods = parsed.Data,
                CurrentPage = parsed.Meta?.CurrentPage ?? page,
                LastPage = Math.Max(1, parsed.Meta?.LastPage ?? 1),
                Total = parsed.Meta?.Total ?? parsed.Data.Count,
                PerPage = parsed.Meta?.PerPage ?? perPage
            };
        }

        public async Task<ForgeModSummary> GetModAsync(
            int modId,
            CancellationToken cancellationToken = default)
        {
            if (_modCache.TryGetValue(modId, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            {
                return cached.Mod;
            }

            var url =
                $"{BaseUrl}/mod/{modId}?include=versions,category" +
                "&fields=id,guid,name,slug,teaser,thumbnail,downloads,detail_url,fika_compatibility,category_id";

            using var response = await SendGetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ForgeApiResponse<ForgeModSummary>>(
                stream, JsonOptions, cancellationToken);

            if (parsed?.Data == null)
            {
                throw new InvalidOperationException($"Forge returned no details for mod {modId}.");
            }

            _modCache[modId] = (DateTime.UtcNow.AddMinutes(10), parsed.Data);
            return parsed.Data;
        }

        public async Task<List<ForgeModVersion>> GetModVersionsAsync(
            int modId,
            string? sptVersion = null,
            CancellationToken cancellationToken = default)
        {
            // Prefer versions already loaded with GetModAsync to avoid a second rate-limited call.
            if (string.IsNullOrWhiteSpace(sptVersion) &&
                _modCache.TryGetValue(modId, out var cached) &&
                cached.ExpiresUtc > DateTime.UtcNow &&
                cached.Mod.Versions is { Count: > 0 })
            {
                return cached.Mod.Versions;
            }

            var qs = new StringBuilder();
            AppendQuery(qs, "per_page", "25");
            AppendQuery(qs, "sort", "-published_at");
            AppendQuery(qs, "fields", "id,version,link,content_length,spt_version_constraint,downloads,fika_compatibility");

            if (!string.IsNullOrWhiteSpace(sptVersion))
            {
                var constraint = BuildSptVersionFilter(sptVersion);
                if (!string.IsNullOrWhiteSpace(constraint))
                {
                    AppendQuery(qs, "filter[spt_version]", constraint);
                }
            }

            var url = $"{BaseUrl}/mod/{modId}/versions?{qs}";
            using var response = await SendGetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ForgeApiResponse<List<ForgeModVersion>>>(
                stream, JsonOptions, cancellationToken);

            return parsed?.Data ?? new List<ForgeModVersion>();
        }

        private static void AppendQuery(StringBuilder qs, string key, string value)
        {
            if (qs.Length > 0)
            {
                qs.Append('&');
            }

            qs.Append(Uri.EscapeDataString(key));
            qs.Append('=');
            qs.Append(Uri.EscapeDataString(value));
        }

        public async Task<ForgeFileTree?> GetFileTreeAsync(
            int modId,
            int versionId,
            CancellationToken cancellationToken = default)
        {
            var url = $"{BaseUrl}/mod/{modId}/versions/{versionId}/file-tree";
            using var response = await SendGetAsync(url, cancellationToken);
            // Some versions return 403/404 even when the download itself works (e.g. LootNET 1.1.0).
            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ForgeApiResponse<ForgeFileTree>>(
                stream, JsonOptions, cancellationToken);

            return parsed?.Data;
        }

        public async Task<ForgeUpdatesResult> CheckUpdatesAsync(
            IEnumerable<string> modVersionPairs,
            string sptVersion,
            CancellationToken cancellationToken = default)
        {
            var pairs = modVersionPairs
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pairs.Count == 0 || string.IsNullOrWhiteSpace(sptVersion))
            {
                return new ForgeUpdatesResult { SptVersion = sptVersion };
            }

            var qs = new StringBuilder();
            AppendQuery(qs, "mods", string.Join(",", pairs));
            AppendQuery(qs, "spt_version", VersionStringHelper.Normalize(sptVersion));

            var url = $"{BaseUrl}/mods/updates?{qs}";
            using var response = await SendGetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<ForgeApiResponse<ForgeUpdatesResult>>(
                stream, JsonOptions, cancellationToken);

            return parsed?.Data ?? new ForgeUpdatesResult { SptVersion = sptVersion };
        }

        public async Task<IReadOnlyList<ForgeDependencyNode>> GetDependenciesAsync(
            int modId,
            string version,
            string? sptVersion,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(sptVersion))
            {
                return Array.Empty<ForgeDependencyNode>();
            }

            var qs = new StringBuilder();
            AppendQuery(qs, "mods", $"{modId}:{version}");
            AppendQuery(qs, "spt_version", VersionStringHelper.Normalize(sptVersion));

            var url = $"{BaseUrl}/mods/dependencies?{qs}";
            using var response = await SendGetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<ForgeDependencyNode>();
            }

            var key = $"{modId}:{version}";
            foreach (var prop in data.EnumerateObject())
            {
                if (!string.Equals(prop.Name.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nodes = prop.Value.Deserialize<List<ForgeDependencyNode>>(JsonOptions);
                return nodes ?? new List<ForgeDependencyNode>();
            }

            // Fallback: first property if exact key casing differs.
            foreach (var prop in data.EnumerateObject())
            {
                var nodes = prop.Value.Deserialize<List<ForgeDependencyNode>>(JsonOptions);
                return nodes ?? new List<ForgeDependencyNode>();
            }

            return Array.Empty<ForgeDependencyNode>();
        }

        /// <summary>
        /// GET with spacing + retries on HTTP 429 (sp-mod.com rate limits burst sync traffic).
        /// </summary>
        private async Task<HttpResponseMessage> SendGetAsync(string url, CancellationToken cancellationToken)
        {
            const int maxAttempts = 5;
            HttpResponseMessage? last = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    var delay = _nextAllowedUtc - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken);
                    }

                    last?.Dispose();
                    last = await _http.GetAsync(url, cancellationToken);

                    // Small spacing between successful calls so pack sync doesn't burst.
                    _nextAllowedUtc = DateTime.UtcNow.AddMilliseconds(350);

                    if (last.StatusCode != (HttpStatusCode)429)
                    {
                        return last;
                    }

                    var retryAfter = ReadRetryAfter(last) ??
                                     TimeSpan.FromSeconds(Math.Min(30, 2 * attempt));
                    _nextAllowedUtc = DateTime.UtcNow.Add(retryAfter);
                    last.Dispose();
                    last = null;

                    if (attempt == maxAttempts)
                    {
                        throw new HttpRequestException(
                            $"HTTP status client error (429 Too Many Requests) for url ({url}). " +
                            "sp-mod.com rate-limited the launcher — wait a minute and sync again.");
                    }

                    await Task.Delay(retryAfter, cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }
            }

            throw new HttpRequestException($"Forge request failed for url ({url}).");
        }

        private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    return wait;
                }
            }

            return null;
        }

        public static string BuildModPageUrl(int modId, string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return $"{WebsiteBaseUrl}/mod/{modId}";
            }

            return $"{WebsiteBaseUrl}/mod/{modId}/{slug.Trim()}";
        }

        /// <summary>
        /// Builds a Forge filter constraint from a detected SPT version string.
        /// </summary>
        public static string? BuildSptVersionFilter(string? sptVersion)
        {
            var normalized = VersionStringHelper.Normalize(sptVersion ?? "");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            // Filter by the SPT minor line (e.g. 4.1.1 -> ~4.1.0).
            // Authors commonly tag mods as 4.1.x / ~4.1.0; pinning ^4.1.1 hid those on Forge.
            var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"~{parts[0]}.{parts[1]}.0";
            }

            return $"^{normalized}";
        }
    }
}
