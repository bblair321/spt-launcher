using System.IO;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class ModPathClassifierTests
{
    [Fact]
    public void Classify_server_only_svm_style_with_runtime()
    {
        var files = new[]
        {
            "Greed.exe",
            "SPT_Runtime/user/mods/[SVM] Server Value Modifier/ServerValueModifier.dll",
            "SPT_Runtime/user/mods/[SVM] Server Value Modifier/Loader/loader.json"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.ServerOnly, result.Kind);
        Assert.True(result.CanAutoInstall);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "SPT_Runtime/user/mods/[SVM] Server Value Modifier/ServerValueModifier.dll",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals("Greed.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_server_only_strips_runtime_when_install_has_no_runtime()
    {
        var files = new[]
        {
            "SPT_Runtime/user/mods/MyMod/package.json"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: false);

        Assert.Equal(ModInstallKind.ServerOnly, result.Kind);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals("user/mods/MyMod/package.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_client_only_bepinex()
    {
        var files = new[]
        {
            "BepInEx/plugins/CoolMod.dll",
            "BepInEx/config/CoolMod.cfg"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.ClientOnly, result.Kind);
        Assert.True(result.HasClientPaths);
        Assert.False(result.HasServerPaths);
        Assert.All(result.InstallableRelativePaths, p =>
            Assert.StartsWith("BepInEx/", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_mixed_client_and_server()
    {
        var files = new[]
        {
            "BepInEx/plugins/ClientBit.dll",
            "user/mods/ServerBit/package.json"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.Mixed, result.Kind);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals("BepInEx/plugins/ClientBit.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "SPT_Runtime/user/mods/ServerBit/package.json",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_unknown_when_only_loose_root_files()
    {
        var files = new[] { "readme.txt", "installer.exe" };
        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.Unknown, result.Kind);
        Assert.False(result.CanAutoInstall);
    }

    [Fact]
    public void Classify_unwraps_single_zip_folder_prefix()
    {
        var files = new[]
        {
            "CoolMod-1.0/BepInEx/plugins/CoolMod.dll"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: false);

        Assert.Equal(ModInstallKind.ClientOnly, result.Kind);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals("BepInEx/plugins/CoolMod.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryMap_user_mods_adds_runtime_prefix_when_needed()
    {
        Assert.True(ModPathClassifier.TryMapToInstallRelative(
            "user/mods/Foo/mod.js",
            installHasSptRuntime: true,
            out var mapped,
            out var kind));

        Assert.Equal(ModInstallKind.ServerOnly, kind);
        Assert.Equal("SPT_Runtime/user/mods/Foo/mod.js", mapped);
    }

    [Fact]
    public void Classify_client_includes_managed_vectorgraphics_deps()
    {
        var files = new[]
        {
            "BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll",
            "BepInEx/plugins/mpstark-dynamicmaps/Maps/Customs_TarkovDev/Customs_TarkovDev.jsonc",
            "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll",
            "EscapeFromTarkov_Data/Managed/Unity.InternalAPIEngineBridge.003.dll"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.ClientOnly, result.Kind);
        Assert.True(result.CanAutoInstall);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "EscapeFromTarkov_Data/Managed/Unity.InternalAPIEngineBridge.003.dll",
                StringComparison.OrdinalIgnoreCase));

        var clientOnly = ModPathClassifier.FilterClientInstallPaths(result.InstallableRelativePaths);
        Assert.Equal(result.InstallableRelativePaths.Count, clientOnly.Count);
        Assert.Contains(
            clientOnly,
            p => p.Contains("Unity.VectorGraphics.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_unwraps_prefix_for_managed_deps()
    {
        var files = new[]
        {
            "DynamicMaps-1.2.1/BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll",
            "DynamicMaps-1.2.1/EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: false);

        Assert.Equal(ModInstallKind.ClientOnly, result.Kind);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_lootnet_style_spt_prefixed_mixed_package()
    {
        var files = new[]
        {
            "BepInEx/plugins/LootNet/LootNet.dll",
            "BepInEx/plugins/LootNet/LootNetFika.dll",
            "BepInEx/plugins/LootNet/weii weii.mp4",
            "SPT/user/mods/LootNetServer/LootNetServer.dll"
        };

        var result = ModPathClassifier.Classify(files, installHasSptRuntime: true);

        Assert.Equal(ModInstallKind.Mixed, result.Kind);
        Assert.True(result.CanAutoInstall);
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals("BepInEx/plugins/LootNet/LootNet.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            result.InstallableRelativePaths,
            p => p.Equals(
                "SPT_Runtime/user/mods/LootNetServer/LootNetServer.dll",
                StringComparison.OrdinalIgnoreCase));
    }
}

public class ModInstallSafetyTests
{
    [Fact]
    public void IsSafeExtractPath_rejects_zip_slip()
    {
        var root = Path.Combine(Path.GetTempPath(), "mod-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var slip = Path.GetFullPath(Path.Combine(root, "..", "outside.txt"));
            Assert.False(ModInstallService.IsSafeExtractPath(root, slip));

            var ok = Path.GetFullPath(Path.Combine(root, "user", "mods", "x.dll"));
            Assert.True(ModInstallService.IsSafeExtractPath(root, ok));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExtractPathFromLockException_reads_mod_file_lock_exception()
    {
        var path = @"D:\SPT\BepInEx\plugins\Foo.dll";
        var ex = new ModFileLockException(path, "Locked file: " + path);
        Assert.Equal(path, ModInstallService.ExtractPathFromLockException(ex));
        Assert.True(ModInstallService.IsFileLockException(ex));
    }

    [Fact]
    public void ExtractPathFromLockException_reads_windows_quoted_path()
    {
        var path = @"D:\SPT\BepInEx\plugins\Foo.dll";
        var ex = new IOException(
            $"The process cannot access the file '{path}' because it is being used by another process.");
        Assert.Equal(path, ModInstallService.ExtractPathFromLockException(ex));
    }

    [Fact]
    public void BuildPublicFileLockMessage_includes_file_path()
    {
        var path = @"D:\SPT\BepInEx\plugins\Foo.dll";
        var ex = new ModFileLockException(
            path,
            $"Locked file: {path}\nThe process cannot access the file because it is being used by another process.");

        var message = ModInstallService.BuildPublicFileLockMessage(@"D:\SPT", ex);
        Assert.Contains(path, message);
        Assert.Contains("File:", message);
    }

    [Theory]
    [InlineData("spt-server-manager")]
    [InlineData("spt-server-manager.exe")]
    [InlineData("SPT-Server-Manager")]
    [InlineData("spt-server-manager-agent")]
    public void Server_manager_processes_are_not_install_blockers(string name)
    {
        Assert.True(ModInstallService.IsServerManagerProcess(name, null));
        Assert.False(ModInstallService.ShouldBlockInstallForProcess(name, exeUnderSptRoot: true));
        Assert.False(ModInstallService.ShouldBlockInstallForProcess(name, exeUnderSptRoot: false));
    }

    [Fact]
    public void Server_manager_exe_path_is_ignored_even_under_spt_root()
    {
        Assert.True(ModInstallService.IsServerManagerProcess(
            "dotnet",
            @"D:\SPT\tools\spt-server-manager.exe"));
    }

    [Theory]
    [InlineData("EscapeFromTarkov", false, true)]
    [InlineData("EscapeFromTarkov_BE", false, true)]
    [InlineData("SPT.Launcher", false, true)]
    [InlineData("Aki.Launcher", true, true)]
    [InlineData("Greed", false, true)]
    public void Game_and_launcher_processes_always_block_install(
        string name,
        bool underRoot,
        bool expected)
    {
        Assert.Equal(expected, ModInstallService.ShouldBlockInstallForProcess(name, underRoot));
    }

    [Fact]
    public void Spt_server_blocks_only_when_running_from_same_install()
    {
        Assert.False(ModInstallService.ShouldBlockInstallForProcess("SPT.Server", exeUnderSptRoot: false));
        Assert.True(ModInstallService.ShouldBlockInstallForProcess("SPT.Server", exeUnderSptRoot: true));
        Assert.True(ModInstallService.ShouldBlockInstallForProcess("Aki.Server", exeUnderSptRoot: true));
    }

    [Fact]
    public void Broad_spt_name_match_no_longer_blocks_unrelated_tools()
    {
        Assert.False(ModInstallService.ShouldBlockInstallForProcess("SPTLauncher", exeUnderSptRoot: false));
        Assert.False(ModInstallService.ShouldBlockInstallForProcess("MySPTTool", exeUnderSptRoot: true));
    }
}

public class ForgeApiHelperTests
{
    [Theory]
    [InlineData("4.1.1", "~4.1.0")]
    [InlineData("v4.1.1-RELEASE", "~4.1.0")]
    [InlineData("4.1.2", "~4.1.0")]
    [InlineData("4.1", "~4.1.0")]
    [InlineData("4.0.13", "~4.0.0")]
    [InlineData("", null)]
    public void BuildSptVersionFilter_builds_minor_line_constraint(string input, string? expected)
    {
        Assert.Equal(expected, ForgeApiService.BuildSptVersionFilter(input));
    }
}
