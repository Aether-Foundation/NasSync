using System.Runtime.InteropServices;
using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// High-level manager for cloud file placeholder operations.
/// Wraps the native CfAPI placeholder functions (create, update, dehydrate, convert)
/// into an ergonomic async C# API with proper error handling.
///
/// <para>
/// Placeholder files appear as regular files in File Explorer and to applications,
/// but their data is stored on the remote NAS rather than locally. When an application
/// opens a placeholder, the platform triggers a FETCH_DATA callback to download the data.
/// </para>
/// </summary>
public sealed class PlaceholderManager
{
    private readonly string _syncRootPath;

    /// <summary>Win32 constant: GENERIC_READ access.</summary>
    private const uint GENERIC_READ = 0x80000000;

    /// <summary>Win32 constant: Share read + write + delete.</summary>
    private const uint FILE_SHARE_ALL = 0x07;

    /// <summary>Win32 constant: Open existing file (do not create).</summary>
    private const uint OPEN_EXISTING = 3;

    /// <summary>Win32 constant: Required to open directory handles.</summary>
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    /// <summary>Win32 constant: Normal file attribute.</summary>
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>Win32 constant: Directory file attribute.</summary>
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    /// <summary>Win32 constant: Read-only file attribute.</summary>
    private const uint FILE_ATTRIBUTE_READONLY = 0x01;

