using System.Runtime.InteropServices;
using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// High-level manager for cloud file placeholder operations.
/// Wraps the native CfAPI placeholder functions (create, convert, dehydrate, set in-sync)
/// into an ergonomic C# API with proper error handling.
///
/// <para>
/// Placeholder files appear as regular files in File Explorer and to applications,
/// but their data is stored on the remote NAS rather than locally. When an application
/// opens a placeholder, the platform triggers a FETCH_DATA callback to download the data.
/// </para>
///
/// <para>
/// Most placeholder-management APIs (<c>CfConvertToPlaceholder</c>,
/// <c>CfUpdatePlaceholder</c>, <c>CfDehydratePlaceholder</c>, <c>CfSetInSyncState</c>)
/// operate on an open <c>HANDLE</c> rather than a path or volume+FileId pair. The helpers
/// here open the handle internally with the required access and flags.
/// </para>
/// </summary>
public sealed class PlaceholderManager
{
    private readonly string _syncRootPath;

    /// <summary>Win32 constant: GENERIC_READ access.</summary>
    private const uint GENERIC_READ = 0x80000000;

    /// <summary>Win32 constant: GENERIC_WRITE access (required by convert/update/dehydrate).</summary>
    private const uint GENERIC_WRITE = 0x40000000;

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

    /// <summary>CF_EOF — sentinel meaning "to the end of the file" for range operations.</summary>
    private const long CF_EOF = -1L;

    /// <summary>CF_CONVERT_FLAG_MARK_IN_SYNC.</summary>
    private const uint CF_CONVERT_FLAG_MARK_IN_SYNC = 0x00000001;

    /// <summary>CF_UPDATE_FLAG_MARK_IN_SYNC.</summary>
    private const uint CF_UPDATE_FLAG_MARK_IN_SYNC = 0x00000002;

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
    /// Placeholders are lightweight representations that appear as real files
    /// but download their data on demand.
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

        // Build the native placeholder create-info array. The marshaller handles the
        // LPWSTR RelativeFileName fields and copies back each entry's Result/CreateUsn.
        var nativeEntries = new CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO[entries.Count];
        var perEntryFlags = markInSync
            ? CfNativeTypes.CF_PLACEHOLDER_CREATE_FLAGS.MARK_IN_SYNC
            : CfNativeTypes.CF_PLACEHOLDER_CREATE_FLAGS.NONE;

        // FileIdentity is a MANDATORY field for file placeholders (cfapi.h docs): the API
        // rejects the whole batch with ERROR_CLOUD_FILE_INVALID_REQUEST if a file entry has
        // a null FileIdentity. We store the relative path as a null-terminated UTF-16 blob —
        // the platform persists it and echoes it back in every callback for that placeholder,
        // letting the engine identify the cloud file without a FileId lookup. These buffers
        // are allocated in unmanaged memory and must be freed after the native call returns.
        var identityPtrs = new IntPtr[entries.Count];

        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PlaceholderEntry entry = entries[i];

                // Allocate the FileIdentity blob (null-terminated UTF-16 relative path).
                IntPtr identityPtr = Marshal.StringToCoTaskMemUni(entry.RelativePath);
                identityPtrs[i] = identityPtr;
                uint identityLength = (uint)((entry.RelativePath.Length + 1) * sizeof(char));

