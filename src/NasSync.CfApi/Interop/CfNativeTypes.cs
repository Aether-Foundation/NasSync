using System.Runtime.InteropServices;

namespace NasSync.CfApi.Interop;

/// <summary>
/// Native structure and enum definitions for the Windows Cloud Files API (CfAPI).
/// These types mirror the declarations in the Windows SDK <c>cfapi.h</c> header and
/// are used exclusively by the P/Invoke layer. Managed code should use the
/// higher-level wrapper types instead.
///
/// <para>
/// Layout notes: all structures target 64-bit Windows (x64 and ARM64), where
/// pointers and <c>LARGE_INTEGER</c> are 8 bytes. <c>StructSize</c> fields are
/// <c>ULONG</c> (4 bytes) per cfapi.h — NOT <c>USHORT</c>. Structures that map to a
/// C <c>union</c> (the callback/operation parameter blocks) use
/// <see cref="LayoutKind.Explicit"/> with byte offsets taken directly from the SDK
/// header so that field placement is unambiguous across architectures.
/// </para>
/// </summary>
internal static class CfNativeTypes
{
    // =========================================================================
    // Opaque key types (DECLARE_OPAQUE_KEY in cfapi.h → { LONGLONG Internal; })
    // =========================================================================

    /// <summary>
    /// Opaque handle to a connected sync root (CF_CONNECTION_KEY).
    /// A single <c>LONGLONG</c>; modeled as <see cref="long"/> in signatures.
    /// </summary>
    internal const int SIZEOF_CF_CONNECTION_KEY = 8;

    /// <summary>
    /// Opaque transfer key (CF_TRANSFER_KEY = LARGE_INTEGER). Modeled as <see cref="long"/>.
    /// </summary>
    internal const int SIZEOF_CF_TRANSFER_KEY = 8;

    /// <summary>Value of CF_CALLBACK_TYPE_NONE, used as the callback table terminator.</summary>
    internal const int CF_CALLBACK_TYPE_NONE = -1;

    // =========================================================================
    // Core enums (values copied verbatim from cfapi.h)
    // =========================================================================

    /// <summary>CF_CALLBACK_TYPE — identifies the callback the platform is invoking.</summary>
    internal enum CF_CALLBACK_TYPE : int
    {
        FETCH_DATA = 0,
        VALIDATE_DATA = 1,
        CANCEL_FETCH_DATA = 2,
        FETCH_PLACEHOLDERS = 3,
        CANCEL_FETCH_PLACEHOLDERS = 4,
        NOTIFY_FILE_OPEN_COMPLETION = 5,
        NOTIFY_FILE_CLOSE_COMPLETION = 6,
        NOTIFY_DEHYDRATE = 7,
        NOTIFY_DEHYDRATE_COMPLETION = 8,
        NOTIFY_DELETE = 9,
        NOTIFY_DELETE_COMPLETION = 10,
        NOTIFY_RENAME = 11,
        NOTIFY_RENAME_COMPLETION = 12,
        NONE = -1,
    }

    /// <summary>CF_REGISTER_FLAGS — flags for CfRegisterSyncRoot.</summary>
    [Flags]
    internal enum CF_REGISTER_FLAGS : uint
    {
        NONE = 0x00000000,
        UPDATE = 0x00000001,
        DISABLE_ON_DEMAND_POPULATION_ON_ROOT = 0x00000002,
        MARK_IN_SYNC_ON_ROOT = 0x00000004,
    }

    /// <summary>CF_HYDRATION_POLICY_PRIMARY — primary hydration behavior.</summary>
    internal enum CF_HYDRATION_POLICY_PRIMARY : ushort
    {
        PARTIAL = 0,
        PROGRESSIVE = 1,
        FULL = 2,
        ALWAYS_FULL = 3,
    }

    /// <summary>CF_HYDRATION_POLICY_MODIFIER — modifiers OR'd into the hydration policy.</summary>
    [Flags]
    internal enum CF_HYDRATION_POLICY_MODIFIER : ushort
    {
        NONE = 0x0000,
        VALIDATION_REQUIRED = 0x0001,
        STREAMING_ALLOWED = 0x0002,
        AUTO_DEHYDRATION_ALLOWED = 0x0004,
        ALLOW_FULL_RESTART_HYDRATION = 0x0008,
    }

