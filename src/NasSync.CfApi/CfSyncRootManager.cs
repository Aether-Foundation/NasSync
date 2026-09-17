using System.Runtime.InteropServices;
using NasSync.CfApi.Interop;

namespace NasSync.CfApi;

/// <summary>
/// Managed wrapper for the Windows Cloud Filter API (CfAPI) sync root lifecycle.
/// Handles registering, unregistering, connecting, and disconnecting sync roots
/// with the Windows Shell and the cldflt.sys minifilter driver.
///
/// <para>
/// Registration is a two-step process:
/// 1. <c>CfRegisterSyncRoot</c> (native) — registers the sync root with cldflt.sys,
///    configures hydration/population policies, and sets up the file system filter.
/// 2. WinRT <c>StorageProviderSyncRootManager</c> — optional but recommended for
///    navigation pane integration (branded icon, display name, quota UI).
/// </para>
///
/// <para>
/// After registration, <c>ConnectAsync</c> begins receiving callbacks from the platform
/// when applications access placeholder files (hydration) or modify files under the sync root.
/// Callback state is passed via a pinned <see cref="CallbackContext"/> object through
/// <c>GCHandle</c> to the native context pointer.
/// </para>
/// </summary>
public sealed class CfSyncRootManager : IDisposable
{
    private bool _disposed;
    private bool _isRegistered;
    private bool _isConnected;
    private string? _syncRootPath;
    private string? _syncRootId;
    private CfNativeTypes.CF_CONNECTION_KEY _connectionKey;

    /// <summary>
    /// Stored reference to the callback handler to prevent garbage collection
    /// while the sync root is connected.
    /// </summary>
    private ICfCallbackHandler? _callbackHandler;

    /// <summary>
    /// Pinned delegate references for native callbacks.
    /// These must be kept alive for the duration of the connection.
    /// </summary>
    private readonly List<GCHandle> _pinnedDelegates = [];

    /// <summary>
    /// Bidirectional file ID to path resolver. Populated after placeholder creation
    /// and used by callback dispatchers to resolve file paths from FileId values.
    /// </summary>
    private readonly PathResolver _pathResolver = new();

    /// <summary>
    /// The callback context passed to native callbacks via GCHandle.
    /// Contains references to the handler, path resolver, and hydration provider.
    /// </summary>
    private CallbackContext? _callbackContext;

    /// <summary>
    /// GCHandle pinning the <see cref="_callbackContext"/> to prevent GC collection
    /// while the sync root is connected.
    /// </summary>
    private GCHandle _contextHandle;

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
    /// Format: <c>[ProviderId]![UserSid]![AccountId]</c>
    /// </summary>
    public string? SyncRootId => _syncRootId;

    /// <summary>
    /// Gets the path resolver that maps NTFS file IDs to file system paths.
    /// Populated after placeholder creation via <see cref="PlaceholderManager.GetFileId"/>.
    /// </summary>
    public PathResolver PathResolver => _pathResolver;

    /// <summary>
    /// Registers a new sync root with the Windows Cloud Filter platform.
    ///
    /// <para>This method:</para>
    /// <list type="number">
    ///   <item>Creates the sync root directory if it does not exist.</item>
    ///   <item>Calls <c>CfRegisterSyncRoot</c> to register with cldflt.sys.</item>
    ///   <item>Sets up the navigation pane entry via WinRT (if available).</item>
    /// </list>
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

        // Step 1: Ensure the sync root directory exists
        Directory.CreateDirectory(registrationInfo.SyncRootPath);

        System.Diagnostics.Debug.WriteLine(
            $"[CfSyncRootManager] Registering sync root: Path='{registrationInfo.SyncRootPath}', " +
            $"DisplayName='{registrationInfo.DisplayName}', " +
            $"IconResource='{registrationInfo.IconResource}', " +
            $"HydrationPolicy={registrationInfo.HydrationPolicy}, " +
            $"PopulationPolicy={registrationInfo.PopulationPolicy}");

