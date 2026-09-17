using System.Runtime.InteropServices;

namespace NasSync.CfApi.Interop;

/// <summary>
/// P/Invoke declarations for the Windows Cloud Filter API (CfAPI).
/// All functions are imported from cldfltl.dll, the user-mode companion to
/// the cldflt.sys kernel minifilter driver that manages placeholder files.
///
/// These declarations are internal and should only be called through the
/// managed wrapper classes (CfSyncRootManager, PlaceholderManager, etc.).
/// </summary>
internal static partial class CfNativeMethods
{
    private const string CLDFLT_DLL = "cldfltl.dll";

    // =========================================================================
    // Sync Root Lifecycle
    // =========================================================================

    /// <summary>
    /// Registers a sync root with the Cloud Filter platform.
    /// Creates the sync root directory structure and registers it with the OS.
    /// The sync root must exist on an NTFS volume.
    /// </summary>
    /// <param name="syncRootPath">Full path to the sync root directory.</param>
    /// <param name="registration">Registration parameters including hydration and population policies.</param>
    /// <param name="platformInfo">Receives the platform version information.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfRegisterSyncRoot", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfRegisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        ref CfNativeTypes.CF_SYNC_REGISTRATION registration,
        out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

    /// <summary>
    /// Unregisters a previously registered sync root.
    /// Removes all placeholders and the sync root registration from the OS.
    /// </summary>
    /// <param name="syncRootPath">Full path to the sync root directory.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfUnregisterSyncRoot", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfUnregisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath);

