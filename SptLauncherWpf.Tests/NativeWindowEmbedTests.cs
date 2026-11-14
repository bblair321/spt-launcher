using SptLauncherWpf.Services;

namespace SptLauncherWpf.Tests;

public class NativeWindowEmbedTests
{
    [Fact]
    public void PickMainWindow_prefers_spt_launcher_title()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 10, "Settings", 800, 600),
                Win(2, 10, "SPT Launcher - Servers", 1200, 800),
                Win(3, 10, "Untitled", 400, 300)
            ],
            processId: 10);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_skips_invisible_and_tool_windows()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 5, "SPT Launcher", 1200, 800, isVisible: false),
                Win(2, 5, "SPT Launcher", 1200, 800, isToolWindow: true),
                Win(3, 5, "SPT Launcher", 80, 80),
                Win(4, 5, "Other", 900, 700)
            ],
            processId: 5);

        Assert.NotNull(picked);
        Assert.Equal(4, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_prefers_unowned_then_largest()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 7, "SPT Launcher", 400, 300, hasOwner: true),
                Win(2, 7, "SPT Launcher", 800, 600),
                Win(3, 7, "SPT Launcher", 2000, 1200, hasOwner: true)
            ],
            processId: 7);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_ignores_other_processes()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 99, "SPT Launcher", 1200, 800),
                Win(2, 3, "Game", 800, 600)
            ],
            processId: 3);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_uses_other_spt_pid_when_seed_has_no_window()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 11, "SPT Launcher - Servers", 1200, 800),
                Win(2, 99, "SPT Launcher", 800, 600)
            ],
            processIds: [10, 11],
            excludeProcessId: 99);

        Assert.NotNull(picked);
        Assert.Equal(1, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_excludes_our_launcher_window()
    {
        var picked = NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 50, "SPT Launcher", 1360, 880),
                Win(2, 80, "SPT Launcher - Servers", 1200, 800)
            ],
            processIds: [80],
            excludeProcessId: 50);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.Handle);
    }

    [Fact]
    public void PickMainWindow_returns_null_when_nothing_fits()
    {
        Assert.Null(NativeWindowEmbed.PickMainWindow(
            [
                Win(1, 1, "SPT Launcher", 50, 50),
                Win(2, 1, "SPT Launcher", 800, 600, isVisible: false)
            ],
            processId: 1));
    }

    [Theory]
    [InlineData("SPT Launcher - Servers", true)]
    [InlineData("SPT-Launcher", true)]
    [InlineData("AKI Launcher", true)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    public void LooksLikeOfficialSptLauncherTitle_matches_known_names(string title, bool expected)
    {
        Assert.Equal(expected, NativeWindowEmbed.LooksLikeOfficialSptLauncherTitle(title));
    }

    private static NativeWindowCandidate Win(
        nint handle,
        int processId,
        string title,
        int width,
        int height,
        bool isVisible = true,
        bool isToolWindow = false,
        bool hasOwner = false) =>
        new()
        {
            Handle = handle,
            ProcessId = processId,
            Title = title,
            Width = width,
            Height = height,
            IsVisible = isVisible,
            IsToolWindow = isToolWindow,
            HasOwner = hasOwner
        };
}
