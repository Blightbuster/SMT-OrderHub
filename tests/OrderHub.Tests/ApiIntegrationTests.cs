using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderHub.Domain;
using OrderHub.Infrastructure.Persistence;

namespace OrderHub.Tests;

/// <summary>
/// Integration tests for the REST API using WebApplicationFactory with an
/// isolated SQLite in-memory database per test class instance.
/// Covers the CRUD surface, search/pagination, and the 409 conflict flow.
/// </summary>
public class ApiIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests()
    {
        // One shared open connection: keeps the in-memory SQLite database
        // alive across all requests handled by this factory instance.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    // Replace the file-based SQLite context with an in-memory one.
                    services.RemoveAllDbContexts();
                    services.AddDbContext<SmtDbContext>(options => options.UseSqlite(_connection));

                    using var scope = services.BuildServiceProvider().CreateScope();
                    scope.ServiceProvider.GetRequiredService<SmtDbContext>().Database.EnsureCreated();
                });
            });

        _client = _factory.CreateClient();
    }

    // ---------- Components ----------

    [Fact]
    public async Task PostComponent_CreatesAndReturns201WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/components",
            new { name = "Resistor 10k 0805", description = "SKU-R-10K-0805", quantity = 5000 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Resistor 10k 0805", json.GetProperty("name").GetString());
        Assert.NotEqual(Guid.Empty, json.GetProperty("rowVersion").GetGuid());
    }

    [Fact]
    public async Task GetComponents_SupportsSearchAndPaging()
    {
        await _client.PostAsJsonAsync("/api/components", new { name = "Alpha Cap", description = "", quantity = 1 });
        await _client.PostAsJsonAsync("/api/components", new { name = "Beta Cap", description = "", quantity = 2 });
        await _client.PostAsJsonAsync("/api/components", new { name = "Gamma Res", description = "", quantity = 3 });

        var paged = await _client.GetFromJsonAsync<JsonElement>("/api/components?searchTerm=cap&page=1&pageSize=1");
        Assert.Equal(2, paged.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, paged.GetProperty("items").GetArrayLength());
        Assert.Equal("Alpha Cap", paged.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task PutComponent_WithStaleRowVersion_Returns409WithCurrentState()
    {
        // Create, then capture the committed RowVersion.
        var created = await (await _client.PostAsJsonAsync("/api/components",
            new { name = "Conflict Comp", description = "", quantity = 10 })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        // First update commits and bumps the token.
        var firstUpdate = await _client.PutAsJsonAsync($"/api/components/{id}",
            new { rowVersion = created.GetProperty("rowVersion").GetGuid(), name = "Conflict Comp", description = "v2", quantity = 11 });
        Assert.Equal(HttpStatusCode.NoContent, firstUpdate.StatusCode);

        // A stale token (simulating a competing user) must yield 409 + current state.
        var staleUpdate = await _client.PutAsJsonAsync($"/api/components/{id}",
            new { rowVersion = created.GetProperty("rowVersion").GetGuid(), name = "Conflict Comp", description = "stale", quantity = 12 });

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var conflict = await staleUpdate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("v2", conflict.GetProperty("description").GetString());
        Assert.NotEqual(created.GetProperty("rowVersion").GetGuid(), conflict.GetProperty("rowVersion").GetGuid());

        // The server-side description must be untouched by the stale request.
        var reloaded = await _client.GetFromJsonAsync<JsonElement>($"/api/components/{id}");
        Assert.Equal("v2", reloaded.GetProperty("description").GetString());
        Assert.Equal(11, reloaded.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task PutComponent_WithFreshRowVersion_Succeeds()
    {
        var created = await (await _client.PostAsJsonAsync("/api/components",
            new { name = "Fresh Comp", description = "", quantity = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await _client.PutAsJsonAsync($"/api/components/{id}",
            new { rowVersion = created.GetProperty("rowVersion").GetGuid(), name = "Fresh Comp", description = "updated", quantity = 2 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var reloaded = await _client.GetFromJsonAsync<JsonElement>($"/api/components/{id}");
        Assert.Equal("updated", reloaded.GetProperty("description").GetString());
    }

    [Fact]
    public async Task DeleteComponent_RemovesEntity()
    {
        var created = await (await _client.PostAsJsonAsync("/api/components",
            new { name = "Doomed Comp", description = "", quantity = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var delete = await _client.DeleteAsync($"/api/components/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var get = await _client.GetAsync($"/api/components/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---------- Orders ----------

    [Fact]
    public async Task PostOrder_ThenExport_ReturnsProductionJson()
    {
        // Component → Board → Order aggregate.
        var component = await (await _client.PostAsJsonAsync("/api/components",
            new { name = "MCU STM32F407", description = "SKU", quantity = 100 })).Content.ReadFromJsonAsync<JsonElement>();

        var board = await (await _client.PostAsJsonAsync("/api/boards",
            new { name = "MCU-Mainboard-v2", description = "Rev C", length = 160.5, width = 100.0 })).Content.ReadFromJsonAsync<JsonElement>();

        var order = await (await _client.PostAsJsonAsync("/api/orders",
            new { name = "SMT-RUN-2026-001", description = "Batch reflow 3" })).Content.ReadFromJsonAsync<JsonElement>();

        // Assign the board to the order via PUT (concurrency contract).
        var put = await _client.PutAsJsonAsync($"/api/orders/{order.GetProperty("id").GetGuid()}",
            new
            {
                rowVersion = order.GetProperty("rowVersion").GetGuid(),
                name = "SMT-RUN-2026-001",
                description = "Batch reflow 3",
                boards = new[] { new { boardId = board.GetProperty("id").GetGuid(), boardQuantity = 5 } }
            });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        // Attach the component placement to the board.
        var boardPut = await _client.PutAsJsonAsync($"/api/boards/{board.GetProperty("id").GetGuid()}",
            new
            {
                rowVersion = board.GetProperty("rowVersion").GetGuid(),
                name = "MCU-Mainboard-v2",
                description = "Rev C",
                length = 160.5,
                width = 100.0,
                components = new[] { new { componentId = component.GetProperty("id").GetGuid(), placementCount = 1 } }
            });
        Assert.Equal(HttpStatusCode.NoContent, boardPut.StatusCode);

        // Export must contain the full aggregate in camelCase.
        var export = await _client.GetAsync($"/api/orders/{order.GetProperty("id").GetGuid()}/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        var json = await export.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SMT-RUN-2026-001", json.GetProperty("name").GetString());
        var boards = json.GetProperty("boards");
        Assert.Equal(1, boards.GetArrayLength());
        Assert.Equal(5, boards[0].GetProperty("quantity").GetInt32());
        Assert.Equal("MCU STM32F407", boards[0].GetProperty("placements")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExportOrder_WithUnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}/export");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutOrder_WithUnknownBoardId_Returns400()
    {
        var order = await (await _client.PostAsJsonAsync("/api/orders",
            new { name = "SMT-RUN-BADREF", description = "" })).Content.ReadFromJsonAsync<JsonElement>();

        var response = await _client.PutAsJsonAsync($"/api/orders/{order.GetProperty("id").GetGuid()}",
            new
            {
                rowVersion = order.GetProperty("rowVersion").GetGuid(),
                name = "SMT-RUN-BADREF",
                description = "",
                boards = new[] { new { boardId = Guid.NewGuid(), boardQuantity = 1 } }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _connection.Dispose();
    }
}

/// <summary>Removes all registered DbContext options (helper for test host override).</summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection RemoveAllDbContexts(this IServiceCollection services)
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(DbContextOptions) ||
                        d.ServiceType == typeof(DbContextOptions<SmtDbContext>) ||
                        d.ServiceType == typeof(SmtDbContext))
            .ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
        return services;
    }
}
