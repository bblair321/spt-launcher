using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SptLauncherWpf.Services
{
    internal sealed class NativeWindowCandidate
    {
        public nint Handle { get; init; }
        public int ProcessId { get; init; }
        public string Title { get; init; } = "";
        public bool IsVisible { get; init; }
        public bool IsToolWindow { get; init; }
        public bool HasOwner { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    internal readonly struct NativeWindowStyleSnapshot
    {
        public nint Style { get; init; }
        public nint ExStyle { get; init; }
    }

    /// <summary>
    /// Win32 helpers to find SPT.Launcher's main HWND and parent it into a WPF host.
    /// Picking is testable via <see cref="PickMainWindow"/>.
    /// </summary>
    internal static class NativeWindowEmbed
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int GwOwner = 4;
        private const int SwHide = 0;
        private const int SwShow = 5;
        private const uint WsCaption = 0x00C00000;
        private const uint WsThickFrame = 0x00040000;
        private const uint WsSysMenu = 0x00080000;
        private const uint WsMinimizeBox = 0x00020000;
        private const uint WsMaximizeBox = 0x00010000;
        private const uint WsPopup = 0x80000000;
        private const uint WsChild = 0x40000000;
        private const uint WsVisible = 0x10000000;
        private const uint WsClipSiblings = 0x04000000;
        private const uint WsClipChildren = 0x02000000;
        private const uint WsExAppWindow = 0x00040000;
        private const uint WsExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;

        internal const int MinEmbedWidth = 200;
        internal const int MinEmbedHeight = 150;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern nint GetWindow(nint hWnd, int uCmd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll")]
        private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

        [DllImport("user32.dll")]
        private static extern nint GetParent(nint hWnd);

        [DllImport("user32.dll")]
        internal static extern bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        internal static NativeWindowCandidate? PickMainWindow(
            IEnumerable<NativeWindowCandidate> windows,
            int processId) =>
            PickMainWindow(windows, [processId], excludeProcessId: 0);

        internal static NativeWindowCandidate? PickMainWindow(
            IEnumerable<NativeWindowCandidate> windows,
            IReadOnlyCollection<int> processIds,
            int excludeProcessId)
        {
            var idSet = processIds.Where(id => id > 0 && id != excludeProcessId).ToHashSet();
            var candidates = windows
                .Where(w => w.ProcessId != excludeProcessId)
                .Where(w => w.IsVisible)
                .Where(w => !w.IsToolWindow)
                .Where(w => w.Width >= MinEmbedWidth && w.Height >= MinEmbedHeight)
                .Where(w => idSet.Contains(w.ProcessId) || LooksLikeOfficialSptLauncherTitle(w))
                .ToList();
            if (idSet.Count > 0)
            {
                var inProcess = candidates.Where(w => idSet.Contains(w.ProcessId)).ToList();
                if (inProcess.Count > 0)
                {
                    candidates = inProcess;
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var titled = candidates.Where(LooksLikeOfficialSptLauncherTitle).ToList();
            var pool = titled.Count > 0 ? titled : candidates;
            return pool
                .OrderBy(w => w.HasOwner)
                .ThenByDescending(w => (long)w.Width * w.Height)
                .ThenBy(w => w.Handle)
                .First();
        }

        internal static bool LooksLikeOfficialSptLauncherTitle(NativeWindowCandidate window) =>
            LooksLikeOfficialSptLauncherTitle(window.Title);

        internal static bool LooksLikeOfficialSptLauncherTitle(string? title)
        {
            var t = (title ?? "").Trim();
            if (t.Length == 0)
            {
                return false;
            }

            return t.Contains("SPT Launcher", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("SPT-Launcher", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("AKI Launcher", StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<nint> WaitForMainWindowAsync(
            int processId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            await WaitForMainWindowAsync([processId], excludeProcessId: 0, timeout, cancellationToken);

        internal static async Task<nint> WaitForMainWindowAsync(
            IReadOnlyCollection<int> processIds,
            int excludeProcessId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ids = processIds
                    .Concat(ListSptLauncherProcessIds(excludeProcessId))
                    .Where(id => id > 0 && id != excludeProcessId)
                    .ToHashSet();
                var picked = PickMainWindow(EnumerateTopLevelWindows(), ids, excludeProcessId);
                if (picked != null)
                {
                    Debug.WriteLine(
                        $"[NativeWindowEmbed] Found '{picked.Title}' pid={picked.ProcessId} hwnd={picked.Handle}");
                    return picked.Handle;
                }

                await Task.Delay(200, cancellationToken);
            }

            return 0;
        }

        internal static IReadOnlyCollection<int> ListSptLauncherProcessIds(int excludeProcessId)
        {
            var ids = new HashSet<int>();
            foreach (var name in new[] { "SPT.Launcher", "Aki.Launcher" })
            {
                Process[]? processes = null;
                try
                {
                    processes = Process.GetProcessesByName(name);
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (process.Id != excludeProcessId && !process.HasExited)
                            {
                                ids.Add(process.Id);
                            }
                        }
                        catch
                        {
                            // process exited while enumerating
                        }
                    }
                }
                catch
                {
                    // GetProcessesByName can fail under low rights
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (var process in processes)
                        {
                            process.Dispose();
                        }
                    }
                }
            }

            return ids;
        }

        internal static nint FindMainWindow(int processId)
        {
            var picked = PickMainWindow(EnumerateTopLevelWindows(), processId);
            return picked?.Handle ?? 0;
        }

        internal static NativeWindowStyleSnapshot CaptureStyles(nint hwnd) =>
            new()
            {
                Style = GetWindowLongPtr(hwnd, GwlStyle),
                ExStyle = GetWindowLongPtr(hwnd, GwlExStyle)
            };

        internal static bool EmbedAsChild(nint hwnd, nint hostHwnd)
        {
            if (hwnd == 0 || hostHwnd == 0 || !IsWindow(hwnd))
            {
                return false;
            }

            SetParent(hwnd, hostHwnd);

            var style = unchecked((uint)(long)GetWindowLongPtr(hwnd, GwlStyle));
            style &= ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox | WsPopup);
            style |= WsChild | WsVisible | WsClipSiblings | WsClipChildren;
            SetWindowLongPtr(hwnd, GwlStyle, unchecked((nint)style));

            var ex = unchecked((uint)(long)GetWindowLongPtr(hwnd, GwlExStyle));
            ex &= ~WsExAppWindow;
            ex |= WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, unchecked((nint)ex));

            SetWindowPos(
                hwnd,
                0,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
            return GetParent(hwnd) == hostHwnd;
        }

        internal static void Detach(nint hwnd, NativeWindowStyleSnapshot? original, bool restoreAsStandalone)
        {
            if (hwnd == 0 || !IsWindow(hwnd))
            {
                return;
            }

            SetParent(hwnd, 0);
            if (restoreAsStandalone && original is { } snap)
            {
                SetWindowLongPtr(hwnd, GwlStyle, snap.Style);
                SetWindowLongPtr(hwnd, GwlExStyle, snap.ExStyle);
                SetWindowPos(hwnd, 0, 0, 0, 0, 0, SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
                ShowWindow(hwnd, SwShow);
            }
            else
            {
                ShowWindow(hwnd, SwHide);
            }
        }

        internal static void SetVisible(nint hwnd, bool visible)
        {
            if (hwnd == 0 || !IsWindow(hwnd))
            {
                return;
            }

            ShowWindow(hwnd, visible ? SwShow : SwHide);
        }

        private static IEnumerable<NativeWindowCandidate> EnumerateTopLevelWindows()
        {
            var list = new List<NativeWindowCandidate>();
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                GetWindowRect(hWnd, out var rect);
                var ex = unchecked((uint)(long)GetWindowLongPtr(hWnd, GwlExStyle));
                list.Add(new NativeWindowCandidate
                {
                    Handle = hWnd,
                    ProcessId = pid,
                    Title = ReadTitle(hWnd),
                    IsVisible = IsWindowVisible(hWnd),
                    IsToolWindow = (ex & WsExToolWindow) != 0,
                    HasOwner = GetWindow(hWnd, GwOwner) != 0,
                    Width = Math.Abs(rect.Width),
                    Height = Math.Abs(rect.Height)
                });
                return true;
            }, 0);

            return list;
        }

        private static string ReadTitle(nint hwnd)
        {
            var buffer = new StringBuilder(512);
            _ = GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }
    }
}
