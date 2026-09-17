using System.Runtime.InteropServices;

namespace NasSync.CfApi.Interop;

/// <summary>
/// P/Invoke declarations for the Windows Cloud Files API (CfAPI).
/// Signatures match <c>cfapi.h</c> exactly. All functions are exported by
/// <c>cldapi.dll</c> (the user-mode companion to the cldflt.sys minifilter).
///
/// <para>
/// Marshalling notes:
/// <list type="bullet">
///   <item>Opaque keys (CF_CONNECTION_KEY, CF_TRANSFER_KEY, CF_REQUEST_KEY) are
///         single <c>LARGE_INTEGER</c> values, passed as <see cref="long"/>.</item>
///   <item>Callback/operation parameter blocks are C unions; they are modeled as
///         explicit-layout structs (see <see cref="CfNativeTypes"/>) and passed by
///         pointer so the caller can overlay the correct union member.</item>
///   <item>The CfAPI callback delegate has the signature
///         <c>void(CF_CALLBACK_INFO*, CF_CALLBACK_PARAMETERS*)</c> — two pointers.</item>
/// </list>
/// </para>
/// </summary>
internal static partial class CfNativeMethods
{
    /// <summary>
    /// The Cloud Files API DLL. On Windows 11 this is <c>cldapi.dll</c>
    /// (older drafts referenced the incorrect <c>cldfltl.dll</c>).
    /// </summary>
    private const string CLDAPI_DLL = "cldapi.dll";

    // =========================================================================
    // Sync Root Lifecycle
    // =========================================================================

