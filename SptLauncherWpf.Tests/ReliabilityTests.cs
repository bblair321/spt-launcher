using System.IO;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class EftVersionNormalizeTests
{
    [Theory]
    [InlineData("1.1.0.0-46624-f8702c22", "1.1.0.0.46624")]
    [InlineData("1.0.4.1-44236-749fe27f", "1.0.4.1.44236")]
    [InlineData("0.16.9-40087", "0.16.9.40087")]
    [InlineData("v0.16.9.5.40743", "0.16.9.5.40743")]
    [InlineData("0.16.9.5.40743-RELEASE", "0.16.9.5.40743")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not-a-version", null)]
    public void NormalizeEftVersion_parses_common_forms(string? input, string? expected)
    {
        Assert.Equal(expected, EftDetectionService.NormalizeEftVersion(input));
    }
}

public class PatcherUrlTests
{
    [Fact]
    public void GetPatcherTargetVariants_adds_zero_stripped_form()
    {
        var variants = EftDetectionService.GetPatcherTargetVariants("0.16.9.5.40743");
        Assert.Contains("0.16.9.5.40743", variants);
        Assert.Contains("16.9.5.40743", variants);
    }

    [Fact]
    public void GetPatcherTargetVariants_adds_zero_prefixed_form_for_16()
    {
        var variants = EftDetectionService.GetPatcherTargetVariants("16.9.5.40743");
        Assert.Contains("16.9.5.40743", variants);
        Assert.Contains("0.16.9.5.40743", variants);
    }

    [Fact]
    public void BuildPatcherUrl_matches_cdn_filename_pattern()
    {
        var url = EftDetectionService.BuildPatcherUrl("1.0.6.5.46221", "16.9.5.40743");
        Assert.Equal(
            "https://slugma.waffle-lord.net/Patcher_1.0.6.5.46221_to_16.9.5.40743.7z",
            url);
    }
}

public class VersionStringHelperTests
{
    [Theory]
    [InlineData("v3.0.3", "3.0.3")]
    [InlineData("4.1.1+build.5", "4.1.1")]
    [InlineData("4.1.1-RELEASE", "4.1.1")]
    [InlineData("4.1.1-RC1", "4.1.1")]
    public void Normalize_strips_common_prefixes_and_suffixes(string input, string expected)
    {
        Assert.Equal(expected, VersionStringHelper.Normalize(input));
    }
}

public class SptRootDirectoryTests
{
    [Fact]
    public void ResolveSptRootDirectory_uses_parent_of_SPT_Runtime()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-root-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "SPT_Runtime");
        Directory.CreateDirectory(runtime);

        try
        {
            var resolved = SptDetectionService.ResolveSptRootDirectory(runtime);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSptRootDirectory_keeps_plain_launcher_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "spt-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var resolved = SptDetectionService.ResolveSptRootDirectory(root);
            Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSptRootDirectory_uses_parent_when_parent_has_game_files()
    {
        var install = Path.Combine(Path.GetTempPath(), "spt-nested-" + Guid.NewGuid().ToString("N"));
        var launcherDir = Path.Combine(install, "LauncherBin");
        Directory.CreateDirectory(launcherDir);
        File.WriteAllText(Path.Combine(install, "EscapeFromTarkov.exe"), "stub");

        try
        {
            var resolved = SptDetectionService.ResolveSptRootDirectory(launcherDir);
            Assert.Equal(Path.GetFullPath(install), Path.GetFullPath(resolved));
        }
        finally
        {
            Directory.Delete(install, recursive: true);
        }
    }
}

public class UpdateApplyHelperTests
{
    [Fact]
    public void LooksLikeWindowsExecutable_accepts_mz_header()
    {
        var path = Path.Combine(Path.GetTempPath(), "mz-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            var bytes = new byte[128];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            File.WriteAllBytes(path, bytes);
            Assert.True(UpdateApplyHelper.LooksLikeWindowsExecutable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LooksLikeWindowsExecutable_rejects_non_mz()
    {
        var path = Path.Combine(Path.GetTempPath(), "bad-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllBytes(path, new byte[128]);
            Assert.False(UpdateApplyHelper.LooksLikeWindowsExecutable(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildReplaceInPlaceScript_includes_timeout_and_restore()
    {
        var script = UpdateApplyHelper.BuildReplaceInPlaceScript(
            processName: "SPTLauncher.exe",
            currentExePath: @"C:\App\SPTLauncher.exe",
            downloadedUpdatePath: @"C:\Temp\update.exe",
            backupPath: @"C:\App\SPTLauncher.old.exe",
            scriptPath: @"C:\Temp\update.cmd",
            maxWaitSeconds: 45);

        Assert.Contains("MAX_WAIT=45", script);
        Assert.Contains("move /y \"C:\\App\\SPTLauncher.exe\" \"C:\\App\\SPTLauncher.old.exe\"", script);
        Assert.Contains("move /y \"C:\\Temp\\update.exe\" \"C:\\App\\SPTLauncher.exe\"", script);
        Assert.Contains("move /y \"C:\\App\\SPTLauncher.old.exe\" \"C:\\App\\SPTLauncher.exe\"", script);
        Assert.Contains(":fail", script);
    }

    [Fact]
    public void IsNewerVersion_compares_3_and_4_part_versions()
    {
        var service = UpdateService.Instance;
        Assert.True(service.IsNewerVersion("3.0.4", new Version(3, 0, 3, 0)));
        Assert.False(service.IsNewerVersion("3.0.3", new Version(3, 0, 3, 0)));
        Assert.False(service.IsNewerVersion("not-a-version", new Version(3, 0, 3, 0)));
    }

    [Fact]
    public void GetBackupPath_uses_old_exe_suffix()
    {
        var backup = UpdateApplyHelper.GetBackupPath(@"C:\Apps\SPTLauncher.exe");
        Assert.Equal(@"C:\Apps\SPTLauncher.old.exe", backup);
    }

    [Theory]
    [InlineData("3.0.4", "3.0.4.0", true)]
    [InlineData("v3.0.4", "3.0.4", true)]
    [InlineData("3.0.4", "3.0.3", false)]
    public void VersionsLookEqual_compares_major_minor_build(string left, string right, bool expected)
    {
        Assert.Equal(expected, UpdateApplyHelper.VersionsLookEqual(left, right));
    }

    [Fact]
    public void TryRemoveBackup_deletes_sibling_old_exe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spt-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "SPTLauncher.exe");
        var backup = Path.Combine(dir, "SPTLauncher.old.exe");
        try
        {
            File.WriteAllBytes(exe, new byte[] { (byte)'M', (byte)'Z', 0, 0 });
            File.WriteAllBytes(backup, new byte[] { (byte)'M', (byte)'Z', 0, 0 });

            Assert.True(UpdateApplyHelper.TryRemoveBackup(exe));
            Assert.False(File.Exists(backup));
            Assert.True(File.Exists(exe));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