    /// <summary>
    /// Creates a new PlaceholderManager for the specified sync root.
    /// </summary>
    /// <param name="syncRootPath">
    /// The full path to the sync root directory (e.g., "C:\Users\xxx\NasSync").
    /// Must be a registered sync root path.
    /// </param>
    /// <exception cref="ArgumentException">Thrown if syncRootPath is null or empty.</exception>
    public PlaceholderManager(string syncRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRootPath);
        _syncRootPath = syncRootPath;
    }

    /// <summary>
    /// Creates placeholder files and directories under the sync root.
    /// Placeholders are lightweight representations (~1 KB each) that appear as
    /// real files but download their data on demand.
    /// </summary>
    /// <param name="entries">
    /// Collection of file/directory entries to create as placeholders.
    /// Each entry specifies the relative path, file size, and timestamps.
    /// </param>
    /// <param name="markInSync">
    /// Whether to mark the placeholders as in-sync (data matches the remote).
    /// Set to true for initial population; false for files that need sync.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous placeholder creation.</returns>
    /// <exception cref="CfApiException">Thrown if the native CfCreatePlaceholders call fails.</exception>
    public Task CreatePlaceholdersAsync(
        IReadOnlyList<PlaceholderEntry> entries,
        bool markInSync = true,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Build native placeholder info array
        var nativeEntries = new CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            nativeEntries[i] = new CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO
            {
                RelativePath = entry.RelativePath,
                FsMetadata = new CfNativeTypes.CF_FS_METADATA
                {
                    FileSize = entry.IsDirectory ? 0 : entry.FileSize,
                    BasicInfo = new CfNativeTypes.FILE_BASIC_INFO
                    {
                        CreationTime = entry.CreatedUtc.ToFileTime(),
                        LastWriteTime = entry.LastModifiedUtc.ToFileTime(),
                        LastAccessTime = entry.LastModifiedUtc.ToFileTime(),
                        ChangeTime = entry.LastModifiedUtc.ToFileTime(),
                        FileAttributes = entry.IsDirectory
                            ? FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_READONLY
                            : FILE_ATTRIBUTE_NORMAL,
                    },
                },
                Flags = markInSync ? CfNativeTypes.CF_CREATE_FLAGS.MARK_IN_SYNC : CfNativeTypes.CF_CREATE_FLAGS.NONE,
            };
        }

        // Call native CfCreatePlaceholders
        int hr = CfNativeMethods.CfCreatePlaceholders(
            _syncRootPath,
            nativeEntries,
            (uint)nativeEntries.Length,
            markInSync ? CfNativeTypes.CF_CREATE_FLAGS.MARK_IN_SYNC : CfNativeTypes.CF_CREATE_FLAGS.NONE,
            IntPtr.Zero, // no completion routine
            IntPtr.Zero, // no completion key
            IntPtr.Zero); // no callback info

        if (hr != 0)
        {
            throw new CfApiException("CfCreatePlaceholders", hr,
                $"Failed to create {entries.Count} placeholder(s) under '{_syncRootPath}'.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dehydrates a file, releasing its local cached data and reverting to placeholder state.
    /// The file remains visible in the file system but its data will be re-downloaded on next access.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">The NTFS file ID of the file to dehydrate.</param>
    /// <returns>A task representing the asynchronous dehydration operation.</returns>
    /// <exception cref="CfApiException">Thrown if the native CfDehydratePlaceholder call fails.</exception>
    public Task DehydrateAsync(string volumeDosName, long fileId)
    {
        int hr = CfNativeMethods.CfDehydratePlaceholder(volumeDosName, fileId, 0);

        if (hr != 0)
        {
            throw new CfApiException("CfDehydratePlaceholder", hr,
                $"Failed to dehydrate file ID {fileId} on volume '{volumeDosName}'.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converts a regular (non-placeholder) file into a cloud placeholder.
    /// The file's data is released after conversion. Use this when a user creates a
    /// new local file that should be synced to the NAS and then dehydrated.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">The NTFS file ID of the file to convert.</param>
    /// <returns>A task representing the asynchronous conversion operation.</returns>
    /// <exception cref="CfApiException">Thrown if the native CfConvertToPlaceholder call fails.</exception>
    public Task ConvertToPlaceholderAsync(string volumeDosName, long fileId)
    {
        int hr = CfNativeMethods.CfConvertToPlaceholder(volumeDosName, fileId, 0);

        if (hr != 0)
        {
            throw new CfApiException("CfConvertToPlaceholder", hr,
                $"Failed to convert file ID {fileId} on volume '{volumeDosName}' to placeholder.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports download progress for a hydration operation.
    /// Causes the Shell to display progress UI in File Explorer and/or as a toast notification.
    /// Should be called periodically during long downloads (e.g., every 64 KB or 100 ms).
    /// </summary>
    /// <param name="fileSize">Total file size in bytes.</param>
    /// <param name="bytesTransferred">Number of bytes downloaded so far.</param>
    public void ReportProgress(long fileSize, long bytesTransferred)
    {
        // Clamp values to valid range
        bytesTransferred = Math.Clamp(bytesTransferred, 0, fileSize);

        int hr = CfNativeMethods.CfReportProviderProgress(_syncRootPath, fileSize, bytesTransferred);

        // Progress reporting failures are non-fatal — silently ignore
        _ = hr;
    }

    // =========================================================================
    // FileId Resolution
    // =========================================================================

    /// <summary>
    /// Retrieves the NTFS file index (FileId) for a given file or directory path.
    /// This FileId matches the <c>FileId</c> field in CfAPI callback structures,
    /// enabling path resolution when callbacks fire.
    ///
    /// <para>
    /// Uses <c>CreateFileW</c> with <c>FILE_FLAG_BACKUP_SEMANTICS</c> to open directories,
    /// then <c>GetFileInformationByHandle</c> to extract the file index.
    /// </para>
    /// </summary>
    /// <param name="fullPath">The full path to the file or directory.</param>
    /// <returns>The 64-bit NTFS file index.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// Thrown if the file cannot be opened or its information cannot be retrieved.
    /// </exception>
    public static long GetFileId(string fullPath)
    {
        using var handle = CfNativeMethods.CreateFileW(
            fullPath,
            GENERIC_READ,
            FILE_SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS, // Required for opening directories
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to open '{fullPath}' for FileId query.");
        }

        if (!CfNativeMethods.GetFileInformationByHandle(handle, out CfNativeTypes.BY_HANDLE_FILE_INFORMATION info))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to get file information for '{fullPath}'.");
        }

        // Combine high and low 32-bit parts into a 64-bit file index
        return ((long)info.FileIndexHigh << 32) | info.FileIndexLow;
    }

    // =========================================================================
    // In-Sync Management
    // =========================================================================

    /// <summary>
    /// Marks a file as in-sync with the remote source. This tells the Cloud Filter platform
    /// that the local file matches the remote version, preventing unnecessary re-download.
    /// Uses <c>CfUpdatePlaceholder</c> with the <c>MARK_IN_SYNC</c> flag.
    /// </summary>
    /// <param name="fullPath">The full path to the file to mark as in-sync.</param>
    public void MarkInSync(string fullPath)
    {
        try
        {
            long fileId = GetFileId(fullPath);
            string volumeDosName = Path.GetPathRoot(fullPath)!;
            var fileInfo = new FileInfo(fullPath);

            var metadata = new CfNativeTypes.CF_FS_METADATA
            {
                FileSize = fileInfo.Length,
                BasicInfo = new CfNativeTypes.FILE_BASIC_INFO
                {
                    CreationTime = fileInfo.CreationTimeUtc.ToFileTime(),
                    LastWriteTime = fileInfo.LastWriteTimeUtc.ToFileTime(),
                    LastAccessTime = fileInfo.LastAccessTimeUtc.ToFileTime(),
                    ChangeTime = fileInfo.LastWriteTimeUtc.ToFileTime(),
                    FileAttributes = FILE_ATTRIBUTE_NORMAL,
                },
            };

            int hr = CfNativeMethods.CfUpdatePlaceholder(
                volumeDosName,
                fileId,
                dehydrate: false,
                updateFlags: 0x04, // CF_UPDATE_FLAG_MARK_IN_SYNC
                ref metadata,
                IntPtr.Zero, // no dehydrate ranges
                0,           // no dehydrate range count
                out long usn);

            if (hr != 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PlaceholderManager] MarkInSync failed for '{fullPath}': HRESULT 0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — file will still work, just won't be marked as in-sync
            System.Diagnostics.Debug.WriteLine(
                $"[PlaceholderManager] MarkInSync error for '{fullPath}': {ex.Message}");
        }
    }
}

/// <summary>
/// Describes a file or directory to be created as a cloud placeholder.
/// </summary>
/// <param name="RelativePath">
/// Relative path from the sync root (e.g., "Documents\report.pdf").
/// Use backslash as the path separator.
/// </param>
/// <param name="IsDirectory">Whether this entry is a directory.</param>
/// <param name="FileSize">File size in bytes (ignored for directories).</param>
/// <param name="LastModifiedUtc">Last modification time in UTC.</param>
/// <param name="CreatedUtc">Creation time in UTC.</param>
public record PlaceholderEntry(
    string RelativePath,
    bool IsDirectory,
    long FileSize,
    DateTimeOffset LastModifiedUtc,
    DateTimeOffset CreatedUtc);
