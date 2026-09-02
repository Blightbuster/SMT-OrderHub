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
}
