using System.Runtime.InteropServices;
using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// Managed wrapper for the Windows Cloud Files API (CfAPI) sync root lifecycle.
/// Handles registering, unregistering, connecting, and disconnecting sync roots
/// with the Windows Shell and the cldflt.sys minifilter driver.
///
/// <para>
/// Registration is a two-step process:
/// 1. <c>CfRegisterSyncRoot</c> (native) — registers the sync root with cldflt.sys,
///    supplying the provider identity (<c>CF_SYNC_REGISTRATION</c>), the policy block
///    (<c>CF_SYNC_POLICIES</c>), and register flags.
/// 2. WinRT <c>StorageProviderSyncRootManager</c> — optional but recommended for
///    navigation pane integration (branded icon, display name, quota UI).
/// </para>
///
/// <para>
/// After registration, <c>ConnectAsync</c> begins receiving callbacks from the platform
/// when applications access placeholder files (hydration) or modify files under the sync
/// root. Each native callback has the signature
/// <c>void(CF_CALLBACK_INFO*, CF_CALLBACK_PARAMETERS*)</c>. The provider context is passed
/// back inside <c>CF_CALLBACK_INFO.CallbackContext</c> as a <see cref="GCHandle"/> pointer
/// to a <see cref="CallbackContext"/>.
/// </para>
/// </summary>
public sealed class CfSyncRootManager : IDisposable
{
    private bool _disposed;
    private bool _isRegistered;
    private bool _isConnected;
    private string? _syncRootPath;
    private string? _syncRootId;
    private long _connectionKey;

    /// <summary>
    /// Stored reference to the callback handler to prevent garbage collection
    /// while the sync root is connected.
    /// </summary>
    private ICfCallbackHandler? _callbackHandler;

    /// <summary>
    /// Bidirectional file ID to path resolver. Used as a fallback when a callback does
    /// not carry a normalized path (i.e., the connection did not request full file paths).
    /// </summary>
    private readonly PathResolver _pathResolver = new();

    /// <summary>
    /// The callback context passed to native callbacks via its <see cref="GCHandle"/> pointer.
    /// </summary>
    private CallbackContext? _callbackContext;

    /// <summary>
    /// GCHandle pinning the <see cref="_callbackContext"/> to prevent GC collection
    /// while the sync root is connected.
    /// </summary>
    private GCHandle _contextHandle;

    /// <summary>
    /// GCHandle pinning the native callback registration table for the connection's lifetime.
    /// </summary>
    private GCHandle _callbackTableHandle;

    /// <summary>
    /// Gets whether a sync root is currently registered with the OS.
    /// </summary>
    public bool IsRegistered => _isRegistered;

    /// <summary>
    /// Gets whether the sync root is connected and actively receiving callbacks.
    /// </summary>
    public bool IsConnected => _isConnected;

    /// <summary>
    /// Gets the local file system path of the registered sync root directory.
    /// </summary>
    public string? SyncRootPath => _syncRootPath;

    /// <summary>
    /// Gets the unique sync root identifier registered with Windows.
    /// Format: <c>[ProviderId]![UserSid]![AccountId]</c>.
    /// </summary>
    public string? SyncRootId => _syncRootId;

    /// <summary>
    /// Gets the path resolver that maps NTFS file IDs to file system paths.
    /// Populated after placeholder creation via <see cref="PlaceholderManager.GetFileId"/>.
    /// </summary>
    public PathResolver PathResolver => _pathResolver;

