using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Interfaces;
using OrderHub.Domain;

namespace OrderHub.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the component repository.
/// </summary>
public class ComponentRepository : IComponentRepository
{
    private readonly SmtDbContext _context;

    public ComponentRepository(SmtDbContext context) => _context = context;

    public Task<Component?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Components.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Component> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Components.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(c => c.Name.ToLower().Contains(searchTerm.ToLower()));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> AllExistAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var found = await _context.Components
            .CountAsync(c => idList.Contains(c.Id), cancellationToken);
        return found == idList.Count;
    }

    public async Task AddAsync(Component component, CancellationToken cancellationToken = default) =>
        await _context.Components.AddAsync(component, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public Task DeleteAsync(Component component, CancellationToken cancellationToken = default)
    {
        _context.Components.Remove(component);
        return Task.CompletedTask;
    }

    public void MarkModified(Component component, Guid originalRowVersion)
    {
        _context.Entry(component).Property(c => c.RowVersion).OriginalValue = originalRowVersion;
    }
}