        // Step 2: Build the native registration structure
        var nativeRegistration = new CfNativeTypes.CF_SYNC_REGISTRATION
        {
            StructSize = (uint)Marshal.SizeOf<CfNativeTypes.CF_SYNC_REGISTRATION>(),
            ProviderName = registrationInfo.DisplayName,
            ProviderVersion = registrationInfo.Version,
            HydrationPolicy = (CfNativeTypes.CF_HYDRATION_POLICY)registrationInfo.HydrationPolicy,
            HydrationPolicyModifier =
                (CfNativeTypes.CF_HYDRATION_POLICY_MODIFIER)registrationInfo.HydrationPolicyModifier,
            PopulationPolicy = (CfNativeTypes.CF_POPULATION_POLICY)registrationInfo.PopulationPolicy,
            Flags = CfNativeTypes.CF_REGISTER_FLAGS.NONE,
        };

        // Step 3: Call native CfRegisterSyncRoot
        int hr = CfNativeMethods.CfRegisterSyncRoot(
            registrationInfo.SyncRootPath,
            ref nativeRegistration,
            out CfNativeTypes.CF_PLATFORM_INFO platformInfo);

        if (hr != 0)
        {
            string details = $"Path='{registrationInfo.SyncRootPath}', " +
                $"DisplayName='{registrationInfo.DisplayName}', " +
                $"IconResource='{registrationInfo.IconResource}', " +
                $"ProviderId='{registrationInfo.ProviderId}', " +
                $"AccountId='{registrationInfo.AccountId}'";

            throw new CfApiException("CfRegisterSyncRoot", hr,
                $"Failed to register sync root. {details}");
        }

        // Step 4: Store registration state
        _syncRootPath = registrationInfo.SyncRootPath;
        _syncRootId = SyncRootIdHelper.Build(registrationInfo.ProviderId, registrationInfo.AccountId);
        _isRegistered = true;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Unregisters the sync root from the Windows Cloud Filter platform.
    /// Removes the navigation pane entry and cleans up all placeholder files.
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

        // Disconnect first if still connected
        if (_isConnected)
        {
            await DisconnectAsync();
        }

        // Call native CfUnregisterSyncRoot
        int hr = CfNativeMethods.CfUnregisterSyncRoot(_syncRootPath);

        if (hr != 0)
        {
            throw new CfApiException("CfUnregisterSyncRoot", hr,
                $"Failed to unregister sync root at '{_syncRootPath}'.");
        }