    /// <summary>
    /// Registers a new sync root with the Windows Cloud Filter platform.
    /// </summary>
    /// <param name="registrationInfo">The sync root registration configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous registration operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a sync root is already registered.</exception>
    /// <exception cref="CfApiException">Thrown if the native CfRegisterSyncRoot call fails.</exception>
    public Task RegisterAsync(
        SyncRootRegistrationInfo registrationInfo,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(registrationInfo);

        if (_isRegistered)
        {
            throw new InvalidOperationException(
                "A sync root is already registered. Call UnregisterAsync first.");
        }

        // Step 1: Ensure the sync root directory exists.
        Directory.CreateDirectory(registrationInfo.SyncRootPath);

        // Step 1.5: Clean up any stale registration for this path left by a previous run.
        // The provider must have WRITE_DATA/WRITE_DAC access; ignoring the result is safe
        // because CfUnregisterSyncRoot fails harmlessly when nothing is registered.
        try
        {
            int unregHr = CfNativeMethods.CfUnregisterSyncRoot(registrationInfo.SyncRootPath);
            if (unregHr == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CfSyncRootManager] Cleaned up stale registration for '{registrationInfo.SyncRootPath}'.");
            }
        }
        catch
        {
            // Ignore — no existing registration to clean up.
        }

        string displayName = string.IsNullOrEmpty(registrationInfo.DisplayName)
            ? "NAS Cloud Sync"
            : registrationInfo.DisplayName;

        // Step 2: Build the native registration (provider identity) structure.
        var nativeRegistration = new CfNativeTypes.CF_SYNC_REGISTRATION
        {
            StructSize = (uint)Marshal.SizeOf<CfNativeTypes.CF_SYNC_REGISTRATION>(),
            ProviderName = displayName,
            ProviderVersion = string.IsNullOrEmpty(registrationInfo.Version) ? "1.0" : registrationInfo.Version,
            SyncRootIdentity = IntPtr.Zero,
            SyncRootIdentityLength = 0,
            FileIdentity = IntPtr.Zero,
            FileIdentityLength = 0,
            ProviderId = Guid.Empty, // Optional — the platform derives one from ProviderName.
        };

        // Step 3: Build the policy block. Managed enum values are aligned to the native
        // CF_*_POLICY values (see SyncRootRegistrationInfo.cs), so these casts are exact.
        var nativePolicies = new CfNativeTypes.CF_SYNC_POLICIES
        {
            StructSize = (uint)Marshal.SizeOf<CfNativeTypes.CF_SYNC_POLICIES>(),
            Hydration = new CfNativeTypes.CF_HYDRATION_POLICY
            {
                Primary = (CfNativeTypes.CF_HYDRATION_POLICY_PRIMARY)registrationInfo.HydrationPolicy,
                Modifier = (CfNativeTypes.CF_HYDRATION_POLICY_MODIFIER)registrationInfo.HydrationPolicyModifier,
            },
            Population = new CfNativeTypes.CF_POPULATION_POLICY
            {
                Primary = (CfNativeTypes.CF_POPULATION_POLICY_PRIMARY)registrationInfo.PopulationPolicy,
                Modifier = CfNativeTypes.CF_POPULATION_POLICY_MODIFIER.NONE,
            },
            InSync = CfNativeTypes.CF_INSYNC_POLICY.TRACK_ALL,
            HardLink = CfNativeTypes.CF_HARDLINK_POLICY.NONE,
            PlaceholderManagement = CfNativeTypes.CF_PLACEHOLDER_MANAGEMENT_POLICY.DEFAULT,
        };

        System.Diagnostics.Debug.WriteLine(
            $"[CfSyncRootManager] CfRegisterSyncRoot: Path='{registrationInfo.SyncRootPath}', " +
            $"Name='{displayName}', RegSize={nativeRegistration.StructSize}, " +
            $"PolSize={nativePolicies.StructSize}, " +
            $"Hydration={nativePolicies.Hydration.Primary}|{nativePolicies.Hydration.Modifier}, " +
            $"Population={nativePolicies.Population.Primary}, Arch={RuntimeInformation.ProcessArchitecture}");

        // Step 4: Call native CfRegisterSyncRoot.
        int hr = CfNativeMethods.CfRegisterSyncRoot(
            registrationInfo.SyncRootPath,
            ref nativeRegistration,
            ref nativePolicies,
            CfNativeTypes.CF_REGISTER_FLAGS.NONE);

