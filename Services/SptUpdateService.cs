using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SptLauncherWpf.Services
{
    public class SptDownloadInfo
    {
        public string Url { get; set; } = "";
        public string FileName { get; set; } = "";
        public long? ContentLength { get; set; }
        public string DisplaySize => ContentLength.HasValue
            ? SptUpdatePreflight.FormatBytes(ContentLength.Value)
            : "Unknown size";
    }

    public class SptUpdateService
    {
        private static SptUpdateService? _instance;
        public static SptUpdateService Instance => _instance ??= new SptUpdateService();

        private SptUpdateService()
        {
        }

        public async Task<SptDownloadInfo> GetDownloadInfoAsync(string downloadUrl, CancellationToken cancellationToken = default)
        {
            var info = new SptDownloadInfo
            {
                Url = downloadUrl,
                FileName = GetFileNameFromUrl(downloadUrl)
            };

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    info.ContentLength = response.Content.Headers.ContentLength;
                    var contentDisposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                    if (!string.IsNullOrWhiteSpace(contentDisposition))
                    {
                        info.FileName = contentDisposition;
                    }
                }
            }
            catch
            {
                // HEAD may be blocked; download will still work
            }

            if (string.IsNullOrWhiteSpace(info.FileName))
            {
                info.FileName = SptInstallUrls.InstallerFileName;
            }

            return info;
        }

        private static string GetFileNameFromUrl(string url)
        {
            try
            {
                var name = Path.GetFileName(new Uri(url).LocalPath);
                return string.IsNullOrWhiteSpace(name) ? SptInstallUrls.InstallerFileName : name;
            }
            catch
            {
                return SptInstallUrls.InstallerFileName;
            }
        }

        /// <summary>
        /// Recursively copies a directory and all its contents, preserving structure
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir, ref int copiedFiles, int totalFiles, HashSet<string> createdDirs, IProgress<double>? progress)
        {
            try
            {
                // Create destination directory if it doesn't exist
                if (!createdDirs.Contains(destDir))
                {
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                        System.Diagnostics.Debug.WriteLine($"[CopyDirectoryRecursive] Created directory: {destDir}");
                    }
                    createdDirs.Add(destDir);
                }

                // Copy all files in the current directory
                var files = Directory.GetFiles(sourceDir);
                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileName(file);
                        var destFile = Path.Combine(destDir, fileName);
                        
                        System.Diagnostics.Debug.WriteLine($"[CopyDirectoryRecursive] Copying: {file} -> {destFile}");
                        
                        File.Copy(file, destFile, overwrite: true);
                        
                        // Try to preserve file attributes
                        try
                        {
                            var sourceInfo = new FileInfo(file);
                            var destInfo = new FileInfo(destFile);
                            destInfo.Attributes = sourceInfo.Attributes;
                            destInfo.CreationTime = sourceInfo.CreationTime;
                            destInfo.LastWriteTime = sourceInfo.LastWriteTime;
                        }
                        catch
                        {
                            // Ignore attribute errors
                        }
                        
                        copiedFiles++;
                        if (totalFiles > 0)
                        {
                            var percent = (double)copiedFiles / totalFiles * 100;
                            progress?.Report(percent);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CopyDirectoryRecursive] Error copying file {file}: {ex.Message}");
                    }
                }

                // Recursively copy subdirectories
                var dirs = Directory.GetDirectories(sourceDir);
                foreach (var dir in dirs)
                {
                    try
                    {
                        var dirName = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(dirName))
                        {
                            continue;
                        }
                        var destSubDir = Path.Combine(destDir, dirName);
                        CopyDirectoryRecursive(dir, destSubDir, ref copiedFiles, totalFiles, createdDirs, progress);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CopyDirectoryRecursive] Error copying directory {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CopyDirectoryRecursive] Error in directory {sourceDir}: {ex.Message}");
            }
        }

        /// <summary>
        /// Counts total files in a directory tree for progress tracking
        /// </summary>
        private int CountFiles(string directory)
        {
            int count = 0;
            try
            {
                count += Directory.GetFiles(directory).Length;
                foreach (var subDir in Directory.GetDirectories(directory))
                {
                    count += CountFiles(subDir);
                }
            }
            catch
            {
                // Ignore errors
            }
            return count;
        }

        /// <summary>
        /// Backs up the entire SPT folder to the specified location, preserving directory structure
        /// </summary>
        public async Task BackupSptFolderAsync(string sptPath, string backupPath, IProgress<double>? progress = null)
        {
            if (!Directory.Exists(sptPath))
            {
                throw new DirectoryNotFoundException($"SPT folder not found: {sptPath}");
            }

            await Task.Run(() =>
            {
                // Normalize source path only
                sptPath = Path.GetFullPath(sptPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                // Don't normalize backup path - just trim trailing separators to avoid cross-drive issues
                backupPath = backupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                System.Diagnostics.Debug.WriteLine($"[BackupSptFolderAsync] Starting backup from {sptPath} to {backupPath}");

                // Count total files for progress tracking
                var totalFiles = CountFiles(sptPath);
                System.Diagnostics.Debug.WriteLine($"[BackupSptFolderAsync] Total files to copy: {totalFiles}");

                // Create backup root directory
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                    System.Diagnostics.Debug.WriteLine($"[BackupSptFolderAsync] Created backup directory: {backupPath}");
                }

                // Track created directories
                var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int copiedFiles = 0;

                // Recursively copy the entire directory structure
                CopyDirectoryRecursive(sptPath, backupPath, ref copiedFiles, totalFiles, createdDirs, progress);

                System.Diagnostics.Debug.WriteLine($"[BackupSptFolderAsync] Backup completed: {copiedFiles}/{totalFiles} files copied");
                
                // Report 100% completion
                if (totalFiles > 0)
                {
                    progress?.Report(100.0);
                }
            });
        }

        /// <summary>
        /// Downloads the installer from the specified URL with progress reporting.
        /// Validates that the file is a real Windows PE before returning.
        /// </summary>
        public async Task DownloadInstallerAsync(
            string downloadUrl,
            string targetPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            using var response = await client.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            try
            {
                await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((double)downloadedBytes / totalBytes * 100);
                    }
                }
            }
            catch
            {
                TryDeleteFile(targetPath);
                throw;
            }

            if (!IsValidWindowsExecutable(targetPath))
            {
                TryDeleteFile(targetPath);
                throw new InvalidDataException(
                    "The downloaded file is not a valid Windows installer. It may be an archive or an error page.");
            }
        }

        public static bool IsValidWindowsExecutable(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length < 1024)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[2];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(header) != 2)
                {
                    return false;
                }
            }

            return header[0] == (byte)'M' && header[1] == (byte)'Z';
        }

        private static void TryDeleteFile(string path)
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
                // Best effort cleanup only
            }
        }

        /// <summary>
        /// Launches a validated installer without wiping the SPT folder.
        /// </summary>
        public async Task LaunchInstallerOnlyAsync(string installerPath, string? workingDirectory = null)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException($"Installer not found: {installerPath}");
            }

            if (!IsValidWindowsExecutable(installerPath))
            {
                throw new InvalidDataException("Installer file is not a valid Windows executable.");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(installerPath) ?? string.Empty,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new Exception("Failed to start installer process");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Restores an SPT folder from a previously created backup directory.
        /// </summary>
        public async Task RestoreBackupAsync(
            string sptPath,
            string backupPath,
            IProgress<string>? statusProgress = null,
            IProgress<double>? progressProgress = null)
        {
            if (!Directory.Exists(backupPath))
            {
                throw new DirectoryNotFoundException($"Backup folder not found: {backupPath}");
            }

            if (string.IsNullOrWhiteSpace(sptPath))
            {
                throw new ArgumentException("SPT path is required.", nameof(sptPath));
            }

            statusProgress?.Report("Stopping check: ensuring SPT folder is ready...");
            if (!Directory.Exists(sptPath))
            {
                Directory.CreateDirectory(sptPath);
            }

            statusProgress?.Report("Cleaning current SPT folder...");
            await CleanSptFolderAsync(sptPath);

            statusProgress?.Report("Restoring backup...");
            await BackupSptFolderAsync(backupPath, sptPath, progressProgress);

            statusProgress?.Report("Restore completed successfully!");
            progressProgress?.Report(100);
        }

        /// <summary>
        /// Cleans (deletes) all files and folders in the SPT directory
        /// </summary>
        public async Task CleanSptFolderAsync(string sptPath)
        {
            if (!Directory.Exists(sptPath))
            {
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    // Delete all files
                    var files = Directory.GetFiles(sptPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch
                        {
                            // Continue with other files if one fails
                        }
                    }

                    // Delete all directories (bottom-up to handle nested directories)
                    var directories = Directory.GetDirectories(sptPath, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length);

                    foreach (var dir in directories)
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: false);
                        }
                        catch
                        {
                            // Continue with other directories if one fails
                        }
                    }

                    // Try to delete top-level directories
                    var topLevelDirs = Directory.GetDirectories(sptPath);
                    foreach (var dir in topLevelDirs)
                    {
                        try
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Continue if one fails
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to clean SPT folder: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Runs the SPT installer and waits for it to complete
        /// </summary>
        public async Task RunInstallerAsync(string installerPath, string installPath)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException($"Installer not found: {installerPath}");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? string.Empty,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(processInfo);
            if (process == null)
            {
                throw new Exception("Failed to start installer process");
            }

            // Wait for the installer to complete
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Installer exited with code: {process.ExitCode}");
            }
        }

        /// <summary>
        /// Moves contents from a subdirectory (like "spt") up to the parent directory
        /// This handles cases where the installer creates a subdirectory instead of installing directly
        /// </summary>
        private async Task MoveSubdirectoryContentsUpAsync(string sptPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(sptPath))
                    {
                        return;
                    }

                    // Check for common subdirectory names that installers might create
                    var subdirectoryNames = new[] { "spt", "SPT", "SPT-AKI", "spt-aki", "SinglePlayerTarkov" };
                    
                    foreach (var subdirName in subdirectoryNames)
                    {
                        var subdirectoryPath = Path.Combine(sptPath, subdirName);
                        if (Directory.Exists(subdirectoryPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[SptUpdateService] Found subdirectory: {subdirectoryPath}, moving contents up...");
                            
                            // Move all files and directories from subdirectory to parent
                            var subdirFiles = Directory.GetFiles(subdirectoryPath, "*", SearchOption.AllDirectories);
                            var subdirDirs = Directory.GetDirectories(subdirectoryPath, "*", SearchOption.AllDirectories)
                                .OrderByDescending(d => d.Length); // Process deepest directories first
                            
                            // Move files
                            foreach (var file in subdirFiles)
                            {
                                var relativePath = Path.GetRelativePath(subdirectoryPath, file);
                                var destPath = Path.Combine(sptPath, relativePath);
                                var destDir = Path.GetDirectoryName(destPath);
                                
                                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                {
                                    Directory.CreateDirectory(destDir);
                                }
                                
                                if (File.Exists(destPath))
                                {
                                    File.SetAttributes(destPath, FileAttributes.Normal);
                                    File.Delete(destPath);
                                }
                                
                                File.Move(file, destPath);
                            }
                            
                            // Move directories (already handled by moving files, but clean up empty dirs)
                            // Delete the subdirectory if it's now empty
                            try
                            {
                                if (Directory.GetFileSystemEntries(subdirectoryPath).Length == 0)
                                {
                                    Directory.Delete(subdirectoryPath);
                                }
                                else
                                {
                                    // If not empty, try to delete remaining empty subdirectories
                                    var remainingDirs = Directory.GetDirectories(subdirectoryPath, "*", SearchOption.AllDirectories)
                                        .OrderByDescending(d => d.Length);
                                    foreach (var dir in remainingDirs)
                                    {
                                        try
                                        {
                                            if (Directory.GetFileSystemEntries(dir).Length == 0)
                                            {
                                                Directory.Delete(dir);
                                            }
                                        }
                                        catch
                                        {
                                            // Ignore errors deleting individual directories
                                        }
                                    }
                                    
                                    // Try to delete the main subdirectory again
                                    if (Directory.GetFileSystemEntries(subdirectoryPath).Length == 0)
                                    {
                                        Directory.Delete(subdirectoryPath);
                                    }
                                }
                            }
                            catch
                            {
                                System.Diagnostics.Debug.WriteLine($"[SptUpdateService] Could not delete subdirectory {subdirectoryPath}, may not be empty");
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"[SptUpdateService] Successfully moved contents from {subdirectoryPath} to {sptPath}");
                            return; // Only process the first matching subdirectory
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SptUpdateService] Error moving subdirectory contents: {ex.Message}");
                    // Don't throw - this is a best-effort cleanup step
                }
            });
        }

        /// <summary>
        /// Applies a validated installer to SPT. Caller must download+validate the installer first.
        /// Never wipes the SPT folder unless the installer file is already a valid PE.
        /// </summary>
        public async Task UpdateSptAsync(
            string sptPath,
            string installerPath,
            bool createBackup,
            string? backupPath,
            IProgress<string>? statusProgress,
            IProgress<double>? progressProgress)
        {
            try
            {
                if (!IsValidWindowsExecutable(installerPath))
                {
                    throw new InvalidDataException(
                        "Refusing to update: installer is missing or not a valid Windows executable.");
                }

                // Step 1: Backup if requested (before any destructive work)
                if (createBackup && !string.IsNullOrEmpty(backupPath))
                {
                    statusProgress?.Report("Backing up SPT folder...");
                    System.Diagnostics.Debug.WriteLine($"[UpdateSptAsync] Starting backup from {sptPath} to {backupPath}");
                    await BackupSptFolderAsync(sptPath, backupPath, progressProgress);
                    System.Diagnostics.Debug.WriteLine($"[UpdateSptAsync] Backup completed successfully");
                    SettingsService.Instance.LastSptBackupPath = backupPath;
                    SettingsService.Instance.SaveSettings();
                    statusProgress?.Report("Backup completed.");
                    progressProgress?.Report(0);
                }

                // Step 2: Clean SPT folder only after installer validation
                statusProgress?.Report("Cleaning SPT folder...");
                progressProgress?.Report(0);
                await CleanSptFolderAsync(sptPath);
                statusProgress?.Report("SPT folder cleaned.");
                progressProgress?.Report(0);

                // Step 3: Copy installer to SPT directory
                statusProgress?.Report("Preparing installer...");
                var installerInSptPath = Path.Combine(sptPath, Path.GetFileName(installerPath));
                File.Copy(installerPath, installerInSptPath, overwrite: true);
                progressProgress?.Report(50);

                // Step 4: Run installer
                statusProgress?.Report("Installing SPT...");
                progressProgress?.Report(75);
                await RunInstallerAsync(installerInSptPath, sptPath);

                // Step 5: Check if installer created a subdirectory and move contents up
                statusProgress?.Report("Finalizing installation...");
                progressProgress?.Report(85);
                await MoveSubdirectoryContentsUpAsync(sptPath);
                progressProgress?.Report(95);

                // Step 6: Clean up installer file
                TryDeleteFile(installerInSptPath);

                progressProgress?.Report(100);
                statusProgress?.Report("Update completed successfully!");
            }
            catch (Exception ex)
            {
                statusProgress?.Report($"Update failed: {ex.Message}");
                throw;
            }
        }
    }
}