    /// <summary>CF_POPULATION_POLICY_PRIMARY — namespace population behavior.</summary>
    internal enum CF_POPULATION_POLICY_PRIMARY : ushort
    {
        PARTIAL = 0,
        FULL = 2,
        ALWAYS_FULL = 3,
    }

    /// <summary>CF_POPULATION_POLICY_MODIFIER — currently only NONE.</summary>
    [Flags]
    internal enum CF_POPULATION_POLICY_MODIFIER : ushort
    {
        NONE = 0x0000,
    }

    /// <summary>CF_INSYNC_POLICY — when the platform clears the in-sync state.</summary>
    [Flags]
    internal enum CF_INSYNC_POLICY : uint
    {
        NONE = 0x00000000,
        TRACK_FILE_CREATION_TIME = 0x00000001,
        TRACK_FILE_READONLY_ATTRIBUTE = 0x00000002,
        TRACK_FILE_HIDDEN_ATTRIBUTE = 0x00000004,
        TRACK_FILE_SYSTEM_ATTRIBUTE = 0x00000008,
        TRACK_DIRECTORY_CREATION_TIME = 0x00000010,
        TRACK_DIRECTORY_READONLY_ATTRIBUTE = 0x00000020,
        TRACK_DIRECTORY_HIDDEN_ATTRIBUTE = 0x00000040,
        TRACK_DIRECTORY_SYSTEM_ATTRIBUTE = 0x00000080,
        TRACK_FILE_LAST_WRITE_TIME = 0x00000100,
        TRACK_DIRECTORY_LAST_WRITE_TIME = 0x00000200,
        TRACK_FILE_ALL = 0x0055550f,
        TRACK_DIRECTORY_ALL = 0x00aaaaf0,
        TRACK_ALL = 0x00ffffff,
        PRESERVE_INSYNC_FOR_SYNC_ENGINE = 0x80000000,
    }

    /// <summary>CF_HARDLINK_POLICY — whether hard links are permitted on placeholders.</summary>
    [Flags]
    internal enum CF_HARDLINK_POLICY : uint
    {
        NONE = 0x00000000,
        ALLOWED = 0x00000001,
    }

    /// <summary>CF_PLACEHOLDER_MANAGEMENT_POLICY — non-provider placeholder operations.</summary>
    [Flags]
    internal enum CF_PLACEHOLDER_MANAGEMENT_POLICY : uint
    {
        DEFAULT = 0x00000000,
        CREATE_UNRESTRICTED = 0x00000001,
        CONVERT_TO_UNRESTRICTED = 0x00000002,
        UPDATE_UNRESTRICTED = 0x00000004,
    }

    /// <summary>CF_CONNECT_FLAGS — extra information requested in callbacks.</summary>
    [Flags]
    internal enum CF_CONNECT_FLAGS : uint
    {
        NONE = 0x00000000,
        REQUIRE_PROCESS_INFO = 0x00000002,
        REQUIRE_FULL_FILE_PATH = 0x00000004,
        BLOCK_SELF_IMPLICIT_HYDRATION = 0x00000008,
    }

    /// <summary>CF_PLACEHOLDER_CREATE_FLAGS — per-entry flags for CfCreatePlaceholders.</summary>
    [Flags]
    internal enum CF_PLACEHOLDER_CREATE_FLAGS : uint
    {
        NONE = 0x00000000,
        DISABLE_ON_DEMAND_POPULATION = 0x00000001,
        MARK_IN_SYNC = 0x00000002,
        SUPERSEDE = 0x00000004,
        ALWAYS_FULL = 0x00000008,
    }

    /// <summary>CF_CREATE_FLAGS — the CreateFlags argument of CfCreatePlaceholders.</summary>
    [Flags]
    internal enum CF_CREATE_FLAGS : uint
    {
        NONE = 0x00000000,
        STOP_ON_ERROR = 0x00000001,
    }

