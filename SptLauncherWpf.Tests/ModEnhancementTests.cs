using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class InstalledModMatchingTests
{
    [Theory]
    [InlineData("Server Value Modifier [SVM]", "servervaluemodifiersvm")]
    [InlineData("server-value-modifier-svm", "servervaluemodifiersvm")]
    [InlineData("", "")]
    public void NormalizeModKey_strips_noise(string input, string expected)
    {
        Assert.Equal(expected, InstalledModsService.NormalizeModKey(input));
    }

    [Fact]
    public void IsInstalledMatch_uses_forge_id()
    {
        var installed = new InstalledModInfo
        {
            DisplayName = "Anything",
            Path = @"C:\SPT\user\mods\Anything",
            ForgeModId = 236
        };
        var forge = new ForgeModSummary { Id = 236, Name = "SVM", Slug = "server-value-modifier-svm" };
        Assert.True(InstalledModsService.IsInstalledMatch(installed, forge));
    }

    [Fact]
    public void IsInstalledMatch_uses_normalized_name()
    {
        var installed = new InstalledModInfo
        {
            DisplayName = "Server Value Modifier SVM",
            Path = @"C:\SPT\user\mods\SVM"
        };
        var forge = new ForgeModSummary
        {
            Id = 1,
            Name = "Server Value Modifier [SVM]",
            Slug = "server-value-modifier-svm"
        };
        Assert.True(InstalledModsService.IsInstalledMatch(installed, forge));
    }

    [Fact]
    public void BuildUpdateQueryPairs_prefers_mod_id()
    {
        var mods = new[]
        {
            new InstalledModInfo
            {
                DisplayName = "SVM",
                Path = "x",
                ForgeModId = 236,
                VersionHint = "2.1.2"
            }
        };

        var pairs = InstalledModsService.BuildUpdateQueryPairs(mods).ToList();
        Assert.Contains("236:2.1.2", pairs);
    }
}

public class ModInstallProgressTests
{
    [Fact]
    public void Message_includes_percent_when_total_known()
    {
        var progress = new ModInstallProgress
        {
            Stage = "Downloading…",
            BytesReceived = 512_000,
            TotalBytes = 1_024_000
        };

        Assert.Contains("50%", progress.Message);
        Assert.Contains("Downloading…", progress.Message);
    }
}
