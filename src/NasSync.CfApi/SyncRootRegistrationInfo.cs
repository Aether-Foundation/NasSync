namespace NasSync.CfApi;

/// <summary>
/// Configuration for registering a sync root with the Windows Cloud Filter API.
/// Maps to the parameters of CfRegisterSyncRoot and StorageProviderSyncRootInfo.
/// </summary>
public sealed class SyncRootRegistrationInfo
{
    /// <summary>
    /// Gets or sets the storage provider identifier (e.g., "NasSync").
    /// This is part of the sync root ID: [ProviderId]![UserSid]![AccountId].
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// Gets or sets the account identifier for this NAS connection.
    /// Allows multiple NAS accounts under the same provider.
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Gets or sets the local file system path that serves as the sync root.
    /// All placeholder files will be created under this directory.
    /// </summary>
    public required string SyncRootPath { get; init; }

    /// <summary>
    /// Gets or sets the display name shown in File Explorer's navigation pane.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets or sets the path to the icon resource for the navigation pane entry.
    /// Format: "path\to\file.dll,-resourceId" or "path\to\icon.ico".
    /// </summary>
    public required string IconResource { get; init; }

    /// <summary>
    /// Gets or sets the primary hydration policy for placeholder files.
    /// Determines how files are downloaded when accessed.
    /// Default is Progressive (download as needed, stream data).
    /// </summary>
    public HydrationPolicy HydrationPolicy { get; init; } = HydrationPolicy.Progressive;

    /// <summary>
    /// Gets or sets the hydration policy modifier.
    /// Default allows automatic dehydration when disk space is low.
    /// </summary>
    public HydrationPolicyModifier HydrationPolicyModifier { get; init; } =
        HydrationPolicyModifier.AutoDehydrationAllowed;

    /// <summary>
    /// Gets or sets the population policy for the sync root.
    /// Determines how placeholder files are initially created.
    /// Default is Full (all placeholders created on registration).
    /// </summary>
    public PopulationPolicy PopulationPolicy { get; init; } = PopulationPolicy.Full;

    /// <summary>
    /// Gets or sets the version string for this sync root registration.
    /// Used to detect when a sync root needs migration.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Gets or sets whether users can pin files for offline availability.
    /// </summary>
    public bool AllowPinning { get; init; } = true;

    /// <summary>
    /// Gets or sets whether sibling sync roots should be shown as a group
    /// under the main provider node in File Explorer.
    /// </summary>
    public bool ShowSiblingsAsGroup { get; init; } = false;
}

/// <summary>
/// Primary hydration policy options for cloud files.
/// Ordered by aggressiveness: Partial &lt; Progressive &lt; Full &lt; AlwaysFull.
/// </summary>
public enum HydrationPolicy
{
    /// <summary>Only download the byte ranges requested by the application.</summary>
    Partial = 0,

    /// <summary>Download data progressively as the application reads the file.</summary>
    Progressive = 1,

    /// <summary>Download the entire file on first access.</summary>
    Full = 2,

    /// <summary>File is always fully hydrated and never dehydrated.</summary>
    AlwaysFull = 3
}

/// <summary>
/// Modifiers that adjust the behavior of the primary hydration policy.
/// </summary>
[Flags]
public enum HydrationPolicyModifier
{
    /// <summary>No modifier applied.</summary>
    None = 0,

    /// <summary>Allow the system to automatically dehydrate files when disk space is low.</summary>
    AutoDehydrationAllowed = 1,

    /// <summary>Allow full hydration restart if a previous download was interrupted.</summary>
    AllowFullRestartHydration = 2
}

/// <summary>
/// Population policy options for the sync root.
/// Determines how placeholder files are initially populated.
/// </summary>
public enum PopulationPolicy
{
    /// <summary>Create all placeholders immediately on registration.</summary>
    Full = 0,

    /// <summary>Always keep all placeholders populated.</summary>
    AlwaysFull = 1
}