    /// <summary>CF_OPERATION_TYPE — the operation passed to CfExecute.</summary>
    internal enum CF_OPERATION_TYPE : int
    {
        TRANSFER_DATA = 0,
        RETRIEVE_DATA = 1,
        ACK_DATA = 2,
        RESTART_HYDRATION = 3,
        TRANSFER_PLACEHOLDERS = 4,
        ACK_DEHYDRATE = 5,
        ACK_DELETE = 6,
        ACK_RENAME = 7,
    }

    /// <summary>CF_IN_SYNC_STATE — used with CfSetInSyncState.</summary>
    internal enum CF_IN_SYNC_STATE : int
    {
        NOT_IN_SYNC = 0,
        IN_SYNC = 1,
    }

    /// <summary>CF_CALLBACK_DELETE_FLAGS — flags in the NOTIFY_DELETE parameters.</summary>
    [Flags]
    internal enum CF_CALLBACK_DELETE_FLAGS : int
    {
        NONE = 0x00000000,
        IS_DIRECTORY = 0x00000001,
        IS_UNDELETE = 0x00000002,
    }

    /// <summary>CF_CALLBACK_RENAME_FLAGS — flags in the NOTIFY_RENAME parameters.</summary>
    [Flags]
    internal enum CF_CALLBACK_RENAME_FLAGS : int
    {
        NONE = 0x00000000,
        IS_DIRECTORY = 0x00000001,
        SOURCE_IN_SCOPE = 0x00000002,
        TARGET_IN_SCOPE = 0x00000004,
    }

    // =========================================================================
    // Registration structures
    // =========================================================================

    /// <summary>
    /// CF_HYDRATION_POLICY — a struct of two USHORTs (primary + modifier), NOT a single enum.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_HYDRATION_POLICY
    {
        internal CF_HYDRATION_POLICY_PRIMARY Primary;
        internal CF_HYDRATION_POLICY_MODIFIER Modifier;
    }

    /// <summary>
    /// CF_POPULATION_POLICY — a struct of two USHORTs (primary + modifier).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_POPULATION_POLICY
    {
        internal CF_POPULATION_POLICY_PRIMARY Primary;
        internal CF_POPULATION_POLICY_MODIFIER Modifier;
    }

    /// <summary>
    /// CF_SYNC_POLICIES — the policy block passed as the 3rd argument to CfRegisterSyncRoot.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_SYNC_POLICIES
    {
        /// <summary>Size of this structure in bytes (ULONG). Must be set by the caller.</summary>
        internal uint StructSize;

        internal CF_HYDRATION_POLICY Hydration;
        internal CF_POPULATION_POLICY Population;
        internal CF_INSYNC_POLICY InSync;
        internal CF_HARDLINK_POLICY HardLink;
        internal CF_PLACEHOLDER_MANAGEMENT_POLICY PlaceholderManagement;
    }

    /// <summary>
    /// CF_SYNC_REGISTRATION — provider identity passed as the 2nd argument to CfRegisterSyncRoot.
    /// Contains NO policy fields; those live in <see cref="CF_SYNC_POLICIES"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CF_SYNC_REGISTRATION
    {
        /// <summary>Size of this structure in bytes (ULONG). Must be set by the caller.</summary>
        internal uint StructSize;

        /// <summary>End-user facing provider name (max 255 chars).</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ProviderName;

        /// <summary>End-user facing provider version (max 255 chars).</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? ProviderVersion;

        /// <summary>Optional opaque sync-root identity blob (LPCVOID).</summary>
        internal IntPtr SyncRootIdentity;

        /// <summary>Length in bytes of <see cref="SyncRootIdentity"/>.</summary>
        internal uint SyncRootIdentityLength;

        /// <summary>Optional opaque file identity blob (LPCVOID).</summary>
        internal IntPtr FileIdentity;

        /// <summary>Length in bytes of <see cref="FileIdentity"/>.</summary>
        internal uint FileIdentityLength;

        /// <summary>Optional provider GUID. Empty lets the platform derive one from the name.</summary>
        internal Guid ProviderId;
    }

