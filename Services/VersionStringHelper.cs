using System;

namespace SptLauncherWpf.Services
{
    internal static class VersionStringHelper
    {
        public static string Normalize(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            version = version.TrimStart('v', 'V').Trim();

            var plusIndex = version.IndexOf('+');
            if (plusIndex > 0)
            {
                version = version.Substring(0, plusIndex);
            }

            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                var suffix = version.Substring(dashIndex).ToUpperInvariant();
                if (suffix == "-RELEASE" || suffix == "-DEV" || suffix == "-ALPHA" || suffix == "-BETA" || suffix.StartsWith("-RC"))
                {
                    version = version.Substring(0, dashIndex);
                }
            }

            return version.Trim();
        }
    }
}
