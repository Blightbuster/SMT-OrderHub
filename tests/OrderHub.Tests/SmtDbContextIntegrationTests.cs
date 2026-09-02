using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderHub.Domain;
using OrderHub.Infrastructure.Persistence;

namespace OrderHub.Tests;

/// <summary>
/// Integration-style tests against a real SQLite in-memory database.
/// Verifies the EF Core model configuration: unique indexes, explicit join
/// tables with extended attributes, and optimistic concurrency tokens.
/// </summary>
public class SmtDbContextIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SmtDbContext> _options;

    public SmtDbContextIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<SmtDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UniqueNameIndex_RejectsDuplicateOrderNames()
    {
        using var context = CreateContext();
        context.Orders.Add(new Order { Name = "SMT-RUN-DUP" });
        await context.SaveChangesAsync();

        context.Orders.Add(new Order { Name = "SMT-RUN-DUP" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task JoinTables_PersistExtendedAttributes()
    {
        var board = new Board { Name = "Board-B1", Length = 100, Width = 50 };
        var component = new Component { Name = "Cap 100nF 0603", Quantity = 1000 };
        var order = new Order { Name = "SMT-RUN-JOIN" };

        order.OrderBoards.Add(new OrderBoard { Order = order, Board = board, BoardQuantity = 7 });
        board.BoardComponents.Add(new BoardComponent { Board = board, Component = component, PlacementCount = 42 });

        using (var context = CreateContext())
        {
            context.AddRange(order, board, component);
            await context.SaveChangesAsync();
        }

        using (var assertContext = CreateContext())
        {
            var loaded = await assertContext.Orders
                .Include(o => o.OrderBoards)
                    .ThenInclude(ob => ob.Board)
                        .ThenInclude(b => b.BoardComponents)
                            .ThenInclude(bc => bc.Component)
                .SingleAsync(o => o.Name == "SMT-RUN-JOIN");

            var orderBoard = loaded.OrderBoards.Single();
            Assert.Equal(7, orderBoard.BoardQuantity);
            Assert.Equal("Board-B1", orderBoard.Board.Name);
            Assert.Equal(42, orderBoard.Board.BoardComponents.Single().PlacementCount);
            Assert.Equal("Cap 100nF 0603", orderBoard.Board.BoardComponents.Single().Component.Name);
        }
    }

    [Fact]
    public async Task RowVersion_IsBumpedAutomaticallyOnUpdate()
    {
        using var context = CreateContext();
        var component = new Component { Name = "Inductor 10uH", Quantity = 10 };
        context.Components.Add(component);
        await context.SaveChangesAsync();

        var originalVersion = component.RowVersion;
        Assert.NotEqual(Guid.Empty, originalVersion);

        component.Quantity = 99;
        await context.SaveChangesAsync();

        Assert.NotEqual(originalVersion, component.RowVersion);
    }

    [Fact]
    public async Task OptimisticConcurrency_StaleRowVersion_ThrowsDbUpdateConcurrencyException()
    {
        Guid staleRowVersion;
        var componentId = Guid.NewGuid();

        // Simulate user A loading the entity.
        using (var contextA = CreateContext())
        {
            var component = new Component { Id = componentId, Name = "Diode 1N4148", Quantity = 500 };
            contextA.Components.Add(component);
            await contextA.SaveChangesAsync();
            staleRowVersion = component.RowVersion;
        }

        // User B updates first — a new RowVersion is committed.
        using (var contextB = CreateContext())
        {
            var component = await contextB.Components.SingleAsync(c => c.Id == componentId);
            component.Quantity = 600;
            await contextB.SaveChangesAsync();
        }

        // User A commits with the stale token — conflict expected.
        using var contextC = CreateContext();
        var staleEntity = await contextC.Components.SingleAsync(c => c.Id == componentId);

        // Simulate a client that round-tripped the OLD RowVersion and now updates:
        // set the ORIGINAL value of the concurrency property to the stale token.
        staleEntity.Quantity = 400;
        contextC.Entry(staleEntity).Property(e => e.RowVersion).OriginalValue = staleRowVersion;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextC.SaveChangesAsync());
    }

    private SmtDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
