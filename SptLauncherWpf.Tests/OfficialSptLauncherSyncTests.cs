using System.IO;
using System.Text.Json.Nodes;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class OfficialSptLauncherSyncTests
{
    [Theory]
    [InlineData("50.39.145.123:6969", "50.39.145.123:6969")]
    [InlineData("50.39.145.123", "50.39.145.123:6969")]
    [InlineData("https://50.39.145.123:6969", "50.39.145.123:6969")]
    [InlineData("https://50.39.145.123:6969/mod-pack", "50.39.145.123:6969")]
    [InlineData("http://play.example.com:6969/", "play.example.com:6969")]
    [InlineData("  1.2.3.4:8443  ", "1.2.3.4:8443")]
    public void TryNormalizeGameServerAddress_accepts_host_and_urls(string input, string expected)
    {
        Assert.Equal(expected, OfficialSptLauncherSync.TryNormalizeGameServerAddress(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:6969")]
    [InlineData("localhost:6969")]
    [InlineData("http://127.0.0.1:6969/mod-pack")]
    [InlineData("192.168.1.10:17865")]
    [InlineData("http://192.168.1.10:17865/mod-pack")]
    public void TryNormalizeGameServerAddress_skips_empty_local_and_lan_agent(string? input)
    {
        Assert.Null(OfficialSptLauncherSync.TryNormalizeGameServerAddress(input));
    }

    [Fact]
    public void TryUpsertLauncherSettingsJson_adds_server_and_preserves_other_fields()
    {
        const string existing = """
            {
              "FirstRun": false,
              "GamePath": "D:\\SPT",
              "Servers": [
                {
                  "Name": "Other",
                  "IpAddress": "10.0.0.5:6969",
                  "ServerId": "1"
                }
              ]
            }
            """;

        var added = OfficialSptLauncherSync.TryUpsertLauncherSettingsJson(
            existing,
            "50.39.145.123:6969",
            "50.39.145.123:6969",
            "1720000000",
            out var updated);

        Assert.True(added);
        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.False(root["FirstRun"]!.GetValue<bool>());
        Assert.Equal("D:\\SPT", root["GamePath"]!.GetValue<string>());
        var servers = root["Servers"]!.AsArray();
        Assert.Equal(2, servers.Count);
        Assert.Equal("50.39.145.123:6969", servers[1]!["IpAddress"]!.GetValue<string>());
        Assert.Equal("50.39.145.123:6969", servers[1]!["Name"]!.GetValue<string>());
        Assert.Equal("1720000000", servers[1]!["ServerId"]!.GetValue<string>());
    }

    [Fact]
    public void TryUpsertLauncherSettingsJson_is_idempotent_for_same_host()
    {
        const string existing = """
            {
              "Servers": [
                {
                  "Name": "Fika",
                  "IpAddress": "50.39.145.123:6969",
                  "ServerId": "9"
                }
              ]
            }
            """;

        Assert.False(OfficialSptLauncherSync.TryUpsertLauncherSettingsJson(
            existing,
            "50.39.145.123",
            "50.39.145.123:6969",
            "999",
            out var updated));
        Assert.Equal(existing, updated);
    }

    [Fact]
    public void TryUpsertLauncherSettingsJson_creates_servers_array_when_missing()
    {
        var added = OfficialSptLauncherSync.TryUpsertLauncherSettingsJson(
            "{}",
            "50.39.145.123:6969",
            "50.39.145.123:6969",
            "1",
            out var updated);

        Assert.True(added);
        var servers = JsonNode.Parse(updated)!["Servers"]!.AsArray();
        Assert.Single(servers);
        Assert.Equal("50.39.145.123:6969", servers[0]!["IpAddress"]!.GetValue<string>());
    }

    [Fact]
    public void TryUpsertLegacyConfigJson_sets_url_and_dev_mode()
    {
        const string existing = """
            {
              "GamePath": "D:\\EFT",
              "Server": {
                "Name": "SPT-AKI",
                "Url": "https://127.0.0.1:6969"
              },
              "IsDevMode": false
            }
            """;

        Assert.True(OfficialSptLauncherSync.TryUpsertLegacyConfigJson(
            existing,
            "https://50.39.145.123:6969",
            out var updated));

        var root = JsonNode.Parse(updated)!.AsObject();
        Assert.Equal("D:\\EFT", root["GamePath"]!.GetValue<string>());
        Assert.Equal("https://50.39.145.123:6969", root["Server"]!["Url"]!.GetValue<string>());
        Assert.True(root["IsDevMode"]!.GetValue<bool>());
    }

    [Fact]
    public void TryAddGameServer_writes_official_4x_settings_next_to_launcher()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-sync-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "SPT_Runtime");
        Directory.CreateDirectory(runtime);
        var launcher = Path.Combine(runtime, "SPT.Launcher.exe");
        File.WriteAllText(launcher, "fake");

        try
        {
            var first = OfficialSptLauncherSync.TryAddGameServer(launcher, "50.39.145.123:6969");
            var again = OfficialSptLauncherSync.TryAddGameServer(launcher, "https://50.39.145.123:6969/mod-pack");

            Assert.Equal(OfficialSptServerSyncStatus.Added, first);
            Assert.Equal(OfficialSptServerSyncStatus.AlreadyPresent, again);

            var settingsPath = Path.Combine(runtime, "user", "Launcher", "LauncherSettings.json");
            Assert.True(File.Exists(settingsPath));
            var json = File.ReadAllText(settingsPath);
            var servers = JsonNode.Parse(json)!["Servers"]!.AsArray();
            Assert.Single(servers);
            Assert.Equal("50.39.145.123:6969", servers[0]!["IpAddress"]!.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TryAddGameServer_updates_existing_3x_config_url()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-sync-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "SPT_Runtime");
        var legacyDir = Path.Combine(runtime, "user", "launcher");
        Directory.CreateDirectory(legacyDir);
        var launcher = Path.Combine(runtime, "SPT.Launcher.exe");
        File.WriteAllText(launcher, "fake");
        File.WriteAllText(
            Path.Combine(legacyDir, "config.json"),
            """{"Server":{"Name":"SPT-AKI","Url":"https://127.0.0.1:6969"},"IsDevMode":false}""");

        try
        {
            var status = OfficialSptLauncherSync.TryAddGameServer(launcher, "50.39.145.123:6969");
            Assert.Equal(OfficialSptServerSyncStatus.Added, status);

            var legacy = File.ReadAllText(Path.Combine(legacyDir, "config.json"));
            var rootJson = JsonNode.Parse(legacy)!.AsObject();
            Assert.Equal("https://50.39.145.123:6969", rootJson["Server"]!["Url"]!.GetValue<string>());
            Assert.True(rootJson["IsDevMode"]!.GetValue<bool>());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
