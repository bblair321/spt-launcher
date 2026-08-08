using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SptLauncherWpf.Services
{
    /// <summary>
    /// Sidecar metadata written next to mods installed via this launcher.
    /// </summary>
    public sealed class ForgeModMarker
    {
        public const string FileName = ".forge-mod.json";

        [JsonPropertyName("forgeModId")]
        public int ForgeModId { get; set; }

        [JsonPropertyName("guid")]
        public string? Guid { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("versionId")]
        public int? VersionId { get; set; }

        [JsonPropertyName("installedAtUtc")]
        public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;

        public static string GetMarkerPathForDirectory(string directory) =>
            Path.Combine(directory, FileName);

        public static string GetMarkerPathForFile(string filePath) =>
            filePath + ".forge-mod.json";

        public static ForgeModMarker? TryRead(string modPath, bool isDirectory)
        {
            try
            {
                var markerPath = isDirectory
                    ? GetMarkerPathForDirectory(modPath)
                    : GetMarkerPathForFile(modPath);

                if (!File.Exists(markerPath))
                {
                    return null;
                }

                var json = File.ReadAllText(markerPath);
                return JsonSerializer.Deserialize<ForgeModMarker>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void Write(string modPath, bool isDirectory, ForgeModMarker marker)
        {
            var markerPath = isDirectory
                ? GetMarkerPathForDirectory(modPath)
                : GetMarkerPathForFile(modPath);

            var dir = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(markerPath, json);
        }
    }
}
