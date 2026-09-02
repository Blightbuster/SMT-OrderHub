using OrderHub.Domain;

namespace OrderHub.Application.Interfaces;

/// <summary>
/// Intention-revealing persistence interface for boards.
/// </summary>
public interface IBoardRepository
{
    Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Board?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Board> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(Board board, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Board board, CancellationToken cancellationToken = default);

    /// <summary>Checks whether all board ids exist (for reference validation).</summary>
    Task<bool> AllExistAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the entity as modified with the client-supplied RowVersion as the
    /// original value, enabling the optimistic concurrency check on save.
    /// </summary>
    void MarkModified(Board board, Guid originalRowVersion);

    /// <summary>
    /// Loads the current database state without change tracking — used to build
    /// the HTTP 409 Conflict payload after a failed concurrent update.
    /// </summary>
    Task<Board?> GetCurrentStateAsync(Guid id, CancellationToken cancellationToken = default);
}
