using System.IO;
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
                  "extraGuids": ["xyz.drakia.bigbrain.extra"],
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
        Assert.Contains("xyz.drakia.bigbrain.extra", pack.Mods[0].ExtraGuids!);
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
        Assert.Equal(
            "http://192.168.0.20:17865/mod-pack/mirror/tarkov-battlepass",
            RequiredModsPackService.AgentMirrorFallbackUrl(
                "https://192.168.0.20:6969/mod-pack/mirror/tarkov-battlepass"));
        Assert.Null(RequiredModsPackService.AgentMirrorFallbackUrl(
            "http://192.168.0.20:17865/mod-pack/mirror/tarkov-battlepass"));
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
    public void Diff_fills_workshop_url_for_battlepass_without_forge_id()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry
                {
                    Name = "Tarkov BattlePass",
                    Slug = "tarkov-battlepass",
                    Guid = "com.bblai.battlepass",
                    Version = "0.2.4"
                }
            ]
        };

        var missing = RequiredModsPackService.Instance.Diff(pack, Array.Empty<InstalledModInfo>());
        Assert.Equal(RequiredModDiffStatus.Missing, missing.Items[0].Status);
        Assert.True(pack.Mods[0].CanAutoInstall);
        Assert.Contains("/api/mods/tarkov-battlepass", pack.Mods[0].DownloadUrl);

        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "BattlePass",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\com.bblai.battlepass",
                IsDirectory = true,
                ForgeGuid = "com.bblai.battlepass",
                VersionHint = "0.2.4"
            }
        };
        var present = RequiredModsPackService.Instance.Diff(pack, installed);
        Assert.Equal(RequiredModDiffStatus.Ok, present.Items[0].Status);
        Assert.False(present.NeedsSync);
        Assert.Equal(0, present.ManualFixCount);
    }

    [Fact]
    public void Diff_empty_local_version_is_ok_when_files_are_present()
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
        Assert.Equal(RequiredModDiffStatus.Ok, diff.Items[0].Status);
        Assert.False(diff.NeedsSync);
    }

    [Fact]
    public void Diff_newer_local_fileversion_is_not_wrong_version()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry { Name = "X", ForgeModId = 1, Version = "2.0.1" }
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
                VersionHint = "2.1.0"
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        Assert.Equal(RequiredModDiffStatus.Ok, diff.Items[0].Status);
    }

    [Fact]
    public void Diff_placeholder_pack_version_does_not_downgrade_real_install()
    {
        var pack = new RequiredModsPack
        {
            Mods =
            [
                new RequiredModEntry
                {
                    Name = "BattlePass",
                    ForgeModId = 1,
                    Version = "1.0.0"
                }
            ]
        };
        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "BattlePass",
                Kind = InstalledModKind.Client,
                Path = @"C:\SPT\BepInEx\plugins\com.bblai.battlepass",
                ForgeModId = 1,
                VersionHint = "0.2.3"
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        Assert.Equal(RequiredModDiffStatus.Ok, diff.Items[0].Status);
        Assert.False(diff.NeedsSync);
        Assert.True(RequiredModsPackService.LooksLikePlaceholderVersion("1.0.0"));
        Assert.True(RequiredModsPackService.DownloadErrorLooksGone(
            "This download isn't a supported .zip/.7z archive. Open the mod on Forge to install it manually."));
    }

    [Fact]
    public void VersionsEqual_ignores_trailing_zeros()
    {
        Assert.True(RequiredModsPackService.VersionsEqual("v2.0.1", "2.0.1.0"));
        Assert.False(RequiredModsPackService.VersionsEqual("2.0.1", "2.1.0"));
    }

    [Fact]
    public void Hosted_zip_fileversion_older_than_pack_is_detected()
    {
        Assert.True(RequiredModsPackService.CompareVersionRank("1.1.0", "1.1.2") < 0);
        Assert.True(RequiredModsPackService.CompareVersionRank("1.0.9", "1.1.2") < 0);
        Assert.False(RequiredModsPackService.CompareVersionRank("1.1.2", "1.1.2") < 0);
        Assert.Equal(
            "1.1.0",
            InstalledModsService.NormalizeFileVersion("1.1.0+551db3eb560423b3b41c804b6e3117f28c0c4e2a"));
    }

    [Fact]
    public void FilterClientInstallPaths_keeps_bepinex_and_managed()
    {
        var mixed = ModPathClassifier.Classify(
            new[]
            {
                "BepInEx/plugins/Client.dll",
                "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll",
                "user/mods/Server/package.json",
                "readme.txt"
            },
            installHasSptRuntime: true);

        var clientOnly = ModPathClassifier.FilterClientInstallPaths(mixed.InstallableRelativePaths);

        Assert.Equal(2, clientOnly.Count);
        Assert.Contains(clientOnly, p => p.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            clientOnly,
            p => p.StartsWith("EscapeFromTarkov_Data/Managed/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(clientOnly, p => p.Contains("user/mods", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void CommonLib_client_files_do_not_match_SAIN_folder()
    {
        var commonLib = new RequiredModEntry
        {
            Name = "WTT - CommonLib",
            Slug = "wtt-commonlib",
            ForgeModId = 2310,
            Guid = "com.fika.core",
            Version = "3.0.6",
            ClientFiles =
            [
                "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll",
                "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLibFika.dll"
            ]
        };
        var sain = new RequiredModEntry
        {
            Name = "SAIN - Solarint's AI Modifications - Full AI Combat System Replacement",
            Slug = "sain",
            ForgeModId = 900,
            Version = "4.5.0",
            ClientFiles = ["BepInEx/plugins/SAIN/SAIN.dll"]
        };

        Assert.False(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\SAIN", commonLib));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\SAIN", sain));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\WTT-ClientCommonLib", commonLib));
        Assert.False(InstalledModsService.MarkerBelongsToInstall(
            new ForgeModMarker
            {
                Name = "WTT - CommonLib",
                Slug = "wtt-commonlib",
                Guid = "com.fika.core",
                Version = "3.0.6"
            },
            @"D:\SPT\BepInEx\plugins\SAIN"));
    }

    [Fact]
    public void Plugin_folder_suffix_still_matches_pack_slug()
    {
        var moreBots = new RequiredModEntry
        {
            Name = "MoreBotsAPI",
            Slug = "morebotsapi",
            ForgeModId = 2426,
            Guid = "com.morebotsapi.tacticaltoaster",
            Version = "2.1.1"
        };
        var untar = new RequiredModEntry
        {
            Name = "UNTAR Go Home!",
            Slug = "untargohome",
            ForgeModId = 2342,
            Guid = "com.untargh.tacticaltoaster",
            Version = "3.2.1"
        };

        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\MoreBotsPlugin", moreBots));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\UNTARGHPlugin", untar));
        Assert.True(InstalledModsService.MarkerBelongsToInstall(
            new ForgeModMarker
            {
                Name = moreBots.Name,
                Slug = moreBots.Slug,
                Guid = moreBots.Guid,
                Version = moreBots.Version
            },
            @"D:\SPT\BepInEx\plugins\MoreBotsPlugin"));
        Assert.True(InstalledModsService.MarkerBelongsToInstall(
            new ForgeModMarker
            {
                Name = untar.Name,
                Slug = untar.Slug,
                Guid = untar.Guid,
                Version = untar.Version
            },
            @"D:\SPT\BepInEx\plugins\UNTARGHPlugin"));
    }

    [Fact]
    public void Hosted_404_falls_back_to_forge_when_mod_id_is_set()
    {
        var entry = new RequiredModEntry
        {
            Name = "ABPS - Acid's Bot Placement System",
            ForgeModId = 2100,
            DownloadUrl = "https://blairsworkshop.com/api/download/abps"
        };
        Assert.True(RequiredModsPackService.DownloadErrorLooksGone(
            "Hosted download failed for ABPS: Response status code does not indicate success: 404 (Not Found)."));
        Assert.True(RequiredModsPackService.ShouldFallbackHostedDownloadToForge(
            entry,
            "Response status code does not indicate success: 404 (Not Found)."));
        Assert.True(RequiredModsPackService.ShouldFallbackHostedDownloadToForge(
            new RequiredModEntry { Name = "WTT- Armory", DownloadUrl = "https://example.invalid/armory" },
            "Response status code does not indicate success: 404 (Not Found)."));
        entry.ForgeModId = 0;
        entry.Name = null;
        entry.Slug = null;
        Assert.False(RequiredModsPackService.ShouldFallbackHostedDownloadToForge(
            entry,
            "Response status code does not indicate success: 404 (Not Found)."));
        Assert.False(RequiredModsPackService.ShouldFallbackHostedDownloadToForge(
            new RequiredModEntry
            {
                Name = "Tarkov BattlePass",
                Slug = "tarkov-battlepass",
                Guid = "com.bblai.battlepass",
                DownloadUrl = "https://blairsworkshop.com/api/download/tarkov-battlepass"
            },
            "Response status code does not indicate success: 404 (Not Found)."));
        Assert.False(RequiredModsPackService.DownloadErrorLooksGone(
            "Version 2.1.1 not found on sp-mod.com for mod id 2100"));
    }

    [Fact]
    public void Parse_forge_mod_id_from_page_url_and_match_armory_name()
    {
        Assert.Equal(2246, RequiredModsPackService.TryParseForgeModIdFromPageUrl(
            "https://sp-mod.com/mod/2246/wtt-armory"));
        Assert.Equal(2246, RequiredModsPackService.TryParseForgeModIdFromPageUrl(
            "https://forge.sp-tarkov.com/mod/2246/wtt-armory"));
        Assert.Null(RequiredModsPackService.TryParseForgeModIdFromPageUrl(
            "https://blairsworkshop.com/mods/wtt-armory"));

        var entry = new RequiredModEntry { Name = "WTT - Armory" };
        var hit = RequiredModsPackService.MatchSearchedMod(
            entry,
            [new ForgeModSummary { Id = 2246, Name = "WTT- Armory", Slug = "wtt-armory" }]);
        Assert.NotNull(hit);
        Assert.Equal(2246, hit!.Id);
        Assert.Equal(new[] { 2246 }, RequiredModsPackService.ForgeIdsToTry(
            new RequiredModEntry
            {
                PageUrl = "https://sp-mod.com/mod/2246/wtt-armory"
            }));
    }

    [Fact]
    public void ForgeVersionsToTry_adds_newer_builds_after_pack_version()
    {
        var versions = new List<ForgeModVersion>
        {
            new() { Id = 1, Version = "2.0.5" },
            new() { Id = 2, Version = "2.1.0" },
            new() { Id = 3, Version = "2.1.1" },
            new() { Id = 4, Version = "3.0.0" },
            new() { Id = 0, Version = "2.0.4" }
        };
        var order = RequiredModsPackService.ForgeVersionsToTry(versions, "2.0.5")
            .Select(v => v.Version)
            .ToList();
        Assert.Equal(new[] { "2.0.5", "2.1.0", "2.1.1", "3.0.0" }, order);
    }

    [Theory]
    [InlineData("~4.1.5", "4.1.5", true)]
    [InlineData("~4.1.0", "4.1.5", true)]
    [InlineData("~4.0.13", "4.1.5", false)]
    [InlineData("~4.0 <4.1.0", "4.1.5", false)]
    [InlineData("~4.0.13", "4.0.13", true)]
    [InlineData("4.1.5", "4.1.6", true)]
    [InlineData("4.1.3", "4.1.6", true)]
    [InlineData("4.0.13", "4.1.6", false)]
    public void SptConstraintAllows_treats_4_1_x_as_compatible(string constraint, string installed, bool expected)
    {
        Assert.Equal(expected, RequiredModsPackService.SptConstraintAllows(constraint, installed));
    }

    [Fact]
    public void ForgeVersionsToTry_skips_4_0_armory_when_spt_is_4_1()
    {
        var versions = new List<ForgeModVersion>
        {
            new() { Id = 1, Version = "2.0.5", SptVersionConstraint = "~4.0 <4.1.0" },
            new() { Id = 3, Version = "2.1.1", SptVersionConstraint = "~4.0.13" },
            new() { Id = 4, Version = "3.0.0", SptVersionConstraint = "~4.1.5" }
        };
        var order = RequiredModsPackService.ForgeVersionsToTry(versions, "2.0.5", "4.1.5")
            .Select(v => v.Version)
            .ToList();
        Assert.Equal(new[] { "3.0.0" }, order);
    }

    [Fact]
    public void ForgeVersionsToTry_picks_lootnet_1_1_2_on_spt_4_1_6()
    {
        var versions = new List<ForgeModVersion>
        {
            new() { Id = 15222, Version = "1.1.2", SptVersionConstraint = "4.1.5" },
            new() { Id = 14954, Version = "1.1.1", SptVersionConstraint = "4.1.3" },
            new() { Id = 14335, Version = "1.1.0", SptVersionConstraint = "~4.1" },
            new() { Id = 14079, Version = "1.0.9", SptVersionConstraint = "4.0.13" }
        };
        var order = RequiredModsPackService.ForgeVersionsToTry(versions, "1.1.2", "4.1.6")
            .Select(v => v.Version)
            .ToList();
        Assert.Equal("1.1.2", order[0]);
        Assert.DoesNotContain("1.0.9", order);
    }

    [Fact]
    public void Hosted_guid_folder_matches_even_when_slug_differs()
    {
        var entry = new RequiredModEntry
        {
            Name = "BattlePass",
            Slug = "tarkov-battlepass",
            Guid = "com.bblai.battlepass",
            Version = "0.2.0",
            DownloadUrl = "https://blairsworkshop.com/api/download/tarkov-battlepass",
            ClientFiles = ["BepInEx/plugins/com.bblai.battlepass/SptBattlePass.Client.dll"]
        };

        Assert.Equal("combblaibattlepass", RequiredModsPackService.PathMatchLeaf(
            @"D:\SPT\BepInEx\plugins\com.bblai.battlepass"));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\com.bblai.battlepass", entry));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(
            @"D:\SPT\BepInEx\plugins\com.bblai.battlepass\SptBattlePass.Client.dll", entry));

        var local = new InstalledModInfo
        {
            DisplayName = "BattlePass",
            Path = @"D:\SPT\BepInEx\plugins\com.bblai.battlepass",
            Kind = InstalledModKind.Client,
            IsDirectory = true,
            VersionHint = "0.2.0",
            ForgeGuid = "com.bblai.battlepass",
            ForgeSlug = "tarkov-battlepass"
        };
        var match = RequiredModsPackService.FindLocalMatch(entry, [local]);
        Assert.NotNull(match);
        Assert.Equal(local.Path, match!.Path);
    }

    [Fact]
    public void WorkshopModsApiUrl_maps_slug_download_to_mods_endpoint()
    {
        var entry = new RequiredModEntry
        {
            Slug = "tarkov-battlepass",
            Guid = "com.bblai.battlepass"
        };
        Assert.Equal(
            "https://blairsworkshop.com/api/mods/tarkov-battlepass",
            RequiredModsPackService.WorkshopModsApiUrl(
                entry,
                "https://blairsworkshop.com/api/download/tarkov-battlepass"));
        Assert.Equal(
            "https://blairsworkshop.com/api/mods/tarkov-battlepass",
            RequiredModsPackService.WorkshopModsApiUrl(
                entry,
                "https://blairsworkshop.com/mods/tarkov-battlepass"));
        Assert.Equal(
            "https://blairsworkshop.com/api/download/cmtxhkhe7000004jyebwpeia6",
            RequiredModsPackService.TryLatestDownloadUrlFromWorkshopModJson(
                """{"slug":"tarkov-battlepass","latestVersion":{"id":"cmtxhkhe7000004jyebwpeia6","version":"0.2.4","downloadUrl":"https://blairsworkshop.com/api/download/cmtxhkhe7000004jyebwpeia6"}}"""));
    }

    private static RequiredModEntry LootNetPackEntry() => new()
    {
        Name = "LootNET",
        Slug = "lootnet",
        ForgeModId = 2679,
        Guid = "com.20fpsguy.LootNet",
        ExtraGuids = ["com.20fpsguy.LootNet.fika", "com.20fpsguy.LootNet.kills"],
        Version = "1.1.2",
        ClientFiles =
        [
            "BepInEx/plugins/LootNet/LootNet.dll",
            "BepInEx/plugins/LootNet/LootNetFika.dll"
        ]
    };

    [Fact]
    public void FindAllLocalMatches_collects_old_lootnet_folder_and_loose_dll_not_uselooseloot()
    {
        var entry = LootNetPackEntry();
        var current = new InstalledModInfo
        {
            DisplayName = "LootNET",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\LootNet",
            IsDirectory = true,
            ForgeModId = 2679,
            ForgeGuid = "com.20fpsguy.LootNet",
            ForgeSlug = "lootnet",
            VersionHint = "1.1.2"
        };
        var leftover = new InstalledModInfo
        {
            DisplayName = "LootNet",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\LootNet.dll",
            IsDirectory = false,
            VersionHint = "1.0.9"
        };
        var looseLoot = new InstalledModInfo
        {
            DisplayName = "Use Loose Loot",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\Gaylatea-UseLooseLoot.dll",
            IsDirectory = false,
            ForgeModId = 2679,
            ForgeGuid = "com.20fpsguy.LootNet",
            ForgeSlug = "lootnet",
            VersionHint = "1.6.0"
        };

        var matches = RequiredModsPackService.FindAllLocalMatches(entry, [current, leftover, looseLoot]);
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Path.EndsWith(@"\LootNet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(matches, m => m.Path.EndsWith(@"\LootNet.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(matches, m => m.Path.Contains("UseLooseLoot", StringComparison.OrdinalIgnoreCase));
        Assert.Single(RequiredModsPackService.StaleLocalCopies(entry, current, matches));
    }

    [Fact]
    public void Diff_marks_leftover_lootnet_copy_as_wrong_version()
    {
        var pack = new RequiredModsPack { Mods = [LootNetPackEntry()] };
        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "LootNET",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\LootNet",
                IsDirectory = true,
                ForgeModId = 2679,
                ForgeGuid = "com.20fpsguy.LootNet",
                ForgeSlug = "lootnet",
                VersionHint = "1.1.2"
            },
            new()
            {
                DisplayName = "LootNet old",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\LootNet.dll",
                IsDirectory = false,
                VersionHint = "1.0.9"
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        var item = Assert.Single(diff.Items.Where(i => i.PackEntry?.Name == "LootNET"));
        Assert.Equal(RequiredModDiffStatus.WrongVersion, item.Status);
        Assert.Contains("leftover", item.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(diff.NeedsSync);
        Assert.DoesNotContain(diff.Items, i => i.Status == RequiredModDiffStatus.Extra);
    }

    [Fact]
    public void Diff_keeps_lootnet_companion_dlls_in_same_folder_as_ok()
    {
        var pack = new RequiredModsPack { Mods = [LootNetPackEntry()] };
        var installed = new List<InstalledModInfo>
        {
            new()
            {
                DisplayName = "LootNet",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\LootNet.dll",
                IsDirectory = false,
                ForgeModId = 2679,
                ForgeGuid = "com.20fpsguy.LootNet",
                VersionHint = "1.1.2"
            },
            new()
            {
                DisplayName = "LootNet.fika",
                Kind = InstalledModKind.Client,
                Path = @"D:\SPT\BepInEx\plugins\LootNetFika.dll",
                IsDirectory = false,
                ForgeGuid = "com.20fpsguy.LootNet.fika",
                VersionHint = "1.1.2"
            }
        };

        var diff = RequiredModsPackService.Instance.Diff(pack, installed);
        var item = Assert.Single(diff.Items.Where(i => i.PackEntry != null));
        Assert.Equal(RequiredModDiffStatus.Ok, item.Status);
        Assert.False(diff.NeedsSync);
    }

    [Fact]
    public void CommonLib_does_not_replace_fika_core_folder()
    {
        var commonLib = new RequiredModEntry
        {
            Name = "WTT - CommonLib",
            Slug = "wtt-commonlib",
            ForgeModId = 2310,
            Guid = "com.fika.core",
            Version = "3.0.6",
            ClientFiles =
            [
                "BepInEx/plugins/WTT-ClientCommonLib/WTT-ClientCommonLib.dll"
            ]
        };
        var fika = new InstalledModInfo
        {
            DisplayName = "Fika.Core",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\Fika.Core",
            IsDirectory = true,
            ForgeGuid = "com.fika.core",
            VersionHint = "1.1.0"
        };
        var wtt = new InstalledModInfo
        {
            DisplayName = "WTT CommonLib",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\WTT-ClientCommonLib",
            IsDirectory = true,
            ForgeModId = 2310,
            ForgeSlug = "wtt-commonlib",
            VersionHint = "3.0.6"
        };

        Assert.True(RequiredModsPackService.IsCoreGuidOnlyCollision(fika.Path, commonLib));
        var matches = RequiredModsPackService.FindAllLocalMatches(commonLib, [fika, wtt]);
        Assert.Single(matches);
        Assert.Equal(wtt.Path, matches[0].Path);
    }

    [Fact]
    public void RemoveLocalCopies_deletes_old_lootnet_and_keeps_unrelated_plugin()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-pack-rm-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        var lootNetDir = Path.Combine(plugins, "LootNet");
        Directory.CreateDirectory(lootNetDir);
        File.WriteAllText(Path.Combine(lootNetDir, "LootNet.dll"), "new");
        File.WriteAllText(Path.Combine(plugins, "LootNet.dll"), "old");
        Directory.CreateDirectory(Path.Combine(plugins, "UIFixes"));
        File.WriteAllText(Path.Combine(plugins, "UIFixes", "UIFixes.dll"), "ui");

        try
        {
            var keep = new[] { lootNetDir };
            var removed = RequiredModsPackService.RemoveLocalCopies(root, LootNetPackEntry(), keep);
            Assert.True(removed >= 1);
            Assert.True(Directory.Exists(lootNetDir));
            Assert.False(File.Exists(Path.Combine(plugins, "LootNet.dll")));
            Assert.True(File.Exists(Path.Combine(plugins, "UIFixes", "UIFixes.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PruneUnlistedPluginFiles_deletes_old_dll_inside_mod_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-pack-prune-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        var lootNetDir = Path.Combine(plugins, "LootNet");
        Directory.CreateDirectory(lootNetDir);
        var current = Path.Combine(lootNetDir, "LootNet.dll");
        var leftover = Path.Combine(lootNetDir, "LootNet.old.dll");
        File.WriteAllText(current, "new");
        File.WriteAllText(leftover, "old");
        File.WriteAllText(Path.Combine(plugins, "Tyfon.UIFixes.dll"), "ui");

        try
        {
            var removed = RequiredModsPackService.PruneUnlistedPluginFiles(
                root,
                LootNetPackEntry(),
                [current]);
            Assert.Equal(1, removed);
            Assert.True(File.Exists(current));
            Assert.False(File.Exists(leftover));
            Assert.True(File.Exists(Path.Combine(plugins, "Tyfon.UIFixes.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LootNet_extra_guid_does_not_match_or_replace_fika_folder()
    {
        var entry = LootNetPackEntry();
        var fikaFolder = new InstalledModInfo
        {
            DisplayName = "Fika",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\Fika",
            IsDirectory = true,
            ForgeGuid = "com.fika.core"
        };
        var fikaCore = new InstalledModInfo
        {
            DisplayName = "Fika.Core",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\Fika.Core",
            IsDirectory = true
        };
        var lootNetFika = new InstalledModInfo
        {
            DisplayName = "LootNet.fika",
            Kind = InstalledModKind.Client,
            Path = @"D:\SPT\BepInEx\plugins\LootNetFika.dll",
            IsDirectory = false,
            ForgeGuid = "com.20fpsguy.LootNet.fika"
        };

        Assert.Equal("lootnet", RequiredModsPackService.IdentifyingGuidToken("com.20fpsguy.LootNet.fika"));
        Assert.True(RequiredModsPackService.IsProtectedCorePluginPath(fikaFolder.Path));
        Assert.True(RequiredModsPackService.IsProtectedCorePluginPath(@"D:\SPT\BepInEx\plugins\Fika.Core.dll"));
        Assert.False(RequiredModsPackService.IsProtectedCorePluginPath(lootNetFika.Path));
        Assert.False(RequiredModsPackService.PathStrictlyMatchesPackEntry(fikaFolder.Path, entry));
        Assert.False(RequiredModsPackService.PathStrictlyMatchesPackEntry(fikaCore.Path, entry));
        Assert.True(RequiredModsPackService.PathStrictlyMatchesPackEntry(lootNetFika.Path, entry));
        Assert.False(RequiredModsPackService.IsReplaceTarget(fikaFolder, entry));
        Assert.False(RequiredModsPackService.IsReplaceTarget(fikaCore, entry));
        Assert.True(RequiredModsPackService.IsReplaceTarget(lootNetFika, entry));

        var matches = RequiredModsPackService.FindAllLocalMatches(entry, [fikaFolder, fikaCore, lootNetFika]);
        Assert.Single(matches);
        Assert.Equal(lootNetFika.Path, matches[0].Path);
    }

    [Fact]
    public void FikaDetection_ignores_lootnet_fika_dll_and_empty_server_folder()
    {
        Assert.False(FikaDetection.IsFikaCoreOrServerDll("LootNetFika.dll"));
        Assert.True(FikaDetection.IsFikaCoreOrServerDll("Fika.Core.dll"));

        var root = Path.Combine(Path.GetTempPath(), "spt-fika-detect-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "user", "mods", "fika-server");
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(server, "leftover.txt"), "junk");
        try
        {
            Assert.False(FikaDetection.FolderHasCompleteFikaServer(server));
            Assert.False(FikaDetection.FolderHasFikaClientDll(Path.Combine(root, "BepInEx", "plugins", "Fika")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RequiredModDiffItem Find(RequiredModsDiffResult diff, string name) =>
        diff.Items.First(i => i.PackEntry?.Name == name);
}
