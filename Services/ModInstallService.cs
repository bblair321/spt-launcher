using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.IO;
using System.Threading;
using SharpCompress.Archives;

namespace SptLauncherWpf.Services
{
    public sealed class ModInstallProgress
    {
        public string Stage { get; init; } = "";
        public long BytesReceived { get; init; }
        public long? TotalBytes { get; init; }

        public string Message
        {
            get
            {
                if (TotalBytes is > 0)
                {
                    var pct = Math.Clamp((int)(BytesReceived * 100 / TotalBytes.Value), 0, 100);
                    return $"{Stage} {FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes.Value)} ({pct}%)";
                }

                if (BytesReceived > 0)
                {
                    return $"{Stage} {FormatBytes(BytesReceived)}";
                }

                return Stage;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1_048_576)
            {
                return $"{bytes / 1_048_576d:0.0} MB";
            }

            if (bytes >= 1024)
            {
                return $"{bytes / 1024d:0.0} KB";
            }

            return $"{bytes} B";
        }
    }

    public sealed class ModInstallReport
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public ModInstallKind Kind { get; init; } = ModInstallKind.Unknown;
        public string SptRoot { get; init; } = "";
        public IReadOnlyList<string> ExtractedFiles { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ServerTargets { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ClientTargets { get; init; } = Array.Empty<string>();
    }

    public sealed class ModInstallService
    {
        private static ModInstallService? _instance;
        public static ModInstallService Instance => _instance ??= new ModInstallService();

        private readonly HttpClient _http;

        private ModInstallService()
        {
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SPTLauncher/3.0 (+https://github.com/bblair321/spt-launcher)");
        }

        public async Task<ModInstallReport> InstallAsync(
            ForgeModSummary mod,
            ForgeModVersion version,
            string sptRoot,
            IReadOnlyList<string>? preferredFileTree,
            IProgress<ModInstallProgress>? progress = null,
            CancellationToken cancellationToken = default,
            bool clientPathsOnly = false)
        {
            if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            {
                return Fail("SPT install folder was not found. Set SPT.Launcher.exe on the Launcher tab first.");
            }

            // Browse Mods refuses Tools (often installers/utilities). Required-pack sync
            // still installs them when the host listed them as client mods.
            if (!clientPathsOnly && IsToolsCategory(mod))
            {
                return Fail(
                    $"\"{mod.Name}\" is listed as a Tool on Forge and is not auto-installed. Open it on Forge instead.");
            }

            if (string.IsNullOrWhiteSpace(version.Link))
            {
                return Fail("This version has no download link on Forge.");
            }

            var running = ModInstallService.FindBlockingInstallProcesses(sptRoot);
            if (running.Count > 0)
            {
                return Fail(
                    "Can't install while related processes are still running:\n" +
                    string.Join("\n", running.Take(10).Select(r => "• " + r)) +
                    "\n\nClose those, then try again. Use Stop on the Launcher tab if SPT is running.");
            }

            var hasRuntime = Directory.Exists(Path.Combine(sptRoot, "SPT_Runtime"));
            var archivePaths = preferredFileTree?.ToList() ?? new List<string>();

            progress?.Report(new ModInstallProgress { Stage = "Downloading…" });
            string downloadPath;
            try
            {
                downloadPath = await DownloadArchiveAsync(version, progress, cancellationToken);
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                return Fail(BuildFileLockMessage(
                    sptRoot,
                    ex,
                    fallbackPath: null,
                    stage: "Downloading / saving the mod archive"));
            }

            try
            {
                var format = DetectArchiveFormat(downloadPath);
                if (format == ArchiveFormat.Unknown)
                {
                    // Tiny Forge uploads are sometimes a bare plugin DLL (externally hosted).
                    if (IsPeDll(downloadPath))
                    {
                        return InstallBarePluginDll(
                            downloadPath,
                            mod,
                            version,
                            sptRoot,
                            clientPathsOnly);
                    }

                    return Fail(
                        "This download isn't a supported .zip/.7z archive. Open the mod on Forge to install it manually.");
                }

                // Antivirus often briefly locks the just-downloaded archive; wait it out.
                try
                {
                    AwaitFileReadable(downloadPath);
                }
                catch (Exception ex) when (IsFileLockException(ex))
                {
                    return Fail(BuildFileLockMessage(
                        sptRoot,
                        ex,
                        fallbackPath: downloadPath,
                        stage: "Opening downloaded archive (often antivirus)"));
                }

                if (archivePaths.Count == 0)
                {
                    try
                    {
                        archivePaths = ListArchiveEntries(downloadPath, format);
                    }
                    catch (Exception ex) when (IsFileLockException(ex))
                    {
                        return Fail(BuildFileLockMessage(
                            sptRoot,
                            ex,
                            fallbackPath: downloadPath,
                            stage: "Reading archive file list"));
                    }
                }
                else
                {
                    // Forge file-tree can disagree with the real zip layout (common for
                    // tiny/external-hosted mods). Always list the archive so extract works.
                    try
                    {
                        var actual = ListArchiveEntries(downloadPath, format);
                        if (actual.Count > 0)
                        {
                            archivePaths = actual;
                        }
                    }
                    catch (Exception ex) when (IsFileLockException(ex))
                    {
                        return Fail(BuildFileLockMessage(
                            sptRoot,
                            ex,
                            fallbackPath: downloadPath,
                            stage: "Reading archive file list"));
                    }
                    catch
                    {
                        // Keep preferred tree if listing fails for a non-lock reason.
                    }
                }

                var classification = ModPathClassifier.Classify(archivePaths, hasRuntime);
                if (!classification.CanAutoInstall)
                {
                    return new ModInstallReport
                    {
                        Success = false,
                        Kind = ModInstallKind.Unknown,
                        SptRoot = sptRoot,
                        Message =
                            $"Could not determine where to install \"{mod.Name}\". " +
                            "Open it on Forge and follow the author's instructions."
                    };
                }

                if (clientPathsOnly)
                {
                    if (classification.Kind == ModInstallKind.ServerOnly)
                    {
                        return Fail(
                            $"\"{mod.Name}\" is server-only (user/mods) and was skipped for client pack sync.");
                    }

                    var clientPaths = ModPathClassifier.FilterClientInstallPaths(
                        classification.InstallableRelativePaths);
                    if (clientPaths.Count == 0)
                    {
                        return Fail(
                            $"\"{mod.Name}\" has no BepInEx client files to install for pack sync.");
                    }

                    classification = new ModPathClassification
                    {
                        Kind = ModInstallKind.ClientOnly,
                        HasServerPaths = false,
                        HasClientPaths = true,
                        HasExtraRootFiles = false,
                        InstallableRelativePaths = clientPaths,
                        SkippedPaths = classification.SkippedPaths
                            .Concat(classification.InstallableRelativePaths.Except(
                                clientPaths, StringComparer.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                        Summary = "Client mod (BepInEx) — pack sync"
                    };
                }

                progress?.Report(new ModInstallProgress
                {
                    Stage = $"Installing ({classification.Summary})…"
                });

                List<string> extracted;
                try
                {
                    extracted = ExtractMappedEntries(
                        downloadPath,
                        format,
                        sptRoot,
                        classification.InstallableRelativePaths,
                        hasRuntime);
                }
                catch (Exception ex) when (IsFileLockException(ex))
                {
                    return Fail(BuildFileLockMessage(
                        sptRoot,
                        ex,
                        fallbackPath: downloadPath,
                        stage: "Extracting / replacing mod files"));
                }

                if (extracted.Count == 0)
                {
                    return Fail(
                        $"Download succeeded for \"{mod.Name}\" {version.Version}, but no files were " +
                        "extracted into the SPT folder (archive layout mismatch). Install it manually from Forge.");
                }

                try
                {
                    WriteInstallMarkers(mod, version, extracted);
                    // Refresh markers on any leftover copies of the same Forge mod so Diff
                    // doesn't keep reporting the old version from an orphaned sidecar.
                    RefreshRelatedClientMarkers(sptRoot, mod, version, extracted);
                }
                catch (Exception ex) when (IsFileLockException(ex))
                {
                    // Files were written; marker is best-effort.
                    Console.WriteLine($"Marker write skipped (locked): {ex.Message}");
                }

                var serverTargets = extracted
                    .Where(IsServerPath)
                    .Select(p => Path.GetDirectoryName(p) ?? p)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

                var clientTargets = extracted
                    .Where(IsClientPath)
                    .Select(p => Path.GetDirectoryName(p) ?? p)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();

                var parts = new List<string> { $"Installed {mod.Name} {version.Version}." };
                if (classification.HasServerPaths)
                {
                    parts.Add("Server files → user/mods");
                }

                if (classification.HasClientPaths)
                {
                    parts.Add("Client files → BepInEx");
                }

                return new ModInstallReport
                {
                    Success = true,
                    Kind = classification.Kind,
                    SptRoot = sptRoot,
                    ExtractedFiles = extracted,
                    ServerTargets = serverTargets,
                    ClientTargets = clientTargets,
                    Message = string.Join(" ", parts)
                };
            }
            finally
            {
                TryDelete(downloadPath);
            }
        }

        public static bool IsToolsCategory(ForgeModSummary mod) =>
            string.Equals(mod.Category?.Slug, "tools", StringComparison.OrdinalIgnoreCase)
            || mod.CategoryId == 1;

        private async Task<string> DownloadArchiveAsync(
            ForgeModVersion version,
            IProgress<ModInstallProgress>? progress,
            CancellationToken cancellationToken)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "SPTLauncherMods");
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"forge-mod-{version.Id}-{Guid.NewGuid():N}.bin");

            using var response = await _http.GetAsync(
                version.Link,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? version.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             tempFile,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.Read))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(new ModInstallProgress
                    {
                        Stage = "Downloading…",
                        BytesReceived = received,
                        TotalBytes = total
                    });
                }

                await output.FlushAsync(cancellationToken);
            }