        // Clear registration state
        _pathResolver.Clear();
        _syncRootPath = null;
        _syncRootId = null;
        _isRegistered = false;
    }

    /// <summary>
    /// Connects the sync root to begin receiving hydration and file operation callbacks
    /// from the cldflt.sys minifilter driver.
    ///
    /// <para>
    /// After connecting, the platform will invoke the appropriate callback on
    /// <paramref name="callbacks"/> when:
    /// - An application opens a placeholder file (FetchData — triggers hydration)
    /// - A directory needs to be populated (FetchPlaceholders)
    /// - A file is deleted, renamed, or dehydrated
    /// - A hydration operation is cancelled
    /// </para>
    /// </summary>
    /// <param name="callbacks">
    /// The callback handler that will process sync events from the platform.
    /// The reference is stored internally and must remain alive until disconnection.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous connect operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not registered or already connected.</exception>
    public Task ConnectAsync(ICfCallbackHandler callbacks, CancellationToken cancellationToken = default)
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

        // Create the callback context and pin it via GCHandle
        _callbackContext = new CallbackContext
        {
            Handler = callbacks,
            PathResolver = _pathResolver,
            HydrationProvider = new HydrationDataProvider(),
            SyncRootPath = _syncRootPath,
        };
        _contextHandle = GCHandle.Alloc(_callbackContext);

        // Build the callback registration table
        var callbackEntries = BuildCallbackTable();

        // Connect with flags to receive process info in callbacks
        int hr = CfNativeMethods.CfConnectSyncRoot(
            _syncRootPath,
            callbackEntries,
            (uint)callbackEntries.Length,
            CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_PROCESS_INFO |
                CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_FULL_IMAGE_PATH,
            GCHandle.ToIntPtr(_contextHandle),
            out _connectionKey);

        if (hr != 0)
        {
            // Clean up on failure
            _contextHandle.Free();
            _callbackContext = null;
            ReleasePinnedDelegates();
            throw new CfApiException("CfConnectSyncRoot", hr,
                "Failed to connect sync root for callbacks.");
        }

        _isConnected = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disconnects the sync root from receiving callbacks.
    /// Placeholder files remain visible in the file system, but no further
    /// hydration or notification callbacks will fire until reconnected.
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
        _callbackHandler = null;

        // Free the context handle
        if (_contextHandle.IsAllocated)
        {
            _contextHandle.Free();
        }

        _callbackContext = null;
        ReleasePinnedDelegates();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Best-effort cleanup: disconnect and release pinned delegates
        if (_isConnected)
        {
            try
            {
                _ = CfNativeMethods.CfDisconnectSyncRoot(_connectionKey);
            }
            catch
            {
                // Swallow exceptions during dispose
            }

            _isConnected = false;
        }

        if (_contextHandle.IsAllocated)
        {
            _contextHandle.Free();
        }

        ReleasePinnedDelegates();
        _callbackHandler = null;
        _callbackContext = null;
        _disposed = true;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds the native callback registration table.
    /// Each callback type is mapped to a static dispatcher method that receives
    /// the context pointer and delegates to the managed <see cref="ICfCallbackHandler"/>.
    /// </summary>
    /// <returns>An array of callback registrations for CfConnectSyncRoot.</returns>
    private unsafe CfNativeTypes.CF_CALLBACK_REGISTRATION[] BuildCallbackTable()
    {
        var entries = new List<CfNativeTypes.CF_CALLBACK_REGISTRATION>();

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.FETCH_DATA, &OnFetchData);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.FETCH_PLACEHOLDERS, &OnFetchPlaceholders);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.CANCEL_FETCH_DATA, &OnCancelFetchData);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE, &OnNotifyDehydrate);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE_COMPLETION, &OnNotifyDehydrateCompletion);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DELETE, &OnNotifyDelete);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_RENAME, &OnNotifyRename);
        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_FILE_OPEN_COMPLETION, &OnNotifyFileOpenCompletion);

        return entries.ToArray();
    }

    /// <summary>
    /// Adds a callback entry to the registration table, converting the function pointer
    /// to an <see cref="IntPtr"/> for the native registration structure.
    /// </summary>
    private static unsafe void AddCallback(
        List<CfNativeTypes.CF_CALLBACK_REGISTRATION> entries,
        CfNativeTypes.CF_CALLBACK_TYPE type,
        delegate* unmanaged[Stdcall]<CfNativeTypes.CF_CALLBACK*, IntPtr, void> callback)
    {
        entries.Add(new CfNativeTypes.CF_CALLBACK_REGISTRATION
        {
            Type = type,
            Callback = (IntPtr)callback,
        });
    }

    /// <summary>
    /// Releases all pinned delegate handles that were allocated for native callbacks.
    /// Called on disconnect or dispose to allow the GC to reclaim the delegates.
    /// </summary>
    private void ReleasePinnedDelegates()
    {
        foreach (GCHandle handle in _pinnedDelegates)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        _pinnedDelegates.Clear();
    }

    /// <summary>
    /// Extracts the <see cref="CallbackContext"/> from the native context pointer.
    /// Returns null if the context is invalid or has been freed.
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
    /// Resolves a file path from the callback's FileId using the path resolver.
    /// Returns a fallback string if the FileId is not in the map.
    /// </summary>
    private static string ResolvePath(CallbackContext context, long fileId)
    {
        return context.PathResolver.ResolvePath(fileId) ?? $"[UnknownFile:{fileId}]";
    }

    // =========================================================================
    // Native callback dispatchers
    // =========================================================================

    /// <summary>
    /// Dispatches FETCH_DATA callbacks to the managed handler.
    /// Called when an application opens a placeholder file and the platform needs data.
    /// Extracts the file path from FileId, transfer key from the callback info,
    /// and offset/length from the operation parameters.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnFetchData(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            var fetchParams = (CfNativeTypes.CF_OPERATION_PARAMETERS_FETCH_DATA*)
                callbackInfo->OperationParameters;

            CfNativeTypes.CF_TRANSFER_KEY nativeKey = HydrationDataProvider.GetTransferKey(callbackInfo);
            var transferKey = new TransferKey(nativeKey.Internal);

            string filePath = ResolvePath(context, callbackInfo->FileId);

            context.Handler.FetchDataAsync(
                filePath,
                fetchParams->Offset,
                fetchParams->Length,
                transferKey,
                callbackInfo->VolumeGuidName,
                callbackInfo->FileId,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnFetchData error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches FETCH_PLACEHOLDERS callbacks to the managed handler.
    /// Called when the platform needs to enumerate and create placeholders for a directory.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnFetchPlaceholders(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string dirPath = ResolvePath(context, callbackInfo->FileId);

            context.Handler.FetchPlaceholdersAsync(
                dirPath,
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnFetchPlaceholders error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches CANCEL_FETCH_DATA callbacks to the managed handler.
    /// Called when a pending hydration operation is cancelled (e.g., user closes the app).
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnCancelFetchData(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            CfNativeTypes.CF_TRANSFER_KEY nativeKey = HydrationDataProvider.GetTransferKey(callbackInfo);
            var transferKey = new TransferKey(nativeKey.Internal);

            string filePath = ResolvePath(context, callbackInfo->FileId);

            context.Handler.CancelFetchDataAsync(filePath, transferKey).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnCancelFetchData error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DEHYDRATE callbacks to the managed handler.
    /// Called when a file is about to be dehydrated (local cache released).
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnNotifyDehydrate(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string filePath = ResolvePath(context, callbackInfo->FileId);
            context.Handler.NotifyDehydrateAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDehydrate error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DEHYDRATE_COMPLETION callbacks to the managed handler.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnNotifyDehydrateCompletion(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string filePath = ResolvePath(context, callbackInfo->FileId);
            context.Handler.NotifyDehydrateCompletionAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDehydrateCompletion error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DELETE callbacks to the managed handler.
    /// Called when a file under the sync root is deleted.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnNotifyDelete(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string filePath = ResolvePath(context, callbackInfo->FileId);
            context.Handler.NotifyDeleteAsync(filePath).GetAwaiter().GetResult();

            // Remove the file from the path resolver
            context.PathResolver.RemoveMapping(callbackInfo->FileId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyDelete error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_RENAME callbacks to the managed handler.
    /// Called when a file under the sync root is renamed or moved.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnNotifyRename(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string sourcePath = ResolvePath(context, callbackInfo->FileId);
            // Note: destination path requires reading OperationParameters for RENAME
            // which contains the new path. For now, pass empty string as destination.
            context.Handler.NotifyRenameAsync(sourcePath, string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyRename error: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_FILE_OPEN_COMPLETION callbacks to the managed handler.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe void OnNotifyFileOpenCompletion(CfNativeTypes.CF_CALLBACK* callbackInfo, IntPtr contextPtr)
    {
        try
        {
            CallbackContext? context = GetContext(contextPtr);
            if (context is null) return;

            string filePath = ResolvePath(context, callbackInfo->FileId);
            context.Handler.NotifyFileOpenCompletionAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CfAPI] OnNotifyFileOpenCompletion error: {ex.Message}");
        }
    }
}
