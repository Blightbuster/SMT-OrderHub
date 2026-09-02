using OrderHub.Domain;

namespace OrderHub.Application.Interfaces;

/// <summary>
/// Intention-revealing persistence interface for components.
/// </summary>
public interface IComponentRepository
{
    Task<Component?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Component> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> AllExistAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task AddAsync(Component component, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Component component, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the entity as modified with the client-supplied RowVersion as the
    /// original value, enabling the optimistic concurrency check on save.
    /// </summary>
    void MarkModified(Component component, Guid originalRowVersion);
}