        if (hr != 0)
        {
            throw new CfApiException("CfRegisterSyncRoot", hr,
                $"Failed to register sync root. Path='{registrationInfo.SyncRootPath}', " +
                $"DisplayName='{displayName}', ProviderId='{registrationInfo.ProviderId}', " +
                $"AccountId='{registrationInfo.AccountId}', " +
                $"RegSize={nativeRegistration.StructSize}, PolSize={nativePolicies.StructSize}, " +
                $"Arch={RuntimeInformation.ProcessArchitecture}");
        }

        // Step 5: Store registration state.
        _syncRootPath = registrationInfo.SyncRootPath;
        _syncRootId = SyncRootIdHelper.Build(registrationInfo.ProviderId, registrationInfo.AccountId);
        _isRegistered = true;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Unregisters the sync root from the Windows Cloud Filter platform.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous unregistration operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no sync root is registered.</exception>
    public async Task UnregisterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isRegistered || _syncRootPath is null)
        {
            throw new InvalidOperationException("No sync root is currently registered.");
        }

        // Disconnect first if still connected.
        if (_isConnected)
        {
            await DisconnectAsync();
        }

        int hr = CfNativeMethods.CfUnregisterSyncRoot(_syncRootPath);

        if (hr != 0)
        {
            throw new CfApiException("CfUnregisterSyncRoot", hr,
                $"Failed to unregister sync root at '{_syncRootPath}'.");
        }

        _pathResolver.Clear();
        _syncRootPath = null;
        _syncRootId = null;
        _isRegistered = false;
    }

    /// <summary>
    /// Connects the sync root to begin receiving hydration and file operation callbacks
    /// from the cldflt.sys minifilter driver.
    /// </summary>
    /// <param name="callbacks">
    /// The callback handler that will process sync events from the platform.
    /// The reference is stored internally and must remain alive until disconnection.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous connect operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not registered or already connected.</exception>
    public unsafe Task ConnectAsync(ICfCallbackHandler callbacks, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isRegistered || _syncRootPath is null)
        {
            throw new InvalidOperationException(
                "Cannot connect: no sync root is registered. Call RegisterAsync first.");
        }

        if (_isConnected)
        {
            throw new InvalidOperationException("Sync root is already connected.");
        }

        _callbackHandler = callbacks ?? throw new ArgumentNullException(nameof(callbacks));

        // Create the callback context and pin it via GCHandle.
        _callbackContext = new CallbackContext
        {
            Handler = callbacks,
            PathResolver = _pathResolver,
            HydrationProvider = new HydrationDataProvider(),
            SyncRootPath = _syncRootPath,
        };
        _contextHandle = GCHandle.Alloc(_callbackContext);

        // Build the callback registration table (terminated by a NONE row) and pin it.
        CfNativeTypes.CF_CALLBACK_REGISTRATION[] table = BuildCallbackTable();
        _callbackTableHandle = GCHandle.Alloc(table, GCHandleType.Pinned);

        // Request full file paths + process info so callbacks carry NormalizedPath.
        int hr = CfNativeMethods.CfConnectSyncRoot(
            _syncRootPath,
            (CfNativeTypes.CF_CALLBACK_REGISTRATION*)_callbackTableHandle.AddrOfPinnedObject(),
            GCHandle.ToIntPtr(_contextHandle),
            CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_PROCESS_INFO |
                CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_FULL_FILE_PATH,
            out _connectionKey);

        if (hr != 0)
        {
            FreeConnectResources();
            throw new CfApiException("CfConnectSyncRoot", hr,
                "Failed to connect sync root for callbacks.");
        }

        // Make the connection key available to the hydration provider via the context.
        _callbackContext.ConnectionKey = _connectionKey;
        _isConnected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disconnects the sync root from receiving callbacks. Placeholder files remain
    /// visible in the file system, but no further callbacks fire until reconnected.
    /// </summary>
    /// <returns>A task representing the asynchronous disconnect operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not connected.</exception>
    public Task DisconnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isConnected)
        {
            throw new InvalidOperationException("Sync root is not connected.");
        }

        int hr = CfNativeMethods.CfDisconnectSyncRoot(_connectionKey);

        if (hr != 0)
        {
            throw new CfApiException("CfDisconnectSyncRoot", hr,
                "Failed to disconnect sync root.");
        }

        _isConnected = false;
        _connectionKey = 0;
        FreeConnectResources();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_isConnected)
        {
            try
            {
                _ = CfNativeMethods.CfDisconnectSyncRoot(_connectionKey);
            }
            catch
            {
                // Swallow exceptions during dispose.
            }

            _isConnected = false;
        }

        FreeConnectResources();
        _disposed = true;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Frees the GCHandle resources allocated during connect (context + callback table)
    /// and clears the handler references.
    /// </summary>
    private void FreeConnectResources()
    {
        if (_contextHandle.IsAllocated)
        {
            _contextHandle.Free();
        }

        if (_callbackTableHandle.IsAllocated)
        {
            _callbackTableHandle.Free();
        }

        _callbackHandler = null;
        _callbackContext = null;
    }

    /// <summary>
    /// Builds the native callback registration table. Each callback type maps to a static
    /// <c>[UnmanagedCallersOnly]</c> dispatcher. The table is terminated with a
    /// <c>CF_CALLBACK_TYPE_NONE</c> row (CF_CALLBACK_REGISTRATION_END) as required by the API.
    /// </summary>
    /// <returns>An array of callback registrations for CfConnectSyncRoot.</returns>
    private static unsafe CfNativeTypes.CF_CALLBACK_REGISTRATION[] BuildCallbackTable()
    {
        return
        [
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.FETCH_DATA, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnFetchData),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.FETCH_PLACEHOLDERS, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnFetchPlaceholders),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.CANCEL_FETCH_DATA, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnCancelFetchData),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnNotifyDehydrate),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE_COMPLETION, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnNotifyDehydrateCompletion),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DELETE, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnNotifyDelete),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_RENAME, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnNotifyRename),
            Entry(CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_FILE_OPEN_COMPLETION, (IntPtr)(delegate* unmanaged<CfNativeTypes.CF_CALLBACK_INFO*, IntPtr, void>)&OnNotifyFileOpenCompletion),

            // CF_CALLBACK_REGISTRATION_END — terminator.
            new CfNativeTypes.CF_CALLBACK_REGISTRATION
            {
                Type = CfNativeTypes.CF_CALLBACK_TYPE.NONE,
                Callback = IntPtr.Zero,
            },
        ];
    }

    /// <summary>
    /// Constructs a single callback registration row.
    /// </summary>
    private static CfNativeTypes.CF_CALLBACK_REGISTRATION Entry(
        CfNativeTypes.CF_CALLBACK_TYPE type, IntPtr callback) =>
        new() { Type = type, Callback = callback };

    /// <summary>
    /// Extracts the <see cref="CallbackContext"/> from the native context pointer carried
    /// in <c>CF_CALLBACK_INFO.CallbackContext</c>. Returns null if invalid or freed.
    /// </summary>
    private static CallbackContext? GetContext(IntPtr contextPtr)
    {
        if (contextPtr == IntPtr.Zero)
        {
            return null;
        }

        GCHandle handle = GCHandle.FromIntPtr(contextPtr);
        return handle.Target as CallbackContext;
    }

    /// <summary>
    /// Resolves the local file path for a callback. Prefers the platform-supplied
    /// <c>NormalizedPath</c> (present when the connection requested REQUIRE_FULL_FILE_PATH);
    /// falls back to the FileId map.
    /// </summary>
    private static unsafe string ResolvePath(CallbackContext context, CfNativeTypes.CF_CALLBACK_INFO* info)
    {
        string? normalized = HydrationDataProvider.ReadPcwstr(info->NormalizedPath);
        if (!string.IsNullOrEmpty(normalized))
        {
            return normalized;
        }

        return context.PathResolver.ResolvePath(info->FileId) ?? $"[UnknownFile:{info->FileId}]";
    }

    // =========================================================================
    // Native callback dispatchers
    // Signature: void(CF_CALLBACK_INFO* info, CF_CALLBACK_PARAMETERS* params)
    // The parameter block is a C union; it is received as IntPtr and reinterpreted
    // per callback type using the matching explicit-layout view.
    // =========================================================================

    /// <summary>Dispatches FETCH_DATA callbacks — the critical hydration path.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnFetchData(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            var p = (CfNativeTypes.CF_CALLBACK_PARAMETERS_FETCH_DATA*)parameters;
            string filePath = ResolvePath(context, info);

            var request = new FetchDataRequest(
                filePath,
                p->RequiredFileOffset,
                p->RequiredLength,
                p->OptionalFileOffset,
                p->OptionalLength,
                info->ConnectionKey,
                info->TransferKey,
                info->RequestKey,
                (long)info->CorrelationVector);

            context.Handler.FetchDataAsync(request, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnFetchData error: {ex.Message}");
        }
    }

    /// <summary>Dispatches FETCH_PLACEHOLDERS callbacks — on-demand directory population.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnFetchPlaceholders(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string dirPath = ResolvePath(context, info);
            context.Handler.FetchPlaceholdersAsync(dirPath, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnFetchPlaceholders error: {ex.Message}");
        }
    }

    /// <summary>Dispatches CANCEL_FETCH_DATA callbacks — a pending hydration was cancelled.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnCancelFetchData(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string filePath = ResolvePath(context, info);
            var transferKey = new TransferKey(info->TransferKey);
            context.Handler.CancelFetchDataAsync(filePath, transferKey)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnCancelFetchData error: {ex.Message}");
        }
    }

    /// <summary>Dispatches NOTIFY_DEHYDRATE callbacks — a file is about to be dehydrated.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnNotifyDehydrate(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string filePath = ResolvePath(context, info);
            context.Handler.NotifyDehydrateAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDehydrate error: {ex.Message}");
        }
    }

    /// <summary>Dispatches NOTIFY_DEHYDRATE_COMPLETION callbacks.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnNotifyDehydrateCompletion(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string filePath = ResolvePath(context, info);
            context.Handler.NotifyDehydrateCompletionAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDehydrateCompletion error: {ex.Message}");
        }
    }

    /// <summary>Dispatches NOTIFY_DELETE callbacks — a file under the sync root was deleted.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnNotifyDelete(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string filePath = ResolvePath(context, info);
            context.Handler.NotifyDeleteAsync(filePath).GetAwaiter().GetResult();

            context.PathResolver.RemoveMapping(info->FileId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDelete error: {ex.Message}");
        }
    }

    /// <summary>Dispatches NOTIFY_RENAME callbacks — a file was renamed or moved.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnNotifyRename(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string sourcePath = ResolvePath(context, info);

            // The new path is in the RENAME parameter block's TargetPath (PCWSTR).
            var p = (CfNativeTypes.CF_CALLBACK_PARAMETERS_RENAME*)parameters;
            string? targetPath = HydrationDataProvider.ReadPcwstr(p->TargetPath);

            context.Handler.NotifyRenameAsync(sourcePath, targetPath ?? string.Empty)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyRename error: {ex.Message}");
        }
    }

    /// <summary>Dispatches NOTIFY_FILE_OPEN_COMPLETION callbacks.</summary>
    [UnmanagedCallersOnly]
    private static unsafe void OnNotifyFileOpenCompletion(CfNativeTypes.CF_CALLBACK_INFO* info, IntPtr parameters)
    {
        try
        {
            CallbackContext? context = GetContext(info->CallbackContext);
            if (context is null) return;

            string filePath = ResolvePath(context, info);
            context.Handler.NotifyFileOpenCompletionAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyFileOpenCompletion error: {ex.Message}");
        }
    }
}
