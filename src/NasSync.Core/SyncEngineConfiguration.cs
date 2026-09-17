namespace NasSync.Core;

/// <summary>
/// Configuration for a <see cref="SyncEngine"/> instance.
/// Contains all parameters needed to register a sync root and connect to a data source.
/// </summary>
/// <param name="EngineId">Unique identifier for this sync engine instance.</param>
/// <param name="ProviderId">The storage provider identifier for sync root registration (e.g., "NasSync").</param>
/// <param name="AccountId">The account identifier for this NAS connection.</param>
/// <param name="SyncRootPath">The local file system path where placeholder files will be created.</param>
/// <param name="DisplayName">The display name shown in File Explorer's navigation pane.</param>
/// <param name="IconResource">Path to the icon resource for the navigation pane entry.</param>
public record SyncEngineConfiguration(
    string EngineId,
    string ProviderId,
    string AccountId,
    string SyncRootPath,
    string DisplayName,
    string IconResource);
