using System.Collections.Concurrent;

namespace NasSync.CfApi;

/// <summary>
/// Bidirectional mapping between NTFS file IDs and file system paths.
/// Used to resolve file paths from the <c>FileId</c> field in native CfAPI callbacks,
/// since <c>CF_CALLBACK</c> only provides file IDs, not paths.
///
/// <para>
/// Populated after <c>CfCreatePlaceholders</c> by opening each created file
/// and querying its NTFS file index via <c>GetFileInformationByHandle</c>.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </para>
/// </summary>
public sealed class PathResolver
{
    private readonly ConcurrentDictionary<long, string> _fileIdToPath = new();
    private readonly ConcurrentDictionary<string, long> _pathToFileId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a bidirectional mapping between a file ID and its full path.
    /// </summary>
    /// <param name="fileId">The NTFS file index (from <c>GetFileInformationByHandle</c>).</param>
    /// <param name="fullPath">The full file system path of the file.</param>
    public void RegisterMapping(long fileId, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        _fileIdToPath[fileId] = fullPath;
        _pathToFileId[fullPath] = fileId;
    }

    /// <summary>
    /// Resolves a file path from an NTFS file ID.
    /// </summary>
    /// <param name="fileId">The NTFS file index to resolve.</param>
    /// <returns>The full file system path, or null if no mapping exists.</returns>
    public string? ResolvePath(long fileId)
    {
        return _fileIdToPath.TryGetValue(fileId, out string? path) ? path : null;
    }

    /// <summary>
    /// Resolves an NTFS file ID from a file path.
    /// </summary>
    /// <param name="fullPath">The full file system path to look up.</param>
    /// <returns>The NTFS file index, or null if no mapping exists.</returns>
    public long? ResolveFileId(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        return _pathToFileId.TryGetValue(fullPath, out long fileId) ? fileId : null;
    }

    /// <summary>
    /// Removes a mapping by file ID.
    /// </summary>
    /// <param name="fileId">The file ID to remove.</param>
    /// <returns>True if the mapping was removed; false if it didn't exist.</returns>
    public bool RemoveMapping(long fileId)
    {
        if (_fileIdToPath.TryRemove(fileId, out string? path))
        {
            _pathToFileId.TryRemove(path, out _);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes a mapping by file path.
    /// </summary>
    /// <param name="fullPath">The path to remove.</param>
    /// <returns>True if the mapping was removed; false if it didn't exist.</returns>
    public bool RemoveByPath(string fullPath)
    {
        if (_pathToFileId.TryRemove(fullPath, out long fileId))
        {
            _fileIdToPath.TryRemove(fileId, out _);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Clears all registered mappings.
    /// </summary>
    public void Clear()
    {
        _fileIdToPath.Clear();
        _pathToFileId.Clear();
    }

    /// <summary>
    /// Gets the number of registered mappings.
    /// </summary>
    public int Count => _fileIdToPath.Count;
}
