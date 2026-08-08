using System.Text.Json.Serialization;

namespace SptLauncherWpf.Services
{
    public sealed class ForgeApiResponse<T>
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("meta")]
        public ForgePaginationMeta? Meta { get; set; }
    }

    public sealed class ForgePaginationMeta
    {
        [JsonPropertyName("current_page")]
        public int CurrentPage { get; set; }

        [JsonPropertyName("last_page")]
        public int LastPage { get; set; }

        [JsonPropertyName("per_page")]
        public int PerPage { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("from")]
        public int? From { get; set; }

        [JsonPropertyName("to")]
        public int? To { get; set; }
    }

    public sealed class ForgeModSummary
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("teaser")]
        public string? Teaser { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("detail_url")]
        public string? DetailUrl { get; set; }

        [JsonPropertyName("fika_compatibility")]
        public bool FikaCompatibility { get; set; }

        [JsonPropertyName("category_id")]
        public int? CategoryId { get; set; }

        [JsonPropertyName("owner")]
        public ForgeOwner? Owner { get; set; }

        [JsonPropertyName("category")]
        public ForgeCategory? Category { get; set; }

        [JsonPropertyName("versions")]
        public List<ForgeModVersion>? Versions { get; set; }
    }

    public sealed class ForgeOwner
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public sealed class ForgeCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public sealed class ForgeModVersion
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("content_length")]
        public long? ContentLength { get; set; }

        [JsonPropertyName("spt_version_constraint")]
        public string? SptVersionConstraint { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("fika_compatibility")]
        public object? FikaCompatibility { get; set; }

        public string FikaCompatibilityText => FikaCompatibility switch
        {
            bool b => b ? "compatible" : "incompatible",
            string s => s,
            _ => "unknown"
        };
    }

    public sealed class ForgeFileTree
    {
        [JsonPropertyName("verified_at")]
        public DateTime? VerifiedAt { get; set; }

        [JsonPropertyName("file_count")]
        public int FileCount { get; set; }

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }

        [JsonPropertyName("files")]
        public List<string> Files { get; set; } = new();
    }

    public sealed class ForgeModsPageResult
    {
        public List<ForgeModSummary> Mods { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int LastPage { get; set; } = 1;
        public int Total { get; set; }
        public int PerPage { get; set; } = 12;
    }

    public sealed class ForgeUpdatesResult
    {
        [JsonPropertyName("spt_version")]
        public string? SptVersion { get; set; }

        [JsonPropertyName("updates")]
        public List<ForgeUpdateEntry> Updates { get; set; } = new();

        [JsonPropertyName("blocked_updates")]
        public List<ForgeBlockedUpdateEntry> BlockedUpdates { get; set; } = new();

        [JsonPropertyName("up_to_date")]
        public List<ForgeUpdateCurrentVersion> UpToDate { get; set; } = new();

        [JsonPropertyName("incompatible_with_spt")]
        public List<ForgeUpdateCurrentVersion> IncompatibleWithSpt { get; set; } = new();
    }

    public sealed class ForgeUpdateEntry
    {
        [JsonPropertyName("current_version")]
        public ForgeUpdateCurrentVersion? CurrentVersion { get; set; }

        [JsonPropertyName("recommended_version")]
        public ForgeRecommendedVersion? RecommendedVersion { get; set; }

        [JsonPropertyName("update_reason")]
        public string? UpdateReason { get; set; }
    }

    public sealed class ForgeBlockedUpdateEntry
    {
        [JsonPropertyName("current_version")]
        public ForgeUpdateCurrentVersion? CurrentVersion { get; set; }

        [JsonPropertyName("block_reason")]
        public string? BlockReason { get; set; }
    }

    public sealed class ForgeUpdateCurrentVersion
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("mod_id")]
        public int ModId { get; set; }

        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    public sealed class ForgeRecommendedVersion
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("content_length")]
        public long? ContentLength { get; set; }

        [JsonPropertyName("fika_compatibility")]
        public object? FikaCompatibility { get; set; }

        public ForgeModVersion ToModVersion() => new()
        {
            Id = Id,
            Version = Version,
            Link = Link,
            ContentLength = ContentLength,
            FikaCompatibility = FikaCompatibility
        };
    }

    public sealed class ForgeDependencyNode
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("conflict")]
        public bool Conflict { get; set; }

        [JsonPropertyName("latest_compatible_version")]
        public ForgeRecommendedVersion? LatestCompatibleVersion { get; set; }

        [JsonPropertyName("dependencies")]
        public List<ForgeDependencyNode> Dependencies { get; set; } = new();
    }
}
