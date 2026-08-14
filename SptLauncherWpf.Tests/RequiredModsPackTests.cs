using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class RequiredModsPackTests
{
    [Fact]
    public void Parse_reads_pack_fields_including_server_manager_extras()
    {
        const string json = """
            {
              "sptVersion": "4.1.2",
              "fikaVersion": "2.4.1",
              "updatedAt": "2026-08-01T00:00:00Z",
              "instanceId": "abc",
              "mods": [
                {
                  "name": "BigBrain",
                  "slug": "bigbrain",
                  "forgeModId": 902,
                  "version": "1.5.0",
                  "guid": "xyz.drakia.bigbrain",
                  "clientFiles": ["BepInEx/plugins/DrakiaXYZ-BigBrain.dll"]
                }
              ],
              "fikaSynced": true,
              "sptPackUrlPath": "/mod-pack",
              "sptHttpModInstalled": true
            }
            """;

        var pack = RequiredModsPackService.Instance.Parse(json);

        Assert.Equal("4.1.2", pack.SptVersion);
        Assert.Equal("abc", pack.InstanceId);
        Assert.True(pack.FikaSynced);
        Assert.Equal("/mod-pack", pack.SptPackUrlPath);
        Assert.Single(pack.Mods);
        Assert.Equal(902, pack.Mods[0].ForgeModId);
        Assert.Equal("xyz.drakia.bigbrain", pack.Mods[0].Guid);
        Assert.Contains("BepInEx/plugins/DrakiaXYZ-BigBrain.dll", pack.Mods[0].ClientFiles!);
        Assert.True(pack.Mods[0].CanAutoInstall);
    }

    [Fact]
    public void Parse_hosted_download_url_enables_auto_install()
    {
        const string json = """
            {
              "mods": [
                {
                  "name": "FOVFix",
                  "slug": "fovfix",
                  "version": "4.1.0",
                  "guid": "com.fontaine.fovfix",
                  "clientFiles": ["BepInEx/plugins/FOVFix.dll"],
                  "downloadUrl": "https://blairsworkshop.com/api/download/cmstbpaz8000104kyz9ucm9qp",
                  "downloadKind": "blairsWorkshopJson",
                  "pageUrl": "https://blairsworkshop.com/mods/fovfix"
                }
              ]
            }
            """;

        var pack = RequiredModsPackService.Instance.Parse(json);
        Assert.Single(pack.Mods);
        Assert.True(pack.Mods[0].CanAutoInstall);
        Assert.Equal(
            "https://blairsworkshop.com/api/download/cmstbpaz8000104kyz9ucm9qp",
            pack.Mods[0].DownloadUrl);
        Assert.Equal("blairsWorkshopJson", pack.Mods[0].DownloadKind);
    }

    [Fact]
    public void TryResolvePackUrl_derives_https_6969_and_lan_http_17865()
    {
        Assert.Equal(
            "https://1.2.3.4:6969/mod-pack",
            RequiredModsPackService.TryResolvePackUrl("1.2.3.4"));
        Assert.Equal(
            "https://play.example.com:6969/mod-pack",
            RequiredModsPackService.TryResolvePackUrl("play.example.com"));
        Assert.Equal(
            "https://1.2.3.4:8443/mod-pack",
            RequiredModsPackService.TryResolvePackUrl("1.2.3.4:8443"));
        Assert.Equal(
            "http://192.168.1.10:17865/mod-pack",
            RequiredModsPackService.TryResolvePackUrl("192.168.1.10:17865"));
        Assert.Equal(
            "https://1.2.3.4:6969/mod-pack",
            RequiredModsPackService.NormalizePackUrl("https://1.2.3.4:6969"));
        Assert.Equal(
            "https://1.2.3.4:6969/mod-pack",
            RequiredModsPackService.TryResolvePackUrl("https://1.2.3.4:6969/mod-pack"));
    }

    [Fact]
    public void Diff_matches_guid_id_slug_and_marks_manual_extra()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry
                {
                    Name = "By Guid",
                    Guid = "com.mod.guid",
                    ForgeModId = 10,
                    Version = "1.0.0"
                },
                new RequiredModEntry
                {
                    Name = "By Id",
                    ForgeModId = 20,
                    Version = "2.0.0"
                },
                new RequiredModEntry
                {
                    Name = "By Slug Match",
                    ForgeModId = 40,
                    Slug = "cool-slug",
                    Version = "3.0.0"
                },
                new RequiredModEntry
                {
                    Name = "Manual Only",
                    Guid = "com.manual.only"
                },
                new RequiredModEntry
                {
                    Name = "Missing Mod",
                    ForgeModId = 99,
                    Slug = "missing-mod",
                    Version = "1.0.0"
                },
                new RequiredModEntry
                {
                    Name = "Wrong Version",
                    ForgeModId = 30,
                    Version = "9.9.9"
                },
                new RequiredModEntry
                {
                    Name = "Slug Only No Id",
                    Slug = "no-forge-id"
                }
            ]
        };

        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "Guid Mod",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\GuidMod",
                ForgeGuid = "com.mod.guid",
                VersionHint = "1.0.0"
            },
            new()
            {
                DisplayName = "Id Mod",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\IdMod",
                ForgeModId = 20,
                VersionHint = "2.0.0"
            },
            new()
            {
                DisplayName = "Cool Slug",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\CoolSlug",
                ForgeSlug = "cool-slug",
                VersionHint = "3.0.0"
            },
            new()
            {
                DisplayName = "Wrong",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\Wrong",
                ForgeModId = 30,
                VersionHint = "1.0.0"
            },
            new()
            {
                DisplayName = "Extra Local",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\Extra"
            },
            new()
            {
                DisplayName = "Server Noise",
                Kind = InstalledModKind.Server,
                Path = @"C:\SPT\user\mods\ServerNoise",
                ForgeModId = 999
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);

        Assert.Equal(RequiredModDiffStatus.Ok, Find(diff, "By Guid").Status);
        Assert.Equal(RequiredModDiffStatus.Ok, Find(diff, "By Id").Status);
        Assert.Equal(RequiredModDiffStatus.Ok, Find(diff, "By Slug Match").Status);
        Assert.Equal(RequiredModDiffStatus.ManualFix, Find(diff, "Manual Only").Status);
        Assert.Equal(RequiredModDiffStatus.ManualFix, Find(diff, "Slug Only No Id").Status);
        Assert.Equal(RequiredModDiffStatus.Missing, Find(diff, "Missing Mod").Status);
        Assert.Equal(RequiredModDiffStatus.WrongVersion, Find(diff, "Wrong Version").Status);
        Assert.Contains(diff.Items, i => i.Status == RequiredModDiffStatus.Extra && i.Installed?.DisplayName == "Extra Local");
        Assert.DoesNotContain(diff.Items, i => i.Installed?.DisplayName == "Server Noise");
        Assert.True(diff.NeedsSync);
        Assert.Equal(1, diff.MissingCount);
        Assert.Equal(1, diff.WrongVersionCount);
        Assert.Equal(2, diff.ManualFixCount);
    }

    [Fact]
    public void Diff_empty_local_version_is_wrong_when_pack_specifies_version()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry { Name = "X", ForgeModId = 1, Version = "2.0.0" }
            ]
        };
        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "X",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\X",
                ForgeModId = 1,
                VersionHint = null
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        Assert.Equal(RequiredModDiffStatus.WrongVersion, diff.Items[0].Status);
    }

    [Fact]
    public void VersionsEqual_tolerates_v_prefix()
    {
        Assert.True(RequiredModsPackService.VersionsEqual("v1.2.3", "1.2.3"));
        Assert.False(RequiredModsPackService.VersionsEqual("1.2.3", "1.2.4"));
    }

    [Fact]
    public void FilterClientInstallPaths_keeps_only_bepinex()
    {
        var mixed = ModPathClassifier.Classify(
            new[]
            {
                "BepInEx/plugins/Client.dll",
                "user/mods/Server/package.json",
                "readme.txt"
            },
            installHasSptRuntime: true);

        var clientOnly = ModPathClassifier.FilterClientInstallPaths(mixed.InstallableRelativePaths);

        Assert.Single(clientOnly);
        Assert.StartsWith("BepInEx/", clientOnly[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diff_does_not_treat_UseLooseLoot_dll_as_LootNET()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry
                {
                    Name = "LootNET",
                    Slug = "lootnet",
                    ForgeModId = 2679,
                    Guid = "com.20fpsguy.LootNet",
                    Version = "1.1.0"
                }
            ]
        };

        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "LootNET",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\Gaylatea-UseLooseLoot.dll",
                IsDirectory = false,
                // Contaminated sidecar from a prior bug:
                ForgeModId = 2679,
                ForgeGuid = "com.20fpsguy.LootNet",
                ForgeSlug = "lootnet",
                VersionHint = "1.6.0"
            },
            new()
            {
                DisplayName = "LootNET",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\LootNet",
                IsDirectory = true,
                ForgeModId = 2679,
                ForgeGuid = "com.20fpsguy.LootNet",
                ForgeSlug = "lootnet",
                VersionHint = "1.1.0"
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        var item = Assert.Single(diff.Items.Where(i => i.PackEntry?.Name == "LootNET"));
        Assert.Equal(RequiredModDiffStatus.Ok, item.Status);
        Assert.Equal(@"D:\SPT\BepInEx\plugins\LootNet", item.Installed?.Path);
    }

    private static RequiredModDiffItem Find(RequiredModsDiffResult diff, string name) =>
        diff.Items.First(i => i.PackEntry?.Name == name);
}
