using System.Runtime.InteropServices;

namespace NasSync.CfApi.Interop;

/// <summary>
/// P/Invoke declarations for the Windows Cloud Filter API (CfAPI).
/// All functions are imported from cldfltl.dll, the user-mode companion to
/// the cldflt.sys kernel minifilter driver that manages placeholder files.
///
/// Uses traditional DllImport for methods with complex struct marshaling,
/// and LibraryImport (source-generated) for simpler signatures.
/// </summary>
internal static partial class CfNativeMethods
{
    private const string CLDFLT_DLL = "cldfltl.dll";

    // =========================================================================
    // Sync Root Lifecycle
    // =========================================================================

    /// <summary>
    /// Registers a sync root with the Cloud Filter platform.
    /// Uses DllImport because CF_SYNC_REGISTRATION contains string fields
    /// that require runtime marshaling.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfRegisterSyncRoot(
        string syncRootPath,
        ref CfNativeTypes.CF_SYNC_REGISTRATION registration,
        out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

    /// <summary>
    /// Unregisters a previously registered sync root.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfUnregisterSyncRoot(string syncRootPath);

    /// <summary>
    /// Connects a registered sync root to begin receiving callbacks.
    /// Uses DllImport for the callback registration array marshaling.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfConnectSyncRoot(
        string syncRootPath,
        [In] CfNativeTypes.CF_CALLBACK_REGISTRATION[] callbackTable,
        uint callbackCount,
        CfNativeTypes.CF_CONNECT_FLAGS connectFlags,
        IntPtr context,
        out CfNativeTypes.CF_CONNECTION_KEY connectionKey);

    /// <summary>
    /// Disconnects a previously connected sync root.
    /// </summary>
    [DllImport(CLDFLT_DLL, SetLastError = true)]
    internal static extern int CfDisconnectSyncRoot(CfNativeTypes.CF_CONNECTION_KEY connectionKey);

    // =========================================================================
    // Placeholder Operations
    // =========================================================================

    /// <summary>
    /// Creates placeholder files and/or directories under the sync root.
    /// Uses DllImport for complex struct array marshaling.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfCreatePlaceholders(
        string syncRootPath,
        [In] CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO[] placeholderArray,
        uint placeholderCount,
        CfNativeTypes.CF_CREATE_FLAGS createFlags,
        IntPtr completionRoutine,
        IntPtr completionKey,
        IntPtr callbackInfo);

    /// <summary>
    /// Updates placeholder metadata (timestamps, attributes, file size).
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfUpdatePlaceholder(
        string volumeDosName,
        long fileId,
        [MarshalAs(UnmanagedType.Bool)] bool dehydrate,
        uint updateFlags,
        ref CfNativeTypes.CF_FS_METADATA fsMetadata,
        IntPtr dehydrateRangeArray,
        uint dehydrateRangeCount,
        out long usn);

    /// <summary>
    /// Dehydrates a file, releasing local cached data.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfDehydratePlaceholder(
        string volumeDosName,
        long fileId,
        uint flags);

    /// <summary>
    /// Converts a regular file to a cloud placeholder.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfConvertToPlaceholder(
        string volumeDosName,
        long fileId,
        uint convertFlags);

    // =========================================================================
    // Data Transfer
    // =========================================================================

    /// <summary>
    /// Provides data to satisfy a FETCH_DATA callback.
    /// Uses unsafe pointer for the data buffer.
    /// </summary>
    [DllImport(CLDFLT_DLL, SetLastError = true)]
    internal static extern unsafe int CfExecute(
        ref Guid volumeGuidName,
        ref long fileId,
        ref CfNativeTypes.CF_TRANSFER_KEY transferKey,
        byte* buffer,
        long length,
        long offset,
        CfNativeTypes.CF_EXECUTE_FLAGS flags,
        out long usn);

    /// <summary>
    /// Gets the transfer key from a FETCH_DATA callback.
    /// </summary>
    [DllImport(CLDFLT_DLL, SetLastError = true)]
    internal static extern unsafe int CfGetTransferKey(
        CfNativeTypes.CF_CALLBACK* callbackInfo,
        out CfNativeTypes.CF_TRANSFER_KEY transferKey);

    /// <summary>
    /// Releases transfer data after completion or cancellation.
    /// </summary>
    [DllImport(CLDFLT_DLL, SetLastError = true)]
    internal static extern int CfReleaseTransferData(
        ref Guid volumeGuidName,
        ref long fileId,
        ref CfNativeTypes.CF_TRANSFER_KEY transferKey);

    // =========================================================================
    // Query Functions
    // =========================================================================

    /// <summary>
    /// Gets placeholder information (hydration state, file size, file IDs).
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfGetPlaceholderInfo(
        string volumeDosName,
        long fileId,
        uint infoLevel,
        IntPtr infoBuffer,
        uint infoBufferLength,
        out uint returnedLength);

    /// <summary>
    /// Gets hydration range information for a placeholder file.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfGetPlaceholderRangeInfo(
        string volumeDosName,
        long fileId,
        IntPtr rangeArray,
        uint rangeArrayLength,
        out uint rangeCount);

    // =========================================================================
    // Progress Reporting
    // =========================================================================

    /// <summary>
    /// Reports download progress to the Shell for UI display.
    /// </summary>
    [DllImport(CLDFLT_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfReportProviderProgress(
        string syncRootPath,
        long fileSize,
        long bytesTransferred);

    // =========================================================================
    // Platform Information
    // =========================================================================

    /// <summary>
    /// Gets the current Cloud Filter platform version information.
    /// </summary>
    [DllImport(CLDFLT_DLL, SetLastError = true)]
    internal static extern int CfGetPlatformInfo(out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

    // =========================================================================
    // Placeholder Compatibility Mode
    // =========================================================================

    /// <summary>
    /// Sets the placeholder compatibility mode for the current process.
    /// </summary>
    [DllImport("ntdll.dll")]
    internal static extern byte RtlSetProcessPlaceholderCompatibilityMode(byte mode);
}
