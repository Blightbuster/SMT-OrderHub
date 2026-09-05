using Microsoft.AspNetCore.Components;
using OrderHub.Client.ApiClient;

namespace OrderHub.Client.Pages;

/// <summary>
/// Developer tools page: quickly populate components, boards (with placements
/// referencing existing components) and orders (with board assignments) with
/// realistic mock data, and clear each entity type again — so the
/// list/search/pagination features can be exercised without manual data entry.
/// </summary>
public partial class DevTools : ComponentBase
{
    [Inject] private IOrderHubApiClient Api { get; set; } = null!;

    private int _componentCount = 25;
    private int _boardCount = 10;
    private int _orderCount = 10;

    private bool _working;
    private bool _confirmClearAll;
    private string? _error;
    private readonly List<string> _log = [];

    private static readonly string[] ComponentNames =
    [
        "Resistor", "Capacitor", "Inductor", "Diode", "Transistor", "LED",
        "Crystal", "Fuse", "Relay", "Connector", "Sensor", "Oscillator"
    ];

    private static readonly string[] BoardNames =
    [
        "Controller", "Amplifier", "Power", "Sensor", "Interface", "Driver",
        "Mainboard", "Logic", "Filter", "Converter"
    ];

    private static readonly string[] Customers =
    [
        "Bosch", "Siemens", "Continental", "ZF", "Brose", "Mahle", "Porsche",
        "Audi", "BMW", "Mercedes", "VW", "ThyssenKrupp"
    ];

    private readonly Random _rng = new();

    private async Task MockComponentsAsync()
    {
        await RunAsync(async () =>
        {
            var created = 0;
            var suffix = DateTime.UtcNow.Ticks % 100000;
            for (var i = 0; i < _componentCount; i++)
            {
                var name = $"{ComponentNames[_rng.Next(ComponentNames.Length)]}-{suffix}-{i + 1:D3}";
                var request = new CreateComponentRequest(
                    Name: name,
                    Description: $"Mock {name.ToLowerInvariant()} for testing (batch {suffix}).",
                    Quantity: _rng.Next(0, 5000));
                await Api.Components.CreateAsync(request);
                created++;
            }
            _log.Add($"Created {created} components.");
        });
    }

    private async Task MockBoardsAsync()
    {
        await RunAsync(async () =>
        {
            // Placements need existing components.
            var components = await Api.Components.SearchAsync(null, 1, 100);
            if (components.Items.Count == 0)
            {
                _log.Add("No components found — create components first.");
                return;
            }

            var created = 0;
            var suffix = DateTime.UtcNow.Ticks % 100000;
            for (var i = 0; i < _boardCount; i++)
            {
                var name = $"{BoardNames[_rng.Next(BoardNames.Length)]} Board {suffix}-{i + 1:D3}";
                // 1..4 distinct random components as placements.
                var placements = components.Items
                    .OrderBy(_ => _rng.Next())
                    .Take(_rng.Next(1, Math.Min(4, components.Items.Count) + 1))
                    .Select(c => new PlacementRequest(c.Id, _rng.Next(1, 50)))
                    .ToList();

                await Api.Boards.CreateAsync(new CreateBoardRequest(
                    Name: name,
                    Description: $"Mock board with {placements.Count} placements (batch {suffix}).",
                    Length: Math.Round(_rng.NextDouble() * 200 + 20, 1),
                    Width: Math.Round(_rng.NextDouble() * 200 + 20, 1)));

                // The create endpoint returns the board but placements are set
                // via update in this data model — check the API contract:
                // CreateBoardRequest has no placements, so update after create.
                var boardList = await Api.Boards.SearchAsync(name, 1, 1);
                var board = boardList.Items.FirstOrDefault();
                if (board is not null)
                {
                    var detail = await Api.Boards.GetByIdAsync(board.Id);
                    if (detail is not null)
                    {
                        await Api.Boards.UpdateAsync(board.Id, new UpdateBoardRequest(
                            detail.RowVersion, detail.Name, detail.Description,
                            detail.Length, detail.Width, placements));
                    }
                }
                created++;
            }
            _log.Add($"Created {created} boards (with placements).");
        });
    }