    /// <summary>
    /// Registers a sync root with the Cloud Filter platform.
    /// <c>HRESULT CfRegisterSyncRoot(LPCWSTR, const CF_SYNC_REGISTRATION*,
    /// const CF_SYNC_POLICIES*, CF_REGISTER_FLAGS)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfRegisterSyncRoot(
        string syncRootPath,
        ref CfNativeTypes.CF_SYNC_REGISTRATION registration,
        ref CfNativeTypes.CF_SYNC_POLICIES policies,
        CfNativeTypes.CF_REGISTER_FLAGS registerFlags);

    /// <summary>Unregisters a previously registered sync root.</summary>
    [DllImport(CLDAPI_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfUnregisterSyncRoot(string syncRootPath);

    /// <summary>
    /// Connects a registered sync root to begin receiving callbacks.
    /// <c>HRESULT CfConnectSyncRoot(LPCWSTR, const CF_CALLBACK_REGISTRATION*,
    /// LPCVOID CallbackContext, CF_CONNECT_FLAGS, CF_CONNECTION_KEY*)</c>.
    /// The callback table must be terminated with a CF_CALLBACK_TYPE_NONE row.
    /// </summary>
    [DllImport(CLDAPI_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern unsafe int CfConnectSyncRoot(
        string syncRootPath,
        CfNativeTypes.CF_CALLBACK_REGISTRATION* callbackTable,
        IntPtr callbackContext,
        CfNativeTypes.CF_CONNECT_FLAGS connectFlags,
        out long connectionKey);

    /// <summary>Disconnects a previously connected sync root.</summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfDisconnectSyncRoot(long connectionKey);

    /// <summary>Gets the current Cloud Filter platform version information.</summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfGetPlatformInfo(out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

    // =========================================================================
    // Placeholder Operations (all operate on an open HANDLE)
    // =========================================================================

    /// <summary>
    /// Creates placeholder files and/or directories under a base directory.
    /// <c>HRESULT CfCreatePlaceholders(LPCWSTR, CF_PLACEHOLDER_CREATE_INFO*, DWORD,
    /// CF_CREATE_FLAGS, PDWORD EntriesProcessed)</c>. The array is in/out — the API
    /// writes each entry's <c>Result</c> and <c>CreateUsn</c> back, so the marshaller
    /// is given <c>[In, Out]</c> to copy the blittable result fields back.
    /// </summary>
    [DllImport(CLDAPI_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfCreatePlaceholders(
        string baseDirectoryPath,
        [In, Out] CfNativeTypes.CF_PLACEHOLDER_CREATE_INFO[] placeholderArray,
        uint placeholderCount,
        CfNativeTypes.CF_CREATE_FLAGS createFlags,
        out uint entriesProcessed);

    /// <summary>
    /// Converts a regular file (referenced by an open handle) into a cloud placeholder.
    /// <c>HRESULT CfConvertToPlaceholder(HANDLE, LPCVOID FileIdentity, DWORD,
    /// CF_CONVERT_FLAGS, USN*, LPOVERLAPPED)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfConvertToPlaceholder(
        IntPtr fileHandle,
        IntPtr fileIdentity,
        uint fileIdentityLength,
        uint convertFlags,
        ref long convertUsn,
        IntPtr overlapped);

    /// <summary>
    /// Updates an existing placeholder's metadata and/or in-sync state.
    /// <c>HRESULT CfUpdatePlaceholder(HANDLE, const CF_FS_METADATA*, LPCVOID, DWORD,
    /// const CF_FILE_RANGE*, DWORD, CF_UPDATE_FLAGS, USN*, LPOVERLAPPED)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int CfUpdatePlaceholder(
        IntPtr fileHandle,
        ref CfNativeTypes.CF_FS_METADATA fsMetadata,
        IntPtr fileIdentity,
        uint fileIdentityLength,
        IntPtr dehydrateRangeArray,
        uint dehydrateRangeCount,
        uint updateFlags,
        ref long updateUsn,
        IntPtr overlapped);

    /// <summary>
    /// Dehydrates a placeholder, releasing local cached data.
    /// <c>HRESULT CfDehydratePlaceholder(HANDLE, LARGE_INTEGER StartingOffset,
    /// LARGE_INTEGER Length, CF_DEHYDRATE_FLAGS, LPOVERLAPPED)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfDehydratePlaceholder(
        IntPtr fileHandle,
        long startingOffset,
        long length,
        uint dehydrateFlags,
        IntPtr overlapped);

    /// <summary>
    /// Sets the in-sync state of a placeholder by handle.
    /// <c>HRESULT CfSetInSyncState(HANDLE, CF_IN_SYNC_STATE, CF_SET_IN_SYNC_FLAGS, USN*)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfSetInSyncState(
        IntPtr fileHandle,
        CfNativeTypes.CF_IN_SYNC_STATE inSyncState,
        uint inSyncFlags,
        ref long inSyncUsn);

    // =========================================================================
    // Data Transfer (CfExecute)
    // =========================================================================

    /// <summary>
    /// Executes a sync-engine operation — most commonly TRANSFER_DATA to deliver
    /// downloaded bytes for a FETCH_DATA callback.
    /// <c>HRESULT CfExecute(const CF_OPERATION_INFO*, CF_OPERATION_PARAMETERS*)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern unsafe int CfExecute(
        ref CfNativeTypes.CF_OPERATION_INFO opInfo,
        CfNativeTypes.CF_OPERATION_PARAMETERS_TRANSFER_DATA* opParams);

    /// <summary>
    /// Reports provider progress to the Shell for a pending transfer.
    /// <c>HRESULT CfReportProviderProgress(CF_CONNECTION_KEY, CF_TRANSFER_KEY,
    /// LARGE_INTEGER Total, LARGE_INTEGER Completed)</c>.
    /// </summary>
    [DllImport(CLDAPI_DLL, SetLastError = true)]
    internal static extern int CfReportProviderProgress(
        long connectionKey,
        long transferKey,
        long providerProgressTotal,
        long providerProgressCompleted);

    // =========================================================================
    // File Handle Operations (for FileId resolution)
    // =========================================================================

    /// <summary>
    /// Opens a file or device. Used with <see cref="GetFileInformationByHandle"/>
    /// to retrieve the NTFS file index (FileId), and to obtain handles required by
    /// the placeholder APIs (CfConvertToPlaceholder, CfUpdatePlaceholder, etc.).
    /// </summary>
    /// <param name="lpFileName">The name of the file or device to open.</param>
    /// <param name="dwDesiredAccess">Requested access (GENERIC_READ = 0x80000000).</param>
    /// <param name="dwShareMode">Sharing mode (FILE_SHARE_READ|WRITE|DELETE = 0x07).</param>
    /// <param name="lpSecurityAttributes">Security attributes (IntPtr.Zero for default).</param>
    /// <param name="dwCreationDisposition">Action on existing/non-existing file (OPEN_EXISTING = 3).</param>
    /// <param name="dwFlagsAndAttributes">File flags (FILE_FLAG_BACKUP_SEMANTICS = 0x02000000 for directories).</param>
    /// <param name="hTemplateFile">Template file handle (IntPtr.Zero for none).</param>
    /// <returns>A safe file handle, or an invalid handle on failure.</returns>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    /// <summary>
    /// Retrieves file metadata by handle. The <c>FileIndexHigh</c>/<c>FileIndexLow</c>
    /// fields give the NTFS file index matching the <c>FileId</c> in CfAPI callbacks.
    /// </summary>
    /// <param name="hFile">A handle to the file (from CreateFileW).</param>
    /// <param name="lpFileInformation">Receives the file information structure.</param>
    /// <returns>True on success; false on failure (check GetLastError).</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        out CfNativeTypes.BY_HANDLE_FILE_INFORMATION lpFileInformation);
}