                nativeEntries[i] = new CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO
                {
                    RelativeFileName = entry.RelativePath,
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
                    FileIdentity = identityPtr,
                    FileIdentityLength = identityLength,
                    Flags = perEntryFlags,
                };
            }

            int hr = CfNativeMethods.CfCreatePlaceholders(
                _syncRootPath,
                nativeEntries,
                (uint)nativeEntries.Length,
                CfNativeTypes.CF_CREATE_FLAGS.STOP_ON_ERROR,
                out uint entriesProcessed);

            if (hr != 0)
            {
                // Surface the first per-entry failure to aid diagnostics. When the API
                // rejects the batch up front (entriesProcessed == 0, no per-entry Result),
                // it is a whole-array validation failure rather than a single bad entry.
                int firstEntryError = 0;
                string? firstErrorPath = null;
                for (int i = 0; i < nativeEntries.Length; i++)
                {
                    if (nativeEntries[i].Result != 0)
                    {
                        firstEntryError = nativeEntries[i].Result;
                        firstErrorPath = nativeEntries[i].RelativeFileName;
                        break;
                    }
                }

                throw new CfApiException("CfCreatePlaceholders", hr,
                    $"Failed to create {entries.Count} placeholder(s) under '{_syncRootPath}' " +
                    $"(processed {entriesProcessed}). First entry error: 0x{firstEntryError:X8} " +
                    $"at '{firstErrorPath}'.");
            }
        }
        finally
        {
            // Free every FileIdentity blob we allocated, regardless of success or failure.
            foreach (IntPtr p in identityPtrs)
            {
                if (p != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converts a regular (non-placeholder) file into a cloud placeholder, releasing its
    /// local data. Use this when a user creates a new local file that should be synced to
    /// the NAS and then dehydrated.
    /// </summary>
    /// <param name="fullPath">The full path to the file to convert.</param>
    /// <exception cref="CfApiException">Thrown if the native CfConvertToPlaceholder call fails.</exception>
    public void ConvertToPlaceholder(string fullPath)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = OpenHandle(fullPath, writeAccess: true);

        long usn = 0;
        int hr = CfNativeMethods.CfConvertToPlaceholder(
            handle.DangerousGetHandle(),
            IntPtr.Zero,        // no file identity
            0,                  // no file identity length
            CF_CONVERT_FLAG_MARK_IN_SYNC,
            ref usn,
            IntPtr.Zero);       // synchronous (no overlapped)

        if (hr != 0)
        {
            throw new CfApiException("CfConvertToPlaceholder", hr,
                $"Failed to convert '{fullPath}' to a placeholder.");
        }
    }

    /// <summary>
    /// Dehydrates a file, releasing its local cached data and reverting to placeholder state.
    /// The file remains visible in the file system but its data re-downloads on next access.
    /// </summary>
    /// <param name="fullPath">The full path to the file to dehydrate.</param>
    /// <exception cref="CfApiException">Thrown if the native CfDehydratePlaceholder call fails.</exception>
    public void Dehydrate(string fullPath)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = OpenHandle(fullPath, writeAccess: true);

        int hr = CfNativeMethods.CfDehydratePlaceholder(
            handle.DangerousGetHandle(),
            0,                  // starting offset
            CF_EOF,             // to end of file
            0,                  // CF_DEHYDRATE_FLAG_NONE
            IntPtr.Zero);       // synchronous

        if (hr != 0)
        {
            throw new CfApiException("CfDehydratePlaceholder", hr,
                $"Failed to dehydrate '{fullPath}'.");
        }
    }

    // =========================================================================
    // FileId Resolution
    // =========================================================================

    /// <summary>
    /// Retrieves the NTFS file index (FileId) for a given file or directory path.
    /// This FileId matches the <c>FileId</c> field in CfAPI callback structures,
    /// enabling path resolution when callbacks fire without a normalized path.
    /// </summary>
    /// <param name="fullPath">The full path to the file or directory.</param>
    /// <returns>The 64-bit NTFS file index.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// Thrown if the file cannot be opened or its information cannot be retrieved.
    /// </exception>
    public static long GetFileId(string fullPath)
    {
        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = OpenHandle(fullPath, writeAccess: false);

        if (!CfNativeMethods.GetFileInformationByHandle(handle, out CfNativeTypes.BY_HANDLE_FILE_INFORMATION info))
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to get file information for '{fullPath}'.");
        }

        // Combine high and low 32-bit parts into a 64-bit file index.
        return ((long)info.FileIndexHigh << 32) | info.FileIndexLow;
    }

    // =========================================================================
    // In-Sync Management
    // =========================================================================

    /// <summary>
    /// Marks a file as in-sync with the remote source. This tells the Cloud Filter platform
    /// that the local file matches the remote version, preventing unnecessary re-download.
    /// Uses <c>CfSetInSyncState</c> on an open handle.
    /// </summary>
    /// <param name="fullPath">The full path to the file to mark as in-sync.</param>
    public void MarkInSync(string fullPath)
    {
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle handle = OpenHandle(fullPath, writeAccess: true);

            long usn = 0;
            int hr = CfNativeMethods.CfSetInSyncState(
                handle.DangerousGetHandle(),
                CfNativeTypes.CF_IN_SYNC_STATE.IN_SYNC,
                0,          // CF_SET_IN_SYNC_FLAG_NONE
                ref usn);

            if (hr != 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PlaceholderManager] MarkInSync failed for '{fullPath}': HRESULT 0x{hr:X8}");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — the file still works, it just won't be marked as in-sync.
            System.Diagnostics.Debug.WriteLine(
                $"[PlaceholderManager] MarkInSync error for '{fullPath}': {ex.Message}");
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Opens a file handle suitable for placeholder operations or FileId queries.
    /// </summary>
    /// <param name="fullPath">The path to open.</param>
    /// <param name="writeAccess">
    /// True to request GENERIC_WRITE (required by convert/update/dehydrate/in-sync APIs);
    /// false for read-only access (sufficient for FileId queries).
    /// </param>
    /// <returns>An open <see cref="Microsoft.Win32.SafeHandles.SafeFileHandle"/>.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">Thrown if the handle cannot be opened.</exception>
    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenHandle(string fullPath, bool writeAccess)
    {
        uint access = writeAccess ? GENERIC_READ | GENERIC_WRITE : GENERIC_READ;

        Microsoft.Win32.SafeHandles.SafeFileHandle handle = CfNativeMethods.CreateFileW(
            fullPath,
            access,
            FILE_SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS, // Required to open directories.
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Failed to open '{fullPath}'.");
        }

        return handle;
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
