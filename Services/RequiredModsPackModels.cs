using System.Text.Json.Serialization;

namespace SptLauncherWpf.Services
{
    public sealed class RequiredModsPack
    {
        [JsonPropertyName("sptVersion")]
        public string? SptVersion { get; set; }

        [JsonPropertyName("fikaVersion")]
        public string? FikaVersion { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("instanceId")]
        public string? InstanceId { get; set; }

        [JsonPropertyName("mods")]
        public List<RequiredModEntry> Mods { get; set; } = new();

        [JsonPropertyName("fikaConfigPath")]
        public string? FikaConfigPath { get; set; }

        [JsonPropertyName("fikaSynced")]
        public bool? FikaSynced { get; set; }

        [JsonPropertyName("fikaMessage")]
        public string? FikaMessage { get; set; }

        [JsonPropertyName("packPath")]
        public string? PackPath { get; set; }

        [JsonPropertyName("sptPackUrlPath")]
        public string? SptPackUrlPath { get; set; }

        [JsonPropertyName("sptHttpModInstalled")]
        public bool? SptHttpModInstalled { get; set; }

        [JsonPropertyName("sptHttpModMessage")]
        public string? SptHttpModMessage { get; set; }
    }

    public sealed class RequiredModEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("forgeModId")]
        public int? ForgeModId { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("clientFiles")]
        public List<string>? ClientFiles { get; set; }

        public string DisplayName =>
            !string.IsNullOrWhiteSpace(Name) ? Name! :
            !string.IsNullOrWhiteSpace(Slug) ? Slug! :
            ForgeModId is int id ? $"Mod {id}" :
            !string.IsNullOrWhiteSpace(Guid) ? Guid! :
            "Unknown mod";

        /// <summary>Auto-sync needs a Forge mod id to download the correct archive.</summary>
        public bool CanAutoInstall => ForgeModId is > 0;
    }

    public enum RequiredModDiffStatus
    {
        Ok,
        Missing,
        WrongVersion,
        ManualFix,
        Extra
    }

    public sealed class RequiredModDiffItem
    {
        public RequiredModDiffStatus Status { get; init; }
        public RequiredModEntry? PackEntry { get; init; }
        public InstalledModInfo? Installed { get; init; }
        public string Message { get; init; } = "";
    }

    public sealed class RequiredModsDiffResult
    {
        public RequiredModsPack Pack { get; init; } = new();
        public IReadOnlyList<RequiredModDiffItem> Items { get; init; } = Array.Empty<RequiredModDiffItem>();

        public int OkCount => Items.Count(i => i.Status == RequiredModDiffStatus.Ok);
        public int MissingCount => Items.Count(i => i.Status == RequiredModDiffStatus.Missing);
        public int WrongVersionCount => Items.Count(i => i.Status == RequiredModDiffStatus.WrongVersion);
        public int ManualFixCount => Items.Count(i => i.Status == RequiredModDiffStatus.ManualFix);
        public int ExtraCount => Items.Count(i => i.Status == RequiredModDiffStatus.Extra);

        public bool NeedsSync => MissingCount > 0 || WrongVersionCount > 0;
        public bool IsReady => !NeedsSync && ManualFixCount == 0;
        public bool HasBlockingIssues => NeedsSync;
    }

    public sealed class RequiredModsSyncProgress
    {
        public string Message { get; init; } = "";
        public int Current { get; init; }
        public int Total { get; init; }
    }

    public sealed class RequiredModsSyncReport
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int InstalledCount { get; init; }
        public int SkippedServerOnlyCount { get; init; }
        public int FailedCount { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
        public RequiredModsDiffResult? DiffAfter { get; init; }
    }
}
