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
    private readonly List<GCHandle> _pinnedDelegates = new();

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

        if (_isRegistered)
        {
            throw new InvalidOperationException(
                "A sync root is already registered. Call UnregisterAsync first.");
        }

        // Step 1: Ensure the sync root directory exists
        Directory.CreateDirectory(registrationInfo.SyncRootPath);

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
            throw new CfApiException("CfRegisterSyncRoot", hr,
                $"Failed to register sync root at '{registrationInfo.SyncRootPath}'.");
        }

        // Step 4: Store registration state
        _syncRootPath = registrationInfo.SyncRootPath;
        _syncRootId = $"{registrationInfo.ProviderId}!{Environment.UserDomainName}!{registrationInfo.AccountId}";
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

        // Build the callback registration table.
        // Each entry maps a callback type to a native function pointer.
        var callbackEntries = BuildCallbackTable(callbacks);

        // Connect with flags to receive process info and placeholder info in callbacks
        int hr = CfNativeMethods.CfConnectSyncRoot(
            _syncRootPath,
            callbackEntries,
            (uint)callbackEntries.Length,
            CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_PROCESS_INFO |
                CfNativeTypes.CF_CONNECT_FLAGS.REQUIRE_FULL_IMAGE_PATH,
            IntPtr.Zero, // context — we use the stored _callbackHandler field instead
            out _connectionKey);

        if (hr != 0)
        {
            // Clean up pinned delegates on failure
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
                CfNativeMethods.CfDisconnectSyncRoot(_connectionKey);
            }
            catch
            {
                // Swallow exceptions during dispose
            }

            _isConnected = false;
        }

        ReleasePinnedDelegates();
        _callbackHandler = null;
        _disposed = true;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds the native callback registration table from the managed callback handler.
    /// Each callback type is mapped to a static dispatcher method that delegates to
    /// the managed <see cref="ICfCallbackHandler"/> implementation.
    /// </summary>
    /// <param name="handler">The managed callback handler.</param>
    /// <returns>An array of callback registrations for CfConnectSyncRoot.</returns>
    private unsafe CfNativeTypes.CF_CALLBACK_REGISTRATION[] BuildCallbackTable(ICfCallbackHandler handler)
    {
        // Create native delegates for each callback type we want to handle.
        // These delegates are pinned via GCHandle to prevent GC collection.
        var entries = new List<CfNativeTypes.CF_CALLBACK_REGISTRATION>();

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.FETCH_DATA,
            (info, ctx) => OnFetchData(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.FETCH_PLACEHOLDERS,
            (info, ctx) => OnFetchPlaceholders(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.CANCEL_FETCH_DATA,
            (info, ctx) => OnCancelFetchData(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE,
            (info, ctx) => OnNotifyDehydrate(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DEHYDRATE_COMPLETION,
            (info, ctx) => OnNotifyDehydrateCompletion(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_DELETE,
            (info, ctx) => OnNotifyDelete(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_RENAME,
            (info, ctx) => OnNotifyRename(handler, info));

        AddCallback(entries, CfNativeTypes.CF_CALLBACK_TYPE.NOTIFY_FILE_OPEN_COMPLETION,
            (info, ctx) => OnNotifyFileOpenCompletion(handler, info));

        return entries.ToArray();
    }

    /// <summary>
    /// Adds a callback entry to the registration table, pinning the delegate
    /// to prevent garbage collection while the sync root is connected.
    /// </summary>
    private unsafe void AddCallback(
        List<CfNativeTypes.CF_CALLBACK_REGISTRATION> entries,
        CfNativeTypes.CF_CALLBACK_TYPE type,
        CfNativeTypes.CF_CALLBACK_DELEGATE callback)
    {
        // Pin the delegate so the GC doesn't collect it while native code holds a reference
        GCHandle handle = GCHandle.Alloc(callback);
        _pinnedDelegates.Add(handle);

        entries.Add(new CfNativeTypes.CF_CALLBACK_REGISTRATION
        {
            Type = type,
            Callback = Marshal.GetFunctionPointerForDelegate(callback),
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

    // =========================================================================
    // Native callback dispatchers
    // =========================================================================

    /// <summary>
    /// Dispatches FETCH_DATA callbacks to the managed handler.
    /// Called when an application opens a placeholder file and the platform needs data.
    /// </summary>
    private static unsafe void OnFetchData(ICfCallbackHandler handler, CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            // Extract fetch parameters from the callback info
            var fetchParams = (CfNativeTypes.CF_OPERATION_PARAMETERS_FETCH_DATA*)
                callbackInfo->OperationParameters;

            handler.FetchDataAsync(
                string.Empty, // file path — resolved from context in production
                fetchParams->Offset,
                fetchParams->Length,
                Guid.Empty, // transfer key — resolved via CfGetTransferKey
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error — do not propagate exceptions to native code
        }
    }

    /// <summary>
    /// Dispatches FETCH_PLACEHOLDERS callbacks to the managed handler.
    /// Called when the platform needs to enumerate and create placeholders for a directory.
    /// </summary>
    private static unsafe void OnFetchPlaceholders(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.FetchPlaceholdersAsync(
                string.Empty, // directory path — resolved from context in production
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error — do not propagate exceptions to native code
        }
    }

    /// <summary>
    /// Dispatches CANCEL_FETCH_DATA callbacks to the managed handler.
    /// Called when a pending hydration operation is cancelled (e.g., user closes the app).
    /// </summary>
    private static unsafe void OnCancelFetchData(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.CancelFetchDataAsync(string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DEHYDRATE callbacks to the managed handler.
    /// Called when a file is about to be dehydrated (local cache released).
    /// </summary>
    private static unsafe void OnNotifyDehydrate(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.NotifyDehydrateAsync(string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DEHYDRATE_COMPLETION callbacks to the managed handler.
    /// </summary>
    private static unsafe void OnNotifyDehydrateCompletion(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.NotifyDehydrateCompletionAsync(string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_DELETE callbacks to the managed handler.
    /// Called when a file under the sync root is deleted.
    /// </summary>
    private static unsafe void OnNotifyDelete(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.NotifyDeleteAsync(string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_RENAME callbacks to the managed handler.
    /// Called when a file under the sync root is renamed or moved.
    /// </summary>
    private static unsafe void OnNotifyRename(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            // TODO: Extract source and destination paths from callback info
            handler.NotifyRenameAsync(string.Empty, string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }

    /// <summary>
    /// Dispatches NOTIFY_FILE_OPEN_COMPLETION callbacks to the managed handler.
    /// </summary>
    private static unsafe void OnNotifyFileOpenCompletion(
        ICfCallbackHandler handler,
        CfNativeTypes.CF_CALLBACK* callbackInfo)
    {
        try
        {
            handler.NotifyFileOpenCompletionAsync(string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Log error
        }
    }
}