    /// <summary>
    /// Connects a registered sync root to begin receiving callbacks from the platform.
    /// The sync root must be registered before connecting.
    /// </summary>
    /// <param name="syncRootPath">Full path to the sync root directory.</param>
    /// <param name="callbackTable">Array of callback registrations (type + function pointer pairs).</param>
    /// <param name="connectFlags">Connection flags (process info, placeholder info, etc.).</param>
    /// <param name="context">User context pointer passed to every callback (e.g., sync engine handle).</param>
    /// <param name="connectionKey">Receives the connection key for later disconnection.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfConnectSyncRoot", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfConnectSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        ReadOnlySpan<CfNativeTypes.CF_CALLBACK_REGISTRATION> callbackTable,
        CfNativeTypes.CF_CONNECT_FLAGS connectFlags,
        IntPtr context,
        out CfNativeTypes.CF_CONNECTION_KEY connectionKey);

    /// <summary>
    /// Disconnects a previously connected sync root.
    /// After disconnection, no further callbacks will be invoked.
    /// </summary>
    /// <param name="connectionKey">The connection key received from CfConnectSyncRoot.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfDisconnectSyncRoot")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfDisconnectSyncRoot(
        CfNativeTypes.CF_CONNECTION_KEY connectionKey);

    // =========================================================================
    // Placeholder Operations
    // =========================================================================

    /// <summary>
    /// Creates one or more placeholder files and/or directories under the sync root.
    /// Placeholders appear as regular files to applications but contain no data until hydrated.
    /// </summary>
    /// <param name="syncRootPath">Full path to the sync root directory.</param>
    /// <param name="placeholderArray">Array of placeholder creation info structures.</param>
    /// <param name="placeholderCount">Number of entries in the placeholder array.</param>
    /// <param name="createFlags">Creation flags (e.g., mark in-sync).</param>
    /// <param name="completionRoutine">Optional completion callback (IntPtr.Zero for none).</param>
    /// <param name="completionKey">Optional completion key.</param>
    /// <param name="callbackInfo">Optional callback info for progress reporting.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfCreatePlaceholders", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfCreatePlaceholders(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        ReadOnlySpan<CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO> placeholderArray,
        uint placeholderCount,
        CfNativeTypes.CF_CREATE_FLAGS createFlags,
        IntPtr completionRoutine,
        IntPtr completionKey,
        IntPtr callbackInfo);

    /// <summary>
    /// Updates the metadata of an existing placeholder file or directory.
    /// Can update file size, timestamps, and attributes without triggering hydration.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">File system file ID of the placeholder.</param>
    /// <param name="dehydrate">Whether to dehydrate the file after update.</param>
    /// <param name="updateFlags">Update flags.</param>
    /// <param name="fsMetadata">New metadata values to apply.</param>
    /// <param name="dehydrateRangeArray">Ranges to keep hydrated (IntPtr.Zero for none).</param>
    /// <param name="dehydrateRangeCount">Number of dehydrate ranges.</param>
    /// <param name="usn">Receives the update sequence number.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfUpdatePlaceholder", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfUpdatePlaceholder(
        [MarshalAs(UnmanagedType.LPWStr)] string volumeDosName,
        long fileId,
        [MarshalAs(UnmanagedType.Bool)] bool dehydrate,
        uint updateFlags,
        ref CfNativeTypes.CF_FS_METADATA fsMetadata,
        IntPtr dehydrateRangeArray,
        uint dehydrateRangeCount,
        out long usn);

    /// <summary>
    /// Dehydrates a file, releasing its local data and reverting to placeholder state.
    /// The file remains visible in the file system but data is no longer cached locally.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">File system file ID of the file to dehydrate.</param>
    /// <param name="flags">Dehydrate flags.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfDehydratePlaceholder", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfDehydratePlaceholder(
        [MarshalAs(UnmanagedType.LPWStr)] string volumeDosName,
        long fileId,
        uint flags);

    /// <summary>
    /// Converts a regular (non-placeholder) file into a cloud placeholder.
    /// The file's data is released after conversion, but the file remains accessible.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">File system file ID of the file to convert.</param>
    /// <param name="convertFlags">Conversion flags.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfConvertToPlaceholder", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfConvertToPlaceholder(
        [MarshalAs(UnmanagedType.LPWStr)] string volumeDosName,
        long fileId,
        uint convertFlags);

    // =========================================================================
    // Data Transfer
    // =========================================================================

    /// <summary>
    /// Provides data to satisfy a pending FETCH_DATA callback.
    /// Called from within the FetchData callback handler to deliver downloaded bytes
    /// to the platform for hydrating the placeholder file.
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File ID from the callback info.</param>
    /// <param name="transferKey">Transfer key from the callback info.</param>
    /// <param name="buffer">Buffer containing the data to provide.</param>
    /// <param name="length">Number of bytes in the buffer.</param>
    /// <param name="offset">Byte offset within the file where this data belongs.</param>
    /// <param name="flags">Execution flags.</param>
    /// <param name="usn">Receives the update sequence number.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfExecute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int CfExecute(
        in Guid volumeGuidName,
        in long fileId,
        in CfNativeTypes.CF_TRANSFER_KEY transferKey,
        byte* buffer,
        long length,
        long offset,
        CfNativeTypes.CF_EXECUTE_FLAGS flags,
        out long usn);

    /// <summary>
    /// Gets the transfer key from a FETCH_DATA callback for use with CfExecute.
    /// </summary>
    /// <param name="callbackInfo">Pointer to the callback info structure.</param>
    /// <param name="transferKey">Receives the transfer key.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfGetTransferKey")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int CfGetTransferKey(
        CfNativeTypes.CF_CALLBACK* callbackInfo,
        out CfNativeTypes.CF_TRANSFER_KEY transferKey);

    /// <summary>
    /// Releases transfer data associated with a completed or cancelled transfer.
    /// Must be called after CfExecute completes or when a fetch is cancelled.
    /// </summary>
    /// <param name="volumeGuidName">Volume GUID from the callback info.</param>
    /// <param name="fileId">File ID from the callback info.</param>
    /// <param name="transferKey">The transfer key to release.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfReleaseTransferData")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfReleaseTransferData(
        in Guid volumeGuidName,
        in long fileId,
        in CfNativeTypes.CF_TRANSFER_KEY transferKey);

    // =========================================================================
    // Query Functions
    // =========================================================================

    /// <summary>
    /// Gets placeholder information for a file, including its hydration state,
    /// file size, and file IDs.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name (e.g., "C:\").</param>
    /// <param name="fileId">File system file ID.</param>
    /// <param name="infoLevel">Information level to query.</param>
    /// <param name="infoBuffer">Buffer to receive the placeholder info.</param>
    /// <param name="infoBufferLength">Size of the info buffer in bytes.</param>
    /// <param name="returnedLength">Receives the actual number of bytes written.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfGetPlaceholderInfo", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfGetPlaceholderInfo(
        [MarshalAs(UnmanagedType.LPWStr)] string volumeDosName,
        long fileId,
        uint infoLevel,
        IntPtr infoBuffer,
        uint infoBufferLength,
        out uint returnedLength);

    /// <summary>
    /// Gets the hydration range information for a placeholder file.
    /// Returns which byte ranges are hydrated (cached locally) and which are dehydrated.
    /// </summary>
    /// <param name="volumeDosName">Volume DOS name.</param>
    /// <param name="fileId">File system file ID.</param>
    /// <param name="rangeArray">Buffer to receive range information.</param>
    /// <param name="rangeArrayLength">Size of the range buffer.</param>
    /// <param name="rangeCount">Receives the number of ranges returned.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfGetPlaceholderRangeInfo", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfGetPlaceholderRangeInfo(
        [MarshalAs(UnmanagedType.LPWStr)] string volumeDosName,
        long fileId,
        IntPtr rangeArray,
        uint rangeArrayLength,
        out uint rangeCount);

    // =========================================================================
    // Progress Reporting
    // =========================================================================

    /// <summary>
    /// Reports download progress for a hydration operation.
    /// Causes the Shell to display progress UI (inline in Explorer or as a toast).
    /// Should be called periodically during long downloads.
    /// </summary>
    /// <param name="syncRootPath">Full path to the sync root directory.</param>
    /// <param name="fileSize">Total file size in bytes.</param>
    /// <param name="bytesTransferred">Number of bytes downloaded so far.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfReportProviderProgress", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfReportProviderProgress(
        [MarshalAs(UnmanagedType.LPWStr)] string syncRootPath,
        long fileSize,
        long bytesTransferred);

    // =========================================================================
    // Platform Information
    // =========================================================================

    /// <summary>
    /// Gets the current Cloud Filter platform version information.
    /// Useful for detecting which CfAPI features are available on the current OS.
    /// </summary>
    /// <param name="platformInfo">Receives the platform version information.</param>
    /// <returns>HRESULT. S_OK (0) on success.</returns>
    [LibraryImport(CLDFLT_DLL, EntryPoint = "CfGetPlatformInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CfGetPlatformInfo(
        out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

    // =========================================================================
    // Placeholder Compatibility Mode
    // =========================================================================

    /// <summary>
    /// Sets the placeholder compatibility mode for the current process.
    /// By default, CfAPI hides reparse points from applications. Use this to expose them.
    /// </summary>
    /// <param name="mode">Compatibility mode (0 = default/hide, 1 = expose).</param>
    /// <returns>The previous compatibility mode.</returns>
    [LibraryImport("ntdll.dll", EntryPoint = "RtlSetProcessPlaceholderCompatibilityMode")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial byte RtlSetProcessPlaceholderCompatibilityMode(byte mode);
}
