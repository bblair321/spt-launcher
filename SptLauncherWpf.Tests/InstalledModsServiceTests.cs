using System.IO;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class InstalledModsServiceTests
{
    [Theory]
    [InlineData("MyMod", false)]
    [InlineData("MyMod.disabled", true)]
    [InlineData("Cool.dll.disabled", true)]
    [InlineData("Cool.dll", false)]
    public void IsDisabledName_detects_suffix(string name, bool expected)
    {
        Assert.Equal(expected, InstalledModsService.IsDisabledName(name));
    }

    [Theory]
    [InlineData("MyMod.disabled", "MyMod")]
    [InlineData("MyMod", "MyMod")]
    [InlineData("Cool.dll.disabled", "Cool.dll")]
    public void StripDisabledSuffix_removes_marker(string name, string expected)
    {
        Assert.Equal(expected, InstalledModsService.StripDisabledSuffix(name));
    }

    [Fact]
    public void GetDisabledPath_appends_suffix()
    {
        var path = Path.Combine(Path.GetTempPath(), "MyMod");
        var disabled = InstalledModsService.GetDisabledPath(path);
        Assert.Equal(Path.GetFullPath(path + InstalledModsService.DisabledSuffix), disabled);
    }

    [Fact]
    public void GetEnabledPath_removes_suffix()
    {
        var path = Path.Combine(Path.GetTempPath(), "MyMod.disabled");
        var enabled = InstalledModsService.GetEnabledPath(path);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MyMod")), enabled);
    }

    [Fact]
    public void ScanInstalledMods_finds_server_and_client_including_disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-mods-" + Guid.NewGuid().ToString("N"));
        var serverMods = Path.Combine(root, "SPT_Runtime", "user", "mods");
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Directory.CreateDirectory(Path.Combine(serverMods, "ServerModA"));
        Directory.CreateDirectory(Path.Combine(serverMods, "ServerModB.disabled"));
        Directory.CreateDirectory(plugins);
        Directory.CreateDirectory(Path.Combine(plugins, "ClientFolder"));
        File.WriteAllText(Path.Combine(plugins, "LooseClient.dll"), "x");
        File.WriteAllText(Path.Combine(plugins, "LooseOff.dll.disabled"), "x");
        File.WriteAllText(Path.Combine(serverMods, "ServerModA", "package.json"),
            """{"name":"ServerModA","version":"1.2.3"}""");

        try
        {
            var mods = InstalledModsService.ScanInstalledMods(root);

            Assert.Contains(mods, m =>
                m.DisplayName == "ServerModA" &&
                m.Kind == InstalledModKind.Server &&
                m.IsEnabled &&
                m.VersionHint == "1.2.3");

            Assert.Contains(mods, m =>
                m.DisplayName == "ServerModB" &&
                m.Kind == InstalledModKind.Server &&
                !m.IsEnabled);

            Assert.Contains(mods, m =>
                m.DisplayName == "ClientFolder" &&
                m.Kind == InstalledModKind.Client &&
                m.IsEnabled &&
                m.IsDirectory);

            Assert.Contains(mods, m =>
                m.DisplayName == "LooseClient" &&
                m.Kind == InstalledModKind.Client &&
                m.IsEnabled &&
                !m.IsDirectory);

            Assert.Contains(mods, m =>
                m.DisplayName == "LooseOff" &&
                m.Kind == InstalledModKind.Client &&
                !m.IsEnabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SetEnabled_renames_folder_to_disabled_and_back()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-toggle-" + Guid.NewGuid().ToString("N"));
        var modDir = Path.Combine(root, "user", "mods", "ToggleMe");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "package.json"), "{}");

        try
        {
            var mod = new InstalledModInfo
            {
                DisplayName = "ToggleMe",
                Path = modDir,
                Kind = InstalledModKind.Server,
                IsEnabled = true,
                IsDirectory = true
            };

            var disabled = InstalledModsService.SetEnabled(mod, enabled: false);
            Assert.False(disabled.IsEnabled);
            Assert.True(Directory.Exists(disabled.Path));
            Assert.False(Directory.Exists(modDir));
            Assert.EndsWith(".disabled", disabled.Path, StringComparison.OrdinalIgnoreCase);

            var enabled = InstalledModsService.SetEnabled(disabled, enabled: true);
            Assert.True(enabled.IsEnabled);
            Assert.True(Directory.Exists(enabled.Path));
            Assert.Equal(Path.GetFullPath(modDir), Path.GetFullPath(enabled.Path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScanInstalledMods_collapses_multi_dll_forge_package()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-mods-group-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);

        var dllA = Path.Combine(plugins, "Tyfon.UIFixes.dll");
        var dllB = Path.Combine(plugins, "Tyfon.UIFixes.Net.dll");
        File.WriteAllText(dllA, "a");
        File.WriteAllText(dllB, "b");

        var marker = new ForgeModMarker
        {
            ForgeModId = 321,
            Guid = "tyfon.uifixes",
            Name = "UI Fixes",
            Version = "6.0.1",
            Slug = "ui-fixes"
        };
        ForgeModMarker.Write(dllA, isDirectory: false, marker);
        ForgeModMarker.Write(dllB, isDirectory: false, marker);

        try
        {
            var mods = InstalledModsService.ScanInstalledMods(root);
            var uiFixes = Assert.Single(mods, m => m.DisplayName == "UI Fixes");
            Assert.Equal(2, uiFixes.AllPaths.Count);
            Assert.Contains(dllA, uiFixes.AllPaths);
            Assert.Contains(dllB, uiFixes.AllPaths);
            Assert.Equal("6.0.1", uiFixes.VersionHint);
            Assert.Equal(321, uiFixes.ForgeModId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Uninstall_removes_all_related_loose_plugins()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-mods-rm-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        var dllA = Path.Combine(plugins, "A.dll");
        var dllB = Path.Combine(plugins, "B.dll");
        File.WriteAllText(dllA, "a");
        File.WriteAllText(dllB, "b");
        var marker = new ForgeModMarker { ForgeModId = 1, Name = "Pack", Version = "1.0.0" };
        ForgeModMarker.Write(dllA, isDirectory: false, marker);
        ForgeModMarker.Write(dllB, isDirectory: false, marker);

        try
        {
            var mod = Assert.Single(InstalledModsService.ScanInstalledMods(root));
            InstalledModsService.Uninstall(mod);
            Assert.False(File.Exists(dllA));
            Assert.False(File.Exists(dllB));
            Assert.False(File.Exists(ForgeModMarker.GetMarkerPathForFile(dllA)));
            Assert.False(File.Exists(ForgeModMarker.GetMarkerPathForFile(dllB)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
