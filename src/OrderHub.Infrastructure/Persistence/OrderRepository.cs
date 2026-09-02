using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Interfaces;
using OrderHub.Domain;

namespace OrderHub.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the order repository. Queries include the
/// join-entity graph needed by the application services.
/// </summary>
public class OrderRepository : IOrderRepository
{
    private readonly SmtDbContext _context;

    public OrderRepository(SmtDbContext context) => _context = context;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Orders
            .Include(o => o.OrderBoards)
                .ThenInclude(ob => ob.Board)
                    .ThenInclude(b => b.BoardComponents)
                        .ThenInclude(bc => bc.Component)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(o => o.Name.ToLower().Contains(searchTerm.ToLower()));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(o => o.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default) =>
        await _context.Orders.AddAsync(order, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public Task DeleteAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Remove(order);
        return Task.CompletedTask;
    }

    public void MarkModified(Order order, Guid originalRowVersion)
    {
        _context.Entry(order).Property(o => o.RowVersion).OriginalValue = originalRowVersion;
    }
}
