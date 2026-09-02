using Microsoft.EntityFrameworkCore;
using OrderHub.Application.Interfaces;
using OrderHub.Domain;

namespace OrderHub.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the board repository.
/// </summary>
public class BoardRepository : IBoardRepository
{
    private readonly SmtDbContext _context;

    public BoardRepository(SmtDbContext context) => _context = context;

    public Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Boards.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<Board?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Boards
            .Include(b => b.BoardComponents)
                .ThenInclude(bc => bc.Component)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Board> Items, int TotalCount)> SearchAsync(
        string? searchTerm, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Boards.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(b => b.Name.ToLower().Contains(searchTerm.ToLower()));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(b => b.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task AddAsync(Board board, CancellationToken cancellationToken = default) =>
        await _context.Boards.AddAsync(board, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);

    public Task DeleteAsync(Board board, CancellationToken cancellationToken = default)
    {
        _context.Boards.Remove(board);
        return Task.CompletedTask;
    }

    public void MarkModified(Board board, Guid originalRowVersion)
    {
        _context.Entry(board).Property(b => b.RowVersion).OriginalValue = originalRowVersion;
    }

    public async Task<bool> AllExistAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var found = await _context.Boards
            .CountAsync(b => idList.Contains(b.Id), cancellationToken);
        return found == idList.Count;
    }
}
