using OrderHub.Domain;

namespace OrderHub.Application.Interfaces;

/// <summary>
/// Exception thrown when a requested entity does not exist.
/// </summary>
public class EntityNotFoundException(Guid id, string entityName)
    : Exception($"{entityName} with id '{id}' was not found.")
{
    public Guid Id { get; } = id;
    public string EntityName { get; } = entityName;
}

/// <summary>
/// Intention-revealing persistence interface for orders.
/// Implemented in Infrastructure over EF Core directly (no generic repository).
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Order?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Order order, CancellationToken cancellationToken = default);
}
