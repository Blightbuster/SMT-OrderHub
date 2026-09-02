using System.Text.Json;
using System.Text.Json.Serialization;
using OrderHub.Application.Interfaces;
using Serilog;

namespace OrderHub.Application.ProductionExport;

/// <summary>
/// Default implementation building the JSON payload for the SMT production line.
/// </summary>
public class OrderProductionService : IOrderProductionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Domain entities reference each other in cycles; the export DTO graph is acyclic,
        // but this guard keeps serialization robust if entities leak in.
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly IOrderRepository _orderRepository;

    public OrderProductionService(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<string> ExportOrderForProductionAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetDetailByIdAsync(orderId, cancellationToken)
                    ?? throw new EntityNotFoundException(orderId, nameof(Domain.Order));

        if (order.OrderBoards.Count == 0)
        {
            throw new InvalidOperationException(
                $"Order '{order.Name}' ({orderId}) cannot be exported: no boards assigned.");
        }

        var payload = new ProductionOrderPayload(
            OrderId: order.Id,
            Name: order.Name,
            Description: order.Description,
            OrderDateUtc: order.OrderDate.ToUniversalTime(),
            Boards: order.OrderBoards
                .Select(ob => new ProductionBoard(
                    Name: ob.Board.Name,
                    LengthMm: ob.Board.Length,
                    WidthMm: ob.Board.Width,
                    Quantity: ob.BoardQuantity,
                    Placements: ob.Board.BoardComponents
                        .Select(bc => new ProductionPlacement(
                            Name: bc.Component.Name,
                            Description: bc.Component.Description,
                            PlacementCount: bc.PlacementCount))
                        .ToList()))
                .ToList());

        var json = JsonSerializer.Serialize(payload, SerializerOptions);

        Log.ForContext<OrderProductionService>()
           .Information("Exported order {OrderId} ({OrderName}) for production: {BoardCount} board(s), {PlacementTotal} placement(s), {PayloadBytes} bytes",
               order.Id, order.Name, payload.Boards.Count,
               payload.Boards.Sum(b => b.Placements.Count), json.Length);

        return json;
    }
}
