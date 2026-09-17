// WinRT navigation pane integration — requires Windows App SDK and Visual Studio build tools.
// To enable: uncomment WinRT packages in NasSync.CfApi.csproj and define WINRT_ENABLED.
#if WINRT_ENABLED

using Windows.Storage;
using Windows.Storage.Provider;

namespace NasSync.CfApi;

/// <summary>
/// Provides WinRT-based sync root registration for File Explorer navigation pane integration.
/// Uses <c>StorageProviderSyncRootManager</c> to create a branded entry in the navigation pane
/// with custom icon, display name, and quota UI support.
///
/// <para>
/// This is complementary to the native <c>CfRegisterSyncRoot</c> registration:
/// - <c>CfRegisterSyncRoot</c> registers with cldflt.sys (file system filter layer)
/// - <c>StorageProviderSyncRootManager.Register</c> registers with the Shell (navigation pane UI)
///
/// Both registrations should be performed for a complete cloud storage provider experience.
/// </para>
///
/// <para>
/// Requires Windows 10 version 1709+ (Desktop Extension SDK 10.0.16299.0).
/// </para>
/// </summary>
public static class WinRtRegistration
{
    /// <summary>
    /// Registers a sync root with the Windows Shell for navigation pane integration.
    /// This creates a branded entry with the provider's icon and display name.
    /// </summary>
    /// <param name="registrationInfo">The sync root registration configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous registration operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the OS does not support cloud file sync roots.
    /// </exception>
    public static async Task RegisterAsync(
        SyncRootRegistrationInfo registrationInfo,
        CancellationToken cancellationToken = default)
    {
        // Verify the OS supports cloud file sync roots
        if (!StorageProviderSyncRootManager.IsSupported())
        {
            throw new InvalidOperationException(
                "This operating system does not support cloud file sync roots. " +
                "Windows 10 version 1709 or later is required.");
        }

        // Get the sync root folder as an IStorageFolder
        StorageFolder syncRootFolder = await StorageFolder.GetFolderFromPathAsync(
            registrationInfo.SyncRootPath);

        // Build the sync root ID in the format: [ProviderId]![UserSid]![AccountId]
        string syncRootId = BuildSyncRootId(registrationInfo.ProviderId, registrationInfo.AccountId);

        // Configure the sync root info
        var syncRootInfo = new StorageProviderSyncRootInfo
        {
            Id = syncRootId,
            Path = syncRootFolder,
            DisplayNameResource = registrationInfo.DisplayName,
            IconResource = registrationInfo.IconResource,
            HydrationPolicy = MapHydrationPolicy(registrationInfo.HydrationPolicy),
            HydrationPolicyModifier = MapHydrationPolicyModifier(registrationInfo.HydrationPolicyModifier),
            PopulationPolicy = MapPopulationPolicy(registrationInfo.PopulationPolicy),
            ProtectionMode = StorageProviderProtectionMode.Personal,
            Version = registrationInfo.Version,
            AllowPinning = registrationInfo.AllowPinning,
            ShowSiblingsAsGroup = registrationInfo.ShowSiblingsAsGroup,
        };

        // Register with the Shell — this creates the navigation pane entry
        StorageProviderSyncRootManager.Register(syncRootInfo);
    }

    /// <summary>
    /// Unregisters a sync root from the Windows Shell, removing the navigation pane entry.
    /// </summary>
    /// <param name="providerId">The storage provider identifier.</param>
    /// <param name="accountId">The account identifier.</param>
    public static void Unregister(string providerId, string accountId)
    {
        string syncRootId = BuildSyncRootId(providerId, accountId);
        StorageProviderSyncRootManager.Unregister(syncRootId);
    }

    /// <summary>
    /// Gets information about all currently registered sync roots.
    /// Useful for detecting existing registrations and managing multi-account scenarios.
    /// </summary>
    /// <returns>A read-only list of registered sync root information.</returns>
    public static IReadOnlyList<StorageProviderSyncRootInfo> GetCurrentSyncRoots()
    {
        return StorageProviderSyncRootManager.GetCurrentSyncRoots().ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks whether a sync root with the specified provider and account IDs is already registered.
    /// </summary>
    /// <param name="providerId">The storage provider identifier.</param>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>True if a sync root with the given IDs is registered; otherwise false.</returns>
    public static bool IsRegistered(string providerId, string accountId)
    {
        string syncRootId = BuildSyncRootId(providerId, accountId);

        try
        {
            StorageProviderSyncRootManager.GetSyncRootInformationForId(syncRootId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds a sync root ID string from provider and account identifiers.
    /// Delegates to <see cref="SyncRootIdHelper"/> for consistent ID construction.
    /// </summary>
    private static string BuildSyncRootId(string providerId, string accountId)
    {
        return SyncRootIdHelper.Build(providerId, accountId);
    }

    /// <summary>
    /// Maps the managed hydration policy enum to the WinRT enum.
    /// </summary>
    private static StorageProviderHydrationPolicy MapHydrationPolicy(HydrationPolicy policy) => policy switch
    {
        HydrationPolicy.Partial => StorageProviderHydrationPolicy.Partial,
        HydrationPolicy.Progressive => StorageProviderHydrationPolicy.Progressive,
        HydrationPolicy.Full => StorageProviderHydrationPolicy.Full,
        HydrationPolicy.AlwaysFull => StorageProviderHydrationPolicy.AlwaysFull,
        _ => StorageProviderHydrationPolicy.Progressive,
    };

    /// <summary>
    /// Maps the managed hydration policy modifier to the WinRT enum.
    /// </summary>
    private static StorageProviderHydrationPolicyModifier MapHydrationPolicyModifier(
        HydrationPolicyModifier modifier)
    {
        var result = StorageProviderHydrationPolicyModifier.None;

        if (modifier.HasFlag(HydrationPolicyModifier.AutoDehydrationAllowed))
        {
            result |= StorageProviderHydrationPolicyModifier.AutoDehydrationAllowed;
        }

        if (modifier.HasFlag(HydrationPolicyModifier.AllowFullRestartHydration))
        {
            result |= StorageProviderHydrationPolicyModifier.AllowFullRestartHydration;
        }

        return result;
    }

    /// <summary>
    /// Maps the managed population policy enum to the WinRT enum.
    /// </summary>
    private static StorageProviderPopulationPolicy MapPopulationPolicy(PopulationPolicy policy) => policy switch
    {
        PopulationPolicy.Full => StorageProviderPopulationPolicy.Full,
        PopulationPolicy.AlwaysFull => StorageProviderPopulationPolicy.AlwaysFull,
        _ => StorageProviderPopulationPolicy.Full,
    };
}

#endif // WINRT_ENABLED
