using System.Runtime.InteropServices;

namespace NasSync.CfApi.Interop;

/// <summary>
/// Native structure and enum definitions for the Windows Cloud Filter API (CfAPI).
/// These types mirror the declarations in cfapi.h and are used exclusively by
/// the P/Invoke layer. Managed code should use the higher-level wrapper types instead.
///
/// All structures assume 64-bit (x64) Windows, which is the only supported platform
/// for Windows 11 Cloud Files features.
/// </summary>
internal static class CfNativeTypes
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>Size of the CF_CONNECT_INFO structure for version validation.</summary>
    internal const int SIZEOF_CF_CONNECT_INFO = 32;

    // =========================================================================
    // Opaque handle types
    // =========================================================================

    /// <summary>
    /// Opaque handle to a connected sync root.
    /// Returned by CfConnectSyncRoot, used by CfDisconnectSyncRoot.
    /// </summary>
    internal readonly struct CF_CONNECTION_KEY
    {
        /// <summary>Internal handle value.</summary>
        internal readonly long Internal;

        internal CF_CONNECTION_KEY(long value) => Internal = value;

        /// <summary>Gets whether this key represents a valid connection.</summary>
        internal bool IsValid => Internal != 0;
    }

    /// <summary>
    /// Opaque transfer key identifying a pending data transfer request.
    /// Used with CfExecute to provide data for a specific fetch operation.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_TRANSFER_KEY
    {
        internal long Internal;
    }

    // =========================================================================
    // Core enums
    // =========================================================================

    /// <summary>
    /// Identifies the type of callback invoked by the platform on the sync provider.
    /// Maps to CF_CALLBACK_TYPE in cfapi.h.
    /// </summary>
    internal enum CF_CALLBACK_TYPE : int
    {
        /// <summary>Platform requests file data for hydration (most critical callback).</summary>
        FETCH_DATA = 0,

        /// <summary>Platform requests validation of already-fetched data.</summary>
        VALIDATE_DATA = 1,

        /// <summary>A previously requested fetch operation was cancelled.</summary>
        CANCEL_FETCH_DATA = 2,

        /// <summary>Platform requests directory contents to create placeholders.</summary>
        FETCH_PLACEHOLDERS = 3,

        /// <summary>A previously requested placeholder fetch was cancelled.</summary>
        CANCEL_FETCH_PLACEHOLDERS = 4,

        /// <summary>A file open operation on a cloud file has completed.</summary>
        NOTIFY_FILE_OPEN_COMPLETION = 5,

        /// <summary>A file is about to be dehydrated (local data released).</summary>
        NOTIFY_DEHYDRATE = 6,

        /// <summary>A file has been fully dehydrated.</summary>
        NOTIFY_DEHYDRATE_COMPLETION = 7,

        /// <summary>A file under the sync root was deleted.</summary>
        NOTIFY_DELETE = 8,

        /// <summary>A file under the sync root was renamed or moved.</summary>
        NOTIFY_RENAME = 9,

        /// <summary>The sync provider is being suspended.</summary>
        SUSPEND = 10,

        /// <summary>The sync provider is being resumed.</summary>
        RESUME = 11,
    }

    /// <summary>
    /// Flags for CfRegisterSyncRoot behavior.
    /// Maps to CF_REGISTER_FLAGS in cfapi.h.
    /// </summary>
    [Flags]
    internal enum CF_REGISTER_FLAGS : uint
    {
        /// <summary>No flags.</summary>
        NONE = 0,

        /// <summary>Update an existing sync root registration.</summary>
        UPDATE = 1,

        /// <summary>Disable on-demand population of directory placeholders.</summary>
        DISABLE_ON_DEMAND_POPULATION = 2,

        /// <summary>Mark the sync root as in-sync during registration.</summary>
        MARK_IN_SYNC = 4,
    }

    /// <summary>
    /// Flags for CfConnectSyncRoot behavior.
    /// Maps to CF_CONNECT_FLAGS in cfapi.h.
    /// </summary>
    [Flags]
    internal enum CF_CONNECT_FLAGS : uint
    {
        /// <summary>No flags.</summary>
        NONE = 0,

        /// <summary>Include process information in callback parameters.</summary>
        REQUIRE_PROCESS_INFO = 1,

        /// <summary>Include full image path of the requesting process.</summary>
        REQUIRE_FULL_IMAGE_PATH = 2,

        /// <summary>Include placeholder info in callback parameters.</summary>
        GET_PLACEHOLDER_INFO = 4,

        /// <summary>Include file size information in callbacks.</summary>
        REQUIRE_FILE_SIZE = 8,
    }

    /// <summary>
    /// Flags for CfCreatePlaceholders behavior.
    /// Maps to CF_CREATE_FLAGS in cfapi.h.
    /// </summary>
    [Flags]
    internal enum CF_CREATE_FLAGS : uint
    {
        /// <summary>Default create behavior.</summary>
        NONE = 0,

        /// <summary>Mark the placeholder as in-sync upon creation.</summary>
        MARK_IN_SYNC = 1,
    }

    /// <summary>
    /// Flags for CfExecute behavior.
    /// Maps to CF_EXECUTE_FLAGS in cfapi.h.
    /// </summary>
    [Flags]
    internal enum CF_EXECUTE_FLAGS : uint
    {
        /// <summary>No flags.</summary>
        NONE = 0,

        /// <summary>The provided data is the last chunk (signals completion).</summary>
        CF_EXECUTE_FLAG_NONE = 0,
    }

    /// <summary>
    /// Placeholder states indicating hydration status.
    /// Maps to CF_PLACEHOLDER_STATE in cfapi.h.
    /// </summary>
    internal enum CF_PLACEHOLDER_STATE : int
    {
        /// <summary>Invalid or unknown state.</summary>
        INVALID = -1,

        /// <summary>State not yet determined.</summary>
        UNSPECIFIED = 0,

        /// <summary>File is a placeholder (cloud-only, no local data).</summary>
        PLACEHOLDER = 1,

        /// <summary>File has been hydrated (local data present).</summary>
        HYDRATED = 2,

        /// <summary>File is partially hydrated (some data ranges available).</summary>
        PARTIAL = 3,

        /// <summary>File is fully available locally.</summary>
        FULL = 4,
    }

    /// <summary>
    /// Hydration policy values for CfRegisterSyncRoot.
    /// Maps to CF_HYDRATION_POLICY in cfapi.h.
    /// Ordered by aggressiveness: PARTIAL &lt; PROGRESSIVE &lt; FULL &lt; ALWAYS_FULL.
    /// </summary>
    internal enum CF_HYDRATION_POLICY : ushort
    {
        /// <summary>Only download the byte ranges requested by the application.</summary>
        PARTIAL = 0,

        /// <summary>Download data progressively as the application reads the file.</summary>
        PROGRESSIVE = 1,

        /// <summary>Download the entire file on first access.</summary>
        FULL = 2,

        /// <summary>File is always fully hydrated and never dehydrated.</summary>
        ALWAYS_FULL = 3,
    }

    /// <summary>
    /// Modifiers for the hydration policy.
    /// Maps to CF_HYDRATION_POLICY_MODIFIER in cfapi.h.
    /// </summary>
    [Flags]
    internal enum CF_HYDRATION_POLICY_MODIFIER : ushort
    {
        /// <summary>No modifier.</summary>
        NONE = 0,

        /// <summary>Allow automatic dehydration by the system when disk space is low.</summary>
        AUTO_DEHYDRATION_ALLOWED = 1,

        /// <summary>Allow full restart of hydration if interrupted.</summary>
        ALLOW_FULL_RESTART_HYDRATION = 2,
    }

    /// <summary>
    /// Population policy values for CfRegisterSyncRoot.
    /// Maps to CF_POPULATION_POLICY in cfapi.h.
    /// </summary>
    internal enum CF_POPULATION_POLICY : ushort
    {
        /// <summary>Create all placeholders on registration.</summary>
        FULL = 0,

        /// <summary>Always keep all placeholders populated.</summary>
        ALWAYS_FULL = 1,
    }

    // =========================================================================
    // Native structures
    // =========================================================================

    /// <summary>
    /// Contains platform version information.
    /// Maps to CF_PLATFORM_INFO in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_PLATFORM_INFO
    {
        /// <summary>Windows build number (e.g., 22000 for Windows 11).</summary>
        internal uint BuildNumber;

        /// <summary>Windows revision number.</summary>
        internal uint RevisionNumber;

        /// <summary>Integration number for platform feature level.</summary>
        internal uint IntegrationNumber;
    }

    /// <summary>
    /// File system metadata for a placeholder file or directory.
    /// Maps to CF_FS_METADATA in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_FS_METADATA
    {
        /// <summary>Basic file information (attributes, timestamps).</summary>
        internal FILE_BASIC_INFO BasicInfo;

        /// <summary>File size in bytes.</summary>
        internal long FileSize;
    }

    /// <summary>
    /// Basic file information structure compatible with Win32 FILE_BASIC_INFO.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_BASIC_INFO
    {
        /// <summary>File creation time (Windows FILETIME as long).</summary>
        internal long CreationTime;

        /// <summary>Last access time (Windows FILETIME as long).</summary>
        internal long LastAccessTime;

        /// <summary>Last write time (Windows FILETIME as long).</summary>
        internal long LastWriteTime;

        /// <summary>Last attribute change time (Windows FILETIME as long).</summary>
        internal long ChangeTime;

        /// <summary>File attribute flags (FILE_ATTRIBUTE_*).</summary>
        internal uint FileAttributes;
    }

    /// <summary>
    /// Information about a placeholder to be created.
    /// Maps to CF_PLACEHOLDER_CREATE_INFO in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_PLACEHOLDER_CREATE_INFO
    {
        /// <summary>Relative path from the sync root (e.g., "folder\file.txt").</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string RelativePath;

        /// <summary>File system metadata for the placeholder.</summary>
        internal CF_FS_METADATA FsMetadata;

        /// <summary>Creation flags (e.g., CF_CREATE_FLAGS.MARK_IN_SYNC).</summary>
        internal CF_CREATE_FLAGS Flags;
    }

    /// <summary>
    /// Callback information provided by the platform when invoking sync provider callbacks.
    /// Maps to CF_CALLBACK in cfapi.h. Contains context about the operation being requested.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_CALLBACK
    {
        /// <summary>Size of this structure.</summary>
        internal uint StructSize;

        /// <summary>Type of callback being invoked.</summary>
        internal CF_CALLBACK_TYPE Type;

        /// <summary>Flags for the callback operation.</summary>
        internal uint CallbackFlags;

        /// <summary>Volume GUID name for the volume containing the file.</summary>
        internal Guid VolumeGuidName;

        /// <summary>File system file ID for the target file.</summary>
        internal long SyncRootFileId;

        /// <summary>File system file ID for the specific file.</summary>
        internal long FileId;

        /// <summary>Total file size in bytes.</summary>
        internal long FileSize;

        /// <summary>Transfer key for data transfer operations.</summary>
        internal CF_TRANSFER_KEY TransferKey;

        /// <summary>Priority of the fetch request.</summary>
        internal int PriorityHint;

        /// <summary>Alignment padding.</summary>
        internal uint _Padding;

        /// <summary>Process ID of the application requesting the operation.</summary>
        internal uint ProcessId;

        /// <summary>Thread ID of the application requesting the operation.</summary>
        internal uint ThreadId;

        /// <summary>Pointer to additional operation parameters (type-specific).</summary>
        internal IntPtr OperationParameters;
    }

    /// <summary>
    /// Parameters for FETCH_DATA callbacks.
    /// Maps to CF_OPERATION_PARAMETERS.FETCH_DATA in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_OPERATION_PARAMETERS_FETCH_DATA
    {
        /// <summary>Byte offset to start reading from.</summary>
        internal long Offset;

        /// <summary>Number of bytes requested.</summary>
        internal long Length;
    }

    /// <summary>
    /// Parameters for FETCH_PLACEHOLDERS callbacks.
    /// Maps to CF_OPERATION_PARAMETERS.FETCH_PLACEHOLDERS in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_OPERATION_PARAMETERS_FETCH_PLACEHOLDERS
    {
        /// <summary>Pattern for directory enumeration.</summary>
        internal uint PatternLength;

        /// <summary>Pattern string pointer.</summary>
        internal IntPtr Pattern;
    }

    // =========================================================================
    // Native callback delegates
    // =========================================================================

    /// <summary>
    /// Native callback function signature for CfAPI callbacks.
    /// This delegate matches the CALLBACK_FUNCTION typedef in cfapi.h.
    /// </summary>
    /// <param name="callbackInfo">Information about the callback invocation.</param>
    /// <param name="context">User-provided context pointer (the sync root path).</param>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal unsafe delegate void CF_CALLBACK_DELEGATE(
        CF_CALLBACK* callbackInfo,
        IntPtr context);

    /// <summary>
    /// Registration entry mapping a callback type to its handler function.
    /// Maps to CF_CALLBACK_REGISTRATION in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_CALLBACK_REGISTRATION
    {
        /// <summary>The type of callback this entry handles.</summary>
        internal CF_CALLBACK_TYPE Type;

        /// <summary>Pointer to the native callback function.</summary>
        internal IntPtr Callback;
    }

    // =========================================================================
    // Sync registration structure
    // =========================================================================

    /// <summary>
    /// Native registration parameters for CfRegisterSyncRoot.
    /// Maps to CF_SYNC_REGISTRATION in cfapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CF_SYNC_REGISTRATION
    {
        /// <summary>Size of this structure in bytes.</summary>
        internal uint StructSize;

        /// <summary>Provider display name (up to CF_PROVIDER_NAME_MAX_LENGTH chars).</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ProviderName;

        /// <summary>Provider version string.</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ProviderVersion;

        /// <summary>File system file ID of the sync root directory.</summary>
        internal long SyncRootFileId;

        /// <summary>File system type (reserved).</summary>
        internal uint FileSystem;

        /// <summary>Primary hydration policy.</summary>
        internal CF_HYDRATION_POLICY HydrationPolicy;

        /// <summary>Hydration policy modifier.</summary>
        internal CF_HYDRATION_POLICY_MODIFIER HydrationPolicyModifier;

        /// <summary>Population policy.</summary>
        internal CF_POPULATION_POLICY PopulationPolicy;

        /// <summary>In-sync tracking policy.</summary>
        internal uint InSyncPolicy;

        /// <summary>Registration flags.</summary>
        internal CF_REGISTER_FLAGS Flags;

        /// <summary>Placeholder management capabilities.</summary>
        internal uint PlaceholderManagementCapabilities;
    }

    // =========================================================================
    // Win32 structures for FileId resolution
    // =========================================================================

    /// <summary>
    /// Windows FILETIME structure (100-nanosecond intervals since January 1, 1601).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        /// <summary>Low-order 32 bits of the file time.</summary>
        internal uint DateTimeLow;

        /// <summary>High-order 32 bits of the file time.</summary>
        internal uint DateTimeHigh;
    }

    /// <summary>
    /// Contains file system metadata retrieved by <c>GetFileInformationByHandle</c>.
    /// The <c>FileIndexHigh</c>/<c>FileIndexLow</c> fields provide the NTFS file index
    /// which corresponds to the <c>FileId</c> used in CfAPI callbacks.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        /// <summary>File attribute flags (FILE_ATTRIBUTE_*).</summary>
        internal uint FileAttributes;

        /// <summary>File creation time.</summary>
        internal FILETIME CreationTime;

        /// <summary>Last access time.</summary>
        internal FILETIME LastAccessTime;

        /// <summary>Last write time.</summary>
        internal FILETIME LastWriteTime;

        /// <summary>Serial number of the volume containing the file.</summary>
        internal uint VolumeSerialNumber;

        /// <summary>High-order 32 bits of the file size.</summary>
        internal uint FileSizeHigh;

        /// <summary>Low-order 32 bits of the file size.</summary>
        internal uint FileSizeLow;

        /// <summary>Number of hard links to the file.</summary>
        internal uint NumberOfLinks;

        /// <summary>High-order 32 bits of the NTFS file index (FileId).</summary>
        internal uint FileIndexHigh;

        /// <summary>Low-order 32 bits of the NTFS file index (FileId).</summary>
        internal uint FileIndexLow;
    }
}