    /// <summary>CF_PLATFORM_INFO — returned by CfGetPlatformInfo / CfRegisterSyncRoot.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_PLATFORM_INFO
    {
        internal uint BuildNumber;
        internal uint RevisionNumber;
        internal uint IntegrationNumber;
    }

    // =========================================================================
    // Callback structures
    // =========================================================================

    /// <summary>
    /// CF_CALLBACK_INFO — the first pointer passed to every CfAPI callback.
    /// Carries the connection key, the callback context registered at connect time,
    /// volume/path strings, file IDs, the transfer key, and process info.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_CALLBACK_INFO
    {
        internal uint StructSize;

        /// <summary>CF_CONNECTION_KEY (opaque LONGLONG) for the active connection.</summary>
        internal long ConnectionKey;

        /// <summary>The CallbackContext pointer supplied to CfConnectSyncRoot.</summary>
        internal IntPtr CallbackContext;

        /// <summary>PCWSTR volume GUID name, e.g. "\\?\Volume{...}".</summary>
        internal IntPtr VolumeGuidName;

        /// <summary>PCWSTR volume DOS name, e.g. "C:\".</summary>
        internal IntPtr VolumeDosName;

        internal uint VolumeSerialNumber;

        /// <summary>LARGE_INTEGER file id of the sync root directory.</summary>
        internal long SyncRootFileId;

        internal IntPtr SyncRootIdentity;
        internal uint SyncRootIdentityLength;

        /// <summary>LARGE_INTEGER file id of the file the callback concerns.</summary>
        internal long FileId;

        /// <summary>LARGE_INTEGER total size of the file.</summary>
        internal long FileSize;

        internal IntPtr FileIdentity;
        internal uint FileIdentityLength;

        /// <summary>
        /// PCWSTR normalized full path of the file. Populated when the connection
        /// requested CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH.
        /// </summary>
        internal IntPtr NormalizedPath;

        /// <summary>CF_TRANSFER_KEY (LARGE_INTEGER) for the pending transfer.</summary>
        internal long TransferKey;

        internal byte PriorityHint;

        internal IntPtr CorrelationVector;
        internal IntPtr ProcessInfo;

        /// <summary>CF_REQUEST_KEY (LARGE_INTEGER) echoed back into CfExecute.</summary>
        internal long RequestKey;
    }

    /// <summary>
    /// CF_CALLBACK_PARAMETERS FETCH_DATA view (the parameter block is a C union; this
    /// overlays the FetchData member using explicit byte offsets from cfapi.h).
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct CF_CALLBACK_PARAMETERS_FETCH_DATA
    {
        [FieldOffset(0)] internal uint ParamSize;
        [FieldOffset(8)] internal int Flags;
        [FieldOffset(16)] internal long RequiredFileOffset;
        [FieldOffset(24)] internal long RequiredLength;
        [FieldOffset(32)] internal long OptionalFileOffset;
        [FieldOffset(40)] internal long OptionalLength;
        [FieldOffset(48)] internal long LastDehydrationTime;
        [FieldOffset(56)] internal int LastDehydrationReason;
    }

    /// <summary>
    /// CF_CALLBACK_PARAMETERS FETCH_PLACEHOLDERS view. <see cref="Pattern"/> is a PCWSTR.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct CF_CALLBACK_PARAMETERS_FETCH_PLACEHOLDERS
    {
        [FieldOffset(0)] internal uint ParamSize;
        [FieldOffset(8)] internal int Flags;
        [FieldOffset(16)] internal IntPtr Pattern;
    }

    /// <summary>
    /// CF_CALLBACK_PARAMETERS NOTIFY_RENAME view. <see cref="TargetPath"/> is a PCWSTR
    /// holding the new (post-rename) full path.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct CF_CALLBACK_PARAMETERS_RENAME
    {
        [FieldOffset(0)] internal uint ParamSize;
        [FieldOffset(8)] internal int Flags;
        [FieldOffset(16)] internal IntPtr TargetPath;
    }