            // Stream must be closed before magic-byte detection / rename — otherwise we lock ourselves.
            var suggested = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrWhiteSpace(suggested))
            {
                suggested = Path.GetFileName(response.RequestMessage?.RequestUri?.LocalPath ?? "");
            }

            var format = ArchiveFormat.Unknown;
            if (!string.IsNullOrWhiteSpace(suggested))
            {
                if (suggested.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    format = ArchiveFormat.Zip;
                }
                else if (suggested.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    format = ArchiveFormat.SevenZip;
                }
            }

            if (format == ArchiveFormat.Unknown)
            {
                format = DetectArchiveFormat(tempFile);
            }

            return RenameTempToFormat(tempFile, format);
        }

        private static string RenameTempToFormat(string tempFile, ArchiveFormat format)
        {
            var ext = format switch
            {
                ArchiveFormat.Zip => ".zip",
                ArchiveFormat.SevenZip => ".7z",
                _ => Path.GetExtension(tempFile)
            };

            if (string.IsNullOrWhiteSpace(ext) ||
                tempFile.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return tempFile;
            }

            var renamed = Path.ChangeExtension(tempFile, ext);
            try
            {
                if (File.Exists(renamed))
                {
                    File.Delete(renamed);
                }

                // Copy+delete survives AV locks better than Move on the just-written download.
                File.Copy(tempFile, renamed, overwrite: true);
                TryDelete(tempFile);
                return File.Exists(renamed) ? renamed : tempFile;
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                throw new ModFileLockException(
                    tempFile,
                    $"Locked file: {tempFile}\n{ex.Message}",
                    ex);
            }
        }

        private static void AwaitFileReadable(string path, int attempts = 20)
        {
            Exception? last = null;
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length >= 0)
                    {
                        return;
                    }
                }
                catch (Exception ex) when (IsFileLockException(ex))
                {
                    last = ex;
                    Thread.Sleep(150 * (i + 1));
                }
            }

            throw last is ModFileLockException modLock
                ? modLock
                : new ModFileLockException(
                    path,
                    $"Locked file: {path}\n{(last?.Message ?? "File is not readable yet.")}",
                    last);
        }

        private enum ArchiveFormat
        {
            Unknown,
            Zip,
            SevenZip
        }

        private static ArchiveFormat DetectArchiveFormat(string path)
        {
            try
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && IsZipArchive(path))
                {
                    return ArchiveFormat.Zip;
                }

                if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && IsSevenZipArchive(path))
                {
                    return ArchiveFormat.SevenZip;
                }

                if (IsZipArchive(path))
                {
                    return ArchiveFormat.Zip;
                }

                if (IsSevenZipArchive(path))
                {
                    return ArchiveFormat.SevenZip;
                }
            }
            catch
            {
                // fall through
            }

            return ArchiveFormat.Unknown;
        }

        private static bool IsPeDll(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                Span<byte> header = stackalloc byte[2];
                using var fs = File.OpenRead(path);
                if (fs.Read(header) < 2)
                {
                    return false;
                }

                // MZ
                return header[0] == (byte)'M' && header[1] == (byte)'Z';
            }
            catch
            {
                return false;
            }
        }

        private static ModInstallReport InstallBarePluginDll(
            string dllPath,
            ForgeModSummary mod,
            ForgeModVersion version,
            string sptRoot,
            bool clientPathsOnly)
        {
            _ = clientPathsOnly;

            var plugins = Path.Combine(sptRoot, "BepInEx", "plugins");
            Directory.CreateDirectory(plugins);

            // Prefer overwriting an already-installed copy of this Forge mod so Diff
            // keeps matching the same path (avoids orphan 1.1.0 DLL + new 1.6.0 DLL).
            var destination = FindExistingClientPluginPath(sptRoot, mod)
                              ?? Path.Combine(plugins, BuildBarePluginFileName(mod));

            try
            {
                AwaitFileReadable(dllPath);
                // Copy then delete: ReplaceFileWithRetry removes the temp download.
                ReplaceFileWithRetry(dllPath, destination);
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                return Fail(BuildFileLockMessage(
                    sptRoot,
                    ex,
                    fallbackPath: destination,
                    stage: "Replacing plugin DLL"));
            }

            try
            {
                WriteInstallMarkers(mod, version, new[] { destination });
                RefreshRelatedClientMarkers(sptRoot, mod, version, new[] { destination });
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                Console.WriteLine($"Marker write skipped (locked): {ex.Message}");
            }

            return new ModInstallReport
            {
                Success = true,
                Kind = ModInstallKind.ClientOnly,
                SptRoot = sptRoot,
                ExtractedFiles = new List<string> { destination },
                ClientTargets = new List<string> { plugins },
                Message = $"Installed {mod.Name} {version.Version} (bare plugin DLL → BepInEx/plugins)."
            };
        }

        private static string BuildBarePluginFileName(ForgeModSummary mod)
        {
            var fileName = !string.IsNullOrWhiteSpace(mod.Slug)
                ? $"{SanitizeFileName(mod.Slug)}.dll"
                : SanitizeFileName(Path.GetFileNameWithoutExtension(mod.Name)) + ".dll";
            if (string.IsNullOrWhiteSpace(fileName) || fileName == ".dll")
            {
                fileName = $"forge-mod-{mod.Id}.dll";
            }

            return fileName;
        }

        private static string? FindExistingClientPluginPath(string sptRoot, ForgeModSummary mod)
        {
            foreach (var local in InstalledModsService.ScanInstalledMods(sptRoot)
                         .Where(m => m.Kind == InstalledModKind.Client && !m.IsDirectory))
            {
                var sameId = local.ForgeModId == mod.Id;
                var sameGuid = !string.IsNullOrWhiteSpace(mod.Guid) &&
                               string.Equals(local.ForgeGuid, mod.Guid, StringComparison.OrdinalIgnoreCase);
                if (!sameId && !sameGuid)
                {
                    continue;
                }

                foreach (var path in local.AllPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static string SanitizeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "mod";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray();
            return new string(chars).Trim('-', ' ', '.');
        }

        private static bool IsZipArchive(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                Span<byte> header = stackalloc byte[4];
                using var fs = File.OpenRead(path);
                if (fs.Read(header) < 4)
                {
                    return false;
                }

                return header[0] == (byte)'P' && header[1] == (byte)'K';
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSevenZipArchive(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                // 7z signature: 37 7A BC AF 27 1C
                Span<byte> header = stackalloc byte[6];
                using var fs = File.OpenRead(path);
                if (fs.Read(header) < 6)
                {
                    return false;
                }

                return header[0] == 0x37 && header[1] == 0x7A && header[2] == 0xBC &&
                       header[3] == 0xAF && header[4] == 0x27 && header[5] == 0x1C;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> ListArchiveEntries(string archivePath, ArchiveFormat format)
        {
            try
            {
                if (format == ArchiveFormat.Zip)
                {
                    using var archive = ZipFile.OpenRead(archivePath);
                    return archive.Entries
                        .Where(e => !string.IsNullOrWhiteSpace(e.FullName))
                        .Select(e => e.FullName)
                        .ToList();
                }

                using var seven = ArchiveFactory.OpenArchive(archivePath);
                return seven.Entries
                    .Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Key))
                    .Select(e => e.Key!.Replace('\\', '/'))
                    .ToList();
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                throw new ModFileLockException(
                    archivePath,
                    $"Locked file: {archivePath}\n{ex.Message}",
                    ex);
            }
        }

        private static List<string> ExtractMappedEntries(
            string archivePath,
            ArchiveFormat format,
            string sptRoot,
            IReadOnlyList<string> installRelativePaths,
            bool installHasSptRuntime)
        {
            var wantedMapped = new HashSet<string>(
                installRelativePaths.Select(ModPathClassifier.NormalizeArchivePath),
                StringComparer.OrdinalIgnoreCase);

            return format == ArchiveFormat.Zip
                ? ExtractZip(archivePath, sptRoot, wantedMapped, installHasSptRuntime)
                : ExtractSevenZip(archivePath, sptRoot, wantedMapped, installHasSptRuntime);
        }

        private static List<string> ExtractZip(
            string zipPath,
            string sptRoot,
            HashSet<string> wantedMapped,
            bool installHasSptRuntime)
        {
            var extracted = new List<string>();
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(zipPath);
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                throw new ModFileLockException(
                    zipPath,
                    $"Locked file: {zipPath}\n{ex.Message}",
                    ex);
            }

            using (archive)
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.FullName) ||
                        entry.FullName.EndsWith('/') ||
                        entry.FullName.EndsWith('\\'))
                    {
                        continue;
                    }

                    var destinationRelative = ResolveDestinationRelative(
                        entry.FullName, installHasSptRuntime, wantedMapped);
                    if (destinationRelative == null)
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(
                        Path.Combine(sptRoot, destinationRelative.Replace('/', Path.DirectorySeparatorChar)));

                    if (!IsSafeExtractPath(sptRoot, destination))
                    {
                        throw new InvalidOperationException($"Blocked unsafe archive path: {entry.FullName}");
                    }

                    WriteExtractedFile(destination, output =>
                    {
                        using var input = entry.Open();
                        input.CopyTo(output);
                    });
                    extracted.Add(destination);
                }
            }

            return extracted;
        }

        private static List<string> ExtractSevenZip(
            string archivePath,
            string sptRoot,
            HashSet<string> wantedMapped,
            bool installHasSptRuntime)
        {
            var extracted = new List<string>();
            IArchive archive;
            try
            {
                archive = ArchiveFactory.OpenArchive(archivePath);
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                throw new ModFileLockException(
                    archivePath,
                    $"Locked file: {archivePath}\n{ex.Message}",
                    ex);
            }

            using (archive)
            {
                // Prefer forward reader for solid 7z archives (avoids repeated decompress / sticky handles).
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    if (reader.Entry.IsDirectory || string.IsNullOrWhiteSpace(reader.Entry.Key))
                    {
                        continue;
                    }

                    var destinationRelative = ResolveDestinationRelative(
                        reader.Entry.Key, installHasSptRuntime, wantedMapped);
                    if (destinationRelative == null)
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(
                        Path.Combine(sptRoot, destinationRelative.Replace('/', Path.DirectorySeparatorChar)));

                    if (!IsSafeExtractPath(sptRoot, destination))
                    {
                        throw new InvalidOperationException($"Blocked unsafe archive path: {reader.Entry.Key}");
                    }

                    WriteExtractedFile(destination, output => reader.WriteEntryTo(output));
                    extracted.Add(destination);
                }
            }

            return extracted;
        }

        private static void WriteExtractedFile(string destination, Action<Stream> writeContent)
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Stage in %TEMP% — writing *.tmp beside BepInEx/plugin DLLs often triggers
            // real-time AV which then locks the destination during replace.
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                "SPTLauncherMods",
                $"extract-{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            try
            {
                using (var output = new FileStream(
                           tempPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    writeContent(output);
                }

                ReplaceFileWithRetry(tempPath, destination);
            }
            catch (ModFileLockException)
            {
                throw;
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                throw new ModFileLockException(
                    destination,
                    $"Locked file: {destination}\n{ex.Message}",
                    ex);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void ReplaceFileWithRetry(string sourceTemp, string destination, int attempts = 12)
        {
            Exception? last = null;
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    TryReplaceFile(sourceTemp, destination);
                    return;
                }
                catch (Exception ex) when (IsFileLockException(ex))
                {
                    last = ex is ModFileLockException
                        ? ex
                        : new ModFileLockException(
                            destination,
                            $"Locked file: {destination}\n{ex.Message}",
                            ex);
                    Thread.Sleep(250 * (i + 1));
                }
            }

            throw last is ModFileLockException modLock
                ? modLock
                : new ModFileLockException(
                    destination,
                    $"Could not write locked file: {destination}",
                    last);
        }

        private static void TryReplaceFile(string sourceTemp, string destination)
        {
            // Prefer Copy from %TEMP% — more reliable across volumes and with AV than Move.
            void CopyIn(bool overwrite)
            {
                File.Copy(sourceTemp, destination, overwrite);
                TryDelete(sourceTemp);
            }

            if (!File.Exists(destination))
            {
                CopyIn(overwrite: false);
                return;
            }

            // 1) Overwrite in place (works when delete is blocked but write is allowed)
            try
            {
                using (var input = new FileStream(sourceTemp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }

                TryDelete(sourceTemp);
                return;
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                // fall through
            }

            // 2) Rename existing aside, then copy new file in
            var aside = destination + $".spt-old-{Guid.NewGuid():N}";
            try
            {
                File.Move(destination, aside);
                try
                {
                    CopyIn(overwrite: false);
                    TryDelete(aside);
                    return;
                }
                catch
                {
                    // Best effort rollback if copy-in failed — do not delete aside here.
                    try
                    {
                        if (!File.Exists(destination) && File.Exists(aside))
                        {
                            File.Move(aside, destination);
                        }
                    }
                    catch
                    {
                        // ignore rollback failures
                    }

                    throw;
                }
            }
            catch (Exception ex) when (IsFileLockException(ex))
            {
                // fall through to delete+copy
            }

            // 3) Delete + copy
            TryDelete(destination);
            if (File.Exists(destination))
            {
                throw new ModFileLockException(
                    destination,
                    $"Locked file: {destination}\nThe process cannot access the file because it is being used by another process.");
            }

            CopyIn(overwrite: false);
        }

        public static string BuildPublicFileLockMessage(string sptRoot, Exception ex) =>
            BuildFileLockMessage(sptRoot, ex);

        private static string BuildFileLockMessage(
            string sptRoot,
            Exception ex,
            string? fallbackPath = null,
            string? stage = null)
        {
            var lockedFile = ExtractPathFromLockException(ex);
            if (string.IsNullOrWhiteSpace(lockedFile))
            {
                lockedFile = fallbackPath;
            }

            var suspects = FindLikelyLockingProcesses(sptRoot, lockedFile);

            var lines = new List<string>
            {
                "Mod install couldn't replace a locked file."
            };

            if (!string.IsNullOrWhiteSpace(stage))
            {
                lines.Add("");
                lines.Add("Stage: " + stage);
            }

            lines.Add("");
            lines.Add("File:");
            lines.Add(string.IsNullOrWhiteSpace(lockedFile) ? "(path unknown — see Details)" : lockedFile);

            if (suspects.Count > 0)
            {
                lines.Add("");
                lines.Add("These processes are holding it (close them, then retry):");
                foreach (var s in suspects.Take(12))
                {
                    lines.Add("• " + s);
                }
            }
            else
            {
                lines.Add("");
                lines.Add("No locking process was identified. Try:");
                lines.Add("• Stop SPT on the Launcher tab");
                lines.Add("• Close EscapeFromTarkov / SPT.Launcher (and SPT.Server if it is this install)");
                lines.Add("• Pause antivirus scanning on your SPT folder / Temp folder");
                lines.Add("• Close File Explorer windows open inside the SPT folder");
                lines.Add("• Fully quit this launcher, then reopen and retry");
                // Note: spt-server-manager does not need to be closed for client mod installs.
            }

            lines.Add("");
            lines.Add("Details: " + GetDeepestMessage(ex));
            return string.Join("\n", lines);
        }

        public static string? ExtractPathFromLockException(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is ModFileLockException modLock &&
                    !string.IsNullOrWhiteSpace(modLock.LockedPath))
                {
                    return modLock.LockedPath;
                }

                var msg = cur.Message ?? "";

                // Our wrapped form: "Locked file: C:\..."
                const string prefix = "Locked file: ";
                var idx = msg.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var rest = msg[(idx + prefix.Length)..];
                    var nl = rest.IndexOfAny(['\r', '\n']);
                    var path = (nl >= 0 ? rest[..nl] : rest).Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }

                // "Could not write locked file: C:\..."
                const string couldNotPrefix = "Could not write locked file: ";
                idx = msg.IndexOf(couldNotPrefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var rest = msg[(idx + couldNotPrefix.Length)..];
                    var nl = rest.IndexOfAny(['\r', '\n']);
                    var path = (nl >= 0 ? rest[..nl] : rest).Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }

                // Windows form: The process cannot access the file 'C:\path' because...
                var q1 = msg.IndexOf('\'');
                var q2 = q1 >= 0 ? msg.IndexOf('\'', q1 + 1) : -1;
                if (q1 >= 0 && q2 > q1 + 2)
                {
                    var candidate = msg.Substring(q1 + 1, q2 - q1 - 1).Trim();
                    if (LooksLikePath(candidate))
                    {
                        return candidate;
                    }
                }

                // " (...path...)" suffix we add in ReplaceFileWithRetry
                if (msg.Contains('(') && msg.Contains(')') &&
                    (msg.Contains(":\\") || msg.Contains(":/")))
                {
                    var start = msg.LastIndexOf('(');
                    var end = msg.LastIndexOf(')');
                    if (start >= 0 && end > start)
                    {
                        var candidate = msg.Substring(start + 1, end - start - 1).Trim();
                        if (LooksLikePath(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        private static bool LooksLikePath(string candidate) =>
            candidate.Contains(":\\") || candidate.Contains(":/") ||
            candidate.StartsWith(@"\\", StringComparison.Ordinal);

        /// <summary>
        /// Processes that should block starting a mod install (game/server/tools under SPT).
        /// </summary>
        public static List<string> FindBlockingInstallProcesses(string sptRoot) =>
            FindLikelyLockingProcesses(sptRoot, lockedFile: null);

        public static List<string> FindLikelyLockingProcesses(string sptRoot, string? lockedFile = null)
        {
            var results = new List<string>();

            // Prefer Restart Manager — it reports who actually has the file open.
            if (!string.IsNullOrWhiteSpace(lockedFile))
            {
                try
                {
                    results.AddRange(
                        FileLockProbe.GetLockingProcessLabels(lockedFile)
                            .Where(label => !IsServerManagerLabel(label)));
                }
                catch
                {
                    // Fall through to name/path heuristics
                }
            }

            var selfId = Process.GetCurrentProcess().Id;
            var rootFull = string.IsNullOrWhiteSpace(sptRoot)
                ? ""
                : Path.GetFullPath(sptRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == selfId || process.HasExited)
                    {
                        continue;
                    }

                    string? exePath = null;
                    try
                    {
                        exePath = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // Access denied for some system processes
                    }

                    var name = process.ProcessName;
                    if (IsServerManagerProcess(name, exePath))
                    {
                        continue;
                    }

                    var underSpt = !string.IsNullOrEmpty(exePath) &&
                                   !string.IsNullOrEmpty(rootFull) &&
                                   exePath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);

                    var matchesLockedFile = !string.IsNullOrEmpty(lockedFile) &&
                                            !string.IsNullOrEmpty(exePath) &&
                                            string.Equals(exePath, lockedFile, StringComparison.OrdinalIgnoreCase);

                    if (matchesLockedFile ||
                        ShouldBlockInstallForProcess(name, underSpt, hasLockedFileHint: !string.IsNullOrWhiteSpace(lockedFile)))
                    {
                        var label = string.IsNullOrEmpty(exePath)
                            ? $"{name} (PID {process.Id})"
                            : $"{name} (PID {process.Id}) — {exePath}";
                        results.Add(label);
                    }
                }
                catch
                {
                    // ignore process probe failures
                }
                finally
                {
                    try { process.Dispose(); } catch { /* ignore */ }
                }
            }

            return results
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True for processes that can lock client/plugin files during install.
        /// Server manager / agent binaries are never blockers.
        /// SPT.Server only blocks when it is running from the same SPT install root.
        /// </summary>
        public static bool ShouldBlockInstallForProcess(
            string processName,
            bool exeUnderSptRoot,
            bool hasLockedFileHint = false)
        {
            if (string.IsNullOrWhiteSpace(processName) || IsServerManagerProcess(processName, exePath: null))
            {
                return false;
            }

            var name = processName.Trim();

            // Game client — always blocks BepInEx / client file writes
            if (name.Equals("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("EscapeFromTarkov_BE", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("BsgLauncher", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Official SPT / AKI game launcher
            if (name.Equals("SPT.Launcher", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Aki.Launcher", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Tools that commonly hold mod files open
            if (name.Equals("Greed", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Fika.Headless", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // SPT.Server only matters when it is the server for this install folder
            if (name.Equals("SPT.Server", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("SPT.Server.Exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Aki.Server", StringComparison.OrdinalIgnoreCase))
            {
                return exeUnderSptRoot;
            }

            // When diagnosing a specific locked file, also flag other Tarkov/Fika processes under the install.
            // Do not use a broad "contains SPT" match — that catches spt-server-manager.
            if (hasLockedFileHint && exeUnderSptRoot &&
                (name.Contains("Tarkov", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Fika", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        public static bool IsServerManagerProcess(string? processName, string? exePath)
        {
            if (LooksLikeServerManager(processName))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return false;
            }

            try
            {
                return LooksLikeServerManager(Path.GetFileNameWithoutExtension(exePath)) ||
                       LooksLikeServerManager(Path.GetFileName(exePath));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsServerManagerLabel(string label) =>
            LooksLikeServerManager(label);

        private static bool LooksLikeServerManager(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("server-manager", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("servermanager", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("spt-server-manager", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFileLockException(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is ModFileLockException || IsSharingViolation(cur) || IsAccessDenied(cur))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetDeepestMessage(Exception ex)
        {
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex.Message;
        }

        private static bool IsSharingViolation(Exception ex)
        {
            const int errorSharingViolation = 32;
            const int errorLockViolation = 33;
            if (ex is IOException io)
            {
                var code = io.HResult & 0xFFFF;
                if (code is errorSharingViolation or errorLockViolation)
                {
                    return true;
                }

                return io.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                       || io.Message.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool IsAccessDenied(Exception ex) =>
            ex is UnauthorizedAccessException ||
            (ex is IOException io && (io.HResult & 0xFFFF) == 5);

        private static string? ResolveDestinationRelative(
            string entryFullName,
            bool installHasSptRuntime,
            HashSet<string> wantedMapped)
        {
            var normalizedEntry = ModPathClassifier.NormalizeArchivePath(entryFullName);

            if (ModPathClassifier.TryMapToInstallRelative(
                    entryFullName,
                    installHasSptRuntime,
                    out var mapped,
                    out _))
            {
                mapped = ModPathClassifier.NormalizeArchivePath(mapped);
                return wantedMapped.Contains(mapped) ? mapped : null;
            }

            if (!normalizedEntry.Contains('/') && wantedMapped.Contains(normalizedEntry))
            {
                return normalizedEntry;
            }

            return null;
        }

        private static void WriteInstallMarkers(
            ForgeModSummary mod,
            ForgeModVersion version,
            IReadOnlyList<string> extractedFiles)
        {
            var marker = new ForgeModMarker
            {
                ForgeModId = mod.Id,
                Guid = mod.Guid,
                Slug = mod.Slug,
                Name = mod.Name,
                Version = version.Version,
                VersionId = version.Id,
                InstalledAtUtc = DateTime.UtcNow
            };

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in extractedFiles)
            {
                if (IsServerPath(file))
                {
                    var modRoot = FindServerModRoot(file);
                    if (!string.IsNullOrWhiteSpace(modRoot))
                    {
                        roots.Add(modRoot);
                    }
                }
                else if (IsClientPath(file))
                {
                    var pluginsRoot = FindPluginsRoot(file);
                    if (pluginsRoot == null)
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(pluginsRoot, file);
                    var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                    if (relative.Contains(Path.DirectorySeparatorChar) || relative.Contains(Path.AltDirectorySeparatorChar))
                    {
                        roots.Add(Path.Combine(pluginsRoot, firstSegment));
                    }
                    else
                    {
                        // Loose plugin DLL — marker beside the file.
                        try
                        {
                            ForgeModMarker.Write(file, isDirectory: false, marker);
                        }
                        catch
                        {
                            // ignore marker write failures
                        }
                    }
                }
            }

            foreach (var root in roots)
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        ForgeModMarker.Write(root, isDirectory: true, marker);
                    }
                }
                catch
                {
                    // ignore marker write failures
                }
            }
        }

        /// <summary>
        /// Updates .forge-mod.json on other loose plugins that already belong to this Forge mod
        /// (same id/guid), so pack Diff doesn't keep "have 1.1.0, need 1.6.0" from an old sidecar
        /// after a successful upgrade that wrote a differently named DLL.
        /// </summary>
        private static void RefreshRelatedClientMarkers(
            string sptRoot,
            ForgeModSummary mod,
            ForgeModVersion version,
            IReadOnlyList<string> extractedFiles)
        {
            var extractedSet = new HashSet<string>(extractedFiles, StringComparer.OrdinalIgnoreCase);
            var marker = new ForgeModMarker
            {
                ForgeModId = mod.Id,
                Guid = mod.Guid,
                Slug = mod.Slug,
                Name = mod.Name,
                Version = version.Version,
                VersionId = version.Id,
                InstalledAtUtc = DateTime.UtcNow
            };

            foreach (var pluginsDir in InstalledModsService.GetClientPluginDirectories(sptRoot))
            {
                if (!Directory.Exists(pluginsDir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(pluginsDir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    var working = InstalledModsService.IsDisabledName(name)
                        ? InstalledModsService.StripDisabledSuffix(name)
                        : name;
                    if (!working.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                        extractedSet.Contains(file))
                    {
                        continue;
                    }

                    var existing = ForgeModMarker.TryRead(file, isDirectory: false);
                    if (existing == null)
                    {
                        continue;
                    }

                    var sameId = existing.ForgeModId > 0 && existing.ForgeModId == mod.Id;
                    var sameGuid = !string.IsNullOrWhiteSpace(mod.Guid) &&
                                   !string.IsNullOrWhiteSpace(existing.Guid) &&
                                   string.Equals(existing.Guid, mod.Guid, StringComparison.OrdinalIgnoreCase);
                    if (!sameId && !sameGuid)
                    {
                        continue;
                    }

                    try
                    {
                        ForgeModMarker.Write(file, isDirectory: false, marker);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        private static string? FindServerModRoot(string filePath)
        {
            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
            var marker = $"{Path.DirectorySeparatorChar}user{Path.DirectorySeparatorChar}mods{Path.DirectorySeparatorChar}";
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var after = normalized[(idx + marker.Length)..];
            var segment = after.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(segment))
            {
                return null;
            }

            return Path.Combine(normalized[..(idx + marker.Length)], segment);
        }

        private static string? FindPluginsRoot(string filePath)
        {
            var normalized = filePath.Replace('/', Path.DirectorySeparatorChar);
            var marker = $"{Path.DirectorySeparatorChar}BepInEx{Path.DirectorySeparatorChar}plugins";
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            return normalized[..(idx + marker.Length)];
        }

        private static bool IsServerPath(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}user{Path.DirectorySeparatorChar}mods{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/user/mods/", StringComparison.OrdinalIgnoreCase);

        private static bool IsClientPath(string path) =>
            path.Contains($"{Path.DirectorySeparatorChar}BepInEx{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/BepInEx/", StringComparison.OrdinalIgnoreCase);

        public static bool IsSafeExtractPath(string rootDirectory, string destinationPath)
        {
            var root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var dest = Path.GetFullPath(destinationPath);
            return dest.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static ModInstallReport Fail(string message) =>
            new() { Success = false, Message = message };

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore temp cleanup failures
            }
        }
    }
}