    private async Task MockOrdersAsync()
    {
        await RunAsync(async () =>
        {
            // Assignments need existing boards.
            var boards = await Api.Boards.SearchAsync(null, 1, 100);
            if (boards.Items.Count == 0)
            {
                _log.Add("No boards found — create boards first.");
                return;
            }

            var created = 0;
            var suffix = DateTime.UtcNow.Ticks % 100000;
            for (var i = 0; i < _orderCount; i++)
            {
                var name = $"Order {Customers[_rng.Next(Customers.Length)]} {suffix}-{i + 1:D3}";
                var createdOrder = await Api.Orders.CreateAsync(new CreateOrderRequest(
                    Name: name,
                    Description: $"Mock production order for testing (batch {suffix})."));

                // 1..3 distinct random boards as assignments.
                var assignments = boards.Items
                    .OrderBy(_ => _rng.Next())
                    .Take(_rng.Next(1, Math.Min(3, boards.Items.Count) + 1))
                    .Select(b => new BoardAssignmentRequest(b.Id, _rng.Next(1, 200)))
                    .ToList();

                await Api.Orders.UpdateAsync(createdOrder.Id, new UpdateOrderRequest(
                    createdOrder.RowVersion, createdOrder.Name, createdOrder.Description, assignments));
                created++;
            }
            _log.Add($"Created {created} orders (with board assignments).");
        });
    }

    // ---- Clearing ----

    private Task ClearOrdersAsync() => RunAsync(async () =>
    {
        var deleted = await DeleteAllAsync((page, ct) => Api.Orders.SearchAsync(null, page, 20, ct),
            o => Api.Orders.DeleteAsync(o.Id));
        _log.Add($"Deleted {deleted} orders.");
    });

    private Task ClearBoardsAsync() => RunAsync(async () =>
    {
        var deleted = await DeleteAllAsync((page, ct) => Api.Boards.SearchAsync(null, page, 20, ct),
            b => Api.Boards.DeleteAsync(b.Id));
        _log.Add($"Deleted {deleted} boards.");
    });

    private Task ClearComponentsAsync() => RunAsync(async () =>
    {
        var deleted = await DeleteAllAsync((page, ct) => Api.Components.SearchAsync(null, page, 20, ct),
            c => Api.Components.DeleteAsync(c.Id));
        _log.Add($"Deleted {deleted} components.");
    });

    private Task ClearAllAsync()
    {
        _confirmClearAll = false;
        return RunAsync(async () =>
        {
            // Delete in dependency order: orders → boards → components.
            var orders = await DeleteAllAsync((page, ct) => Api.Orders.SearchAsync(null, page, 20, ct),
                o => Api.Orders.DeleteAsync(o.Id));
            var boards = await DeleteAllAsync((page, ct) => Api.Boards.SearchAsync(null, page, 20, ct),
                b => Api.Boards.DeleteAsync(b.Id));
            var components = await DeleteAllAsync((page, ct) => Api.Components.SearchAsync(null, page, 20, ct),
                c => Api.Components.DeleteAsync(c.Id));
            _log.Add($"Cleared everything: {orders} orders, {boards} boards, {components} components.");
        });
    }

    /// <summary>
    /// Deletes every entity across all pages. Re-fetches page 1 after each
    /// deletion batch so progress is re-validated against the server.
    /// </summary>
    private async Task<int> DeleteAllAsync<T>(
        Func<int, CancellationToken, Task<PagedResultDto<T>>> search,
        Func<T, Task> delete)
    {
        var total = 0;
        while (true)
        {
            var page = await search(1, CancellationToken.None);
            if (page.Items.Count == 0) break;

            foreach (var item in page.Items)
            {
                await delete(item);
                total++;
            }
        }
        return total;
    }

    /// <summary>Shared wrapper: busy flag, error handling, log.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        _working = true;
        _error = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _working = false;
        }
    }
}