    /// <summary>CF_CALLBACK_REGISTRATION — one row of the connect-time callback table.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_CALLBACK_REGISTRATION
    {
        internal CF_CALLBACK_TYPE Type;
        internal IntPtr Callback;
    }

    // =========================================================================
    // Placeholder structures
    // =========================================================================

    /// <summary>Win32 FILE_BASIC_INFO (LARGE_INTEGER ×4 + DWORD attributes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_BASIC_INFO
    {
        internal long CreationTime;
        internal long LastAccessTime;
        internal long LastWriteTime;
        internal long ChangeTime;
        internal uint FileAttributes;
    }

    /// <summary>CF_FS_METADATA — basic info plus file size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_FS_METADATA
    {
        internal FILE_BASIC_INFO BasicInfo;
        internal long FileSize;
    }

    /// <summary>
    /// CF_PLACEHOLDER_CREATE_INFO — one entry for CfCreatePlaceholders.
    /// <see cref="Result"/> and <see cref="CreateUsn"/> are written back by the API.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CF_PLACEHOLDER_CREATE_INFO
    {
        /// <summary>Relative file name from the base directory (backslash separated).</summary>
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string RelativeFileName;

        internal CF_FS_METADATA FsMetadata;

        internal IntPtr FileIdentity;
        internal uint FileIdentityLength;

        internal CF_PLACEHOLDER_CREATE_FLAGS Flags;

        /// <summary>Per-entry HRESULT written back by the API.</summary>
        internal int Result;

        /// <summary>USN written back by the API.</summary>
        internal long CreateUsn;
    }

    // =========================================================================
    // CfExecute operation structures
    // =========================================================================

    /// <summary>CF_OPERATION_INFO — describes the operation handed to CfExecute.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CF_OPERATION_INFO
    {
        internal uint StructSize;
        internal CF_OPERATION_TYPE Type;
        internal long ConnectionKey;
        internal long TransferKey;
        internal IntPtr CorrelationVector;
        internal IntPtr SyncStatus;
        internal long RequestKey;
    }

    /// <summary>
    /// CF_OPERATION_PARAMETERS TRANSFER_DATA view (union overlay with explicit offsets).
    /// Used to deliver downloaded bytes back to the platform.
    ///
    /// <para>
    /// The native <c>CF_OPERATION_PARAMETERS</c> is a union sized by its largest member
    /// (<c>RetrieveData</c>, 40 bytes of payload), making the whole struct 48 bytes
    /// including the leading <c>ParamSize</c>. <c>CfExecute</c> validates that
    /// <c>ParamSize == sizeof(CF_OPERATION_PARAMETERS)</c>, so the trailing
    /// <see cref="_UnionPad"/> field exists purely to size this overlay to 48 bytes —
    /// matching the real union — rather than the 40 bytes the TransferData member alone
    /// would occupy.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct CF_OPERATION_PARAMETERS_TRANSFER_DATA
    {
        [FieldOffset(0)] internal uint ParamSize;
        [FieldOffset(8)] internal int Flags;

        /// <summary>NTSTATUS completion status. 0 = success; non-zero reports an error.</summary>
        [FieldOffset(12)] internal int CompletionStatus;

        /// <summary>LPCVOID pointer to the data buffer (null when reporting an error).</summary>
        [FieldOffset(16)] internal IntPtr Buffer;

        [FieldOffset(24)] internal long Offset;
        [FieldOffset(32)] internal long Length;

        /// <summary>
        /// Padding mirroring the union's largest member (RetrieveData.ReturnedLength) so that
        /// <c>sizeof</c> this overlay equals the real <c>CF_OPERATION_PARAMETERS</c> size (48).
        /// </summary>
        [FieldOffset(40)] internal long _UnionPad;
    }

    // =========================================================================
    // Win32 structures for FileId resolution
    // =========================================================================

    /// <summary>BY_HANDLE_FILE_INFORMATION — used to derive the NTFS file index (FileId).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        internal uint FileAttributes;
        internal long CreationTime;
        internal long LastAccessTime;
        internal long LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }
}
