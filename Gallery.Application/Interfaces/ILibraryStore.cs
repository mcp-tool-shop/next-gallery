using Gallery.Domain.Models;

namespace Gallery.Application.Interfaces;

/// <summary>
/// Manages library folders (watched directories).
/// </summary>
public interface ILibraryStore
{
    Task<IReadOnlyList<LibraryFolder>> GetAllAsync(CancellationToken ct = default);
    Task<LibraryFolder?> GetByPathAsync(string path, CancellationToken ct = default);
    Task<LibraryFolder> AddAsync(string path, CancellationToken ct = default);
    Task RemoveAsync(long id, CancellationToken ct = default);
    Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default);
    Task UpdateLastScannedAsync(long id, DateTimeOffset scannedAt, CancellationToken ct = default);
}
