namespace NasSync.Adapters;

/// <summary>
/// Factory interface for creating NAS adapter instances.
/// Implementations are registered at startup and resolved by adapter type name.
/// </summary>
public interface INasAdapterFactory
{
    /// <summary>
    /// Gets the unique type identifier for adapters created by this factory
    /// (e.g., "smb", "webdav", "synology-api").
    /// </summary>
    string AdapterType { get; }

    /// <summary>
    /// Gets the human-readable display name for this adapter type.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Creates a new NAS adapter instance with the specified configuration.
    /// </summary>
    /// <param name="configuration">Adapter-specific configuration dictionary.</param>
    /// <returns>A configured but not yet connected NAS adapter instance.</returns>
    INasAdapter CreateAdapter(IReadOnlyDictionary<string, string> configuration);

    /// <summary>
    /// Validates the provided configuration without creating an adapter.
    /// </summary>
    /// <param name="configuration">Adapter-specific configuration dictionary.</param>
    /// <returns>A validation result indicating success or listing errors.</returns>
    ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, string> configuration);
}

/// <summary>
/// Result of a configuration validation operation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the list of validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new([]);

    /// <summary>
    /// Creates a failed validation result with the specified errors.
    /// </summary>
    /// <param name="errors">List of error messages describing validation failures.</param>
    public static ValidationResult Failure(IReadOnlyList<string> errors) => new(errors);

    private ValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors;
    }
}
