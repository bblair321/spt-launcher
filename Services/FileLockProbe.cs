using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SptLauncherWpf.Services
{
    /// <summary>
    /// Uses the Windows Restart Manager API to find which processes hold a file open.
    /// </summary>
    public static class FileLockProbe
    {
        private const int CCH_RM_MAX_APP_NAME = 255;
        private const int CCH_RM_MAX_SVC_NAME = 63;
        private const int ERROR_MORE_DATA = 234;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmCritical = 5
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            out uint lpdwRebootReasons);

        public static List<string> GetLockingProcessLabels(string filePath)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return results;
            }

            var sessionKey = Guid.NewGuid().ToString();
            if (RmStartSession(out var session, 0, sessionKey) != 0)
            {
                return results;
            }

            try
            {
                var files = new[] { Path.GetFullPath(filePath) };
                if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0)
                {
                    return results;
                }

                uint procInfoNeeded = 0;
                uint procInfo = 0;
                var result = RmGetList(session, out procInfoNeeded, ref procInfo, null, out _);
                if (result != ERROR_MORE_DATA || procInfoNeeded == 0)
                {
                    return results;
                }

                var processInfo = new RM_PROCESS_INFO[procInfoNeeded];
                procInfo = procInfoNeeded;
                if (RmGetList(session, out _, ref procInfo, processInfo, out _) != 0)
                {
                    return results;
                }

                var selfId = Process.GetCurrentProcess().Id;
                foreach (var info in processInfo)
                {
                    var pid = info.Process.dwProcessId;
                    if (pid == selfId || pid <= 0)
                    {
                        continue;
                    }

                    var appName = string.IsNullOrWhiteSpace(info.strAppName)
                        ? "Unknown"
                        : info.strAppName.Trim();

                    string? exePath = null;
                    string processName = appName;
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        processName = process.ProcessName;
                        try
                        {
                            exePath = process.MainModule?.FileName;
                        }
                        catch
                        {
                            // Access denied for some processes
                        }
                    }
                    catch
                    {
                        // Process may have exited
                    }

                    results.Add(string.IsNullOrEmpty(exePath)
                        ? $"{processName} (PID {pid}) — {appName}"
                        : $"{processName} (PID {pid}) — {exePath}");
                }
            }
            finally
            {
                RmEndSession(session);
            }

            return results
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public sealed class ModFileLockException : System.IO.IOException
    {
        public string LockedPath { get; }

        public ModFileLockException(string lockedPath, string message, Exception? inner = null)
            : base(message, inner)
        {
            LockedPath = lockedPath;
        }
    }
}
